using CommunityToolkit.Mvvm.ComponentModel;
using OSTream.Frontend.Navigation;
using System;
namespace OSTream.Frontend.ViewModels;

public abstract class ViewModelBase : ObservableObject;
public enum ScrollTarget { Top, AlbumPanel }
public abstract class PageViewModel(AppServices app) : ViewModelBase, IDisposable
{
    protected AppServices App { get; } = app;
    public event Action<ScrollTarget, bool>? ScrollRequested;
    protected void RequestScroll(ScrollTarget target, bool instant = false)
    {
        ScrollRequested?.Invoke(target, instant);
    }
    public abstract void Update(Route route);
    public abstract void Dispose();
}