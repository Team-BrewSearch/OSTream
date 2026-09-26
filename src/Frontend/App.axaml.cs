using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging;
using OSTream.Core.Models;
using OSTream.Frontend.Controls;
using OSTream.Frontend.Themes;
using OSTream.Frontend.ViewModels;
using OSTream.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
namespace OSTream.Frontend;

public partial class App : Application
{
    public override void Initialize()
    {
        Tokens.Install(this);
        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://OSTream/App.axaml"))
        {
            Source = new Uri("avares://OSTream/Themes/Base.axaml"),
        });
        AvaloniaXamlLoader.Load(this);
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppServices services = new();
            LogUnhandledExceptions(services.Log);
            ArtView.Loader = services.Images;
            ApplyTheme(services.Settings.Value.Theme);
            services.Settings.Changed += (old, now) =>
            {
                if (old.Theme != now.Theme)
                {
                    ApplyTheme(now.Theme);
                }
            };
            MainViewModel main = new(services);
            MainWindow window = new() { DataContext = main };
            services.Shell.Top = window;
            desktop.MainWindow = window;
            ActualThemeVariantChanged += (_, _) => FreezeTransitions(window);
            Program.Instance?.Listen(() => Dispatcher.UIThread.Post(services.Shell.Raise));
            desktop.Exit += (_, _) => services.Dispose();
            AppDomain.CurrentDomain.ProcessExit += (_, _) => services.Dispose();
            main.Start();
        }
        base.OnFrameworkInitializationCompleted();
    }
    private static void LogUnhandledExceptions(ILogger log)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => log.LogCritical(e.ExceptionObject as Exception, "Unhandled exception");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            log.LogError(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };
        Dispatcher.UIThread.UnhandledException += (_, e) => log.LogCritical(e.Exception, "Unhandled UI exception");
    }
    private static void FreezeTransitions(TopLevel top)
    {
        List<IDisposable> frozen = [];
        foreach (Animatable visual in top.GetVisualDescendants().OfType<Animatable>())
        {
            if (visual.Transitions is { Count: > 0 } && visual.SetValue(Animatable.TransitionsProperty, null, BindingPriority.Animation) is { } undo)
            {
                frozen.Add(undo);
            }
        }
        top.RequestAnimationFrame(_ => top.RequestAnimationFrame(_ => frozen.ForEach(undo => undo.Dispose())));
    }
    private void ApplyTheme(ThemeChoice theme)
    {
        RequestedThemeVariant = theme switch
        {
            ThemeChoice.Light => ThemeVariant.Light,
            ThemeChoice.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}