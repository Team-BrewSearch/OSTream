using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OSTream.Core;
using OSTream.Frontend.Controls;
using System;
using System.Collections.ObjectModel;
namespace OSTream.Services;

public sealed partial class ToastItem : ObservableObject
{
    private readonly ToastService _owner;
    private readonly TimeSpan _duration;
    private readonly DispatcherTimer _timer;
    internal ToastItem(ToastService owner, string message, ToastKind kind, string detail, ToastAction? action)
    {
        _owner = owner;
        Message = message;
        Kind = kind;
        Detail = detail;
        Action = action;
        _duration = TimeSpan.FromMilliseconds(kind == ToastKind.Error ? 7000 : action is not null ? 6000 : 3500);
        _timer = new DispatcherTimer { Interval = _duration };
        _timer.Tick += (_, _) => Dismiss();
        _timer.Start();
    }
    public string Message { get; }
    public string Detail { get; }
    public ToastKind Kind { get; }
    public ToastAction? Action { get; }
    public bool HasDetail => Detail.Length > 0;
    public bool HasAction => Action is not null;
    public IconKind Icon => Kind switch { ToastKind.Success => IconKind.Check, ToastKind.Error => IconKind.Alert, _ => IconKind.Info };
    public double IconSize => Kind switch { ToastKind.Success => 12, ToastKind.Error => 14, _ => 14 };
    [ObservableProperty] public partial bool IsLeaving { get; set; }
    [ObservableProperty] public partial bool IsPaused { get; private set; }
    public void Pause()
    {
        IsPaused = true;
        _timer.Stop();
    }
    public void Resume()
    {
        IsPaused = false;
        if (IsLeaving)
        {
            return;
        }

        _timer.Interval = _duration;
        _timer.Start();
    }
    [RelayCommand]
    private void Run()
    {
        Action?.Run();
        Dismiss();
    }
    [RelayCommand]
    public void Dismiss()
    {
        if (IsLeaving)
        {
            return;
        }

        _timer.Stop();
        IsLeaving = true;
        _ = DispatcherTimer.RunOnce(() => _owner.Items.Remove(this), TimeSpan.FromMilliseconds(200));
    }
    internal void Drop()
    {
        _timer.Stop();
    }
}
public sealed class ToastService : INotifier
{
    public ObservableCollection<ToastItem> Items { get; } = [];
    public void Toast(string message, ToastKind kind = ToastKind.Info, string detail = "", ToastAction? action = null)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Toast(message, kind, detail, action));
            return;
        }
        Items.Add(new ToastItem(this, message, kind, detail, action));
        while (Items.Count > Config.MaxToasts)
        {
            Items[0].Drop();
            Items.RemoveAt(0);
        }
    }
    public void Error(Exception error, string prefix = "")
    {
        Toast(prefix.Length > 0 ? prefix : error.Message, ToastKind.Error, prefix.Length > 0 ? error.Message : "");
    }
}