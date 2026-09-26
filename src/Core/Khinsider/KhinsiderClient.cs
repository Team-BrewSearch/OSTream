using System;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OSTream.Core.Caching;
namespace OSTream.Core.Khinsider;
public sealed class KhinsiderException(string message, bool offline = false, int? status = null) : Exception(message)
{
    public bool Offline { get; } = offline;
    public int? Status { get; } = status;
}
public sealed class KhinsiderClient : IDisposable
{
    static readonly TimeSpan PageTimeout = TimeSpan.FromSeconds(30);
    const int MaxBufferedResponse = 32 * 1024 * 1024;
    readonly LruCache<string, IReadOnlyDictionary<string, string>> _trackUrls = new(512, ttl: Config.TrackUrlTtl);
    readonly SingleFlight<IReadOnlyDictionary<string, string>> _resolving = new();
    readonly ILogger Log;
    public KhinsiderClient(ILogger logger, HttpMessageHandler? handler = null)
    {
        Log = logger;
        Http = NewHttpClient(handler);
    }
    public static HttpClient NewHttpClient(HttpMessageHandler? handler = null) => new(handler ?? new SocketsHttpHandler
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
    public HttpClient Http { get; }
    public static HttpRequestMessage PageRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var h = request.Headers;
        h.TryAddWithoutValidation("User-Agent", Config.UserAgent);
        h.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
        h.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        h.TryAddWithoutValidation("Referer", Config.BaseUrl);
        h.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
        h.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
        h.TryAddWithoutValidation("Sec-Fetch-Site", "none");
        h.TryAddWithoutValidation("Sec-Fetch-User", "?1");
        h.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
        return request;
    }
    public static HttpRequestMessage MediaRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", Config.UserAgent);
        request.Headers.TryAddWithoutValidation("Referer", Config.BaseUrl + "/");
        return request;
    }
    public static string KhinsiderUrl(string? path)
    {
        path = (path ?? "").Trim();
        if (path.StartsWith(Config.BaseUrl, StringComparison.Ordinal)) path = path[Config.BaseUrl.Length..] is { Length: > 0 } rest ? rest : "/";
        if (!path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal))
            throw new KhinsiderException("path must be a KHInsider path such as /search?search=zelda");
        var url = Config.BaseUrl + path;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !string.Equals(uri.Host, Config.Host, StringComparison.OrdinalIgnoreCase))
            throw new KhinsiderException("Only downloads.khinsider.com can be fetched.");
        return url;
    }
    public static string? SecureMediaUrl(string? url)
    {
        if (!Uri.TryCreate(Formatting.SiteUrl(url), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme == Uri.UriSchemeHttp && uri.IsDefaultPort) uri = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri;
        if (uri.Scheme != Uri.UriSchemeHttps) return null;
        var host = uri.Host.ToLowerInvariant();
        foreach (var allowed in Config.MediaHosts)
        {
            var a = allowed.ToLowerInvariant();
            if (host == a || host.EndsWith("." + a, StringComparison.Ordinal))
                return uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length >= 2 ? uri.AbsoluteUri : null;
        }
        return null;
    }
    public Task<(string FinalUrl, string Html)> GetPageAsync(string path, CancellationToken ct = default) => FetchAsync(KhinsiderUrl(path), ct);
    async Task<(string FinalUrl, string Html)> FetchAsync(string url, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(PageTimeout);
        try
        {
            using var request = PageRequest(url);
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw StatusError((int)response.StatusCode);
            var html = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
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
    public static KhinsiderException StatusError(int status) => status switch
    {
        403 => new KhinsiderException("KHInsider refused the request (Cloudflare check). Wait a moment and try again.", status: status),
        404 => new KhinsiderException("That page does not exist on KHInsider.", status: status),
        _ => new KhinsiderException($"KHInsider returned HTTP {status}.", status: status),
    };
    public static KhinsiderException NetworkError(HttpRequestException ex) =>
        ex.StatusCode is { } code ? StatusError((int)code)
        : ex.HttpRequestError == HttpRequestError.ConfigurationLimitExceeded ? new KhinsiderException("KHInsider sent a response that is too large.")
        : new KhinsiderException($"Could not reach KHInsider: {ex.InnerException?.Message ?? ex.Message}", offline: true);
    public Task<IReadOnlyDictionary<string, string>> ResolveTrackAsync(string path, CancellationToken ct = default)
    {
        var url = KhinsiderUrl(path);
        if (_trackUrls.TryGet(url, out var cached)) return Task.FromResult(cached);
        var task = _resolving.Run(url, ResolveCoreAsync);
        return ct.CanBeCanceled ? task.WaitAsync(ct) : task;
    }
    async Task<IReadOnlyDictionary<string, string>> ResolveCoreAsync(string url)
    {
        var (_, html) = await FetchAsync(url, CancellationToken.None).ConfigureAwait(false);
        Dictionary<string, string> urls;
        try
        {
            urls = Parsers.ParseTrackUrls(html);
        }
        catch (Exception ex) when (ex is not KhinsiderException)
        {
            throw new KhinsiderException("KHInsider sent a song page OSTream could not read.");
        }
        if (urls.Count == 0) throw new KhinsiderException("No audio links found on the song page.");
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
    public void Dispose() => Http.Dispose();
}