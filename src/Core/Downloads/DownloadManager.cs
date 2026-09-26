using Microsoft.Extensions.Logging;
using OSTream.Core.Khinsider;
using OSTream.Core.Models;
using OSTream.Core.Storage;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
namespace OSTream.Core.Downloads;

public sealed class DownloadManager : IDisposable
{
    private static readonly string[] CoverExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"];
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);
    private const int Chunk = 64 * 1024;
    private const int ThrottleRetries = 3;
    private readonly KhinsiderClient _client;
    private readonly Func<string> _root;
    private readonly List<DownloadJob> _jobs = [];
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _worker;
    private readonly ILogger Log;
    private long _throttledUntil;
    public DownloadManager(KhinsiderClient client, Func<string> root, ILogger logger)
    {
        _client = client;
        _root = root;
        Log = logger;
        _worker = Task.Run(RunAsync);
    }
    public event Action? Changed;
    public event Action<TimeSpan>? Throttled;
    internal TimeSpan ThrottleDelay { get; set; } = TimeSpan.FromSeconds(30);
    public string Root => Path.GetFullPath(_root());
    public JobSnapshot Add(DownloadRequest request)
    {
        if (request.Tracks.Count == 0)
        {
            throw new KhinsiderException("A download needs at least one track.");
        }
        if (request.Tracks.Count > Config.MaxTracksPerJob)
        {
            throw new KhinsiderException($"Too many tracks (max {Config.MaxTracksPerJob}).");
        }
        foreach (DownloadTrackRequest track in request.Tracks)
        {
            _ = KhinsiderClient.KhinsiderUrl(track.Path);
        }
        DownloadJob job = new(request);
        lock (_lock)
        {
            _jobs.Add(job);
        }
        _ = _signal.Release();
        Changed?.Invoke();
        return job.Snapshot();
    }
    public IReadOnlyList<JobSnapshot> Snapshot()
    {
        lock (_lock)
        {
            return _jobs.Select(j => j.Snapshot()).ToArray();
        }
    }
    public bool HasActive
    {
        get
        {
            lock (_lock)
            {
                return _jobs.Any(j => j.IsActive);
            }
        }
    }
    private DownloadJob Get(string id)
    {
        return _jobs.FirstOrDefault(j => j.Id == id) ?? throw new KhinsiderException("No such download.");
    }
    public void Cancel(string id)
    {
        lock (_lock)
        {
            DownloadJob job = Get(id);
            if (!job.IsActive)
            {
                return;
            }
            job.Cancel.Cancel();
            if (job.Status == JobStatus.Queued)
            {
                job.Finish();
            }
        }
        Changed?.Invoke();
    }
    public void Retry(string id)
    {
        lock (_lock)
        {
            DownloadJob job = Get(id);
            if (job.IsActive)
            {
                return;
            }
            foreach (DownloadTrack track in job.Tracks)
            {
                if (!track.Finished)
                {
                    track.Reset();
                }
            }
            job.Cancel = new CancellationTokenSource();
            job.Error = null;
            job.Status = JobStatus.Queued;
        }
        _ = _signal.Release();
        Changed?.Invoke();
    }
    public void Remove(string id)
    {
        lock (_lock)
        {
            DownloadJob job = Get(id);
            job.Cancel.Cancel();
            _ = _jobs.Remove(job);
        }
        Changed?.Invoke();
    }
    public void ClearFinished()
    {
        lock (_lock)
        {
            _ = _jobs.RemoveAll(j => !j.IsActive);
        }
        Changed?.Invoke();
    }
    public string FolderFor(string? id = null)
    {
        string root = Root;
        if (id is null)
        {
            return root;
        }
        string folder;
        lock (_lock)
        {
            DownloadJob job = Get(id);
            root = job.Root ?? root;
            folder = Path.GetFullPath(job.Path ?? Path.Combine(root, job.Folder));
        }
        string relative = Path.GetRelativePath(root, folder);
        return relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? throw new KhinsiderException("Folder is outside the download directory.")
            : folder;
    }
    private async Task RunAsync()
    {
        CancellationToken shutdown = _shutdown.Token;
        while (!shutdown.IsCancellationRequested)
        {
            DownloadJob? job;
            lock (_lock)
            {
                job = _jobs.FirstOrDefault(j => j.Status == JobStatus.Queued);
                job?.Status = JobStatus.Running;
            }
            if (job is null)
            {
                try
                {
                    await _signal.WaitAsync(shutdown).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                continue;
            }
            try
            {
                Changed?.Invoke();
                try
                {
                    await RunJobAsync(job).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.LogError(ex, "Download job failed: {JobId}", job.Id);
                    lock (_lock)
                    {
                        job.Fail(ex.Message);
                    }
                }
                Changed?.Invoke();
            }
            catch (Exception ex)
            {
                Log.LogError(ex, "Download notification failed");
            }
        }
    }
    private async Task RunJobAsync(DownloadJob job)
    {
        string root = Root;
        string folder = Path.Combine(root, job.Folder);
        try
        {
            _ = Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lock (_lock)
            {
                job.Fail($"Cannot create folder: {ex.Message}");
            }
            return;
        }
        job.Root = root;
        job.Path = folder;
        CancellationToken cancel = job.Cancel.Token;
        await SaveCoverAsync(job, folder, cancel).ConfigureAwait(false);
        int total = job.Tracks.Length;
        (DownloadTrack track, int index)[] work = job.Tracks.Select((track, index) => (track, index)).Where(w => !w.track.Finished).ToArray();
        try
        {
            await Parallel.ForEachAsync(work, new ParallelOptions { MaxDegreeOfParallelism = Config.ParallelTracks, CancellationToken = cancel },
                async (w, ct) => await DownloadTrackAsync(job, w.track, w.index, total, folder, ct).ConfigureAwait(false)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
        }
        lock (_lock)
        {
            job.Finish();
        }
    }
    private async Task SaveCoverAsync(DownloadJob job, string folder, CancellationToken cancel)
    {
        string url = job.CoverFull;
        if (url.Length == 0)
        {
            return;
        }
        string ext = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
        if (!CoverExtensions.Contains(ext))
        {
            ext = ".jpg";
        }
        string dest = Path.Combine(folder, "cover" + ext);
        try
        {
            if (Directory.EnumerateFiles(folder, "cover.*").Any(f => !f.EndsWith(AtomicFile.TempSuffix, StringComparison.Ordinal)))
            {
                return;
            }
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using HttpRequestMessage request = KhinsiderClient.MediaRequest(url);
            using HttpResponseMessage response = await _client.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true)
            {
                return;
            }
            await AtomicFile.WriteAsync(dest, file => response.Content.CopyToAsync(file, timeout.Token)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or UnauthorizedAccessException)
        {
        }
    }
    private async Task<IReadOnlyDictionary<string, string>> ResolveAsync(string path, CancellationToken cancel)
    {
        for (int attempt = 0; ; attempt++)
        {
            long wait = Interlocked.Read(ref _throttledUntil) - Environment.TickCount64;
            if (wait > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(wait), cancel).ConfigureAwait(false);
            }
            try
            {
                return await _client.ResolveTrackAsync(path, cancel).ConfigureAwait(false);
            }
            catch (KhinsiderException ex) when (ex.Status == 403 && attempt < ThrottleRetries)
            {
                Throttle(ThrottleDelay * (1 << attempt));
            }
        }
    }
    private void Throttle(TimeSpan delay)
    {
        long now = Environment.TickCount64;
        long until = now + (long)delay.TotalMilliseconds;
        long current;
        do
        {
            current = Interlocked.Read(ref _throttledUntil);
            if (current >= until)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _throttledUntil, until, current) != current);
        if (current <= now)
        {
            Throttled?.Invoke(delay);
        }
    }
    private async Task DownloadTrackAsync(DownloadJob job, DownloadTrack track, int index, int total, string folder, CancellationToken cancel)
    {
        if (cancel.IsCancellationRequested)
        {
            track.Status = TrackStatus.Canceled;
            return;
        }
        try
        {
            FileInfo saved = new(Path.Combine(folder, FileNaming.TrackFileName(track.Title, track.Number, track.Cd, track.Pad, index, total, job.Format.Key())));
            if (IsComplete(saved, track))
            {
                Skip(track, saved);
                return;
            }
            track.Status = TrackStatus.Resolving;
            IReadOnlyDictionary<string, string> urls = await ResolveAsync(track.Path, cancel).ConfigureAwait(false);
            string fmt = job.Format.Preference().Select(f => f.Key()).FirstOrDefault(urls.ContainsKey) ?? urls.Keys.First();
            track.Fallback = fmt == job.Format.Key() ? null : fmt;
            string dest = Path.Combine(folder, FileNaming.TrackFileName(track.Title, track.Number, track.Cd, track.Pad, index, total, fmt));
            FileInfo existing = new(dest);
            if (IsComplete(existing, track))
            {
                Skip(track, existing);
                return;
            }
            using CancellationTokenSource idle = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            idle.CancelAfter(IdleTimeout);
            using HttpRequestMessage request = KhinsiderClient.MediaRequest(urls[fmt]);
            using HttpResponseMessage response = await _client.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, idle.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"HTTP Error {(int)response.StatusCode}: {response.ReasonPhrase}");
            }
            long? expected = response.Content.Headers.ContentLength;
            _ = Interlocked.Exchange(ref track.Total, expected is > 0 and var length ? length : track.EstBytes);
            track.Status = TrackStatus.Downloading;
            await AtomicFile.WriteAsync(dest, async file =>
            {
                byte[] buffer = ArrayPool<byte>.Shared.Rent(Chunk);
                try
                {
                    await using Stream source = await response.Content.ReadAsStreamAsync(idle.Token).ConfigureAwait(false);
                    while (true)
                    {
                        idle.CancelAfter(IdleTimeout);
                        int read = await source.ReadAsync(buffer.AsMemory(0, Chunk), idle.Token).ConfigureAwait(false);
                        if (read == 0)
                        {
                            break;
                        }
                        await file.WriteAsync(buffer.AsMemory(0, read), idle.Token).ConfigureAwait(false);
                        _ = Interlocked.Add(ref track.Bytes, read);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
                cancel.ThrowIfCancellationRequested();
                long written = Interlocked.Read(ref track.Bytes);
                if (expected is > 0 && written != expected)
                {
                    throw new IOException($"download ended at {written} of {expected} bytes");
                }
            }).ConfigureAwait(false);
            _ = Interlocked.Exchange(ref track.Total, Interlocked.Read(ref track.Bytes));
            track.Status = TrackStatus.Done;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            track.Status = TrackStatus.Canceled;
        }
        catch (OperationCanceledException)
        {
            track.Error = "timed out";
            track.Status = TrackStatus.Error;
        }
        catch (KhinsiderException ex)
        {
            track.Error = ex.Message;
            track.Status = TrackStatus.Error;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            track.Error = $"{ex.GetType().Name}: {ex.InnerException?.Message ?? ex.Message}";
            track.Status = TrackStatus.Error;
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Download failed: {Track}", track.Title);
            track.Error = ex.Message;
            track.Status = TrackStatus.Error;
        }
    }
    private static bool IsComplete(FileInfo file, DownloadTrack track)
    {
        return file.Exists && file.Length > 0 && (track.EstBytes == 0 || file.Length >= track.EstBytes * 0.95);
    }
    private static void Skip(DownloadTrack track, FileInfo file)
    {
        _ = Interlocked.Exchange(ref track.Bytes, file.Length);
        _ = Interlocked.Exchange(ref track.Total, file.Length);
        track.Status = TrackStatus.Skipped;
    }
    public void Dispose()
    {
        lock (_lock)
        {
            foreach (DownloadJob job in _jobs)
            {
                job.Cancel.Cancel();
            }
        }
        _shutdown.Cancel();
        try
        {
            _ = _worker.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
        }
    }
}