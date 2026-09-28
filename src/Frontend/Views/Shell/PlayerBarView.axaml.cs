using Avalonia.Controls;
using Avalonia.Input;
using OSTream.Frontend.Controls;
using OSTream.ViewModels;
namespace OSTream.Views.Shell;

public partial class PlayerBarView : UserControl
{
    public PlayerBarView()
    {
        InitializeComponent();
        foreach (SliderBar? seek in new[] { Seek, MiniSeek })
        {
            seek.TipText = ratio => Vm?.TimeAt(ratio) ?? "";
            seek.Input += ratio => Vm?.SeekInput(ratio);
            seek.Commit += ratio => Vm?.SeekCommit(ratio);
        }
        VolumeSlider.Input += ratio => Vm?.PreviewVolume(ratio);
        VolumeSlider.Commit += ratio => Vm?.SetVolume(ratio);
        Volume.PointerWheelChanged += OnVolumeWheel;
        VolumeSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsPointerOverProperty)
            {
                VolumeSlider.Classes.Set("dragging", VolumeSlider.IsDragging);
            }
        };
    }
    private PlayerBarViewModel? Vm => DataContext as PlayerBarViewModel;
    private void OnVolumeWheel(object? sender, PointerWheelEventArgs e)
    {
        e.Handled = true;
        Vm?.NudgeVolume(e.Delta.Y > 0 ? 0.05 : -0.05);
    }
}