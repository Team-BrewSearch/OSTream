using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OSTream.Core.Models;
using OSTream.Navigation;
using OSTream.Services;
namespace OSTream.ViewModels;
public sealed partial class FavoritesViewModel : PageViewModel
{
    string _signature = "";
    string? _selected;
    public FavoritesViewModel(AppServices app) : base(app)
    {
        Panel = new AlbumPanelViewModel(app, inline: true, UserActions.AlbumRoute, RequestScroll);
        app.Library.Changed += OnLibrary;
    }
    public AlbumPanelViewModel Panel { get; }
    [ObservableProperty] public partial bool IsTracksTab { get; private set; }
    [ObservableProperty] public partial int AlbumCount { get; private set; }
    [ObservableProperty] public partial int TrackCount { get; private set; }
    [ObservableProperty] public partial IReadOnlyList<AlbumCardViewModel> Cards { get; private set; } = [];
    [ObservableProperty] public partial TrackListViewModel? Tracks { get; private set; }
    public bool IsAlbumsTab => !IsTracksTab;
    public bool ShowActions => IsTracksTab && TrackCount > 0;
    public bool NoAlbums => IsAlbumsTab && AlbumCount == 0;
    public bool NoTracks => IsTracksTab && TrackCount == 0;
    public bool ShowGrid => IsAlbumsTab && AlbumCount > 0;
    public bool ShowTracks => IsTracksTab && TrackCount > 0;
    void Notify()
    {
        foreach (var name in new[] { nameof(IsAlbumsTab), nameof(ShowActions), nameof(NoAlbums), nameof(NoTracks), nameof(ShowGrid), nameof(ShowTracks) })
            OnPropertyChanged(name);
    }
    void OnLibrary(LibraryData old, LibraryData now) => Render();
    void Render()
    {
        var lib = App.Library.Value;
        AlbumCount = lib.FavAlbums.Count;
        TrackCount = lib.FavTracks.Count;
        var signature = $"{IsTracksTab}|{string.Join(",", lib.FavAlbums.Select(a => a.Slug))}|{string.Join(",", lib.FavTracks.Select(t => t.Id))}";
        Notify();
        if (signature == _signature) return;
        _signature = signature;
        if (IsTracksTab)
        {
            Cards = [];
            Tracks?.Dispose();
            Tracks = new TrackListViewModel(App, lib.FavTracks, new TrackListOptions
            {
                ShowAlbum = true,
                OnPlay = index => App.Player.PlayList(App.Library.Value.FavTracks, index),
            });
        }
        else
        {
            Tracks?.Dispose();
            Tracks = null;
            Cards = AlbumCardViewModel.Build(App, lib.FavAlbums, slug => Route.Build("/favorites", [new("tab", "albums"), new("album", slug)]), _selected);
        }
    }
    public override void Update(Route route)
    {
        IsTracksTab = route.Param("tab") == "tracks";
        _selected = IsTracksTab ? null : route.Param("album");
        Render();
        foreach (var card in Cards) card.IsSelected = card.Slug == _selected;
        Panel.Show(_selected);
    }
    [RelayCommand] void ShowTab(string tab) => App.Navigator.Navigate("/favorites", "tab", tab);
    [RelayCommand] void PlayAll() => App.Player.PlayList(App.Library.Value.FavTracks);
    [RelayCommand] void Shuffle() => App.Player.PlayList(App.Library.Value.FavTracks, 0, true);
    [RelayCommand] void Browse() => App.Navigator.Navigate("/");
    public override void Dispose()
    {
        App.Library.Changed -= OnLibrary;
        Tracks?.Dispose();
        Panel.Dispose();
    }
}