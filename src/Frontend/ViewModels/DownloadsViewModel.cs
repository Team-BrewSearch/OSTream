using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OSTream.Core;
using OSTream.Core.Models;
using OSTream.Navigation;
namespace OSTream.ViewModels;
public sealed partial class DownloadsViewModel : PageViewModel
{
    public DownloadsViewModel(AppServices app) : base(app)
    {
        app.Downloads.Changed += Render;
        app.Settings.Changed += OnSettings;
        Render();
    }
    public ObservableCollection<JobCardViewModel> Jobs { get; } = [];
    [ObservableProperty] public partial string Summary { get; private set; } = "";
    [ObservableProperty] public partial string Path { get; private set; } = "";
    [ObservableProperty] public partial bool CanClear { get; private set; }
    [ObservableProperty] public partial bool IsEmpty { get; private set; }
    void OnSettings(SettingsData old, SettingsData now)
    {
        if (old.DownloadDir != now.DownloadDir) Render();
    }
    void Render()
    {
        var jobs = App.Downloads.Jobs;
        var active = jobs.Count(j => j.IsActive);
        Summary = jobs.Count > 0 ? Formatting.Plural(jobs.Count, "download") + (active > 0 ? $" · {active} active" : "") : "";
        Path = $"Saving to {App.Settings.DownloadDir}";
        CanClear = jobs.Count - active > 0;
        IsEmpty = jobs.Count == 0;
        JobCardViewModel.Sync(App, Jobs, jobs, detailed: true);
    }
    public override void Update(Route route) => App.Downloads.Refresh();
    [RelayCommand] void OpenFolder() => _ = App.OpenJobFolder(null);
    [RelayCommand] void ClearFinished() => App.Downloads.ClearFinished();
    [RelayCommand] void Browse() => App.Navigator.Navigate("/");
    public override void Dispose()
    {
        App.Downloads.Changed -= Render;
        App.Settings.Changed -= OnSettings;
    }
}