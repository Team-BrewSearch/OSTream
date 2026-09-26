using System.Threading.Tasks;
using System;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using OSTream.Core.Khinsider;
using OSTream.Core.Models;
using OSTream.Core.Storage;
namespace OSTream.Core.Downloads;
public sealed class DownloadService : IDisposable
{
    static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    readonly DownloadManager _manager;
    readonly SettingsStore _settings;
    readonly Catalog _catalog;
    readonly INotifier _notifier;
    readonly Func<string?, Task> _openFolder;
    readonly SynchronizationContext? _ui;
    readonly Timer _poll;
    readonly Action _managerChanged;
    readonly Action<TimeSpan> _managerThrottled;
    bool _polling;
    int _refreshQueued;
    bool _disposed;
    readonly ILogger Log;
    public DownloadService(DownloadManager manager, SettingsStore settings, Catalog catalog, INotifier notifier, Func<string?, Task> openFolder, ILogger logger)
    {
        _manager = manager;
        _settings = settings;
        _catalog = catalog;
        _notifier = notifier;
        _openFolder = openFolder;
        _ui = SynchronizationContext.Current;
        _poll = new Timer(_ => Post(Refresh));
        _managerChanged = () =>
        {
            if (Interlocked.Exchange(ref _refreshQueued, 1) == 0) Post(Refresh);
        };
        manager.Changed += _managerChanged;
        _managerThrottled = delay => _notifier.Toast("KHInsider is limiting requests", ToastKind.Info,
            $"Downloads pause for {(int)delay.TotalSeconds} s, then resume.");
        manager.Throttled += _managerThrottled;
        Log = logger;
    }
    public event Action? Changed;
    public IReadOnlyList<JobSnapshot> Jobs { get; private set; } = [];
    public string Directory => _manager.Root;
    public int ActiveCount => Jobs.Count(j => j.IsActive);
    void Post(Action action) => _ui.PostOrRun(action);
    public void Refresh()
    {
        if (_disposed) return;
        Interlocked.Exchange(ref _refreshQueued, 0);
        var previous = Jobs;
        Jobs = _manager.Snapshot();
        Announce(previous, Jobs);
        var active = Jobs.Any(j => j.IsActive);
        if (active != _polling)
        {
            _polling = active;
            _poll.Change(active ? PollInterval : Timeout.InfiniteTimeSpan, active ? PollInterval : Timeout.InfiniteTimeSpan);
        }
        Changed?.Invoke();
    }
    void Announce(IReadOnlyList<JobSnapshot> previous, IReadOnlyList<JobSnapshot> jobs)
    {
        var before = previous.ToDictionary(j => j.Id, j => j.IsActive);
        foreach (var job in jobs)
        {
            if (!before.TryGetValue(job.Id, out var wasActive) || !wasActive || job.IsActive) continue;
            var id = job.Id;
            if (job.Status == JobStatus.Done)
            {
                var note = job.Counts.Fallback > 0 ? $" · {job.Counts.Fallback} as {job.FallbackLabel} (no {job.Format.Label()})" : "";
                _notifier.Toast($"Downloaded “{job.Title}”", ToastKind.Success, Formatting.Plural(job.Counts.Total, "track") + note,
                    new ToastAction("Open folder", () => _ = _openFolder(id)));
            }
            else if (job.Status is JobStatus.Partial or JobStatus.Error)
            {
                _notifier.Toast($"“{job.Title}” finished with errors", ToastKind.Error, job.Error ?? "",
                    new ToastAction("Retry", () => Retry(id)));
            }
        }
    }
    static DownloadTrackRequest JobTrack(Track track, AudioFormat format, int index, bool usePosition = false, int total = 0)
    {
        var size = format.Preference().Select(f => track.Sizes.GetValueOrDefault(f.Key())).FirstOrDefault(s => s is not null);
        var padFrom = total > 0 ? total : track.Album.TrackCount > 0 ? track.Album.TrackCount : 1;
        return new DownloadTrackRequest(
            track.Title,
            usePosition ? (index + 1).ToString(CultureInfo.InvariantCulture) : track.Number,
            usePosition ? "" : track.Cd,
            track.Path,
            padFrom.ToString(CultureInfo.InvariantCulture).Length,
            Formatting.ParseSize(size));
    }
    void Enqueue(DownloadRequest request, string label)
    {
        try
        {
            _manager.Add(request);
            _notifier.Toast($"Queued {label}", ToastKind.Info, $"{request.Format.Label()} · saving to “{request.Folder}”");
        }
        catch (KhinsiderException ex)
        {
            _notifier.Toast("Could not start the download", ToastKind.Error, ex.Message);
        }
        Refresh();
    }
    public AudioFormat EffectiveFormat(Album? album)
    {
        var wanted = _settings.Value.Format;
        if (album is null || album.Formats.Count == 0) return wanted;
        foreach (var format in wanted.Preference())
            if (album.Formats.Contains(format.Key())) return format;
        return AudioFormat.Mp3;
    }
    public void DownloadAlbum(Album album, IReadOnlyList<Track>? tracks = null)
    {
        tracks ??= album.Tracks;
        var format = EffectiveFormat(album);
        Enqueue(new DownloadRequest(
            album.Title,
            Formatting.JoinNonEmpty(" · ", album.Platforms.Count > 0 ? album.Platforms[0] : null, album.Year),
            album.Title,
            format,
            album.Cover,
            album.CoverFull,
            Formatting.AlbumUrl(album.Slug),
            tracks.Select((t, i) => JobTrack(t, format, i, total: album.Tracks.Count)).ToArray()), Formatting.Plural(tracks.Count, "track"));
    }
    public void DownloadTrack(Track track)
    {
        var format = _settings.Value.Format;
        var album = track.Album;
        Enqueue(new DownloadRequest(
            track.Title,
            album.Title,
            album.Title.Length > 0 ? album.Title : "Singles",
            format,
            album.Thumb,
            _catalog.Cached(album.Slug)?.CoverFull ?? "",
            album.Slug.Length > 0 ? Formatting.AlbumUrl(album.Slug) : "",
            [JobTrack(track, format, 0)]), $"“{track.Title}”");
    }
    public void DownloadPlaylist(Playlist playlist)
    {
        var format = _settings.Value.Format;
        Enqueue(new DownloadRequest(
            playlist.Name,
            "Playlist",
            playlist.Name,
            format,
            playlist.Tracks.FirstOrDefault(t => t.Album.Thumb.Length > 0)?.Album.Thumb ?? "",
            "",
            "",
            playlist.Tracks.Select((t, i) => JobTrack(t, format, i, usePosition: true, total: playlist.Tracks.Count)).ToArray()),
            Formatting.Plural(playlist.Tracks.Count, "track"));
    }
    void Mutate(Action action, string error)
    {
        try
        {
            action();
        }
        catch (KhinsiderException ex)
        {
            _notifier.Toast(error, ToastKind.Error, ex.Message);
        }
        Refresh();
    }
    public void Cancel(string id) => Mutate(() => _manager.Cancel(id), "Could not cancel the download");
    public void Retry(string id) => Mutate(() => _manager.Retry(id), "Could not retry the download");
    public void Remove(string id) => Mutate(() => _manager.Remove(id), "Could not remove the download");
    public void ClearFinished() => Mutate(_manager.ClearFinished, "Could not clear finished downloads");
    public string FolderFor(string? id) => _manager.FolderFor(id);
    public void Dispose()
    {
        _disposed = true;
        _manager.Changed -= _managerChanged;
        _manager.Throttled -= _managerThrottled;
        _poll.Dispose();
    }
}