using System;
namespace OSTream.Core;
public enum ToastKind { Info, Success, Error }
public sealed record ToastAction(string Label, Action Run);
public interface INotifier
{
    void Toast(string message, ToastKind kind = ToastKind.Info, string detail = "", ToastAction? action = null);
}