using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OSTream.Core;
using OSTream.Core.Models;
using OSTream.Frontend;
using OSTream.Frontend.Navigation;
using OSTream.Frontend.Services;
using OSTream.Frontend.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
namespace OSTream.ViewModels;

public sealed partial class PlaylistsViewModel : PageViewModel
{
    private string? _id;
    private string _signature = "";
    public PlaylistsViewModel(AppServices app) : base(app)
    {
        app.Library.Changed += OnLibrary;
    }

    [ObservableProperty] public partial bool IsOverview { get; private set; } = true;
    [ObservableProperty] public partial bool IsDetail { get; private set; }
    [ObservableProperty] public partial bool IsNotFound { get; private set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GridItems))]
    public partial IReadOnlyList<PlaylistCardViewModel> Cards { get; private set; } = [];
    public IReadOnlyList<object> GridItems => [.. Cards, NewPlaylistTile.Instance];
    [ObservableProperty] public partial Playlist? Playlist { get; private set; }
    [ObservableProperty] public partial MosaicViewModel? Mosaic { get; private set; }
    [ObservableProperty] public partial IReadOnlyList<string> Tags { get; private set; } = [];
    [ObservableProperty] public partial string Created { get; private set; } = "";
    [ObservableProperty] public partial TrackListViewModel? Tracks { get; private set; }
    public bool HasPlaylists => Cards.Count > 0;
    public bool NoPlaylists => IsOverview && Cards.Count == 0;
    public bool HasTracks => Playlist?.Tracks.Count > 0;
    partial void OnCardsChanged(IReadOnlyList<PlaylistCardViewModel> value)
    {
        OnPropertyChanged(nameof(HasPlaylists));
        OnPropertyChanged(nameof(NoPlaylists));
    }
    partial void OnPlaylistChanged(Playlist? value)
    {
        OnPropertyChanged(nameof(HasTracks));
    }

    partial void OnIsOverviewChanged(bool value)
    {
        OnPropertyChanged(nameof(NoPlaylists));
    }

    private void OnLibrary(LibraryData old, LibraryData now)
    {
        if (!ReferenceEquals(old.Playlists, now.Playlists))
        {
            Render();
        }
    }
    private void Render()
    {
        Playlist? playlist = _id is null ? null : App.Library.GetPlaylist(_id);
        IReadOnlyList<Playlist> playlists = App.Library.Value.Playlists;
        string signature = _id is not null
            ? $"{playlist?.Id}|{playlist?.Name}|{playlist?.UpdatedAt}|{string.Join(",", playlist?.Tracks.Select(t => t.Id) ?? [])}"
            : string.Join(";", playlists.Select(p => $"{p.Id}|{p.Name}|{p.Tracks.Count}|{string.Join(",", p.Tracks.Take(4).Select(t => t.Album.Thumb))}"));
        if (signature == _signature)
        {
            return;
        }

        _signature = signature;
        IsOverview = _id is null;
        IsDetail = playlist is not null;
        IsNotFound = _id is not null && playlist is null;
        Tracks?.Dispose();
        Tracks = null;
        if (IsOverview)
        {
            Cards = playlists.Select((p, i) => new PlaylistCardViewModel(App, p, i)).ToArray();
            return;
        }
        Playlist = playlist;
        if (playlist is null)
        {
            return;
        }

        IReadOnlyList<Track> tracks = playlist.Tracks;
        Mosaic = new MosaicViewModel(tracks, playlist.Id);
        List<string> tags = [Formatting.Plural(tracks.Count, "track")];
        if (tracks.Count > 0)
        {
            tags.Add(Formatting.Length(tracks.Sum(t => t.DurationSec)));
        }

        tags.Add(Formatting.Plural(tracks.Select(t => t.Album.Slug).Distinct().Count(), "album"));
        Tags = tags;
        Created = $"Created {Formatting.ShortDate(playlist.CreatedAt)}";
        string id = playlist.Id;
        Tracks = new TrackListViewModel(App, tracks, new TrackListOptions
        {
            ShowAlbum = true,
            Empty = "This playlist is empty. Add tracks from any album with the ⋯ menu.",
            OnPlay = index => App.Player.PlayList(App.Library.GetPlaylist(id)?.Tracks ?? [], index),
            Menu = (_, index) => new TrackMenuContext(PlaylistId: id, Index: index, Count: tracks.Count),
        });
    }
    public override void Update(Route route)
    {
        string? next = route.Segment(1);
        if (next != _id)
        {
            _id = next;
            _signature = "";
            RequestScroll(ScrollTarget.Top, instant: true);
        }
        Render();
    }
    [RelayCommand]
    private async System.Threading.Tasks.Task Create()
    {
        string? name = await App.Dialogs.PromptAsync("New playlist", placeholder: "My playlist", confirm: "Create");
        if (name is null)
        {
            return;
        }

        Playlist playlist = App.Library.CreatePlaylist(name);
        App.Navigator.Navigate($"/playlists/{playlist.Id}");
    }
    [RelayCommand]
    private void All()
    {
        App.Navigator.Navigate("/playlists");
    }

    [RelayCommand]
    private void Play()
    {
        if (Playlist is { } p)
        {
            App.Player.PlayList(p.Tracks);
        }
    }
    [RelayCommand]
    private void Shuffle()
    {
        if (Playlist is { } p)
        {
            App.Player.PlayList(p.Tracks, 0, true);
        }
    }
    [RelayCommand]
    private void Download()
    {
        if (Playlist is { } p)
        {
            App.Downloads.DownloadPlaylist(p);
        }
    }
    [RelayCommand]
    private async System.Threading.Tasks.Task Rename()
    {
        if (Playlist is not { } p)
        {
            return;
        }

        string? name = await App.Dialogs.PromptAsync("Rename playlist", p.Name, confirm: "Rename");
        if (name is not null)
        {
            App.Library.RenamePlaylist(p.Id, name);
        }
    }
    [RelayCommand]
    private async System.Threading.Tasks.Task Delete()
    {
        if (Playlist is not { } p)
        {
            return;
        }

        bool ok = await App.Dialogs.ConfirmAsync($"Delete “{p.Name}”?",
            "The playlist is removed. Downloaded files and favorites are not affected.", "Delete playlist", danger: true);
        if (!ok)
        {
            return;
        }

        App.Library.DeletePlaylist(p.Id);
        App.Toasts.Toast($"Deleted “{p.Name}”");
        App.Navigator.Navigate("/playlists");
    }
    public override void Dispose()
    {
        App.Library.Changed -= OnLibrary;
        Tracks?.Dispose();
    }
}
public sealed class NewPlaylistTile
{
    public static readonly NewPlaylistTile Instance = new();
}