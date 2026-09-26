using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OSTream.Frontend.Controls;
namespace OSTream.Frontend.ViewModels;

public sealed partial class NavItemViewModel(string id, string path, string label, IconKind icon, IRelayCommand<string> go) : ViewModelBase
{
    public string Id { get; } = id;
    public string Path { get; } = path;
    public string Label { get; } = label;
    public IconKind Icon { get; } = icon;
    public string Tip { get; init; } = label;
    public IRelayCommand<string> GoCommand { get; } = go;
    public bool LiveBadge { get; init; }
    [ObservableProperty] public partial bool IsActive { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBadge))]
    public partial int Badge { get; set; }
    public bool HasBadge => Badge > 0;
}