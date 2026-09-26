using Avalonia.Controls;
namespace OSTream.Views.Components;
public partial class AlbumGrid : ItemsControl
{
    public AlbumGrid() => InitializeComponent();
    protected override System.Type StyleKeyOverride => typeof(ItemsControl);
}