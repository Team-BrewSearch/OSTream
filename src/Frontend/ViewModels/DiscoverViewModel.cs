using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OSTream.Controls;
using OSTream.Core;
using OSTream.Core.Khinsider;
using OSTream.Core.Models;
using OSTream.Navigation;
namespace OSTream.ViewModels;
public sealed partial class ListTabViewModel(string id, string label, bool selected) : ViewModelBase
{
    public string Id { get; } = id;
    public string Label { get; } = label;
    public bool IsSelected { get; } = selected;
}
public sealed partial class DiscoverViewModel : PageViewModel
{
    readonly Action<bool> _setBusy;
    CancellationTokenSource? _load;
    string? _key;
    IReadOnlyList<AlbumSummary> _albums = [];
    int _limit = Config.GridPage;
    string? _selected;
    bool _scrollAfterLoad;
    Route? _lastRoute;
    public DiscoverViewModel(AppServices app, Action<bool> setBusy) : base(app)
    {
        _setBusy = setBusy;
        Panel = new AlbumPanelViewModel(app, inline: true, slug => app.Navigator.With("album", slug), RequestScroll);
    }
    public AlbumPanelViewModel Panel { get; }
    [ObservableProperty] public partial bool IsSearch { get; private set; }
    [ObservableProperty] public partial string Query { get; private set; } = "";
    [ObservableProperty] public partial string HeadTitle { get; private set; } = "";
    [ObservableProperty] public partial string HeadPrefix { get; private set; } = "";
    [ObservableProperty] public partial IReadOnlyList<ListTabViewModel> Tabs { get; private set; } = [];
    [ObservableProperty] public partial bool IsRolling { get; private set; }
    [ObservableProperty] public partial bool IsLoading { get; private set; }
    [ObservableProperty] public partial int SkeletonCount { get; private set; }
    [ObservableProperty] public partial IReadOnlyList<AlbumCardViewModel> Cards { get; private set; } = [];
    [ObservableProperty] public partial bool IsEmpty { get; private set; }
    [ObservableProperty] public partial string EmptyTitle { get; private set; } = "";
    [ObservableProperty] public partial string EmptyText { get; private set; } = "";
    [ObservableProperty] public partial IconKind EmptyIcon { get; private set; } = IconKind.Music;
    [ObservableProperty] public partial bool IsError { get; private set; }
    [ObservableProperty] public partial string ErrorTitle { get; private set; } = "";
    [ObservableProperty] public partial string ErrorText { get; private set; } = "";
    [ObservableProperty] public partial IconKind ErrorIcon { get; private set; } = IconKind.Alert;
    [ObservableProperty] public partial string MoreLabel { get; private set; } = "";
    [ObservableProperty] public partial string MoreLeft { get; private set; } = "";
    [ObservableProperty] public partial bool HasMore { get; private set; }
    public IEnumerable<int> Skeletons => Enumerable.Range(0, SkeletonCount);
    partial void OnSkeletonCountChanged(int value) => OnPropertyChanged(nameof(Skeletons));
    string ListId => _key is { } k && k.StartsWith("l:", StringComparison.Ordinal) ? k[2..] : Config.Lists[0].Id;
    void RenderHead(string status)
    {
        if (IsSearch)
        {
            HeadPrefix = status == "ready" ? $"{Formatting.Plural(_albums.Count, "result")} for " : "Searching ";
            HeadTitle = $"“{Query}”";
            return;
        }
        var list = Config.ListFor(ListId);
        HeadPrefix = "";
        HeadTitle = list.Title;
        Tabs = Config.Lists.Select(l => new ListTabViewModel(l.Id, l.Label, l.Id == list.Id)).ToArray();
    }
    void RenderResults()
    {
        IsLoading = false;
        IsError = false;
        if (_albums.Count == 0)
        {
            Cards = [];
            HasMore = false;
            IsEmpty = true;
            (EmptyIcon, EmptyTitle, EmptyText) = IsSearch
                ? (IconKind.Search, $"No albums match “{Query}”", "Try fewer or different words, such as the game’s full title.")
                : (IconKind.Music, "Nothing here right now", "KHInsider returned an empty list.");
            return;
        }
        IsEmpty = false;
        var ranked = !IsSearch && Config.ListFor(ListId).Ranked;
        Cards = AlbumCardViewModel.Build(App, _albums.Take(_limit), slug => App.Navigator.With("album", slug), _selected, ranked);
        var left = _albums.Count - Math.Min(_limit, _albums.Count);
        HasMore = left > 0;
        MoreLabel = $"Show {Math.Min(left, Config.GridPage)} more";
        MoreLeft = $"· {Formatting.Plural(left, "album")} left";
    }
    async Task LoadGrid(string key, string query, string listId)
    {
        _load?.Cancel();
        var cts = _load = new CancellationTokenSource();
        _key = key;
        _albums = [];
        _limit = Config.GridPage;
        IsSearch = query.Length > 0;
        Query = query;
        RenderHead("loading");
        Cards = [];
        IsEmpty = IsError = HasMore = false;
        SkeletonCount = IsSearch ? 12 : 18;
        IsLoading = true;
        _setBusy(IsSearch);
        try
        {
            _albums = IsSearch ? await App.Catalog.SearchAsync(query, cts.Token) : await App.Catalog.GetListAsync(Config.ListFor(listId).Id, cts.Token);
            if (cts.IsCancellationRequested) return;
            RevealSelected();
            RenderHead("ready");
            RenderResults();
            if (_scrollAfterLoad && _selected is not null) RequestScroll(ScrollTarget.AlbumPanel);
        }
        catch (OperationCanceledException)
        {
        }
        catch (KhinsiderException ex)
        {
            if (cts.IsCancellationRequested) return;
            RenderHead("error");
            IsLoading = false;
            IsError = true;
            var offline = ex.Offline;
            ErrorIcon = offline ? IconKind.WifiOff : IconKind.Alert;
            ErrorTitle = offline ? "Can’t reach KHInsider" : IsSearch ? "Search failed" : "Could not load albums";
            ErrorText = offline ? "Check your internet connection, then try again." : ex.Message;
        }
        finally
        {
            if (!cts.IsCancellationRequested)
            {
                _scrollAfterLoad = false;
                _setBusy(false);
            }
        }
    }
    bool RevealSelected()
    {
        var at = _albums.Select((a, i) => (a, i)).FirstOrDefault(x => x.a.Slug == _selected, (null!, -1)).Item2;
        if (at < _limit) return false;
        _limit = (at + 1 + Config.GridPage - 1) / Config.GridPage * Config.GridPage;
        return true;
    }
    public override void Update(Route route)
    {
        _lastRoute = route;
        var query = route.Name == "search" ? (route.Param("q") ?? "").Trim() : "";
        var listId = route.Param("list") ?? Config.Lists[0].Id;
        _selected = route.Param("album");
        if (route.Name == "search" && query.Length == 0)
        {
            App.Navigator.Navigate("/", replace: true);
            return;
        }
        var nextKey = query.Length > 0 ? $"q:{query}" : $"l:{Config.ListFor(listId).Id}";
        if (nextKey != _key)
        {
            _scrollAfterLoad = _selected is not null;
            LoadGrid(nextKey, query, listId).Forget(App.Log);
            Panel.Show(_selected, scroll: false);
            return;
        }
        if (RevealSelected()) RenderResults();
        foreach (var card in Cards) card.IsSelected = card.Slug == _selected;
        Panel.Show(_selected);
    }
    [RelayCommand] void SelectList(string id) => App.Navigator.Navigate("/", "list", id);
    [RelayCommand]
    void ShowMore()
    {
        _limit += Config.GridPage;
        RenderResults();
    }
    [RelayCommand]
    void Retry()
    {
        _key = null;
        if (_lastRoute is not null) Update(_lastRoute);
    }
    [RelayCommand] void BackToDiscover() => App.Navigator.Navigate("/");
    [RelayCommand]
    async Task Random()
    {
        if (IsRolling) return;
        IsRolling = true;
        try
        {
            var slug = await App.Catalog.RandomAlbumSlugAsync();
            if (slug.Length > 0) App.Navigator.SetParam("album", slug);
        }
        catch (Exception ex) when (ex is KhinsiderException or OperationCanceledException)
        {
            App.Toasts.Error(ex, "Could not fetch a random album");
        }
        finally
        {
            IsRolling = false;
        }
    }
    public override void Dispose()
    {
        _load?.Cancel();
        Panel.Dispose();
        _setBusy(false);
    }
}