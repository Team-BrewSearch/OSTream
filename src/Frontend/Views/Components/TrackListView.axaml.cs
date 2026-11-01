using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OSTream.ViewModels;
using System.Collections.Generic;
using System.Linq;
namespace OSTream.Views.Components;

public partial class TrackListView : UserControl
{
    public TrackListView()
    {
        InitializeComponent();
        Rows.AddHandler(KeyDownEvent, OnRowKey, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) =>
        {
            Classes.Set("has-album", (DataContext as TrackListViewModel)?.ShowAlbum == true);
        };
    }
    private void OnRowKey(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down) || e.Source is not Button { } row || !row.Classes.Contains("track-row"))
        {
            return;
        }

        List<Button> rows = Rows.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("track-row") && b.IsVisible).ToList();
        int index = rows.IndexOf(row);
        int next = index + (e.Key == Key.Down ? 1 : -1);
        if (next >= 0 && next < rows.Count)
        {
            _ = rows[next].Focus(NavigationMethod.Directional);
            rows[next].BringIntoView();
        }
        e.Handled = true;
    }
}