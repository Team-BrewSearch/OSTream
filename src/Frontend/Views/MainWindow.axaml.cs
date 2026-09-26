using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OSTream.Controls;
using OSTream.Core.Models;
using OSTream.ViewModels;
using OSTream.Views.Components;
namespace OSTream.Views;
public partial class MainWindow : Window
{
    MainViewModel? _vm;
    bool _closingConfirmed;
    System.IDisposable? _hideRail;
    ScrollAnimation? _scroll;
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnPreviewKey, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnPreviewPointer, RoutingStrategies.Tunnel, handledEventsToo: true);
        Scrim.PointerPressed += (_, _) => _vm?.Rail.Toggle(false);
        ContentScroll.ScrollChanged += (_, e) =>
        {
            if (e.OffsetDelta != default && _vm?.Menus.IsOpen == true && !Overlays.IsKeyboardFocusWithin) _vm.Menus.Close();
        };
        SizeChanged += (_, e) =>
        {
            ApplyBreakpoints(e.NewSize.Width);
            _vm?.Menus.Close();
        };
    }
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not MainViewModel vm) return;
        _vm = vm;
        vm.ScrollRequested += OnScrollRequested;
        vm.Rail.PropertyChanged += OnRailChanged;
        TopBar.FocusChanged = focused => vm.SearchFocused = focused;
        RestoreBounds(vm.App.Settings.Value.Window);
        ApplyBreakpoints(Width);
    }
    void ApplyBreakpoints(double width)
    {
        if (_vm is null || double.IsNaN(width) || width <= 0) return;
        var narrow = width <= 1280;
        Root.Classes.Set("w1280", narrow);
        Root.Classes.Set("w1100", width <= 1100);
        Root.Classes.Set("compact", width <= 960);
        Root.Classes.Set("mobile", width <= 720);
        foreach (var target in new StyledElement[] { Sidebar, PlayerBar, PageHost, TopBar })
        {
            target.Classes.Set("w1100", width <= 1100);
            target.Classes.Set("compact", width <= 960);
            target.Classes.Set("mobile", width <= 720);
        }
        if (_vm.Rail.IsNarrow != narrow)
        {
            _vm.Rail.IsDrawerOpen = false;
            _vm.Rail.IsNarrow = narrow;
        }
        Rail.Width = width <= 720 ? Math.Min(width, 340) : 300;
        UpdateRail(animate: false);
    }
    void OnRailChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RailViewModel.IsVisible) or nameof(RailViewModel.IsDrawerOpen) or nameof(RailViewModel.IsNarrow)) UpdateRail(animate: true);
    }
    void UpdateRail(bool animate)
    {
        if (_vm is null) return;
        var rail = _vm.Rail;
        Rail.Classes.Set("drawer", rail.IsNarrow);
        _hideRail?.Dispose();
        if (!rail.IsNarrow)
        {
            Rail.Transitions = null;
            Rail.RenderTransform = null;
            Rail.IsVisible = rail.IsVisible;
            Scrim.Classes.Set("open", false);
            return;
        }
        var open = rail.IsDrawerOpen;
        Scrim.Classes.Set("open", open);
        var slide = TransformOperations.CreateBuilder(1);
        slide.AppendTranslate(Rail.Width, 0);
        var shift = slide.Build();
        if (open)
        {
            if (!Rail.IsVisible)
            {
                var transitions = Rail.Transitions;
                Rail.Transitions = null;
                Rail.RenderTransform = shift;
                Rail.IsVisible = true;
                Rail.Transitions = transitions;
            }
            Dispatcher.UIThread.Post(() => Rail.RenderTransform = TransformOperations.Identity, DispatcherPriority.Render);
        }
        else if (animate && Rail.IsVisible)
        {
            Rail.RenderTransform = shift;
            _hideRail = DispatcherTimer.RunOnce(() => Rail.IsVisible = false, TimeSpan.FromMilliseconds(280));
        }
        else
        {
            Rail.RenderTransform = shift;
            Rail.IsVisible = false;
        }
    }
    void OnScrollRequested(ScrollTarget target, bool instant)
    {
        Dispatcher.UIThread.Post(() =>
        {
            double y = 0;
            if (target == ScrollTarget.AlbumPanel)
            {
                var panel = PageHost.GetVisualDescendants().OfType<AlbumPanelView>().FirstOrDefault(p => p.IsVisible);
                if (panel?.TranslatePoint(new Point(0, 0), ContentScroll.Presenter ?? (Visual)ContentScroll) is not { } p) return;
                y = ContentScroll.Offset.Y + p.Y - 28;
            }
            ScrollTo(y, instant);
        }, DispatcherPriority.Loaded);
    }
    void ScrollTo(double y, bool instant)
    {
        y = Math.Clamp(y, 0, Math.Max(0, ContentScroll.Extent.Height - ContentScroll.Viewport.Height));
        _scroll?.Cancel();
        if (instant)
        {
            ContentScroll.Offset = new Vector(ContentScroll.Offset.X, y);
            return;
        }
        _scroll = new ScrollAnimation(ContentScroll, y);
    }
    sealed class ScrollAnimation
    {
        readonly ScrollViewer _viewer;
        readonly double _from, _to, _duration;
        TimeSpan? _start;
        bool _canceled;
        public ScrollAnimation(ScrollViewer viewer, double to)
        {
            _viewer = viewer;
            _from = viewer.Offset.Y;
            _to = to;
            _duration = Math.Clamp(Math.Abs(to - _from) / 4, 200, 450);
            TopLevel.GetTopLevel(viewer)?.RequestAnimationFrame(Frame);
        }
        public void Cancel() => _canceled = true;
        void Frame(TimeSpan now)
        {
            if (_canceled) return;
            _start ??= now;
            var t = Math.Min(1, (now - _start.Value).TotalMilliseconds / _duration);
            var eased = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
            _viewer.Offset = new Vector(_viewer.Offset.X, _from + (_to - _from) * eased);
            if (t < 1) TopLevel.GetTopLevel(_viewer)?.RequestAnimationFrame(Frame);
        }
    }
    void OnPreviewPointer(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null) return;
        var point = e.GetCurrentPoint(this).Properties;
        if (point.IsXButton1Pressed)
        {
            _vm.App.Navigator.Back();
            e.Handled = true;
            return;
        }
        if (point.IsXButton2Pressed)
        {
            _vm.App.Navigator.Forward();
            e.Handled = true;
            return;
        }
        var source = e.Source as Visual;
        if (source?.GetSelfAndVisualAncestors().OfType<InputElement>().Any(el => el.Focusable && el.IsEffectivelyEnabled) != true)
            FocusManager?.Focus(null!, NavigationMethod.Pointer);
        if (_vm.Menus.IsOpen && !Overlays.IsInMenu(source) && !IsWithin(source, _vm.Menus.Anchor as Visual)) _vm.Menus.Close();
        if (_vm.TopBar.IsSettingsOpen && !IsWithin(source, TopBar.FlyoutPanel) && !IsWithin(source, TopBar.SettingsAnchor))
            _vm.TopBar.IsSettingsOpen = false;
    }
    static bool IsWithin(Visual? visual, Visual? container) =>
        visual is not null && container is not null && (visual == container || container.IsVisualAncestorOf(visual));
    void OnPreviewKey(object? sender, KeyEventArgs e)
    {
        if (_vm is not { } vm) return;
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (Overlays.HandleDialogKey(e) || Overlays.HandleLightboxKey(e) || Overlays.HandleMenuKey(e))
        {
            e.Handled = true;
            return;
        }
        if (vm.App.Dialogs.Current is not null || vm.App.Lightbox.Current is not null) return;
        if (ctrl && e.Key == Key.K)
        {
            vm.TopBar.FocusSearch();
            e.Handled = true;
            return;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key is Key.Left or Key.Right)
        {
            if (e.Key == Key.Left) vm.App.Navigator.Back();
            else vm.App.Navigator.Forward();
            e.Handled = true;
            return;
        }
        if (ctrl || e.KeyModifiers.HasFlag(KeyModifiers.Alt)) return;
        var focused = FocusManager?.GetFocusedElement() as Visual;
        if (focused is TextBox || focused?.FindAncestorOfType<TextBox>() is not null) return;
        var onControl = focused is Button or SliderBar;
        var ownsArrows = focused is SliderBar || focused is Button b && (b.Classes.Contains("seg") || b.Classes.Contains("theme-option"));
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var player = vm.App.Player;
        if (e.KeySymbol == "/")
        {
            vm.TopBar.FocusSearch();
            e.Handled = true;
            return;
        }
        switch (e.Key)
        {
            case Key.Space:
                if (onControl) return;
                player.Toggle();
                break;
            case Key.Left or Key.Right:
                if (ownsArrows) return;
                if (shift)
                {
                    if (e.Key == Key.Left) player.Prev();
                    else player.Next();
                }
                else
                {
                    player.SeekBy(e.Key == Key.Left ? -5 : 5);
                }
                break;
            case Key.Up or Key.Down when shift:
                vm.Player.NudgeVolume(e.Key == Key.Up ? 0.05 : -0.05);
                break;
            case Key.M:
                player.ToggleMute();
                break;
            case Key.S:
                player.ToggleShuffle();
                break;
            case Key.R:
                player.CycleRepeat();
                break;
            case Key.Q:
                vm.Rail.Toggle();
                break;
            case Key.Escape:
                if (vm.TopBar.IsSettingsOpen)
                {
                    vm.TopBar.IsSettingsOpen = false;
                    break;
                }
                if (vm.Rail.IsNarrow && vm.Rail.IsDrawerOpen)
                {
                    vm.Rail.Toggle(false);
                    break;
                }
                return;
            default:
                return;
        }
        e.Handled = true;
    }
    void RestoreBounds(WindowBounds? bounds)
    {
        if (bounds is not { Width: > 0, Height: > 0 } b) return;
        Width = Math.Max(MinWidth, b.Width);
        Height = Math.Max(MinHeight, b.Height);
        var onScreen = Screens.All.Any(s => s.WorkingArea.Contains(new PixelPoint(b.X + 40, b.Y + 20)));
        if (onScreen)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(b.X, b.Y);
        }
        if (b.Maximized) WindowState = WindowState.Maximized;
    }
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_vm is not { } vm) return;
        if (!_closingConfirmed && vm.App.Manager.HasActive)
        {
            e.Cancel = true;
            var quit = await vm.App.Dialogs.ConfirmAsync("Quit while downloading?",
                "Downloads in progress stop when OSTream closes. Files that finished are kept; retry the rest later.", "Quit", danger: true);
            if (!quit) return;
            _closingConfirmed = true;
            Close();
            return;
        }
        var maximized = WindowState == WindowState.Maximized;
        var saved = vm.App.Settings.Value.Window;
        var bounds = maximized && saved is not null
            ? saved with { Maximized = true }
            : new WindowBounds(Position.X, Position.Y, (int)Width, (int)Height, maximized);
        vm.App.Settings.Update(s => s with { Window = bounds });
    }
}