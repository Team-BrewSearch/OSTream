using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OSTream.Core;
using OSTream.Core.Models;
namespace OSTream.ViewModels;
public sealed partial class RailViewModel : ViewModelBase
{
    readonly AppServices _app;
    int _upNextVersion = -1;
    public RailViewModel(AppServices app)
    {
        _app = app;
        app.Downloads.Changed += RenderDownloads;
        app.Player.Changed += RenderUpNext;
        app.Settings.Changed += (old, now) =>
        {
            if (old.RailTab != now.RailTab) OnPropertyChanged(nameof(IsDownloadsTab));
            if (old.RailTab != now.RailTab) OnPropertyChanged(nameof(IsUpNextTab));
            if (old.RailCollapsed != now.RailCollapsed) OnPropertyChanged(nameof(IsVisible));
        };
        RenderDownloads();
        RenderUpNext();
    }
    public ObservableCollection<JobCardViewModel> Jobs { get; } = [];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    public partial bool IsNarrow { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    public partial bool IsDrawerOpen { get; set; }
    public bool IsVisible => IsNarrow ? IsDrawerOpen : !_app.Settings.Value.RailCollapsed;
    public bool IsDownloadsTab => _app.Settings.Value.RailTab == RailTab.Downloads;
    public bool IsUpNextTab => _app.Settings.Value.RailTab == RailTab.UpNext;
    [ObservableProperty] public partial int DownloadsCount { get; private set; }
    [ObservableProperty] public partial bool HasJobs { get; private set; }
    [ObservableProperty] public partial string OverallText { get; private set; } = "";
    [ObservableProperty] public partial double OverallPercent { get; private set; }
    [ObservableProperty] public partial bool CanClearFinished { get; private set; }
    [ObservableProperty] public partial UpNextItemViewModel? NowPlaying { get; private set; }
    [ObservableProperty] public partial IReadOnlyList<UpNextItemViewModel> UpNext { get; private set; } = [];
    [ObservableProperty] public partial int UpNextCount { get; private set; }
    public bool HasNowPlaying => NowPlaying is not null;
    public bool HasUpNext => UpNext.Count > 0;
    public string NextLabel => $"NEXT UP · {UpNextCount}";
    partial void OnNowPlayingChanged(UpNextItemViewModel? value) => OnPropertyChanged(nameof(HasNowPlaying));
    partial void OnUpNextChanged(IReadOnlyList<UpNextItemViewModel> value) => OnPropertyChanged(nameof(HasUpNext));
    partial void OnUpNextCountChanged(int value) => OnPropertyChanged(nameof(NextLabel));
    public void Toggle(bool? force = null)
    {
        if (IsNarrow) IsDrawerOpen = force ?? !IsDrawerOpen;
        else _app.Settings.Update(s => s with { RailCollapsed = !(force ?? s.RailCollapsed) });
    }
    [RelayCommand] void Close() => Toggle(false);
    [RelayCommand] void ShowTab(string tab) => _app.Settings.Update(s => s with { RailTab = tab == "upnext" ? RailTab.UpNext : RailTab.Downloads });
    [RelayCommand] void ClearFinished() => _app.Downloads.ClearFinished();
    [RelayCommand] void ViewAll() => _app.Navigator.Navigate("/downloads");
    [RelayCommand] void ClearUpNext() => _app.Player.ClearUpNext();
    void RenderDownloads()
    {
        var jobs = _app.Downloads.Jobs;
        var active = jobs.Count(j => j.IsActive);
        DownloadsCount = active > 0 ? active : jobs.Count;
        JobCardViewModel.Sync(_app, Jobs, jobs, detailed: false);
        HasJobs = jobs.Count > 0;
        if (!HasJobs) return;
        var totalTracks = jobs.Sum(j => j.Counts.Total);
        var doneTracks = jobs.Sum(j => j.Counts.Done);
        var bytesTotal = jobs.Sum(j => j.BytesTotal);
        var bytesDone = jobs.Sum(j => j.BytesDone);
        var pct = bytesTotal > 0 ? Formatting.JsRound(bytesDone * 100.0 / bytesTotal) : Formatting.JsRound(doneTracks * 100.0 / Math.Max(1, totalTracks));
        OverallText = $"{doneTracks} / {totalTracks} · {pct}%";
        OverallPercent = pct;
        CanClearFinished = jobs.Count - active > 0;
    }
    void RenderUpNext()
    {
        var player = _app.Player;
        if (player.Queue.Version == _upNextVersion) return;
        _upNextVersion = player.Queue.Version;
        var upcoming = player.UpNext().ToList();
        UpNextCount = upcoming.Count;
        NowPlaying = player.Current is { } current ? new UpNextItemViewModel(_app, player.CurrentQueueIndex, current, true) : null;
        UpNext = upcoming.Take(Config.UpNextLimit).Select(u => new UpNextItemViewModel(_app, u.QueueIndex, u.Track, false)).ToArray();
    }
}