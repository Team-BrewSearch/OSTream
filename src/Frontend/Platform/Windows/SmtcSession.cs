using Avalonia.Threading;
using OSTream.Core.Player;
using System;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage.Streams;
namespace OSTream.Frontend.Platform.Windows;

internal sealed class SmtcSession : IMediaSession
{
    private readonly AppServices _app;
    private readonly MediaPlayer _host;
    private readonly SystemMediaTransportControls _smtc;
    private object? _shownTrack;
    private double _sentPosition = double.NaN;
    private double _sentDuration;
    private long _sentAt;
    private bool _sentPlaying;
    private SmtcSession(AppServices app)
    {
        _app = app;
        _host = new MediaPlayer();
        _host.CommandManager.IsEnabled = false;
        _smtc = _host.SystemMediaTransportControls;
        _smtc.IsEnabled = true;
        _smtc.IsPlayEnabled = _smtc.IsPauseEnabled = _smtc.IsNextEnabled = _smtc.IsPreviousEnabled = _smtc.IsStopEnabled = true;
        _smtc.ButtonPressed += OnButton;
        _smtc.PlaybackPositionChangeRequested += (_, e) => Dispatcher.UIThread.Post(() => _app.Player.Seek(e.RequestedPlaybackPosition.TotalSeconds));
        app.Player.Changed += Update;
        app.Player.TimeChanged += OnTime;
        Update();
    }
    public static SmtcSession? TryCreate(AppServices app)
    {
        try
        {
            return new SmtcSession(app);
        }
        catch (Exception)
        {
            return null;
        }
    }
    private void OnButton(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
    {
        PlayerService player = _app.Player;
        switch (e.Button)
        {
            case SystemMediaTransportControlsButton.Play when player.Status != PlayStatus.Playing: player.Toggle(); break;
            case SystemMediaTransportControlsButton.Pause when player.Status == PlayStatus.Playing: player.Toggle(); break;
            case SystemMediaTransportControlsButton.Stop:
                if (player.Status == PlayStatus.Playing)
                {
                    player.Toggle();
                }
                player.Seek(0);
                break;
            case SystemMediaTransportControlsButton.Next: player.Next(); break;
            case SystemMediaTransportControlsButton.Previous: player.Prev(); break;
        }
    });
    }
    private void Update()
    {
        NowPlaying now = MediaSessions.Snapshot(_app);
        _smtc.PlaybackStatus = now.Track is null ? MediaPlaybackStatus.Stopped : now.IsPlaying ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;
        _smtc.IsNextEnabled = now.CanGoNext;
        _smtc.IsPreviousEnabled = now.CanGoPrevious;
        OnTime(_app.Player.Time);
        if (ReferenceEquals(_shownTrack, now.Track))
        {
            return;
        }
        _shownTrack = now.Track;
        SystemMediaTransportControlsDisplayUpdater updater = _smtc.DisplayUpdater;
        updater.ClearAll();
        if (now.Track is { } track)
        {
            updater.Type = MediaPlaybackType.Music;
            updater.MusicProperties.Title = track.Title;
            updater.MusicProperties.AlbumTitle = track.Album.Title;
            updater.MusicProperties.Artist = track.Album.Title.Length > 0 ? track.Album.Title : "KHInsider";
            if (Core.Khinsider.KhinsiderClient.SecureMediaUrl(track.Album.Thumb) is { } art)
            {
                updater.Thumbnail = RandomAccessStreamReference.CreateFromUri(new Uri(art));
            }
        }
        updater.Update();
    }
    private void OnTime(PlaybackTime time)
    {
        if (time.Duration <= 0)
        {
            return;
        }
        bool playing = _app.Player.Status == PlayStatus.Playing;
        long at = Environment.TickCount64;
        double elapsed = (at - _sentAt) / 1000.0;
        double expected = _sentPosition + (playing && _sentPlaying ? elapsed : 0);
        if (playing == _sentPlaying && time.Duration == _sentDuration && Math.Abs(time.Current - expected) < 1.5 && elapsed < 5)
        {
            return;
        }
        _sentPosition = time.Current;
        _sentDuration = time.Duration;
        _sentAt = at;
        _sentPlaying = playing;
        _smtc.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
        {
            StartTime = TimeSpan.Zero,
            MinSeekTime = TimeSpan.Zero,
            Position = TimeSpan.FromSeconds(time.Current),
            MaxSeekTime = TimeSpan.FromSeconds(time.Duration),
            EndTime = TimeSpan.FromSeconds(time.Duration),
        });
    }
    public void Dispose()
    {
        _app.Player.Changed -= Update;
        _app.Player.TimeChanged -= OnTime;
        _smtc.ButtonPressed -= OnButton;
        _host.Dispose();
    }
}