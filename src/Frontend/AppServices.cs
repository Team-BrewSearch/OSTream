using Microsoft.Extensions.Logging;
using OSTream.Core.Downloads;
using OSTream.Core.Khinsider;
using OSTream.Core.Logging;
using OSTream.Core.Player;
using OSTream.Core.Storage;
using OSTream.Frontend.Navigation;
using OSTream.Frontend.Platform;
using OSTream.Frontend.Services;
using OSTream.Services;
using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
namespace OSTream.Frontend;

public sealed class AppServices : IDisposable
{
    private readonly FileLoggerProvider _logs;
    private readonly HttpClient _imageHttp;
#if WINDOWS
    private readonly Platform.Windows.WebViewPageLoader? _webPages;
#endif
    public static string LogDir => Path.Combine(AppPaths.ConfigDir, "logs");
    public AppServices()
    {
        _logs = new FileLoggerProvider(LogDir, echo: Console.Error);
        Log = _logs.CreateLogger("App");
        Log.LogInformation("OSTream {Version} starting on {Os}, .NET {Runtime}",
            typeof(AppServices).Assembly.GetName().Version, RuntimeInformation.OSDescription, Environment.Version);
        Settings = new SettingsStore();
        Library = new LibraryStore();
        PageLoader? pages = null;
#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            _webPages = new Platform.Windows.WebViewPageLoader(() => Shell?.Top, Path.Combine(AppPaths.CacheDir, "WebView2"), Log);
            pages = _webPages.LoadAsync;
        }
#endif
        Client = new KhinsiderClient(Log, pages: pages);
        Catalog = new Catalog(Client);
        Toasts = new ToastService();
        Watch(Settings.Store, "settings", "Could not save settings");
        Watch(Library.Store, "library", "Could not save your library");
        Shell = new PlatformShell(Toasts);
        _imageHttp = KhinsiderClient.NewHttpClient();
        Images = new ImageLoader(_imageHttp, AppPaths.CacheDir);
        Manager = new DownloadManager(Client, () => Settings.DownloadDir, Log);
        Downloads = new DownloadService(Manager, Settings, Catalog, Toasts, OpenJobFolder, Log);
        Player = new PlayerService(Client, Settings, Library, Toasts, Log);
        Navigator = new Navigator();
        Dialogs = new DialogService();
        Menus = new MenuService();
        Lightbox = new LightboxService(Shell.OpenUrl);
        Actions = new UserActions(this);
        MediaSession = MediaSessions.Create(this);
    }
    public SettingsStore Settings { get; }
    public LibraryStore Library { get; }
    public KhinsiderClient Client { get; }
    public Catalog Catalog { get; }
    public ToastService Toasts { get; }
    public PlatformShell Shell { get; }
    public ImageLoader Images { get; }
    public DownloadManager Manager { get; }
    public DownloadService Downloads { get; }
    public PlayerService Player { get; }
    public Navigator Navigator { get; }
    public DialogService Dialogs { get; }
    public MenuService Menus { get; }
    public LightboxService Lightbox { get; }
    public UserActions Actions { get; }
    public ILogger Log { get; }
    public IMediaSession MediaSession { get; }
    private void Watch<T>(JsonStore<T> store, string name, string saveError) where T : class
    {
        store.FlushFailed += ex =>
        {
            Log.LogError(ex, "Saving {Name} failed", name);
            Toasts.Toast(saveError, Core.ToastKind.Error, ex.Message);
        };
        if (store.LoadError is not { } error)
        {
            return;
        }

        Log.LogError(error, "Loading {Name} failed", name);
        Toasts.Toast($"Your {name} could not be read", Core.ToastKind.Error,
            store.IsReadOnly ? "Changes will not be saved this session." : $"A copy was kept at {store.BackupPath}.");
    }
    public async Task OpenJobFolder(string? jobId)
    {
        try
        {
            await Shell.OpenFolder(Downloads.FolderFor(jobId));
        }
        catch (KhinsiderException ex)
        {
            Toasts.Toast("Could not open the folder", Core.ToastKind.Error, ex.Message);
        }
    }
    private bool _disposed;
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true))
        {
            return;
        }

        MediaSession.Dispose();
        Player.Dispose();
        Downloads.Dispose();
        Manager.Dispose();
        Settings.Dispose();
        Library.Dispose();
        Client.Dispose();
#if WINDOWS
        _webPages?.Dispose();
#endif
        Images.Dispose();
        _imageHttp.Dispose();
        _logs.Dispose();
    }
}