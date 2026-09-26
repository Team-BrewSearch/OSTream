using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OSTream.Core;
using OSTream.Core.Downloads;
using OSTream.Core.Models;
using OSTream.Frontend;
using OSTream.Frontend.Controls;
using OSTream.Frontend.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
namespace OSTream.ViewModels;

public sealed record JobTrackLine(string Name, string? Error, string Value, string State, IconKind Icon, bool Busy)
{
    public bool HasIcon => Icon != IconKind.None;
    public bool IsQueued => State == "queued";
}
public sealed partial class JobCardViewModel : ViewModelBase
{
    private readonly AppServices _app;
    private JobSnapshot? _last;
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
        if (!ReferenceEquals(job, _last))
        {
            Render(job);
        }
    }
    private void Render(JobSnapshot job)
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
            JobTrackLine[] lines = job.Tracks.Select(Line).ToArray();
            if (!lines.SequenceEqual(TrackLines))
            {
                TrackLines = lines;
            }
        }
    }
    partial void OnIsExpandedChanged(bool value)
    {
        if (_last is not null)
        {
            Render(_last);
        }
    }
    private static string StatusOf(JobSnapshot job)
    {
        JobCounts c = job.Counts;
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
    private string SideOf(JobSnapshot job)
    {
        if (job.Status == JobStatus.Running)
        {
            string eta = Formatting.Eta(job.Eta);
            string speed = job.Speed > 0 ? $"{Formatting.Bytes(job.Speed)}/s" : "";
            return Detailed ? Formatting.JoinNonEmpty(" · ", speed, eta) : eta.Length > 0 ? eta : speed;
        }
        return job.Status == JobStatus.Queued ? "—" : job.Format.Label();
    }
    private static JobTrackLine Line(TrackSnapshot t)
    {
        string name = (t.Number.Length > 0 ? $"{(t.Cd.Length > 0 ? $"{t.Cd}-" : "")}{t.Number}. " : "") + t.Title;
        string value = t.Status switch
        {
            TrackStatus.Downloading => t.Total > 0 ? $"{Formatting.JsRound(t.Bytes * 100.0 / t.Total)}%" : Formatting.Bytes(t.Bytes),
            TrackStatus.Done => Formatting.Bytes(t.Total) + (t.Fallback is { } fallback ? $" · {fallback.ToUpperInvariant()}" : ""),
            TrackStatus.Skipped => "already saved",
            TrackStatus.Resolving => "resolving",
            TrackStatus.Canceled => "canceled",
            _ => "",
        };
        (string? state, IconKind icon) = t.Status switch
        {
            TrackStatus.Done or TrackStatus.Skipped => ("done", IconKind.Check),
            TrackStatus.Error => ("error", IconKind.Alert),
            TrackStatus.Downloading or TrackStatus.Resolving => ("active", IconKind.None),
            TrackStatus.Canceled => ("", IconKind.X),
            _ => ("queued", IconKind.None),
        };
        return new JobTrackLine(name, t.Error, value, state, icon, t.Status is TrackStatus.Downloading or TrackStatus.Resolving);
    }
    [RelayCommand]
    private void Cancel()
    {
        _app.Downloads.Cancel(Id);
    }
    [RelayCommand]
    private void Retry()
    {
        _app.Downloads.Retry(Id);
    }
    [RelayCommand]
    private void OpenFolder()
    {
        _ = _app.OpenJobFolder(Id);
    }
    [RelayCommand]
    private void ToggleDetails()
    {
        IsExpanded = !IsExpanded;
    }
    [RelayCommand]
    private void Remove()
    {
        IsRemoving = true;
        _ = DispatcherTimer.RunOnce(() => _app.Downloads.Remove(Id), TimeSpan.FromMilliseconds(180));
    }
    public static void Sync(AppServices app, ObservableCollection<JobCardViewModel> cards, IReadOnlyList<JobSnapshot> jobs, bool detailed)
    {
        Dictionary<string, JobCardViewModel> existing = cards.ToDictionary(c => c.Id);
        for (int i = 0; i < jobs.Count; i++)
        {
            JobSnapshot job = jobs[i];
            if (existing.Remove(job.Id, out JobCardViewModel? card))
            {
                card.Update(job);
            }
            else
            {
                card = new JobCardViewModel(app, job, detailed);
            }
            int at = cards.IndexOf(card);
            if (at == i)
            {
                continue;
            }
            if (at >= 0)
            {
                cards.Move(at, i);
            }
            else
            {
                cards.Insert(i, card);
            }
        }
        foreach (JobCardViewModel? gone in existing.Values)
        {
            _ = cards.Remove(gone);
        }
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
    private void Jump()
    {
        if (!IsCurrent)
        {
            app.Player.JumpTo(QueueIndex);
        }
    }
    [RelayCommand]
    private void Remove()
    {
        app.Player.RemoveFromQueue(QueueIndex);
    }
}