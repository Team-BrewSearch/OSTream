using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OSTream.Core;
using OSTream.Core.Khinsider;
using OSTream.Core.Models;
using OSTream.Frontend;
using OSTream.Frontend.Controls;
using OSTream.Frontend.Navigation;
using OSTream.Frontend.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace OSTream.ViewModels;

public sealed partial class ListTabViewModel(string id, string label, bool selected) : ViewModelBase
{
    public string Id { get; } = id;
    public string Label { get; } = label;
    public bool IsSelected { get; } = selected;
}
public sealed partial class DiscoverViewModel : PageViewModel
{
    private readonly Action<bool> _setBusy;
    private CancellationTokenSource? _load;
    private string? _key;
    private IReadOnlyList<AlbumSummary> _albums = [];
    private int _limit = Config.GridPage;
    private string? _selected;
    private bool _scrollAfterLoad;
    private Route? _lastRoute;
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
    partial void OnSkeletonCountChanged(int value)
    {
        OnPropertyChanged(nameof(Skeletons));
    }
    private string ListId => _key is { } k && k.StartsWith("l:", StringComparison.Ordinal) ? k[2..] : Config.Lists[0].Id;
    private void RenderHead(string status)
    {
        if (IsSearch)
        {
            HeadPrefix = status == "ready" ? $"{Formatting.Plural(_albums.Count, "result")} for " : "Searching ";
            HeadTitle = $"“{Query}”";
            return;
        }
        AlbumListInfo list = Config.ListFor(ListId);
        HeadPrefix = "";
        HeadTitle = list.Title;
        Tabs = Config.Lists.Select(l => new ListTabViewModel(l.Id, l.Label, l.Id == list.Id)).ToArray();
    }
    private void RenderResults()
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
        bool ranked = !IsSearch && Config.ListFor(ListId).Ranked;
        Cards = AlbumCardViewModel.Build(App, _albums.Take(_limit), slug => App.Navigator.With("album", slug), _selected, ranked);
        int left = _albums.Count - Math.Min(_limit, _albums.Count);
        HasMore = left > 0;
        MoreLabel = $"Show {Math.Min(left, Config.GridPage)} more";
        MoreLeft = $"· {Formatting.Plural(left, "album")} left";
    }
    private async Task LoadGrid(string key, string query, string listId)
    {
        _load?.Cancel();
        CancellationTokenSource cts = _load = new CancellationTokenSource();
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
            if (cts.IsCancellationRequested)
            {
                return;
            }
            _ = RevealSelected();
            RenderHead("ready");
            RenderResults();
            if (_scrollAfterLoad && _selected is not null)
            {
                RequestScroll(ScrollTarget.AlbumPanel);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (KhinsiderException ex)
        {
            if (cts.IsCancellationRequested)
            {
                return;
            }
            RenderHead("error");
            IsLoading = false;
            IsError = true;
            bool offline = ex.Offline;
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
    private bool RevealSelected()
    {
        int at = _albums.Select((a, i) => (a, i)).FirstOrDefault(x => x.a.Slug == _selected, (null!, -1)).Item2;
        if (at < _limit)
        {
            return false;
        }
        _limit = (at + 1 + Config.GridPage - 1) / Config.GridPage * Config.GridPage;
        return true;
    }
    public override void Update(Route route)
    {
        _lastRoute = route;
        string query = route.Name == "search" ? (route.Param("q") ?? "").Trim() : "";
        string listId = route.Param("list") ?? Config.Lists[0].Id;
        _selected = route.Param("album");
        if (route.Name == "search" && query.Length == 0)
        {
            App.Navigator.Navigate("/", replace: true);
            return;
        }
        string nextKey = query.Length > 0 ? $"q:{query}" : $"l:{Config.ListFor(listId).Id}";
        if (nextKey != _key)
        {
            _scrollAfterLoad = _selected is not null;
            LoadGrid(nextKey, query, listId).Forget(App.Log);
            Panel.Show(_selected, scroll: false);
            return;
        }
        if (RevealSelected())
        {
            RenderResults();
        }
        foreach (AlbumCardViewModel card in Cards)
        {
            card.IsSelected = card.Slug == _selected;
        }
        Panel.Show(_selected);
    }
    [RelayCommand]
    private void SelectList(string id)
    {
        App.Navigator.Navigate("/", "list", id);
    }
    [RelayCommand]
    private void ShowMore()
    {
        _limit += Config.GridPage;
        RenderResults();
    }
    [RelayCommand]
    private void Retry()
    {
        _key = null;
        if (_lastRoute is not null)
        {
            Update(_lastRoute);
        }
    }
    [RelayCommand]
    private void BackToDiscover()
    {
        App.Navigator.Navigate("/");
    }
    [RelayCommand]
    private async Task Random()
    {
        if (IsRolling)
        {
            return;
        }
        IsRolling = true;
        try
        {
            string slug = await App.Catalog.RandomAlbumSlugAsync();
            if (slug.Length > 0)
            {
                App.Navigator.SetParam("album", slug);
            }
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