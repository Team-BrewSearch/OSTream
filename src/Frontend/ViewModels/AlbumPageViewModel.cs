using CommunityToolkit.Mvvm.Input;
using OSTream.Frontend;
using OSTream.Frontend.Navigation;
using OSTream.Frontend.Services;
using OSTream.Frontend.ViewModels;
namespace OSTream.ViewModels;

public sealed partial class AlbumPageViewModel : PageViewModel
{
    public AlbumPageViewModel(AppServices app) : base(app)
    {
        Panel = new AlbumPanelViewModel(app, inline: false, UserActions.AlbumRoute, RequestScroll);
    }

    public AlbumPanelViewModel Panel { get; }
    public override void Update(Route route)
    {
        Panel.Show(route.Segment(1), scroll: false);
        RequestScroll(ScrollTarget.Top, instant: true);
    }
    [RelayCommand]
    private void Back()
    {
        if (App.Navigator.CanGoBack)
        {
            App.Navigator.Back();
        }
        else
        {
            App.Navigator.Navigate("/");
        }
    }
    public override void Dispose()
    {
        Panel.Dispose();
    }
}