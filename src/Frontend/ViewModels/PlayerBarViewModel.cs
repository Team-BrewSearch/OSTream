using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OSTream.Controls;
using OSTream.Core;
using OSTream.Core.Models;
using OSTream.Core.Player;
using OSTream.Services;
namespace OSTream.ViewModels;
public sealed partial class PlayerBarViewModel : ViewModelBase
{
    readonly AppServices _app;
    readonly RailViewModel _rail;
    bool _seeking;
    long _shownCurrent = -1, _shownTotal = -1; 
    public PlayerBarViewModel(AppServices app, RailViewModel rail)
    {
        _app = app;
        _rail = rail;
        app.Player.Changed += RenderTrack;
        app.Player.TimeChanged += RenderTime;
        app.Settings.Changed += (_, _) => { RenderModes(); RenderTrack(); };
        app.Library.Changed += (_, _) => RenderLike();
        app.Downloads.Changed += () => QueueCount = app.Downloads.ActiveCount;
        rail.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RailViewModel.IsVisible)) OnPropertyChanged(nameof(IsQueueOn));
        };
        RenderTrack();
        RenderModes();
        RenderTime(app.Player.Time);
    }
    [ObservableProperty] public partial bool HasTrack { get; private set; }
    [ObservableProperty] public partial string Title { get; private set; } = "Not playing";
    [ObservableProperty] public partial string Sub { get; private set; } = "Pick a track to start listening";
    [ObservableProperty] public partial string Thumb { get; private set; } = "";
    [ObservableProperty] public partial string ThumbSeed { get; private set; } = "idle";
    [ObservableProperty] public partial string AlbumTip { get; private set; } = "";
    [ObservableProperty] public partial bool CanOpenAlbum { get; private set; }
    [ObservableProperty] public partial bool IsPlaying { get; private set; }
    [ObservableProperty] public partial bool IsLoading { get; private set; }
    [ObservableProperty] public partial bool CanNext { get; private set; }
    [ObservableProperty] public partial bool IsLiked { get; private set; }
    [ObservableProperty] public partial bool Shuffle { get; private set; }
    [ObservableProperty] public partial RepeatMode Repeat { get; private set; }
    [ObservableProperty] public partial string CurrentTime { get; private set; } = "0:00";
    [ObservableProperty] public partial string TotalTime { get; private set; } = "0:00";
    [ObservableProperty] public partial double SeekValue { get; private set; }
    [ObservableProperty] public partial double Buffered { get; private set; }
    [ObservableProperty] public partial double Volume { get; private set; }
    [ObservableProperty] public partial int QueueCount { get; private set; }
    public double Duration { get; private set; }
    public IconKind PlayIcon => IsPlaying ? IconKind.Pause : IconKind.Play;
    public string PlayLabel => IsPlaying ? "Pause" : "Play";
    public bool RepeatOn => Repeat != RepeatMode.Off;
    public IconKind RepeatIcon => Repeat == RepeatMode.One ? IconKind.RepeatOne : IconKind.Repeat;
    public string RepeatTip => Repeat switch { RepeatMode.All => "Repeat all", RepeatMode.One => "Repeat one", _ => "Repeat off" };
    public IconKind VolumeIcon => Volume == 0 ? IconKind.VolumeMute : Volume < 0.5 ? IconKind.VolumeLow : IconKind.Volume;
    public string MuteLabel => Volume == 0 ? "Unmute" : "Mute";
    public string LikeLabel => IsLiked ? "Remove from favorites" : "Add to favorites";
    public bool IsQueueOn => _rail.IsVisible;
    partial void OnIsPlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(PlayIcon));
        OnPropertyChanged(nameof(PlayLabel));
    }
    partial void OnRepeatChanged(RepeatMode value)
    {
        OnPropertyChanged(nameof(RepeatOn));
        OnPropertyChanged(nameof(RepeatIcon));
        OnPropertyChanged(nameof(RepeatTip));
    }
    partial void OnVolumeChanged(double value)
    {
        OnPropertyChanged(nameof(VolumeIcon));
        OnPropertyChanged(nameof(MuteLabel));
    }
    partial void OnIsLikedChanged(bool value) => OnPropertyChanged(nameof(LikeLabel));
    void RenderTrack()
    {
        var player = _app.Player;
        var track = player.Current;
        HasTrack = track is not null;
        Title = track?.Title ?? "Not playing";
        Thumb = track?.Album.Thumb ?? "";
        ThumbSeed = track?.Album.Slug is { Length: > 0 } slug ? slug : "idle";
        AlbumTip = track is null ? "" : $"Go to {(track.Album.Title.Length > 0 ? track.Album.Title : "album")}";
        CanOpenAlbum = track?.Album.Slug.Length > 0;
        Sub = track is null
            ? "Pick a track to start listening"
            : $"{(track.Album.Title.Length > 0 ? track.Album.Title : "KHInsider")} · {player.Queue.Position + 1} / {player.Queue.Count}";
        IsPlaying = player.Status == PlayStatus.Playing;
        IsLoading = player.Status == PlayStatus.Loading;
        CanNext = HasTrack && (player.Queue.HasNext || _app.Settings.Value.Repeat == RepeatMode.All);
        RenderLike();
        if (HasTrack) return;
        TotalTime = "0:00";
        _shownTotal = 0;
    }
    void RenderLike() => IsLiked = _app.Player.Current is { } t && _app.Library.IsFavTrack(t.Id);
    void RenderModes()
    {
        var s = _app.Settings.Value;
        Shuffle = s.Shuffle;
        Repeat = s.Repeat;
        Volume = s.Muted ? 0 : s.Volume;
    }
    void RenderTime(PlaybackTime time)
    {
        Duration = time.Duration;
        if (!_seeking)
        {
            if ((long)time.Current != _shownCurrent) CurrentTime = Formatting.Time(_shownCurrent = (long)time.Current);
            SeekValue = time.Duration > 0 ? time.Current / time.Duration : 0;
        }
        if ((long)time.Duration != _shownTotal) TotalTime = Formatting.Time(_shownTotal = (long)time.Duration);
        Buffered = time.Duration > 0 ? Math.Min(1, time.Buffered / time.Duration) : 0;
    }
    public string TimeAt(double ratio) => Formatting.Time(ratio * Duration);
    public void SeekInput(double ratio)
    {
        _seeking = true;
        _shownCurrent = -1;
        CurrentTime = Formatting.Time(ratio * Duration);
    }
    public void SeekCommit(double ratio)
    {
        _seeking = false;
        _app.Player.Seek(ratio * Duration);
    }
    public void SetVolume(double ratio) => _app.Player.SetVolume(ratio);
    public void PreviewVolume(double ratio)
    {
        Volume = Math.Clamp(ratio, 0, 1);
        _app.Player.PreviewVolume(ratio);
    }
    public void NudgeVolume(double delta)
    {
        var s = _app.Settings.Value;
        _app.Player.SetVolume((s.Muted ? 0 : s.Volume) + delta);
    }
    [RelayCommand] void Toggle() => _app.Player.Toggle();
    [RelayCommand] void Prev() => _app.Player.Prev();
    [RelayCommand] void Next() => _app.Player.Next();
    [RelayCommand] void ToggleShuffle() => _app.Player.ToggleShuffle();
    [RelayCommand] void CycleRepeat() => _app.Player.CycleRepeat();
    [RelayCommand] void ToggleMute() => _app.Player.ToggleMute();
    [RelayCommand] void ToggleQueue() => _rail.Toggle();
    [RelayCommand]
    void Like()
    {
        if (_app.Player.Current is { } t) _app.Actions.ToggleFavorite(t);
    }
    [RelayCommand]
    void OpenAlbum()
    {
        if (_app.Player.Current?.Album.Slug is { Length: > 0 } slug) _app.Navigator.Navigate(UserActions.AlbumRoute(slug));
    }
}