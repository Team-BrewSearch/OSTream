using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OSTream.Core.Downloads;
using OSTream.Core.Models;
using OSTream.Frontend;
using OSTream.Frontend.Controls;
using OSTream.Frontend.ViewModels;
using System.Collections.Generic;
using System.Linq;
namespace OSTream.ViewModels;

public sealed record QueueRow(string Title, string Value, double Percent, bool Pending);
public sealed partial class SidebarViewModel : ViewModelBase
{
    private const int MaxRows = 3;
    private readonly AppServices _app;
    private readonly NavItemViewModel _downloads, _favorites, _playlists;
    public SidebarViewModel(AppServices app)
    {
        _app = app;
        Library =
        [
            new("discover", "/", "Discover", IconKind.Home, GoCommand),
            _downloads = new("downloads", "/downloads", "Downloads", IconKind.Downloads, GoCommand) { LiveBadge = true },
            _favorites = new("favorites", "/favorites", "Favorites", IconKind.Heart, GoCommand),
        ];
        Collection =
        [
            new("recent", "/recent", "Recently Played", IconKind.Clock, GoCommand) { Tip = "Recently played" },
            _playlists = new("playlists", "/playlists", "Playlists", IconKind.Book, GoCommand),
        ];
        app.Downloads.Changed += RenderQueue;
        app.Library.Changed += (_, _) => RenderLibrary();
        RenderQueue();
        RenderLibrary();
        OnActiveChanged(Active);
    }
    public IReadOnlyList<NavItemViewModel> Library { get; }
    public IReadOnlyList<NavItemViewModel> Collection { get; }
    [ObservableProperty] public partial string? Active { get; set; } = "discover";
    [ObservableProperty] public partial IReadOnlyList<QueueRow> QueueRows { get; private set; } = [];
    [ObservableProperty] public partial string MoreText { get; private set; } = "";
    public bool HasQueue => QueueRows.Count > 0;
    public bool HasMore => MoreText.Length > 0;
    partial void OnActiveChanged(string? value)
    {
        foreach (NavItemViewModel? item in Library.Concat(Collection))
        {
            item.IsActive = item.Id == value;
        }
    }
    partial void OnQueueRowsChanged(IReadOnlyList<QueueRow> value)
    {
        OnPropertyChanged(nameof(HasQueue));
    }
    partial void OnMoreTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasMore));
    }
    private void RenderQueue()
    {
        List<JobSnapshot> jobs = _app.Downloads.Jobs.Where(j => j.IsActive).ToList();
        _downloads.Badge = jobs.Count;
        QueueRows = jobs.Take(MaxRows).Select(job => job.Status == JobStatus.Queued
            ? new QueueRow(job.Title, "queued", 0, true)
            : new QueueRow(job.Title, $"{job.Counts.Done}/{job.Counts.Total}", job.Percent, false)).ToArray();
        MoreText = jobs.Count > MaxRows ? $"+{jobs.Count - MaxRows} more" : "";
    }
    private void RenderLibrary()
    {
        LibraryData lib = _app.Library.Value;
        _favorites.Badge = lib.FavAlbums.Count + lib.FavTracks.Count;
        _playlists.Badge = lib.Playlists.Count;
    }
    [RelayCommand]
    private void Go(string path)
    {
        _app.Navigator.Navigate(path);
    }
}