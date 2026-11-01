using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using OSTream.Core;
using OSTream.Core.Khinsider;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
namespace OSTream.Frontend.Platform.Windows;

internal sealed class WebViewPageLoader(Func<TopLevel?> host, string dataDir, ILogger log) : IDisposable
{
    private const string ChallengeHost = "challenges.cloudflare.com";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CoreWebView2Controller? _controller;
    private bool _disposed;

    public Task<(string FinalUrl, string Html)> LoadAsync(string url, CancellationToken ct)
    {
        return Dispatcher.UIThread.InvokeAsync(() => LoadOnUiThreadAsync(url, ct));
    }

    private async Task<(string FinalUrl, string Html)> LoadOnUiThreadAsync(string url, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CoreWebView2 web = (_controller ??= await CreateAsync()).CoreWebView2;
            Channel<CoreWebView2NavigationCompletedEventArgs> completed = Channel.CreateUnbounded<CoreWebView2NavigationCompletedEventArgs>();
            ulong? navigation = null;
            void OnStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
            {
                navigation = e.NavigationId;
            }

            void OnCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                if (e.NavigationId == navigation)
                {
                    _ = completed.Writer.TryWrite(e);
                }
            }
            web.NavigationStarting += OnStarting;
            web.NavigationCompleted += OnCompleted;
            using CancellationTokenRegistration stop = ct.Register(() => Dispatcher.UIThread.Post(web.Stop));
            try
            {
                web.Navigate(url);
                while (true)
                {
                    CoreWebView2NavigationCompletedEventArgs result = await completed.Reader.ReadAsync(ct);
                    int status = result.HttpStatusCode;
                    if (status is >= 200 and < 400)
                    {
                        return (web.Source, await ScriptAsync(web, "document.documentElement.outerHTML"));
                    }
                    if (status == 0)
                    {
                        throw new KhinsiderException($"Could not reach KHInsider ({result.WebErrorStatus}).", offline: true);
                    }
                    string title = await ScriptAsync(web, "document.title");
                    if (status == 403 && title.StartsWith("Just a moment", StringComparison.Ordinal))
                    {
                        continue; // Cloudflare's challenge page reloads itself once its script passes.
                    }
                    log.LogWarning("KHInsider returned {Status} for {Url} in WebView2 (title: {Title})", status, url, title);
                    throw KhinsiderClient.StatusError(status);
                }
            }
            finally
            {
                web.NavigationStarting -= OnStarting;
                web.NavigationCompleted -= OnCompleted;
            }
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    private async Task<CoreWebView2Controller> CreateAsync()
    {
        nint hwnd = host()?.TryGetPlatformHandle()?.Handle ?? throw new KhinsiderException("OSTream is still starting. Try again in a moment.");
        CoreWebView2Environment env;
        CoreWebView2Controller controller;
        try
        {
            env = await CoreWebView2Environment.CreateAsync(null, dataDir);
            controller = await env.CreateCoreWebView2ControllerAsync(hwnd);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            throw new KhinsiderException("OSTream needs the Microsoft Edge WebView2 Runtime to load KHInsider. Install it, then restart OSTream.");
        }
        catch (Exception ex) when (ex is not KhinsiderException)
        {
            log.LogError(ex, "Starting WebView2 failed");
            throw new KhinsiderException($"Could not start the KHInsider page loader: {ex.Message}");
        }
        controller.IsVisible = false;
        CoreWebView2 web = controller.CoreWebView2;
        web.Settings.AreDefaultScriptDialogsEnabled = false;
        web.IsMuted = true;
        web.NewWindowRequested += (_, e) => e.Handled = true;
        web.DownloadStarting += (_, e) => e.Cancel = true;
        web.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        web.WebResourceRequested += (_, e) =>
        {
            if (!IsNeeded(e.Request.Uri))
            {
                e.Response = env.CreateWebResourceResponse(null, 403, "Blocked", "");
            }
        };
        return controller;
    }

    private static bool IsNeeded(string uri)
    {
        return Uri.TryCreate(uri, UriKind.Absolute, out Uri? u)
            && (u.Host.Equals(Config.Host, StringComparison.OrdinalIgnoreCase) || u.Host.Equals(ChallengeHost, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<string> ScriptAsync(CoreWebView2 web, string script)
    {
        using JsonDocument result = JsonDocument.Parse(await web.ExecuteScriptAsync(script));
        return result.RootElement.GetString() ?? "";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (Dispatcher.UIThread.CheckAccess())
        {
            _controller?.Close();
        }
    }
}
