using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OSTream.Core;
using OSTream.Core.Khinsider;
using OSTream.Core.Models;
using OSTream.Frontend;
using OSTream.Frontend.Controls;
using OSTream.Frontend.Services;
using OSTream.Frontend.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace OSTream.ViewModels;

public sealed record TagViewModel(string Text, string Kind = "", string? Tip = null);
public sealed record TextPart(string Text, bool Bold = false, bool Separator = false);
public sealed partial class AlbumPanelViewModel : ViewModelBase, IDisposable
{
    private readonly AppServices _app;
    private readonly Func<string, string> _hrefFor;
    private readonly Action<ScrollTarget, bool> _scroll;
    private CancellationTokenSource? _load;
    public AlbumPanelViewModel(AppServices app, bool inline, Func<string, string> hrefFor, Action<ScrollTarget, bool> scroll)
    {
        _app = app;
        Inline = inline;
        _hrefFor = hrefFor;
        _scroll = scroll;
        app.Library.Changed += OnLibrary;
        app.Settings.Changed += OnSettings;
    }
    public bool Inline { get; }
    public string? Slug { get; private set; }
    [ObservableProperty] public partial bool IsVisible { get; private set; }
    [ObservableProperty] public partial bool IsLoading { get; private set; }
    [ObservableProperty] public partial bool IsReady { get; private set; }
    [ObservableProperty] public partial bool IsError { get; private set; }
    [ObservableProperty] public partial string ErrorText { get; private set; } = "";
    [ObservableProperty] public partial IconKind ErrorIcon { get; private set; } = IconKind.Alert;
    [ObservableProperty] public partial Album? Album { get; private set; }
    [ObservableProperty] public partial string Eyebrow { get; private set; } = "";
    [ObservableProperty] public partial IReadOnlyList<TagViewModel> Tags { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<TextPart> Meta { get; private set; } = [];
    [ObservableProperty] public partial string DownloadFormat { get; private set; } = "";
    [ObservableProperty] public partial bool IsFav { get; private set; }
    [ObservableProperty] public partial TrackListViewModel? Tracks { get; private set; }
    [ObservableProperty] public partial IReadOnlyList<AlbumCardViewModel> Related { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<double> SkeletonWidths { get; private set; } = [];
    [ObservableProperty] public partial string FilterText { get; set; } = "";
    [ObservableProperty] public partial string FilterCount { get; private set; } = "";
    public string Title => Album?.Title ?? "";
    public string AltTitles => Album is null ? "" : string.Join(" · ", Album.AltTitles);
    public bool HasAltTitles => Album?.AltTitles.Count > 0;
    public string Cover => Album?.Cover ?? "";
    public bool Zoomable => Album?.Images.Count > 0;
    public string GalleryLabel => Album is null ? "" : $"View artwork ({Formatting.Plural(Album.Images.Count, "image")})";
    public bool ShowFilter => Album?.Tracks.Count > Config.FilterMinTracks;
    public string FilterPlaceholder => Album is null ? "" : $"Filter {Album.Tracks.Count} tracks…";
    public bool HasRelated => Related.Count > 0;
    public string FavLabel => IsFav ? "Favorited" : "Add to favorites";
    partial void OnIsFavChanged(bool value)
    {
        OnPropertyChanged(nameof(FavLabel));
    }
    partial void OnRelatedChanged(IReadOnlyList<AlbumCardViewModel> value)
    {
        OnPropertyChanged(nameof(HasRelated));
    }
    partial void OnFilterTextChanged(string value)
    {
        if (Tracks is null || Album is null)
        {
            return;
        }
        int visible = Tracks.Filter(value);
        FilterCount = value.Length > 0 ? $"{visible} of {Album.Tracks.Count}" : "";
    }
    partial void OnAlbumChanged(Album? value)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(AltTitles));
        OnPropertyChanged(nameof(HasAltTitles));
        OnPropertyChanged(nameof(Cover));
        OnPropertyChanged(nameof(Zoomable));
        OnPropertyChanged(nameof(GalleryLabel));
        OnPropertyChanged(nameof(ShowFilter));
        OnPropertyChanged(nameof(FilterPlaceholder));
    }
    public void Show(string? slug, bool scroll = true)
    {
        if (slug == Slug)
        {
            return;
        }
        Slug = string.IsNullOrEmpty(slug) ? null : slug;
        FilterText = "";
        _load?.Cancel();
        IsVisible = Slug is not null;
        if (Slug is null)
        {
            Clear();
            return;
        }
        Load().Forget(_app.Log);
        if (scroll)
        {
            _scroll(ScrollTarget.AlbumPanel, false);
        }
    }
    private void Clear()
    {
        Tracks?.Dispose();
        Tracks = null;
        Album = null;
        IsLoading = IsReady = IsError = false;
    }
    [RelayCommand]
    private void Retry()
    {
        Load().Forget(_app.Log);
    }
    private async Task Load()
    {
        string wanted = Slug!;
        _load?.Cancel();
        if (_app.Catalog.Cached(wanted) is { } cached)
        {
            Render(cached);
            return;
        }
        Clear();
        IsLoading = true;
        SkeletonWidths = Enumerable.Range(0, 8).Select(_ => 40 + Math.Round(Random.Shared.NextDouble() * 40)).ToArray();
        CancellationTokenSource cts = _load = new CancellationTokenSource();
        try
        {
            Album album = await _app.Catalog.GetAlbumAsync(wanted, cts.Token);
            if (wanted == Slug)
            {
                Render(album);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (KhinsiderException ex)
        {
            if (wanted != Slug)
            {
                return;
            }
            Clear();
            IsError = true;
            ErrorText = ex.Message;
            ErrorIcon = ex.Offline ? IconKind.WifiOff : IconKind.Alert;
        }
    }
    private void Render(Album album)
    {
        Tracks?.Dispose();
        Album = album;
        Tracks = new TrackListViewModel(_app, album.Tracks, new TrackListOptions
        {
            HasCd = album.HasCd,
            AlbumNumbering = true,
            OnPlay = index => _app.Player.PlayList(album.Tracks, index),
            Menu = (_, _) => new TrackMenuContext(InAlbum: true),
        });
        Related = AlbumCardViewModel.Build(_app, album.Related.Take(Config.RelatedLimit), _hrefFor, null);
        IsFav = _app.Library.IsFavAlbum(album.Slug);
        RenderHeader();
        IsLoading = IsError = false;
        IsReady = true;
        if (FilterText.Length > 0)
        {
            OnFilterTextChanged(FilterText);
        }
    }
    private void RenderHeader()
    {
        if (Album is not { } album)
        {
            return;
        }
        AudioFormat fmt = _app.Downloads.EffectiveFormat(album);
        AudioFormat wanted = _app.Settings.Value.Format;
        string length = album.TotalLength.Length > 0 ? album.TotalLength : Formatting.Length(album.TotalSec);
        _ = album.TotalSizes.TryGetValue(fmt.Key(), out string? size);
        Eyebrow = Formatting.JoinNonEmpty(" · ", album.Type.Length > 0 ? album.Type : "Album", album.DateAdded.Length > 0 ? $"Added {album.DateAdded}" : null).ToUpperInvariant();
        List<TagViewModel> tags = new(album.Platforms.Count + 6);
        foreach (string p in album.Platforms)
        {
            tags.Add(new(p, "accent"));
        }
        if (album.Year.Length > 0)
        {
            tags.Add(new(album.Year));
        }
        tags.Add(new(Formatting.Plural(album.Tracks.Count, "track")));
        if (length.Length > 0)
        {
            tags.Add(new(length));
        }
        if (!string.IsNullOrEmpty(size))
        {
            tags.Add(new(size));
        }
        if (album.Formats.Count > 0)
        {
            tags.Add(new(string.Join(" · ", album.Formats.Select(f => f.ToUpperInvariant()))));
        }
        if (fmt != wanted)
        {
            tags.Add(new($"No {wanted.Label()}", "warn", $"This album has no {wanted.Label()} files, so it uses {fmt.Label()}"));
        }
        Tags = tags;
        List<TextPart> meta = [];
        void Add(string label, string value)
        {
            if (meta.Count > 0)
            {
                meta.Add(new("·", Separator: true));
            }
            meta.Add(new(label + " "));
            meta.Add(new(value, Bold: true));
        }
        if (album.Developers.Count > 0)
        {
            Add("Developed by", string.Join(", ", album.Developers));
        }
        if (album.Publishers.Count > 0)
        {
            Add("Published by", string.Join(", ", album.Publishers));
        }
        if (album.Catalog.Length > 0)
        {
            Add("Catalog", album.Catalog);
        }
        Meta = meta;
        DownloadFormat = $"· {fmt.Label()}";
    }
    private void OnLibrary(LibraryData old, LibraryData now)
    {
        if (Album is not null && !ReferenceEquals(old.FavAlbums, now.FavAlbums))
        {
            IsFav = _app.Library.IsFavAlbum(Album.Slug);
        }
    }
    private void OnSettings(SettingsData old, SettingsData now)
    {
        if (old.Format != now.Format)
        {
            RenderHeader();
        }
    }
    [RelayCommand]
    private void Top()
    {
        _scroll(ScrollTarget.Top, false);
    }
    [RelayCommand]
    private void Play()
    {
        if (Album is { } a)
        {
            _app.Player.PlayList(a.Tracks);
        }
    }
    [RelayCommand]
    private void Shuffle()
    {
        if (Album is { } a)
        {
            _app.Player.PlayList(a.Tracks, 0, true);
        }
    }
    [RelayCommand]
    private void Download()
    {
        if (Album is { } a)
        {
            _app.Downloads.DownloadAlbum(a);
        }
    }
    [RelayCommand]
    private void Gallery()
    {
        if (Album is { Images.Count: > 0 } a)
        {
            _app.Lightbox.Open(a.Images, 0, a.Title);
        }
    }
    [RelayCommand]
    private void ToggleFavorite()
    {
        if (Album is { } a)
        {
            _app.Actions.ToggleFavorite(a);
        }
    }
    [RelayCommand]
    private void More(object anchor)
    {
        if (Album is { } a)
        {
            _app.Actions.OpenAlbumMenu(anchor, a);
        }
    }
    public void Dispose()
    {
        _load?.Cancel();
        Tracks?.Dispose();
        _app.Library.Changed -= OnLibrary;
        _app.Settings.Changed -= OnSettings;
    }
}