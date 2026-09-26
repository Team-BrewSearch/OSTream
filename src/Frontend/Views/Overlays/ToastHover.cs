using Avalonia.Controls;
using Avalonia.Input;
using OSTream.Services;
namespace OSTream.Views.Overlays;
public static class ToastHover
{
    public static void Attach(ItemsControl toasts)
    {
        toasts.ContainerPrepared += (_, e) =>
        {
            e.Container.PointerEntered += OnEntered;
            e.Container.PointerExited += OnExited;
        };
        toasts.ContainerClearing += (_, e) =>
        {
            e.Container.PointerEntered -= OnEntered;
            e.Container.PointerExited -= OnExited;
        };
    }
    static void OnEntered(object? sender, PointerEventArgs e) => ((sender as Control)?.DataContext as ToastItem)?.Pause();
    static void OnExited(object? sender, PointerEventArgs e) => ((sender as Control)?.DataContext as ToastItem)?.Resume();
}