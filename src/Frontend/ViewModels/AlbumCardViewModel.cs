using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OSTream.Core;
using OSTream.Core.Models;
using OSTream.Frontend;
using OSTream.Frontend.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
namespace OSTream.ViewModels;

public sealed partial class AlbumCardViewModel : ViewModelBase
{
    private readonly AppServices _app;
    public AlbumCardViewModel(AppServices app, AlbumSummary album, string href, int index, bool ranked, bool selected)
    {
        _app = app;
        Album = album;
        Href = href;
        Subtitle = Formatting.AlbumSubtitle(album.Platforms, album.Year, album.Type);
        Rank = ranked && album.Rank is { } rank ? $"#{rank}" : null;
        EntranceDelay = Math.Min(index % Config.GridPage, 14) * 16;
        IsSelected = selected;
    }
    public AlbumSummary Album { get; }
    public string Slug => Album.Slug;
    public string Title => Album.Title;
    public string Thumb => Album.Thumb;
    public string Subtitle { get; }
    public string? Rank { get; }
    public bool HasRank => Rank is not null;
    public string Href { get; }
    public int EntranceDelay { get; }
    [ObservableProperty] public partial bool IsSelected { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [RelayCommand]
    private void Open()
    {
        _app.Navigator.Open(Href);
    }

    [RelayCommand]
    private async Task Play()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _app.Actions.PlayAlbumAsync(Slug);
        }
        finally
        {
            IsBusy = false;
        }
    }
    public static List<AlbumCardViewModel> Build(AppServices app, IEnumerable<AlbumSummary> albums, Func<string, string> hrefFor, string? selected, bool ranked = false)
    {
        return albums.Select((a, i) => new AlbumCardViewModel(app, a, hrefFor(a.Slug), i, ranked, a.Slug == selected)).ToList();
    }
}
public sealed partial class PlaylistCardViewModel(AppServices app, Playlist playlist, int index) : ViewModelBase
{
    public string Id => playlist.Id;
    public string Name => playlist.Name;
    public string Subtitle { get; } = Formatting.JoinNonEmpty(" · ", Formatting.Plural(playlist.Tracks.Count, "track"),
        Formatting.Length(playlist.Tracks.Sum(t => t.DurationSec)));
    public MosaicViewModel Mosaic { get; } = new(playlist.Tracks, playlist.Id);
    public bool HasTracks => playlist.Tracks.Count > 0;
    public int EntranceDelay { get; } = Math.Min(index, 14) * 16;
    [RelayCommand]
    private void Open()
    {
        app.Navigator.Navigate($"/playlists/{playlist.Id}");
    }

    [RelayCommand]
    private void Play()
    {
        app.Player.PlayList(playlist.Tracks);
    }
}
public sealed class MosaicViewModel(IReadOnlyList<Track> tracks, string seed)
{
    public IReadOnlyList<string> Covers { get; } = tracks.Select(t => t.Album.Thumb).Where(t => t.Length > 0).Distinct().Take(4).ToArray();
    public string Seed { get; } = seed;
    public bool IsGrid => Covers.Count >= 4;
    public string FirstCover => Covers.Count > 0 ? Covers[0] : "";
    public string Seed0 => Seed + "0";
    public string Seed1 => Seed + "1";
    public string Seed2 => Seed + "2";
    public string Seed3 => Seed + "3";
    public string Cover0 => Covers.ElementAtOrDefault(0) ?? "";
    public string Cover1 => Covers.ElementAtOrDefault(1) ?? "";
    public string Cover2 => Covers.ElementAtOrDefault(2) ?? "";
    public string Cover3 => Covers.ElementAtOrDefault(3) ?? "";
}