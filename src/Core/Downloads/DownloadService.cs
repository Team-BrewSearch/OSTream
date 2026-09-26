using Microsoft.Extensions.Logging;
using OSTream.Core.Khinsider;
using OSTream.Core.Models;
using OSTream.Core.Storage;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace OSTream.Core.Downloads;

public sealed class DownloadService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private readonly DownloadManager _manager;
    private readonly SettingsStore _settings;
    private readonly Catalog _catalog;
    private readonly INotifier _notifier;
    private readonly Func<string?, Task> _openFolder;
    private readonly SynchronizationContext? _ui;
    private readonly Timer _poll;
    private readonly Action _managerChanged;
    private readonly Action<TimeSpan> _managerThrottled;
    private bool _polling;
    private int _refreshQueued;
    private bool _disposed;
    private readonly ILogger Log;
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
            if (Interlocked.Exchange(ref _refreshQueued, 1) == 0)
            {
                Post(Refresh);
            }
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
    private void Post(Action action)
    {
        _ui.PostOrRun(action);
    }
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }
        _ = Interlocked.Exchange(ref _refreshQueued, 0);
        IReadOnlyList<JobSnapshot> previous = Jobs;
        Jobs = _manager.Snapshot();
        Announce(previous, Jobs);
        bool active = Jobs.Any(j => j.IsActive);
        if (active != _polling)
        {
            _polling = active;
            _ = _poll.Change(active ? PollInterval : Timeout.InfiniteTimeSpan, active ? PollInterval : Timeout.InfiniteTimeSpan);
        }
        Changed?.Invoke();
    }
    private void Announce(IReadOnlyList<JobSnapshot> previous, IReadOnlyList<JobSnapshot> jobs)
    {
        Dictionary<string, bool> before = previous.ToDictionary(j => j.Id, j => j.IsActive);
        foreach (JobSnapshot job in jobs)
        {
            if (!before.TryGetValue(job.Id, out bool wasActive) || !wasActive || job.IsActive)
            {
                continue;
            }
            string id = job.Id;
            if (job.Status == JobStatus.Done)
            {
                string note = job.Counts.Fallback > 0 ? $" · {job.Counts.Fallback} as {job.FallbackLabel} (no {job.Format.Label()})" : "";
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
    private static DownloadTrackRequest JobTrack(Track track, AudioFormat format, int index, bool usePosition = false, int total = 0)
    {
        string? size = format.Preference().Select(f => track.Sizes.GetValueOrDefault(f.Key())).FirstOrDefault(s => s is not null);
        int padFrom = total > 0 ? total : track.Album.TrackCount > 0 ? track.Album.TrackCount : 1;
        return new DownloadTrackRequest(
            track.Title,
            usePosition ? (index + 1).ToString(CultureInfo.InvariantCulture) : track.Number,
            usePosition ? "" : track.Cd,
            track.Path,
            padFrom.ToString(CultureInfo.InvariantCulture).Length,
            Formatting.ParseSize(size));
    }
    private void Enqueue(DownloadRequest request, string label)
    {
        try
        {
            _ = _manager.Add(request);
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
        AudioFormat wanted = _settings.Value.Format;
        if (album is null || album.Formats.Count == 0)
        {
            return wanted;
        }
        foreach (AudioFormat format in wanted.Preference())
        {
            if (album.Formats.Contains(format.Key()))
            {
                return format;
            }
        }
        return AudioFormat.Mp3;
    }
    public void DownloadAlbum(Album album, IReadOnlyList<Track>? tracks = null)
    {
        tracks ??= album.Tracks;
        AudioFormat format = EffectiveFormat(album);
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
        AudioFormat format = _settings.Value.Format;
        AlbumRef album = track.Album;
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
        AudioFormat format = _settings.Value.Format;
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
    private void Mutate(Action action, string error)
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
    public void Cancel(string id)
    {
        Mutate(() => _manager.Cancel(id), "Could not cancel the download");
    }
    public void Retry(string id)
    {
        Mutate(() => _manager.Retry(id), "Could not retry the download");
    }
    public void Remove(string id)
    {
        Mutate(() => _manager.Remove(id), "Could not remove the download");
    }
    public void ClearFinished()
    {
        Mutate(_manager.ClearFinished, "Could not clear finished downloads");
    }
    public string FolderFor(string? id)
    {
        return _manager.FolderFor(id);
    }
    public void Dispose()
    {
        _disposed = true;
        _manager.Changed -= _managerChanged;
        _manager.Throttled -= _managerThrottled;
        _poll.Dispose();
    }
}