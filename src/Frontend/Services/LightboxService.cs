using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OSTream.Core.Models;
namespace OSTream.Services;
public sealed partial class LightboxViewModel : ObservableObject
{
    readonly Action _close;
    readonly Func<string, Task> _open;
    public LightboxViewModel(IReadOnlyList<AlbumImage> images, int start, string title, Action close, Func<string, Task> open)
    {
        Images = images;
        Title = title;
        _close = close;
        _open = open;
        Index = (start % images.Count + images.Count) % images.Count;
    }
    public IReadOnlyList<AlbumImage> Images { get; }
    public string Title { get; }
    public bool HasMany => Images.Count > 1;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Counter), nameof(Source))]
    public partial int Index { get; set; }
    public string Counter => $"{Index + 1} / {Images.Count}";
    public string Source => Images[Index].Full.Length > 0 ? Images[Index].Full : Images[Index].Thumb;
    [RelayCommand] public void Prev() => Index = (Index - 1 + Images.Count) % Images.Count;
    [RelayCommand] public void Next() => Index = (Index + 1) % Images.Count;
    [RelayCommand] void Select(AlbumImage image) => Index = Math.Max(0, IndexOf(image));
    [RelayCommand] public void Close() => _close();
    [RelayCommand] async Task OpenOriginal() => await _open(Source);
    int IndexOf(AlbumImage image)
    {
        for (var i = 0; i < Images.Count; i++)
            if (ReferenceEquals(Images[i], image)) return i;
        return -1;
    }
}
public sealed partial class LightboxService(Func<string, Task> openUrl) : ObservableObject
{
    [ObservableProperty] public partial LightboxViewModel? Current { get; private set; }
    public void Open(IReadOnlyList<AlbumImage> images, int start, string title)
    {
        if (images.Count == 0) return;
        Current = new LightboxViewModel(images, start, title, () => Current = null, openUrl);
    }
}