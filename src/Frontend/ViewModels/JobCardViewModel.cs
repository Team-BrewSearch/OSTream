using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OSTream.Controls;
using OSTream.Core;
using OSTream.Core.Downloads;
using OSTream.Core.Models;
namespace OSTream.ViewModels;
public sealed record JobTrackLine(string Name, string? Error, string Value, string State, IconKind Icon, bool Busy)
{
    public bool HasIcon => Icon != IconKind.None;
    public bool IsQueued => State == "queued";
}
public sealed partial class JobCardViewModel : ViewModelBase
{
    readonly AppServices _app;
    JobSnapshot? _last;
    public JobCardViewModel(AppServices app, JobSnapshot job, bool detailed)
    {
        _app = app;
        Id = job.Id;
        Title = job.Title;
        Cover = job.Cover;
        Detailed = detailed;
        Update(job);
    }
    public string Id { get; }
    public string Title { get; }
    public string Cover { get; }
    public bool Detailed { get; }
    [ObservableProperty] public partial JobStatus Status { get; private set; }
    [ObservableProperty] public partial string Sub { get; private set; } = "";
    [ObservableProperty] public partial double Progress { get; private set; }
    [ObservableProperty] public partial string StatusText { get; private set; } = "";
    [ObservableProperty] public partial string? StatusTip { get; private set; }
    [ObservableProperty] public partial string SideText { get; private set; } = "";
    [ObservableProperty] public partial bool IsStarting { get; private set; }
    [ObservableProperty] public partial bool IsRemoving { get; private set; }
    [ObservableProperty] public partial bool IsExpanded { get; set; }
    [ObservableProperty] public partial IReadOnlyList<JobTrackLine> TrackLines { get; private set; } = [];
    [ObservableProperty] public partial string DetailsLabel { get; private set; } = "";
    public bool CanCancel => Status is JobStatus.Queued or JobStatus.Running;
    public bool CanOpenFolder => Status == JobStatus.Done;
    public bool CanRetry => Status is JobStatus.Partial or JobStatus.Error or JobStatus.Canceled;
    public bool CanRemove => !CanCancel;
    public string StatusClass => Status.ToString().ToLowerInvariant();
    partial void OnStatusChanged(JobStatus value)
    {
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanOpenFolder));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(StatusClass));
    }
    public void Update(JobSnapshot job)
    {
        if (!ReferenceEquals(job, _last)) Render(job);
    }
    void Render(JobSnapshot job)
    {
        _last = job;
        Status = job.Status;
        Progress = job.Percent;
        IsStarting = job.Status == JobStatus.Running && job.BytesDone == 0 && job.Counts.Done == 0;
        StatusText = StatusOf(job);
        StatusTip = job.Error;
        SideText = SideOf(job);
        Sub = job.Counts.Total == 1
            ? Formatting.JoinNonEmpty(" · ", job.Subtitle, job.Format.Label())
            : Formatting.JoinNonEmpty(" · ", Formatting.Plural(job.Counts.Total, "track"), job.BytesTotal > 0 ? Formatting.Bytes(job.BytesTotal) : null, job.Format.Label());
        DetailsLabel = IsExpanded ? "Hide tracks" : $"Show {Formatting.Plural(job.Counts.Total, "track")}";
        if (Detailed && IsExpanded)
        {
            var lines = job.Tracks.Select(Line).ToArray();
            if (!lines.SequenceEqual(TrackLines)) TrackLines = lines;
        }
    }
    partial void OnIsExpandedChanged(bool value)
    {
        if (_last is not null) Render(_last);
    }
    static string StatusOf(JobSnapshot job)
    {
        var c = job.Counts;
        return job.Status switch
        {
            JobStatus.Queued => "Queued",
            JobStatus.Running when job.BytesDone == 0 && c.Done == 0 => "Starting…",
            JobStatus.Running => c.Total == 1 ? $"Downloading · {Formatting.JsRound(job.Percent)}%" : $"Downloading {c.Done} / {c.Total}",
            JobStatus.Done => $"Done · {Formatting.Bytes(job.BytesDone)}{(c.Fallback > 0 ? $" · {c.Fallback} {job.FallbackLabel}" : "")}",
            JobStatus.Partial => $"{c.Failed} failed · {c.Done} / {c.Total} saved",
            JobStatus.Canceled => c.Done > 0 ? $"Canceled · {c.Done} / {c.Total} saved" : "Canceled",
            _ => job.Error ?? "Failed",
        };
    }
    string SideOf(JobSnapshot job)
    {
        if (job.Status == JobStatus.Running)
        {
            var eta = Formatting.Eta(job.Eta);
            var speed = job.Speed > 0 ? $"{Formatting.Bytes(job.Speed)}/s" : "";
            return Detailed ? Formatting.JoinNonEmpty(" · ", speed, eta) : eta.Length > 0 ? eta : speed;
        }
        return job.Status == JobStatus.Queued ? "—" : job.Format.Label();
    }
    static JobTrackLine Line(TrackSnapshot t)
    {
        var name = (t.Number.Length > 0 ? $"{(t.Cd.Length > 0 ? $"{t.Cd}-" : "")}{t.Number}. " : "") + t.Title;
        var value = t.Status switch
        {
            TrackStatus.Downloading => t.Total > 0 ? $"{Formatting.JsRound(t.Bytes * 100.0 / t.Total)}%" : Formatting.Bytes(t.Bytes),
            TrackStatus.Done => Formatting.Bytes(t.Total) + (t.Fallback is { } fallback ? $" · {fallback.ToUpperInvariant()}" : ""),
            TrackStatus.Skipped => "already saved",
            TrackStatus.Resolving => "resolving",
            TrackStatus.Canceled => "canceled",
            _ => "",
        };
        var (state, icon) = t.Status switch
        {
            TrackStatus.Done or TrackStatus.Skipped => ("done", IconKind.Check),
            TrackStatus.Error => ("error", IconKind.Alert),
            TrackStatus.Downloading or TrackStatus.Resolving => ("active", IconKind.None),
            TrackStatus.Canceled => ("", IconKind.X),
            _ => ("queued", IconKind.None),
        };
        return new JobTrackLine(name, t.Error, value, state, icon, t.Status is TrackStatus.Downloading or TrackStatus.Resolving);
    }
    [RelayCommand] void Cancel() => _app.Downloads.Cancel(Id);
    [RelayCommand] void Retry() => _app.Downloads.Retry(Id);
    [RelayCommand] void OpenFolder() => _ = _app.OpenJobFolder(Id);
    [RelayCommand] void ToggleDetails() => IsExpanded = !IsExpanded;
    [RelayCommand]
    void Remove()
    {
        IsRemoving = true; 
        DispatcherTimer.RunOnce(() => _app.Downloads.Remove(Id), TimeSpan.FromMilliseconds(180));
    }
    public static void Sync(AppServices app, ObservableCollection<JobCardViewModel> cards, IReadOnlyList<JobSnapshot> jobs, bool detailed)
    {
        var existing = cards.ToDictionary(c => c.Id);
        for (var i = 0; i < jobs.Count; i++)
        {
            var job = jobs[i];
            if (existing.Remove(job.Id, out var card)) card.Update(job);
            else card = new JobCardViewModel(app, job, detailed);
            var at = cards.IndexOf(card);
            if (at == i) continue;
            if (at >= 0) cards.Move(at, i);
            else cards.Insert(i, card);
        }
        foreach (var gone in existing.Values) cards.Remove(gone);
    }
}
public sealed partial class UpNextItemViewModel(AppServices app, int queueIndex, Track track, bool isCurrent) : ViewModelBase
{
    public int QueueIndex { get; } = queueIndex;
    public string Title => track.Title;
    public string Sub { get; } = Formatting.JoinNonEmpty(" · ", track.Album.Title, track.Duration);
    public string Thumb => track.Album.Thumb;
    public string Seed => track.Album.Slug.Length > 0 ? track.Album.Slug : track.Title;
    public bool IsCurrent { get; } = isCurrent;
    [RelayCommand]
    void Jump()
    {
        if (!IsCurrent) app.Player.JumpTo(QueueIndex);
    }
    [RelayCommand] void Remove() => app.Player.RemoveFromQueue(QueueIndex);
}