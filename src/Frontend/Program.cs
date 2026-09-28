using Avalonia;
using Avalonia.Media;
using OSTream.Core.Storage;
using OSTream.Frontend.Platform;
using OSTream.Frontend.Themes;
using System;
namespace OSTream.Frontend;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--dir")
            {
                AppPaths.DownloadDirOverride = AppPaths.Normalize(args[i + 1]);
            }
        }

        if (!SingleInstance.TryAcquire(AppPaths.ConfigDir, out SingleInstance? instance))
        {
            return 0;
        }

        using (instance)
        {
            Instance = instance;
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
    }
    internal static SingleInstance? Instance { get; private set; }
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .With(new X11PlatformOptions { OverlayPopups = true })
        .With(new Win32PlatformOptions { OverlayPopups = true })
        .With(new SkiaOptions { MaxGpuResourceSizeBytes = 32L * 1024 * 1024 })
        .With(new FontManagerOptions { DefaultFamilyName = Tokens.UiFontName })
#if DEBUG
        .WithDeveloperTools()
#endif
        .LogToTrace();
    }
}