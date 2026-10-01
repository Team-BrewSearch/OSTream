using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OSTream.Core;
using OSTream.Core.Models;
using OSTream.Frontend;
using OSTream.Frontend.Navigation;
using OSTream.Frontend.ViewModels;
using System;
using System.Threading.Tasks;
namespace OSTream.ViewModels;

public sealed partial class TopBarViewModel : ViewModelBase
{
    private readonly AppServices _app;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(Config.SearchDebounceMs) };
    private bool _syncing;
    public TopBarViewModel(AppServices app)
    {
        _app = app;
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            if (SearchText.Trim().Length >= 2)
            {
                Go(SearchText, _app.Navigator.Current.Name == "search");
            }
        };
        app.Settings.Changed += (_, _) => RefreshSettings();
        RefreshSettings();
    }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValue))]
    public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial bool IsSettingsOpen { get; set; }
    [ObservableProperty] public partial AudioFormat Format { get; private set; }
    [ObservableProperty] public partial ThemeChoice Theme { get; private set; }
    [ObservableProperty] public partial string DownloadDir { get; private set; } = "";
    public bool HasValue => SearchText.Length > 0;
    public bool IsSystemTheme => Theme == ThemeChoice.System;
    public bool IsLightTheme => Theme == ThemeChoice.Light;
    public bool IsDarkTheme => Theme == ThemeChoice.Dark;
    public bool IsFlac => Format == AudioFormat.Flac;
    public bool IsMp3 => Format == AudioFormat.Mp3;
    public bool IsOgg => Format == AudioFormat.Ogg;
    public bool IsWav => Format == AudioFormat.Wav;
    public event Action? FocusSearchRequested;
    partial void OnSearchTextChanged(string value)
    {
        if (_syncing)
        {
            return;
        }

        _debounce.Stop();
        _debounce.Start();
    }
    partial void OnFormatChanged(AudioFormat value)
    {
        OnPropertyChanged(nameof(IsFlac));
        OnPropertyChanged(nameof(IsMp3));
        OnPropertyChanged(nameof(IsOgg));
        OnPropertyChanged(nameof(IsWav));
    }
    partial void OnThemeChanged(ThemeChoice value)
    {
        OnPropertyChanged(nameof(IsSystemTheme));
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsDarkTheme));
    }

    private void RefreshSettings()
    {
        Format = _app.Settings.Value.Format;
        Theme = _app.Settings.Value.Theme;
        DownloadDir = _app.Settings.DownloadDir;
    }
    public void Sync(Route route, bool focused)
    {
        if (focused)
        {
            return;
        }

        _syncing = true;
        SearchText = route.Name == "search" ? route.Param("q") ?? "" : "";
        _syncing = false;
    }
    private void Go(string value, bool replace)
    {
        string q = value.Trim();
        if (q.Length > 0)
        {
            _app.Navigator.Navigate("/search", "q", q, replace);
        }
        else if (_app.Navigator.Current.Name == "search")
        {
            _app.Navigator.Navigate("/", replace: replace);
        }
    }
    [RelayCommand]
    private void Submit()
    {
        _debounce.Stop();
        Go(SearchText, _app.Navigator.Current.Name == "search");
    }
    [RelayCommand]
    public void Clear()
    {
        _debounce.Stop();
        _syncing = true;
        SearchText = "";
        _syncing = false;
        FocusSearchRequested?.Invoke();
        if (_app.Navigator.Current.Name == "search")
        {
            _app.Navigator.Navigate("/");
        }
    }
    public void FocusSearch()
    {
        FocusSearchRequested?.Invoke();
    }

    [RelayCommand]
    private void SetFormat(AudioFormat format)
    {
        _app.Settings.Update(s => s with { Format = format });
    }

    [RelayCommand]
    private void ToggleSettings()
    {
        IsSettingsOpen = !IsSettingsOpen;
    }

    [RelayCommand]
    private void SetTheme(ThemeChoice theme)
    {
        _app.Settings.Update(s => s with { Theme = theme });
    }

    public void CycleTheme(int delta)
    {
        ThemeChoice next = (ThemeChoice)(((int)Theme + delta + 3) % 3);
        _app.Settings.Update(s => s with { Theme = next });
    }
    [RelayCommand]
    private async Task ChangeDownloadDir()
    {
        string? picked = await _app.Shell.PickFolderAsync("Choose the download folder", _app.Settings.DownloadDir);
        if (picked is null)
        {
            return;
        }

        _app.Settings.Update(s => s with { DownloadDir = picked });
        _app.Toasts.Toast("Download folder changed", ToastKind.Success, picked);
    }
    [RelayCommand]
    private void OpenDownloadDir()
    {
        _ = _app.OpenJobFolder(null);
    }

    [RelayCommand]
    private void Home()
    {
        _app.Navigator.Navigate("/");
    }
}
