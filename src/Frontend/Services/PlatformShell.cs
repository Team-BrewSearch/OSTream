using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using OSTream.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
namespace OSTream.Frontend.Services;

public sealed class PlatformShell(INotifier notifier)
{
    public TopLevel? Top { get; set; }
    public void Raise()
    {
        if (Top is not Window window)
        {
            return;
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }
    public async Task OpenUrl(string url)
    {
        try
        {
            if (Top is null || !IsLaunchable(url, out Uri? uri))
            {
                return;
            }

            if (!await Top.Launcher.LaunchUriAsync(uri))
            {
                notifier.Toast(url, ToastKind.Info, "Open this link in your browser");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or PlatformNotSupportedException)
        {
            notifier.Toast("Could not open the link", ToastKind.Error, ex.Message);
        }
    }
    internal static bool IsLaunchable(string url, [NotNullWhen(true)] out Uri? uri)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }

    public async Task OpenFolder(string path)
    {
        try
        {
            _ = Directory.CreateDirectory(path);
            if (Top is null || !await Top.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path)))
            {
                notifier.Toast("Could not open the folder", ToastKind.Error, path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException)
        {
            notifier.Toast("Could not open the folder", ToastKind.Error, ex.Message);
        }
    }
    public async Task CopyText(string text)
    {
        try
        {
            if (Top?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
                notifier.Toast("Link copied", ToastKind.Success);
                return;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException)
        {
        }
        notifier.Toast(text, ToastKind.Info, "Copy this link");
    }
    public async Task<string?> PickFolderAsync(string title, string? startAt)
    {
        if (Top?.StorageProvider is not { CanPickFolder: true } storage)
        {
            return null;
        }

        try
        {
            IStorageFolder? start = null;
            if (startAt is not null && Directory.Exists(startAt))
            {
                start = await storage.TryGetFolderFromPathAsync(startAt);
            }

            IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false, SuggestedStartLocation = start });
            return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException)
        {
            notifier.Toast("Could not open the folder picker", ToastKind.Error, ex.Message);
            return null;
        }
    }
}