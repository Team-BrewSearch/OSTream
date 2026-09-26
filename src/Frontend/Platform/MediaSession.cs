using System;
using OSTream.Core.Models;
using OSTream.Core.Player;
namespace OSTream.Platform;
public interface IMediaSession : IDisposable;
public sealed record NowPlaying(Track? Track, PlayStatus Status, bool Shuffle, RepeatMode Repeat, double Volume, bool CanGoNext, bool CanGoPrevious)
{
    public bool IsPlaying => Status is PlayStatus.Playing or PlayStatus.Loading;
}
public static class MediaSessions
{
    public static IMediaSession Create(AppServices app)
    {
#if WINDOWS
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return (IMediaSession?)Windows.SmtcSession.TryCreate(app) ?? new NoMediaSession();
#endif
        if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD()) return new MprisSession(app);
        return new NoMediaSession();
    }
    public static NowPlaying Snapshot(AppServices app)
    {
        var p = app.Player;
        var s = app.Settings.Value;
        return new NowPlaying(p.Current, p.Status, s.Shuffle, s.Repeat, s.Muted ? 0 : s.Volume,
            p.Queue.HasNext || (s.Repeat == RepeatMode.All && p.Current is not null), p.Current is not null);
    }
}
sealed class NoMediaSession : IMediaSession
{
    public void Dispose() { }
}