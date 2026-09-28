using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OSTream.Core;
using OSTream.Core.Models;
using OSTream.Frontend;
using OSTream.Frontend.Navigation;
using OSTream.Frontend.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
namespace OSTream.ViewModels;

public sealed partial class RecentViewModel : PageViewModel
{
    private readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromSeconds(60) };
    private string _signature = "";
    public RecentViewModel(AppServices app) : base(app)
    {
        app.Library.Changed += OnLibrary;
        _ticker.Tick += (_, _) => Tracks?.RefreshMeta();
        _ticker.Start();
    }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial TrackListViewModel? Tracks { get; private set; }
    public bool IsEmpty => Tracks is null;
    private void OnLibrary(LibraryData old, LibraryData now)
    {
        if (!ReferenceEquals(old.Recent, now.Recent))
        {
            Render();
        }
    }
    private void Render()
    {
        IReadOnlyList<Track> recent = App.Library.Value.Recent;
        string signature = string.Join(",", recent.Select(t => $"{t.Id}@{t.PlayedAt}"));
        if (signature == _signature)
        {
            return;
        }

        _signature = signature;
        Tracks?.Dispose();
        Tracks = recent.Count == 0 ? null : new TrackListViewModel(App, recent, new TrackListOptions
        {
            ShowAlbum = true,
            MetaLabel = "Played",
            Meta = (t, _) => t.PlayedAt is { } at ? Formatting.TimeAgo(at) : "",
            OnPlay = index => App.Player.PlayList(App.Library.Value.Recent, index),
        });
    }
    public override void Update(Route route)
    {
        Render();
    }

    [RelayCommand]
    private void PlayAll()
    {
        App.Player.PlayList(App.Library.Value.Recent);
    }

    [RelayCommand]
    private void Browse()
    {
        App.Navigator.Navigate("/");
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task Clear()
    {
        if (await App.Dialogs.ConfirmAsync("Clear listening history?",
                "This removes every track from Recently played. Favorites and playlists are not affected.", "Clear history", danger: true))
        {
            App.Library.ClearRecent();
        }
    }
    public override void Dispose()
    {
        _ticker.Stop();
        App.Library.Changed -= OnLibrary;
        Tracks?.Dispose();
    }
}