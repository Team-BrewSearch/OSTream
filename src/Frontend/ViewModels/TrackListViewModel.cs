using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OSTream.Core.Models;
using OSTream.Core.Player;
using OSTream.Frontend;
using OSTream.Frontend.Services;
using OSTream.Frontend.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
namespace OSTream.ViewModels;

public sealed record TrackListOptions
{
    public bool HasCd { get; init; }
    public bool ShowAlbum { get; init; }
    public bool AlbumNumbering { get; init; }
    public string MetaLabel { get; init; } = "Format";
    public Func<Track, int, string>? Meta { get; init; }
    public Func<Track, int, TrackMenuContext>? Menu { get; init; }
    public string Empty { get; init; } = "No tracks.";
    public required Action<int> OnPlay { get; init; }
}
public sealed partial class TrackListViewModel : ViewModelBase, IDisposable
{
    private readonly AppServices _app;
    private readonly Dictionary<string, List<TrackRowViewModel>> _byId = [];
    private List<TrackRowViewModel> _current = [];
    public TrackListViewModel(AppServices app, IReadOnlyList<Track> tracks, TrackListOptions options)
    {
        _app = app;
        Options = options;
        Rows = tracks.Select((t, i) => new TrackRowViewModel(app, this, t, i)).ToArray();
        foreach (TrackRowViewModel row in Rows)
        {
            if (!_byId.TryGetValue(row.Track.Id, out List<TrackRowViewModel>? list))
            {
                _byId[row.Track.Id] = list = [];
            }
            list.Add(row);
        }
        app.Player.Changed += SyncPlaying;
        app.Player.TimeChanged += SyncProgress;
        app.Library.Changed += SyncFavorites;
        app.Settings.Changed += SyncFormat;
        SyncPlaying();
    }
    public TrackListOptions Options { get; }
    public IReadOnlyList<TrackRowViewModel> Rows { get; }
    public bool HasCd => Options.HasCd;
    public bool ShowAlbum => Options.ShowAlbum;
    public string MetaLabel => Options.MetaLabel.ToUpperInvariant();
    public bool IsEmpty => Rows.Count == 0;
    public string EmptyText => Options.Empty;
    public int Filter(string query)
    {
        string q = query.Trim();
        int visible = 0;
        foreach (TrackRowViewModel row in Rows)
        {
            bool match = q.Length == 0
                || row.Track.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || row.Track.Album.Title.Contains(q, StringComparison.OrdinalIgnoreCase);
            row.IsVisible = match;
            if (match)
            {
                visible++;
            }
        }
        return visible;
    }
    internal void Activate(TrackRowViewModel row)
    {
        if (row.IsPlaying)
        {
            _app.Player.Toggle();
        }
        else
        {
            Options.OnPlay(row.Index);
        }
    }
    internal void OpenMenu(TrackRowViewModel row, object anchor)
    {
        _app.Actions.OpenTrackMenu(anchor, row.Track, Options.Menu?.Invoke(row.Track, row.Index) ?? new TrackMenuContext());
    }
    private void SyncPlaying()
    {
        Track? current = _app.Player.Current;
        PlayStatus status = _app.Player.Status;
        List<TrackRowViewModel> rows = current is not null && _byId.TryGetValue(current.Id, out List<TrackRowViewModel>? list) ? list : [];
        foreach (TrackRowViewModel row in _current)
        {
            if (!rows.Contains(row))
            {
                row.SetPlaying(false, status);
            }
        }
        foreach (TrackRowViewModel row in rows)
        {
            row.SetPlaying(true, status);
        }
        _current = rows;
    }
    private void SyncProgress(PlaybackTime time)
    {
        foreach (TrackRowViewModel row in _current)
        {
            row.Progress = time.Duration > 0 ? time.Current / time.Duration : 0;
        }
    }
    private void SyncFavorites(LibraryData old, LibraryData now)
    {
        if (ReferenceEquals(old.FavTracks, now.FavTracks))
        {
            return;
        }
        foreach (TrackRowViewModel row in Rows)
        {
            row.IsFav = _app.Library.IsFavTrack(row.Track.Id);
        }
    }
    private void SyncFormat(SettingsData old, SettingsData now)
    {
        if (old.Format == now.Format || Options.Meta is not null)
        {
            return;
        }
        foreach (TrackRowViewModel row in Rows)
        {
            row.UpdateFormat();
        }
    }
    public void RefreshMeta()
    {
        foreach (TrackRowViewModel row in Rows)
        {
            row.UpdateFormat();
        }
    }
    public void Dispose()
    {
        _app.Player.Changed -= SyncPlaying;
        _app.Player.TimeChanged -= SyncProgress;
        _app.Library.Changed -= SyncFavorites;
        _app.Settings.Changed -= SyncFormat;
    }
}
public sealed partial class TrackRowViewModel : ViewModelBase
{
    private readonly AppServices _app;
    private readonly TrackListViewModel _list;
    internal TrackRowViewModel(AppServices app, TrackListViewModel list, Track track, int index)
    {
        _app = app;
        _list = list;
        Track = track;
        Index = index;
        string number = list.Options.AlbumNumbering && track.Number.Length > 0 ? track.Number : (index + 1).ToString(CultureInfo.InvariantCulture);
        Number = number.PadLeft(2, '0');
        IsFav = app.Library.IsFavTrack(track.Id);
        UpdateFormat();
    }
    public Track Track { get; }
    public int Index { get; }
    public string Number { get; }
    public string Title => Track.Title;
    public string Cd => Track.Cd;
    public bool HasCd => _list.HasCd;
    public bool ShowAlbum => _list.ShowAlbum && Track.Album.Title.Length > 0;
    public string AlbumTitle => Track.Album.Title;
    public string Duration => Track.Duration.Length > 0 ? Track.Duration : "—";
    public string DownloadLabel => $"Download {Track.Title}";
    [ObservableProperty] public partial bool IsVisible { get; set; } = true;
    [ObservableProperty] public partial bool IsPlaying { get; private set; }
    [ObservableProperty] public partial bool IsPaused { get; private set; }
    [ObservableProperty] public partial bool IsLoading { get; private set; }
    [ObservableProperty] public partial double Progress { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavLabel))]
    public partial bool IsFav { get; set; }
    public string FavLabel => IsFav ? "Remove from favorites" : "Add to favorites";
    [ObservableProperty] public partial string FormatMain { get; private set; } = "";
    [ObservableProperty] public partial string FormatSize { get; private set; } = "";
    [ObservableProperty] public partial bool IsFallback { get; private set; }
    [ObservableProperty] public partial string? FallbackTip { get; private set; }
    internal void UpdateFormat()
    {
        if (_list.Options.Meta is { } meta)
        {
            FormatMain = meta(Track, Index);
            FormatSize = "";
            IsFallback = false;
            return;
        }
        AudioFormat fmt = _app.Settings.Value.Format;
        foreach (AudioFormat format in fmt.Preference())
        {
            if (!Track.Sizes.TryGetValue(format.Key(), out string? size))
            {
                continue;
            }
            (FormatMain, FormatSize, IsFallback, FallbackTip) = format == fmt
                ? (fmt.Label(), $" · {size}", false, null)
                : (format.Label(), $" · {size}", true, $"No {fmt.Label()} for this track");
            return;
        }
        (FormatMain, FormatSize, IsFallback, FallbackTip) = (fmt.Label(), "", false, null);
    }
    internal void SetPlaying(bool current, PlayStatus status)
    {
        IsPlaying = current;
        IsPaused = current && status == PlayStatus.Paused;
        IsLoading = current && status == PlayStatus.Loading;
        if (!current)
        {
            Progress = 0;
        }
    }
    [RelayCommand]
    private void Activate()
    {
        _list.Activate(this);
    }
    [RelayCommand]
    private void ToggleFavorite()
    {
        _app.Actions.ToggleFavorite(Track);
    }
    [RelayCommand]
    private void Download()
    {
        _app.Downloads.DownloadTrack(Track);
    }
    [RelayCommand]
    private void More(object anchor)
    {
        _list.OpenMenu(this, anchor);
    }
    [RelayCommand]
    private void OpenAlbum()
    {
        _app.Navigator.Navigate(UserActions.AlbumRoute(Track.Album.Slug));
    }
}