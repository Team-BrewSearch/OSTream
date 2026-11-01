using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OSTream.Frontend.Controls;
using System;
using System.Collections.Generic;
namespace OSTream.Services;

public sealed record MenuEntry(
    string Label,
    IconKind Icon = IconKind.None,
    Action? Run = null,
    Func<IReadOnlyList<MenuEntry>>? Submenu = null,
    bool Danger = false,
    string? Hint = null)
{
    public bool IsSeparator { get; private init; }
    public bool IsItem => !IsSeparator;
    public bool HasSubmenu => Submenu is not null;
    public bool HasHint => !string.IsNullOrEmpty(Hint);
    public static MenuEntry Separator { get; } = new("") { IsSeparator = true };
}
public sealed partial class MenuService : ObservableObject
{
    private readonly Stack<IReadOnlyList<MenuEntry>> _stack = new();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen))]
    public partial IReadOnlyList<MenuEntry>? Entries { get; private set; }
    public object? Anchor { get; private set; }
    public bool IsOpen => Entries is not null;
    public event Action<object?, bool>? AnchorChanged;
    public void Open(object anchor, IReadOnlyList<MenuEntry> entries)
    {
        if (ReferenceEquals(Anchor, anchor) && IsOpen)
        {
            Close();
            return;
        }
        Close();
        Anchor = anchor;
        _stack.Clear();
        Entries = entries;
        AnchorChanged?.Invoke(anchor, true);
    }
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        object? anchor = Anchor;
        Entries = null;
        Anchor = null;
        _stack.Clear();
        AnchorChanged?.Invoke(anchor, false);
    }
    public bool CanGoBack => _stack.Count > 0;
    [RelayCommand]
    public void Choose(MenuEntry entry)
    {
        if (entry.Run == GoBack)
        {
            GoBack();
            return;
        }
        if (entry.Submenu is { } submenu)
        {
            _stack.Push(Entries!);
            Entries = [new MenuEntry("Back", IconKind.ChevronLeft, Run: GoBack), MenuEntry.Separator, .. submenu()];
            return;
        }
        Close();
        entry.Run?.Invoke();
    }
    private void GoBack()
    {
        if (_stack.Count > 0)
        {
            Entries = _stack.Pop();
        }
    }
}
