using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OSTream.Core.Models;
namespace OSTream.Core.Downloads;
public enum JobStatus { Queued, Running, Done, Partial, Error, Canceled }
public enum TrackStatus { Queued, Resolving, Downloading, Done, Skipped, Error, Canceled }
public sealed record DownloadTrackRequest(string Title, string Number, string Cd, string Path, int Pad, long EstBytes);
public sealed record DownloadRequest(
    string Title,
    string Subtitle,
    string Folder,
    AudioFormat Format,
    string Cover,
    string CoverFull,
    string AlbumUrl,
    IReadOnlyList<DownloadTrackRequest> Tracks);
public sealed record JobCounts(int Total, int Done, int Failed, int Fallback);
public sealed record TrackSnapshot(string Title, string Number, string Cd, TrackStatus Status, long Bytes, long Total, string? Error, string? Fallback);
public sealed record JobSnapshot(
    string Id,
    string Title,
    string Subtitle,
    string Folder,
    AudioFormat Format,
    string Cover,
    string AlbumUrl,
    JobStatus Status,
    string? Error,
    string? Path,
    JobCounts Counts,
    long BytesDone,
    long BytesTotal,
    long Speed,
    double? Eta,
    IReadOnlyList<TrackSnapshot> Tracks)
{
    public bool IsActive => Status is JobStatus.Queued or JobStatus.Running;
    public double Percent => Status == JobStatus.Done ? 100
        : BytesTotal > 0 ? Math.Min(100, BytesDone * 100.0 / BytesTotal)
        : Counts.Total > 0 ? Counts.Done * 100.0 / Counts.Total : 0;
    public string FallbackLabel
    {
        get
        {
            var formats = Tracks.Select(t => t.Fallback).OfType<string>().Distinct().ToArray();
            return formats.Length == 1 ? formats[0].ToUpperInvariant() : "other formats";
        }
    }
}
internal sealed class DownloadTrack(DownloadTrackRequest request)
{
    public string Title { get; } = Clip(request.Title, 300, "Untitled");
    public string Number { get; } = Clip(request.Number, 16);
    public string Cd { get; } = Clip(request.Cd, 8);
    public string Path { get; } = request.Path;
    public int Pad { get; } = Math.Clamp(request.Pad, 0, 6);
    public long EstBytes { get; } = Math.Max(0, request.EstBytes);
    public volatile TrackStatus Status;
    public long Bytes;
    public long Total;
    public volatile string? Error;
    public volatile string? Fallback;
    public bool Finished => Status is TrackStatus.Done or TrackStatus.Skipped;
    public long Size => Interlocked.Read(ref Total) is > 0 and var total ? total : EstBytes;
    public void Reset()
    {
        Status = TrackStatus.Queued;
        Interlocked.Exchange(ref Bytes, 0);
        Interlocked.Exchange(ref Total, 0);
        Error = null;
        Fallback = null;
    }
    public TrackSnapshot Snapshot() => new(Title, Number, Cd, Status, Interlocked.Read(ref Bytes), Size, Error, Fallback);
    internal static string Clip(string? value, int max, string fallback = "")
    {
        var s = string.IsNullOrEmpty(value) ? fallback : value;
        return s.Length > max ? s[..max] : s;
    }
}
internal sealed class DownloadJob
{
    readonly Queue<(long Time, long Bytes)> _samples = new();
    JobSnapshot? _final;
    public DownloadJob(DownloadRequest request)
    {
        Id = Guid.NewGuid().ToString("N")[..10];
        Title = DownloadTrack.Clip(request.Title, 300, "Untitled");
        Subtitle = DownloadTrack.Clip(request.Subtitle, 300);
        Folder = FileNaming.SafeName(string.IsNullOrEmpty(request.Folder) ? Title : request.Folder);
        Format = request.Format;
        Cover = Khinsider.KhinsiderClient.SecureMediaUrl(request.Cover) ?? "";
        CoverFull = Khinsider.KhinsiderClient.SecureMediaUrl(request.CoverFull) ?? "";
        AlbumUrl = DownloadTrack.Clip(request.AlbumUrl, 500);
        Tracks = request.Tracks.Select(t => new DownloadTrack(t)).ToArray();
    }
    public string Id { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string Folder { get; }
    public AudioFormat Format { get; }
    public string Cover { get; }
    public string CoverFull { get; }
    public string AlbumUrl { get; }
    public DownloadTrack[] Tracks { get; }
    public volatile JobStatus Status = JobStatus.Queued;
    public volatile string? Error;
    public volatile string? Path;
    public volatile string? Root;
    public CancellationTokenSource Cancel = new();
    public bool IsActive => Status is JobStatus.Queued or JobStatus.Running;
    public void Finish()
    {
        var failed = Tracks.Count(t => t.Status == TrackStatus.Error);
        var done = Tracks.Count(t => t.Finished);
        if (Cancel.IsCancellationRequested)
        {
            Status = JobStatus.Canceled;
            foreach (var t in Tracks)
                if (t.Status is TrackStatus.Queued or TrackStatus.Resolving or TrackStatus.Downloading) t.Status = TrackStatus.Canceled;
        }
        else if (failed == 0)
        {
            Status = JobStatus.Done;
        }
        else
        {
            Status = done == 0 ? JobStatus.Error : JobStatus.Partial;
            Error = $"{failed} of {Tracks.Length} tracks failed";
        }
    }
    public void Fail(string error)
    {
        foreach (var t in Tracks)
        {
            if (t.Status is not (TrackStatus.Queued or TrackStatus.Resolving or TrackStatus.Downloading)) continue;
            t.Error = error;
            t.Status = TrackStatus.Error;
        }
        Status = JobStatus.Error;
        Error = error;
    }
    long Speed(long bytesDone)
    {
        var now = Environment.TickCount64;
        lock (_samples)
        {
            _samples.Enqueue((now, bytesDone));
            while (_samples.Count > 40) _samples.Dequeue();
            while (_samples.Count > 2 && now - _samples.Peek().Time > 5000) _samples.Dequeue();
            if (Status != JobStatus.Running || _samples.Count < 2) return 0;
            var (t0, b0) = _samples.Peek();
            return now > t0 ? Math.Max(0, (bytesDone - b0) * 1000 / (now - t0)) : 0;
        }
    }
    public JobSnapshot Snapshot()
    {
        if (_final is { } final && !IsActive) return final;
        var tracks = new TrackSnapshot[Tracks.Length];
        long done = 0, total = 0;
        for (var i = 0; i < Tracks.Length; i++)
        {
            tracks[i] = Tracks[i].Snapshot();
            done += tracks[i].Bytes;
            total += tracks[i].Total;
        }
        total = Math.Max(total, done);
        var speed = Speed(done);
        int doneCount = 0, failed = 0, fallback = 0;
        for (var i = 0; i < tracks.Length; i++)
        {
            var t = tracks[i];
            if (t.Status is TrackStatus.Done or TrackStatus.Skipped) doneCount++;
            else if (t.Status == TrackStatus.Error) failed++;
            if (t.Fallback is not null) fallback++;
        }
        var counts = new JobCounts(tracks.Length, doneCount, failed, fallback);
        double? eta = speed > 0 && total > done ? Math.Round((double)(total - done) / speed) : null;
        var snapshot = new JobSnapshot(Id, Title, Subtitle, Folder, Format, Cover, AlbumUrl, Status, Error, Path, counts, done, total, speed, eta, tracks);
        _final = snapshot.IsActive ? null : snapshot;
        return snapshot;
    }
}