using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using OSTream.Core;
using OSTream.Frontend.Controls;
using OSTream.Frontend.ViewModels;
using OSTream.Frontend.Views.Overlays;
using OSTream.Services;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
namespace OSTream.Views.Overlays;

public partial class OverlaysView : UserControl
{
    private MainViewModel? _vm;
    private IInputElement? _focusBeforeDialog;
    private int _lightboxLoad;
    public OverlaysView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as MainViewModel);
        Menu.SizeChanged += (_, _) => PositionMenu();
        DialogLayer.PointerPressed += (_, e) =>
        {
            if (e.Source == DialogLayer)
            {
                _vm?.Dialogs.Current?.Cancel();
            }
        };
        DialogInput.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Enter)
            {
                return;
            }
            (_vm?.Dialogs.Current)?.ConfirmCommand.Execute(null);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        LightboxStage.PointerPressed += (_, e) =>
        {
            if (e.Source == LightboxStage)
            {
                _vm?.Lightbox.Current?.Close();
            }
        };
        ToastHover.Attach(Toasts);
    }
    private void Attach(MainViewModel? vm)
    {
        _vm = vm;
        if (vm is null)
        {
            return;
        }
        vm.Menus.PropertyChanged += OnMenuChanged;
        vm.Menus.AnchorChanged += (anchor, open) => (anchor as StyledElement)?.Classes.Set("expanded", open);
        vm.Dialogs.PropertyChanged += OnDialogChanged;
        vm.Lightbox.PropertyChanged += OnLightboxChanged;
    }
    private void OnMenuChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MenuService.Entries) || _vm is null)
        {
            return;
        }
        if (!_vm.Menus.IsOpen)
        {
            return;
        }
        MenuScroll.MaxHeight = Math.Max(120, (TopLevel.GetTopLevel(this)?.ClientSize.Height ?? 800) * 0.6);
        PositionMenu();
        Entrance.Play(Menu, "pop-in");
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (MenuButtons().FirstOrDefault() is { } first)
            {
                QuietFocus.Move(first);
            }
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }
    private Button[] MenuButtons()
    {
        return MenuItems.GetVisualDescendants().OfType<Button>().Where(b => b.IsVisible && b.Classes.Contains("menu-item")).ToArray();
    }
    private void PositionMenu()
    {
        if (_vm?.Menus.Anchor is not Visual anchor || !_vm.Menus.IsOpen)
        {
            return;
        }
        Point? origin = anchor.TranslatePoint(new Point(0, 0), MenuLayer);
        if (origin is not { } a)
        {
            return;
        }
        Size size = Menu.DesiredSize;
        if (size.Width <= 0)
        {
            Menu.Measure(Size.Infinity);
        }
        size = Menu.DesiredSize;
        Rect bounds = MenuLayer.Bounds;
        const double gap = 6;
        double anchorRight = a.X + anchor.Bounds.Width;
        double anchorBottom = a.Y + anchor.Bounds.Height;
        double top = anchorBottom + gap;
        if (top + size.Height > bounds.Height - 8)
        {
            top = Math.Max(8, a.Y - size.Height - gap);
        }
        double left = anchorRight - size.Width;
        if (left < 8)
        {
            left = Math.Min(a.X, bounds.Width - size.Width - 8);
        }
        Canvas.SetLeft(Menu, Math.Round(Math.Max(8, left)));
        Canvas.SetTop(Menu, Math.Round(top));
    }
    public bool HandleMenuKey(KeyEventArgs e)
    {
        if (_vm is not { Menus.IsOpen: true } vm)
        {
            return false;
        }
        Button[] buttons = MenuButtons();
        int index = Array.FindIndex(buttons, b => b.IsFocused);
        switch (e.Key)
        {
            case Key.Escape:
                InputElement? anchor = vm.Menus.Anchor as InputElement;
                vm.Menus.Close();
                _ = (anchor?.Focus(NavigationMethod.Tab));
                return true;
            case Key.Down or Key.Up when buttons.Length > 0:
                int next = e.Key == Key.Down ? index + 1 : index - 1;
                _ = buttons[(next + buttons.Length) % buttons.Length].Focus(NavigationMethod.Tab);
                return true;
            case Key.Tab:
                vm.Menus.Close();
                return false;
        }
        return false;
    }
    public bool IsInMenu(Visual? visual)
    {
        return visual is not null && (visual == Menu || Menu.IsVisualAncestorOf(visual));
    }
    private void OnDialogChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DialogService.Current) || _vm is null)
        {
            return;
        }
        if (_vm.Dialogs.Current is { } dialog)
        {
            _focusBeforeDialog = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (dialog.IsPrompt)
                {
                    _ = DialogInput.Focus(NavigationMethod.Tab);
                    DialogInput.SelectAll();
                }
                else
                {
                    QuietFocus.Move(DialogConfirm);
                }
            }, Avalonia.Threading.DispatcherPriority.Loaded);
            Entrance.Play(Dialog, "pop-in");
        }
        else
        {
            _ = (_focusBeforeDialog?.Focus());
            _focusBeforeDialog = null;
        }
    }
    public bool HandleDialogKey(KeyEventArgs e)
    {
        if (_vm?.Dialogs.Current is not { } dialog)
        {
            return false;
        }
        if (e.Key == Key.Escape)
        {
            dialog.Cancel();
            return true;
        }
        return false;
    }
    private void OnLightboxChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LightboxService.Current) || _vm?.Lightbox.Current is not { } box)
        {
            return;
        }
        box.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(LightboxViewModel.Source))
            {
                LoadLightbox(box).Forget(_vm.App.Log);
            }
        };
        LoadLightbox(box).Forget(_vm.App.Log);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => QuietFocus.Move(LightboxClose), Avalonia.Threading.DispatcherPriority.Loaded);
    }
    private async Task LoadLightbox(LightboxViewModel box)
    {
        int request = ++_lightboxLoad;
        LightboxSpinner.IsVisible = true;
        LightboxImage.Source = null;
        int width = (int)((TopLevel.GetTopLevel(this)?.ClientSize.Width ?? 1600) * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1));
        Bitmap? bitmap = ArtView.Loader is { } loader ? await loader.LoadAsync(box.Source, Math.Min(width, 2048)) : null;
        if (request != _lightboxLoad)
        {
            return;
        }
        LightboxSpinner.IsVisible = false;
        LightboxImage.Source = bitmap;
    }
    public bool HandleLightboxKey(KeyEventArgs e)
    {
        if (_vm?.Lightbox.Current is not { } box)
        {
            return false;
        }
        switch (e.Key)
        {
            case Key.Escape: box.Close(); return true;
            case Key.Left: box.Prev(); return true;
            case Key.Right: box.Next(); return true;
        }
        return false;
    }
}