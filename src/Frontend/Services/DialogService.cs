using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading.Tasks;
namespace OSTream.Services;

public sealed partial class DialogViewModel : ObservableObject
{
    private readonly TaskCompletionSource<string?> _result = new();
    public required string Title { get; init; }
    public string Message { get; init; } = "";
    public bool IsPrompt { get; init; }
    public string Placeholder { get; init; } = "";
    public string ConfirmLabel { get; init; } = "Confirm";
    public bool Danger { get; init; }
    public bool HasMessage => Message.Length > 0;
    [ObservableProperty] public partial string Value { get; set; } = "";
    internal Task<string?> Result => _result.Task;
    internal event Action? Closed;
    [RelayCommand]
    private void Confirm()
    {
        string value = IsPrompt ? Value.Trim() : "ok";
        Close(value.Length > 0 ? value : null);
    }
    [RelayCommand]
    public void Cancel()
    {
        Close(null);
    }
    private void Close(string? value)
    {
        if (_result.TrySetResult(value))
        {
            Closed?.Invoke();
        }
    }
}
public sealed partial class DialogService : ObservableObject
{
    [ObservableProperty] public partial DialogViewModel? Current { get; private set; }
    private Task<string?> Show(DialogViewModel dialog)
    {
        Current?.Cancel();
        dialog.Closed += () =>
        {
            if (Current == dialog)
            {
                Current = null;
            }
        };
        Current = dialog;
        return dialog.Result;
    }
    public Task<string?> PromptAsync(string title, string value = "", string placeholder = "", string confirm = "Save")
    {
        return Show(new DialogViewModel { Title = title, IsPrompt = true, Value = value, Placeholder = placeholder, ConfirmLabel = confirm });
    }
    public async Task<bool> ConfirmAsync(string title, string message = "", string confirm = "Confirm", bool danger = false)
    {
        return await Show(new DialogViewModel { Title = title, Message = message, ConfirmLabel = confirm, Danger = danger }) is not null;
    }
}