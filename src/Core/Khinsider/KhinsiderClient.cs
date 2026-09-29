using Microsoft.Extensions.Logging;
using OSTream.Core.Caching;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
namespace OSTream.Core.Khinsider;

public sealed class KhinsiderException(string message, bool offline = false, int? status = null) : Exception(message)
{
    public bool Offline { get; } = offline;
    public int? Status { get; } = status;
}
public delegate Task<(string FinalUrl, string Html)> PageLoader(string url, CancellationToken ct);
public sealed class KhinsiderClient : IDisposable
{
    private static readonly TimeSpan PageTimeout = TimeSpan.FromSeconds(30);
    private const int MaxBufferedResponse = 32 * 1024 * 1024;
    private readonly LruCache<string, IReadOnlyDictionary<string, string>> _trackUrls = new(512, ttl: Config.TrackUrlTtl);
    private readonly SingleFlight<IReadOnlyDictionary<string, string>> _resolving = new();
    private readonly PageLoader? _pages;
    private readonly ILogger Log;
    public KhinsiderClient(ILogger logger, HttpMessageHandler? handler = null, PageLoader? pages = null)
    {
        Log = logger;
        Http = NewHttpClient(handler);
        _pages = pages;
    }
    public static HttpClient NewHttpClient(HttpMessageHandler? handler = null)
    {
        return new(handler ?? new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            ConnectTimeout = TimeSpan.FromSeconds(15),
            MaxConnectionsPerServer = 8,
        })
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = MaxBufferedResponse,
        };
    }

    public HttpClient Http { get; }
    public static HttpRequestMessage PageRequest(string url)
    {
        HttpRequestMessage request = new(HttpMethod.Get, url);
        HttpRequestHeaders h = request.Headers;
        _ = h.TryAddWithoutValidation("User-Agent", Config.UserAgent);
        _ = h.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
        _ = h.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        _ = h.TryAddWithoutValidation("Referer", Config.BaseUrl);
        _ = h.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
        _ = h.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
        _ = h.TryAddWithoutValidation("Sec-Fetch-Site", "none");
        _ = h.TryAddWithoutValidation("Sec-Fetch-User", "?1");
        _ = h.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
        return request;
    }
    public static HttpRequestMessage MediaRequest(string url)
    {
        HttpRequestMessage request = new(HttpMethod.Get, url);
        _ = request.Headers.TryAddWithoutValidation("User-Agent", Config.UserAgent);
        _ = request.Headers.TryAddWithoutValidation("Referer", Config.BaseUrl + "/");
        return request;
    }
    public static string KhinsiderUrl(string? path)
    {
        path = (path ?? "").Trim();
        if (path.StartsWith(Config.BaseUrl, StringComparison.Ordinal))
        {
            path = path[Config.BaseUrl.Length..] is { Length: > 0 } rest ? rest : "/";
        }

        if (!path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal))
        {
            throw new KhinsiderException("path must be a KHInsider path such as /search?search=zelda");
        }

        string url = Config.BaseUrl + path;
        return !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || !string.Equals(uri.Host, Config.Host, StringComparison.OrdinalIgnoreCase)
            ? throw new KhinsiderException("Only downloads.khinsider.com can be fetched.")
            : url;
    }
    public static string? SecureMediaUrl(string? url)
    {
        if (!Uri.TryCreate(Formatting.SiteUrl(url), UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Scheme == Uri.UriSchemeHttp && uri.IsDefaultPort)
        {
            uri = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        string host = uri.Host.ToLowerInvariant();
        foreach (string allowed in Config.MediaHosts)
        {
            string a = allowed.ToLowerInvariant();
            if (host == a || host.EndsWith("." + a, StringComparison.Ordinal))
            {
                return uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length >= 2 ? uri.AbsoluteUri : null;
            }
        }
        return null;
    }
    public Task<(string FinalUrl, string Html)> GetPageAsync(string path, CancellationToken ct = default)
    {
        return FetchAsync(KhinsiderUrl(path), ct);
    }

    private async Task<(string FinalUrl, string Html)> FetchAsync(string url, CancellationToken ct)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(PageTimeout);
        try
        {
            if (_pages is not null)
            {
                return await _pages(url, timeout.Token).ConfigureAwait(false);
            }

            using HttpRequestMessage request = PageRequest(url);
            using HttpResponseMessage response = await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                Log.LogWarning("KHInsider returned {Status} for {Url} (cf-mitigated: {Mitigated}, cf-ray: {Ray}, title: {Title})",
                    (int)response.StatusCode, url, Header(response, "cf-mitigated"), Header(response, "CF-RAY"),
                    Regex.Match(body, "<title>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase).Groups[1].Value.Trim());
                throw StatusError((int)response.StatusCode);
            }

            string html = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return (response.RequestMessage?.RequestUri?.AbsoluteUri ?? url, html);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new KhinsiderException("KHInsider took too long to respond.");
        }
        catch (HttpRequestException ex)
        {
            throw NetworkError(ex);
        }
    }
    private static string Header(HttpResponseMessage response, string name)
    {
        return response.Headers.TryGetValues(name, out IEnumerable<string>? values) ? string.Join(",", values) : "none";
    }

    public static KhinsiderException StatusError(int status)
    {
        return status switch
        {
            403 => new KhinsiderException("KHInsider refused the request (Cloudflare check). Wait a moment and try again.", status: status),
            404 => new KhinsiderException("That page does not exist on KHInsider.", status: status),
            _ => new KhinsiderException($"KHInsider returned HTTP {status}.", status: status),
        };
    }

    public static KhinsiderException NetworkError(HttpRequestException ex)
    {
        return ex.StatusCode is { } code ? StatusError((int)code)
            : ex.HttpRequestError == HttpRequestError.ConfigurationLimitExceeded ? new KhinsiderException("KHInsider sent a response that is too large.")
            : new KhinsiderException($"Could not reach KHInsider: {ex.InnerException?.Message ?? ex.Message}", offline: true);
    }

    public Task<IReadOnlyDictionary<string, string>> ResolveTrackAsync(string path, CancellationToken ct = default)
    {
        string url = KhinsiderUrl(path);
        if (_trackUrls.TryGet(url, out IReadOnlyDictionary<string, string>? cached))
        {
            return Task.FromResult(cached);
        }

        Task<IReadOnlyDictionary<string, string>> task = _resolving.Run(url, ResolveCoreAsync);
        return ct.CanBeCanceled ? task.WaitAsync(ct) : task;
    }
    private async Task<IReadOnlyDictionary<string, string>> ResolveCoreAsync(string url)
    {
        (string _, string? html) = await FetchAsync(url, CancellationToken.None).ConfigureAwait(false);
        Dictionary<string, string> urls;
        try
        {
            urls = Parsers.ParseTrackUrls(html);
        }
        catch (Exception ex) when (ex is not KhinsiderException)
        {
            throw new KhinsiderException("KHInsider sent a song page OSTream could not read.");
        }
        if (urls.Count == 0)
        {
            throw new KhinsiderException("No audio links found on the song page.");
        }

        _trackUrls.Set(url, urls);
        return urls;
    }
    public void ForgetTrack(string path)
    {
        try
        {
            _trackUrls.Remove(KhinsiderUrl(path));
        }
        catch (KhinsiderException)
        {
        }
    }
    public void Dispose()
    {
        Http.Dispose();
    }
}