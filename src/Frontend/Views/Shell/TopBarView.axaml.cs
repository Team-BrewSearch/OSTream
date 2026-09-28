using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OSTream.ViewModels;
namespace OSTream.Views.Shell;

public partial class TopBarView : UserControl
{
    public TopBarView()
    {
        InitializeComponent();
        SearchBox.GotFocus += (_, _) => SetFocused(true);
        SearchBox.LostFocus += (_, _) => SetFocused(false);
        SearchBox.AddHandler(KeyDownEvent, OnSearchKey, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is TopBarViewModel vm)
            {
                vm.FocusSearchRequested += FocusSearch;
            }
        };
    }
    private TopBarViewModel? Vm => DataContext as TopBarViewModel;
    public System.Action<bool>? FocusChanged { get; set; }
    public Control FlyoutPanel => Flyout;
    public Control SettingsAnchor => SettingsButton;
    private void SetFocused(bool focused)
    {
        Search.Classes.Set("focused", focused);
        Kbd.Opacity = focused ? 0 : 1;
        FocusChanged?.Invoke(focused);
    }
    private void FocusSearch()
    {
        _ = SearchBox.Focus(NavigationMethod.Tab);
        SearchBox.SelectAll();
    }
    private void OnSearchKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Vm?.SubmitCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (!string.IsNullOrEmpty(SearchBox.Text))
            {
                Vm?.Clear();
            }
            else
            {
                _ = (TopLevel.GetTopLevel(this)?.FocusManager?.Focus(null!, NavigationMethod.Unspecified, KeyModifiers.None));
            }

            e.Handled = true;
        }
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!Flyout.IsVisible || Vm is null)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            Vm.IsSettingsOpen = false;
            _ = SettingsButton.Focus(NavigationMethod.Tab);
            e.Handled = true;
        }
        else if (ThemeOptions.IsKeyboardFocusWithin && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            Vm.CycleTheme(e.Key is Key.Right or Key.Down ? 1 : -1);
            e.Handled = true;
        }
    }
}