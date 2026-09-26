using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OSTream.Core.Audio;
using OSTream.Core.Khinsider;
using OSTream.Core.Models;
using OSTream.Core.Storage;
namespace OSTream.Core.Player;
using Microsoft.Extensions.Logging;
public enum PlayStatus { Idle, Loading, Playing, Paused }
public readonly record struct PlaybackTime(double Current, double Duration, double Buffered);
public sealed class PlayerService : IDisposable
{
    static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);
    readonly KhinsiderClient _client;
    readonly SettingsStore _settings;
    readonly LibraryStore _library;
    readonly INotifier _notifier;
    readonly ILogger Log;
    readonly AudioEngine _engine;
    readonly SynchronizationContext? _ui;
    readonly Timer _ticker;
    readonly PlayQueue _queue = new();
    CancellationTokenSource? _load;
    long _session;
    bool _sourceReady;   
    int _failStreak;
    bool _countedRecent;
    bool _ticking;
    public PlayerService(KhinsiderClient client, SettingsStore settings, LibraryStore library, INotifier notifier, ILogger logger)
    {
        _client = client;
        _settings = settings;
        _library = library;
        _notifier = notifier;
        _engine = new AudioEngine();
        _ui = SynchronizationContext.Current;
        _ticker = new Timer(_ => Post(Tick));
        _engine.Event += e => Post(() => OnEngineEvent(e));
        Log = logger;
        ApplyVolume(settings.Value);
        settings.Changed += (old, now) =>
        {
            if (old.Volume != now.Volume || old.Muted != now.Muted) ApplyVolume(now);
        };
    }
    public event Action? Changed;
    public event Action<PlaybackTime>? TimeChanged;
    public PlayStatus Status { get; private set; } = PlayStatus.Idle;
    public string? Error { get; private set; }
    public PlayQueue Queue => _queue;
    public Track? Current => _queue.Current;
    public int CurrentQueueIndex => _queue.CurrentQueueIndex;
    public IEnumerable<(int QueueIndex, Track Track)> UpNext() => _queue.UpNext();
    public PlaybackTime Time
    {
        get
        {
            var fallback = Current?.DurationSec ?? 0;
            var duration = _sourceReady && _engine.Duration > 0 ? _engine.Duration : fallback;
            var current = _sourceReady ? Math.Min(_engine.Position, duration > 0 ? duration : double.MaxValue) : 0;
            var buffered = _sourceReady ? Math.Min(duration, current + _engine.BufferedAhead) : 0;
            return new PlaybackTime(current, duration, buffered);
        }
    }
    void Post(Action action) => _ui.PostOrRun(action);
    void Emit()
    {
        Changed?.Invoke();
        UpdateTicker();
    }
    void EmitTime() => TimeChanged?.Invoke(Time);
    void UpdateTicker()
    {
        var run = Status is PlayStatus.Playing or PlayStatus.Loading;
        if (run == _ticking) return;
        _ticking = run;
        _ticker.Change(run ? TickInterval : Timeout.InfiniteTimeSpan, run ? TickInterval : Timeout.InfiniteTimeSpan);
    }
    void Tick()
    {
        if (_ticking) EmitTime();
    }
    void ApplyVolume(SettingsData s) => _engine.SetVolume(s.Muted ? 0 : Math.Clamp(s.Volume, 0, 1));
    bool _disposed;
    async Task Load()
    {
        if (_disposed) return;
        Track? track = null;
        try
        {
            track = Current;
            _load?.Cancel();
            _load = new CancellationTokenSource();
            var ct = _load.Token;
            var session = ++_session;
            if (track is null)
            {
                Stop();
                return;
            }
            _engine.Stop();
            _sourceReady = false;
            Status = PlayStatus.Loading;
            Error = null;
            _countedRecent = false;
            Emit();
            EmitTime();
            HttpRangeStream? stream = null;
            try
            {
                var urls = await _client.ResolveTrackAsync(track.Path, ct);
                var streams = PlayableStreams(urls);
                if (streams.Count == 0) throw new KhinsiderException("No playable stream for this track.");
                (var decoder, stream) = await OpenStreamAsync(track, streams, ct);
                if (session != _session)
                {
                    decoder.Dispose();
                    return;
                }
                _engine.Load(decoder, stream, session);
                _sourceReady = true;
            }
            catch (Exception) when (session != _session || ct.IsCancellationRequested)
            {
                stream?.Dispose();
            }
            catch (Exception ex) when (ex is KhinsiderException or IOException or InvalidDataException or System.Net.Http.HttpRequestException or ObjectDisposedException or OperationCanceledException)
            {
                stream?.Dispose();
                if (ex is not KhinsiderException) _client.ForgetTrack(track.Path);
                HandleFailure(track, ex is KhinsiderException ? ex.Message : "The audio stream could not be loaded.");
            }
            catch (Exception ex)
            {
                stream?.Dispose();
                Log.LogError(ex, "Track load failed: {Track}", track.Title);
                HandleFailure(track, "The audio stream could not be loaded.");
            }
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Load outer failure");
        }
    }
    List<(AudioFormat Format, string Url)> PlayableStreams(IReadOnlyDictionary<string, string> urls)
    {
        var streams = new List<(AudioFormat, string)>();
        foreach (var format in _settings.Value.Format.Preference())
            if (urls.TryGetValue(format.Key(), out var url)) streams.Add((format, url));
        return streams;
    }
    async Task<(IAudioDecoder Decoder, HttpRangeStream Stream)> OpenStreamAsync(Track track, List<(AudioFormat Format, string Url)> streams, CancellationToken ct)
    {
        for (var i = 0; ; i++)
        {
            var (format, url) = streams[i];
            var stream = await HttpRangeStream.OpenAsync(_client.Http, () => KhinsiderClient.MediaRequest(url), ct);
            try
            {
                using (ct.Register(() => stream.Dispose()))
                    return (await Task.Run(() => OpenDecoder(format, stream), ct), stream);
            }
            catch (InvalidDataException ex) when (i + 1 < streams.Count && !ct.IsCancellationRequested)
            {
                Log.LogWarning("Cannot play {Format} for {Track} ({Reason}); trying {Next}", format.Label(), track.Title, ex.Message, streams[i + 1].Format.Label());
                stream.Dispose();
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
    }
    static IAudioDecoder OpenDecoder(AudioFormat format, Stream stream) => format switch
    {
        AudioFormat.Flac => new FlacDecoder(stream),
        AudioFormat.Ogg => new VorbisDecoder(stream),
        AudioFormat.Wav => new WavDecoder(stream),
        _ => new Mp3Decoder(stream),
    };
    void HandleFailure(Track track, string message)
    {
        if (_disposed) return;
        _failStreak++;
        _sourceReady = false;
        Status = PlayStatus.Paused;
        Error = message;
        Emit();
        Log.LogError("Playback failed for {Track}: {Message}", track.Title, message);
        var skip = _queue.HasNext && _failStreak < 3;
        _notifier.Toast($"Couldn't play “{track.Title}”", ToastKind.Error, skip ? "Skipping to the next track." : message);
        if (!skip) return;
        var session = _session;
        _ = Task.Delay(900).ContinueWith(_ => Post(() =>
        {
            if (session == _session && !_disposed) Next();
        }), TaskScheduler.Default);
    }
    void Stop()
    {
        _session++;
        _load?.Cancel();
        _engine.Stop();
        _sourceReady = false;
        Status = PlayStatus.Idle;
        Emit();
        EmitTime();
    }
    void PrefetchNext()
    {
        if (_queue.PeekNext() is { } next) _ = _client.ResolveTrackAsync(next.Path).ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
    }
    void OnEngineEvent(EngineEvent e)
    {
        if (e.Session != _session) return;
        switch (e.Kind)
        {
            case EngineEventKind.Playing:
                Status = PlayStatus.Playing;
                _failStreak = 0;
                if (!_countedRecent && Current is { } track)
                {
                    _countedRecent = true;
                    _library.AddRecent(track);
                    PrefetchNext();
                }
                Emit();
                break;
            case EngineEventKind.Paused:
                if (Status == PlayStatus.Loading) return;
                Status = Current is null ? PlayStatus.Idle : PlayStatus.Paused;
                Emit();
                break;
            case EngineEventKind.Waiting:
                if (Status != PlayStatus.Playing) return;
                Status = PlayStatus.Loading;
                Emit();
                break;
            case EngineEventKind.Ended:
                Next(auto: true);
                break;
            case EngineEventKind.Error:
                if (Current is not { } failed) return;
                _client.ForgetTrack(failed.Path);
                HandleFailure(failed, e.Error ?? "The audio stream could not be loaded.");
                break;
        }
    }
    public void PlayList(IReadOnlyList<Track> tracks, int startIndex = 0, bool? shuffle = null)
    {
        if (tracks.Count == 0) return;
        if (shuffle is { } wanted && wanted != _settings.Value.Shuffle) _settings.Update(s => s with { Shuffle = wanted });
        var start = shuffle == true ? Random.Shared.Next(tracks.Count) : Math.Clamp(startIndex, 0, tracks.Count - 1);
        _queue.SetList(tracks, start, _settings.Value.Shuffle);
        _failStreak = 0;
        _ = Load();
    }
    public void PlayNext(IReadOnlyList<Track> tracks)
    {
        if (Current is null)
        {
            PlayList(tracks);
            return;
        }
        _queue.InsertNext(tracks);
        Emit();
        PrefetchNext();
    }
    public void AddToQueue(IReadOnlyList<Track> tracks)
    {
        if (Current is null)
        {
            PlayList(tracks);
            return;
        }
        _queue.Append(tracks);
        Emit();
    }
    public void JumpTo(int queueIndex)
    {
        if (!_queue.JumpTo(queueIndex)) return;
        _failStreak = 0;
        _ = Load();
    }
    public void RemoveFromQueue(int queueIndex)
    {
        if (_queue.Remove(queueIndex)) Emit();
    }
    public void ClearUpNext()
    {
        _queue.ClearUpNext();
        Emit();
    }
    public void Toggle()
    {
        if (Current is null || Status == PlayStatus.Loading) return;
        if (Status == PlayStatus.Playing) _engine.Pause();
        else if (!_sourceReady) _ = Load();
        else _engine.Play();
    }
    public void Next(bool auto = false)
    {
        if (Current is null) return;
        var repeat = _settings.Value.Repeat;
        if (auto && repeat == RepeatMode.One)
        {
            _engine.Seek(0);
            _engine.Play();
            return;
        }
        if (!_queue.Advance(repeat == RepeatMode.All, _settings.Value.Shuffle))
        {
            if (auto)
            {
                _engine.Seek(0);
                Status = PlayStatus.Paused;
                Emit();
                EmitTime();
            }
            return;
        }
        _ = Load();
    }
    public void Prev()
    {
        if (Current is null) return;
        if (Time.Current > 3 || _queue.Position == 0)
        {
            _engine.Seek(0);
            EmitTimeSoon();
            return;
        }
        _queue.Back();
        _ = Load();
    }
    public void Seek(double seconds)
    {
        var duration = Time.Duration;
        if (duration <= 0 || !_sourceReady) return;
        _engine.Seek(Math.Clamp(seconds, 0, Math.Max(0, duration - 0.25)));
        EmitTimeSoon();
    }
    public void SeekBy(double delta) => Seek(Time.Current + delta);
    void EmitTimeSoon() => _ = Task.Delay(30).ContinueWith(_ => Post(EmitTime), TaskScheduler.Default);
    public void PreviewVolume(double volume) => _engine.SetVolume(Math.Clamp(volume, 0, 1));
    public void SetVolume(double volume)
    {
        var v = Math.Clamp(volume, 0, 1);
        _settings.Update(s => s with { Volume = v, Muted = volume <= 0 });
    }
    public void ToggleMute()
    {
        var s = _settings.Value;
        _settings.Update(x => s.Muted || s.Volume == 0
            ? x with { Muted = false, Volume = s.Volume > 0 ? s.Volume : 0.5 }
            : x with { Muted = true });
    }
    public void ToggleShuffle()
    {
        var shuffle = !_settings.Value.Shuffle;
        _settings.Update(s => s with { Shuffle = shuffle });
        _queue.Reorder(shuffle);
        Emit();
    }
    public void CycleRepeat()
    {
        _settings.Update(s => s with { Repeat = s.Repeat switch { RepeatMode.Off => RepeatMode.All, RepeatMode.All => RepeatMode.One, _ => RepeatMode.Off } });
        Emit();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _load?.Cancel();
        _ticker.Dispose();
        _engine.Dispose();
    }
}
