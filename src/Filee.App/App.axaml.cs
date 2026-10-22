// Application start-up: builds services, applies settings, wires global gestures to the donut toolbar.
// Lite: Windows only, no updates, no "What's new", no engine downloads, no watch folders.

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Filee.App.Controls;
using Filee.App.Services;
using Filee.App.Services.Triggers;
using Filee.Core.Conversion;
using Filee.Core.Localization;
using Filee.Core.Platform;
using Filee.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Filee.App;

public partial class App : Application
{
    private readonly List<string> _pendingConvertFiles = [];
    private IDisposable? _convertDebounce;
    private bool _selectionPending;

    /// <summary>Forwarded launches and empty selections can come in bursts: a few lines a minute are enough.</summary>
    private static readonly LogThrottle Throttle = new(TimeSpan.FromSeconds(30));

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        SingleSelectionGuard.Register();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppHost.Build();
            Start(desktop);
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Ends Filee as the tray's Quit does.</summary>
    public static void Quit() => (Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();

    private void Start(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var log = AppHost.Get<ILogger<App>>();
        // Exceptions in UI event handlers end Filee; write them down first (they are not marked handled).
        Dispatcher.UIThread.UnhandledException += (_, e) =>
            log.LogCritical(e.Exception, "Unhandled exception on the UI thread");
        var watchdog = new UiWatchdog(action => Dispatcher.UIThread.Post(action), AppHost.Get<ILogger<UiWatchdog>>());
        watchdog.Start();
        var store = AppHost.Get<UserDataStore>();
        store.Load();

        var loc = AppHost.Get<LocalizationService>();
        loc.Attach(this);
        ApplySettings(store.Settings);
        // Every engine's availability, checked once off the UI thread.
        _ = AppHost.Get<ConverterCatalog>().CheckInBackgroundAsync();

        // Global gestures → donut toolbar
        var triggers = AppHost.Get<TriggerService>();
        var radial = AppHost.Get<RadialController>();
        var platform = AppHost.Get<IPlatformServices>();
        triggers.DragStarted += (_, e) => radial.ShowForDrag(e.X, e.Y);
        triggers.DragEnded += (_, _) => radial.OnDragGestureEnded();
        triggers.SelectionGesture += async (_, e) =>
        {
            if (_selectionPending || store.Settings.Paused)
                return;
            _selectionPending = true;
            try
            {
                var files = platform.GetFileManagerSelection();
                if (files.Count > 0 && !store.Settings.Paused)
                    radial.ShowForFiles(e.X, e.Y, files);
                else if (files.Count == 0 && Throttle.TryEnter("empty-selection", out var held))
                    log.LogInformation("The selection shortcut found no selected files in {Window}{Held}",
                        platform.DescribeForegroundWindow() is { Length: > 0 } window ? window : "the foreground window",
                        LogThrottle.HeldText(held));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                log.LogWarning(ex, "Could not read the file manager selection");
                AppHost.Get<WindowService>().ShowMain("triggers");
                if (AppHost.Get<WindowService>().Main is { } window)
                    _ = AppHost.Get<WindowService>().MessageAsync(window, ex.Message);
            }
            finally
            {
                _selectionPending = false;
            }
        };
        triggers.Start();
        log.LogInformation("Filee {Version} started", Program.Version);
        // Which way the transparent windows (donut, toast) are drawn, for reports of one that isn't see-through.
        log.LogInformation("{Rendering}", WindowsRendering.Describe(Environment.GetEnvironmentVariable(WindowsRendering.Variable)));
        TransparentWindows.Watch(log);

        store.SettingsChanged += (_, _) => ApplySettings(store.Settings);

        AppHost.Get<TrayService>().Create(this);
        AppHost.Get<WindowService>().EnsureToast();

        // Later launches (context menu, Send To, double-clicking the exe) forward their arguments here.
        if (Program.Instance is { } instance)
            ListenForLaterLaunches(instance, log);
        Program.InstanceAcquired += again => ListenForLaterLaunches(again, log);

        desktop.Exit += (_, _) =>
        {
            watchdog.Dispose();
            AppHost.Get<MemoryTrimmer>().Dispose();
            // Not Dispose: SharpHook's stop can crash the quit when an event is in flight (see AbandonForExit).
            AppHost.Get<TriggerService>().AbandonForExit();
            AppHost.Get<TrayService>().Dispose();
        };

        HandleCommandLine(Program.Options, firstLaunch: true);
    }

    private void ListenForLaterLaunches(SingleInstance instance, ILogger log)
    {
        instance.ArgumentsReceived += args =>
        {
            if (Throttle.TryEnter("forwarded", out var held))
                log.LogInformation("A later launch handed over {Count} argument(s){Held}", args.Length, LogThrottle.HeldText(held));
            Dispatcher.UIThread.Post(() => HandleCommandLine(CommandLine.Parse(args), forwarded: true));
        };
        instance.StartListening();
    }

    /// <summary>Applies everything derived from settings. Called at start-up and whenever settings are saved.</summary>
    private static void ApplySettings(AppSettings settings)
    {
        var loc = AppHost.Get<LocalizationService>();
        if (loc.NeedsUpdate(settings.Language))
            loc.SetLanguage(settings.Language);

        var custom = CustomTheme.Load(AppHost.Get<UserDataStore>().Directory, AppHost.Get<ILogger<App>>());
        AppHost.Get<ThemeService>().Apply(settings.Theme, loc.Language, custom);
        AppHost.Get<TriggerService>().Apply(settings);
        AppHost.Get<ConverterCatalog>().Priority = settings.EnginePriority;
        AppHost.Get<SystemIntegrationService>().Apply(settings);
    }

    private void HandleCommandLine(CommandLine options, bool firstLaunch = false, bool forwarded = false)
    {
        // Pairs with "A later launch handed over …": the request reached the UI thread too.
        if (forwarded && Throttle.TryEnter("forwarded-handled", out var held))
            AppHost.Get<ILogger<App>>().LogInformation("Handling a later launch: {Request}{Held}",
                options.Quit ? "quit" : options.ConvertFiles.Count > 0 ? $"convert {options.ConvertFiles.Count} file(s)" : "show Filee",
                LogThrottle.HeldText(held));
        if (options.Quit)
        {
            (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
            return;
        }
        // The Windows 11 Explorer menu hands over huge selections in a temp file; its paths are in ConvertFiles now.
        options.DeleteListFile();
        if (options.ConvertFiles.Count > 0)
        {
            // Explorer may start one process per selected file: collect them for a moment, then open once.
            _pendingConvertFiles.AddRange(options.ConvertFiles);
            _convertDebounce?.Dispose();
            _convertDebounce = DispatcherTimer.RunOnce(OpenPendingFiles, TimeSpan.FromMilliseconds(350));
            return;
        }

        var store = AppHost.Get<UserDataStore>();
        if (!options.Background || options.ShowSettings || !firstLaunch)
            AppHost.Get<WindowService>().ShowMain(store.Settings.FirstRunCompleted ? null : "home");
        if (!store.Settings.FirstRunCompleted)
        {
            store.Settings.FirstRunCompleted = true;
            store.SaveSettings();
        }
    }

    private void OpenPendingFiles()
    {
        var files = _pendingConvertFiles.Distinct(FileSystemPaths.Comparer).ToList();
        _pendingConvertFiles.Clear();
        var (x, y) = AppHost.Get<TriggerService>().LastCursor;
        if (x == 0 && y == 0 && AppHost.Get<WindowService>().Main is { } main)
            (x, y) = (main.Position.X + 200, main.Position.Y + 200);
        AppHost.Get<RadialController>().ShowForFiles(x, y, files, forAnotherProcess: true);
    }
}
