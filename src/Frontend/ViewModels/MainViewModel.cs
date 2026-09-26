using System;
using CommunityToolkit.Mvvm.ComponentModel;
using OSTream.Navigation;
using OSTream.Services;
namespace OSTream.ViewModels;
public sealed partial class MainViewModel : ViewModelBase
{
    public MainViewModel(AppServices app)
    {
        App = app;
        TopBar = new TopBarViewModel(app);
        Sidebar = new SidebarViewModel(app);
        Rail = new RailViewModel(app);
        Player = new PlayerBarViewModel(app, Rail);
        app.Navigator.Changed += OnRoute;
    }
    public AppServices App { get; }
    public TopBarViewModel TopBar { get; }
    public SidebarViewModel Sidebar { get; }
    public RailViewModel Rail { get; }
    public PlayerBarViewModel Player { get; }
    public ToastService Toasts => App.Toasts;
    public DialogService Dialogs => App.Dialogs;
    public MenuService Menus => App.Menus;
    public LightboxService Lightbox => App.Lightbox;
    string? _kind;
    [ObservableProperty] public partial PageViewModel? Page { get; private set; }
    public event Action<ScrollTarget, bool>? ScrollRequested;
    public bool SearchFocused { get; set; }
    void OnRoute(Route route)
    {
        App.Menus.Close();
        var kind = route.Name is "album" or "downloads" or "favorites" or "recent" or "playlists" ? route.Name : "discover";
        if (kind != _kind || Page is null)
        {
            if (Page is { } old)
            {
                old.ScrollRequested -= OnScrollRequested;
                old.Dispose();
            }
            PageViewModel page = kind switch
            {
                "album" => new AlbumPageViewModel(App),
                "downloads" => new DownloadsViewModel(App),
                "favorites" => new FavoritesViewModel(App),
                "recent" => new RecentViewModel(App),
                "playlists" => new PlaylistsViewModel(App),
                _ => new DiscoverViewModel(App, busy => TopBar.IsBusy = busy),
            };
            page.ScrollRequested += OnScrollRequested;
            (_kind, Page) = (kind, page);
            ScrollRequested?.Invoke(ScrollTarget.Top, true);
        }
        Page.Update(route);
        Sidebar.Active = kind == "album" ? null : kind;
        TopBar.Sync(route, SearchFocused);
        if (Rail.IsNarrow && Rail.IsDrawerOpen) Rail.Toggle(false);
    }
    void OnScrollRequested(ScrollTarget target, bool instant) => ScrollRequested?.Invoke(target, instant);
    public void Start() => App.Navigator.Start();
}