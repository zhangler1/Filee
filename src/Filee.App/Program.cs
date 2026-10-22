// Entry point, in this order:
//  0. The log and the handlers for unhandled exceptions, so even a start that fails leaves a line; then one line
//     describing this launch (version, path, rights, OS).
//  1. Single instance: a second launch forwards its arguments to the running instance and exits. When it can't (the
//     running Filee is elevated, or hung) it says so.
//  2. --quit (from a script or shortcut) runs without any window.
//  3. Start Avalonia. The app lives in the tray, so it only exits via the tray "Quit" command (or --quit).
// Lite: Windows only, no installer commands, no embedded fonts (system fonts only).

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Filee.App.Services;
using Filee.Core.Platform;
using Filee.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Filee.App;

internal static class Program
{
    /// <summary>Parsed command line of this process.</summary>
    public static CommandLine Options { get; private set; } = CommandLine.Parse([]);

    /// <summary>Owned by the first instance for its whole lifetime.</summary>
    public static SingleInstance? Instance { get; private set; }

    /// <summary>How long to wait for a running Filee to save and exit.</summary>
    private static readonly TimeSpan QuitTimeout = TimeSpan.FromSeconds(20);

    /// <summary>How long a later launch waits for a Filee that is still starting to take its arguments.</summary>
    private static readonly TimeSpan ForwardTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Integrity levels of this process and Explorer, read at start-up.</summary>
    public static ElevationState Elevation { get; private set; } = ElevationState.Unknown;

    /// <summary>Program's own log, set up before anything else so even a failed start leaves a line.</summary>
    internal static ILogger Log { get; private set; } = NullLogger.Instance;

    /// <summary>The running Filee's version.</summary>
    public static string Version => typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "?";

    /// <summary>Raised on the UI thread when the single-instance lock was taken again after a failed restart.</summary>
    public static event Action<SingleInstance>? InstanceAcquired;

    [STAThread]
    public static int Main(string[] args)
    {
        Options = CommandLine.Parse(args);
        Log = StartupDiagnostics.CreateEarlyLogger(Path.Combine(UserDataStore.DefaultDirectory, "logs"), out _);
        StartupDiagnostics.LogUnhandledExceptions(Log);
        Elevation = Filee.Platform.Windows.Elevation.Probe();
        var startup = StartupDiagnostics.Describe(Version, Environment.ProcessPath,
            "lite", Elevation, StartupDiagnostics.OperatingSystemText(), args.Length);

        SingleInstance instance;
        try
        {
            instance = SingleInstance.Acquire();
        }
        catch (UnauthorizedAccessException ex)
        {
            // The lock belongs to a Filee running as administrator: this process may not even open it.
            Log.LogWarning("{Startup}", startup);
            Log.LogWarning(ex, "Another Filee is running with administrator rights; this launch can't reach it");
            if (!Options.Quit)
                TellUserOnce("startup.elevated_other");
            return 1;
        }
        Instance = instance;
        if (Options.Quit)
        {
            Log.LogInformation("{Startup}", startup);
            return RunQuitCommand(instance);
        }
        if (!instance.IsFirst)
        {
            var result = instance.Forward(args, ForwardTimeout);
            if (result != ForwardResult.Delivered)
            {
                // Logged only when it failed: another program may start one Filee per selected file.
                Log.LogWarning("{Startup}", startup);
                Log.LogWarning("Could not hand {Count} argument(s) to the running Filee: {Result}", args.Length, result);
                TellUserOnce(result == ForwardResult.AccessDenied ? "startup.elevated_other" : "startup.no_answer");
            }
            ReleaseInstance();
            return result == ForwardResult.Delivered ? 0 : 1;
        }

        Log.LogInformation("{Startup}", startup);
        if (Elevation.ShellCannotReachFilee)
            Log.LogWarning("Filee runs as administrator ({Elevation}) while Explorer doesn't: Explorer can't drop files on it, " +
                           "use its menu or have its selection read", Elevation);

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        finally
        {
            ReleaseInstance();
        }
    }

    /// <summary>Lets go of the single-instance lock (at exit, or before restarting without administrator rights).</summary>
    public static void ReleaseInstance()
    {
        Instance?.Dispose();
        Instance = null;
    }

    /// <summary>Takes the lock back after a restart that didn't happen, so later launches find this Filee again.</summary>
    public static void ReacquireInstance()
    {
        if (Instance is not null)
            return;
        try
        {
            var instance = SingleInstance.Acquire();
            if (!instance.IsFirst)
            {
                // Another Filee started meanwhile (perhaps the restart did happen after all): it is the running one.
                Log.LogWarning("Another Filee took the single-instance lock during the restart");
                instance.Dispose();
                return;
            }
            Instance = instance;
            InstanceAcquired?.Invoke(instance);
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.LogWarning(ex, "Could not take the single-instance lock back");
        }
    }

    /// <summary>
    /// Shows a launch problem in a system message box (Filee's own windows aren't available in this process). A lock
    /// shared between launches lets only the first one show it.
    /// </summary>
    private static void TellUserOnce(string key)
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            using var shown = new Mutex(initiallyOwned: true, @"Local\" + SingleInstance.DefaultName + "-notice", out var createdNew);
            if (!createdNew)
                return;
            try
            {
                Filee.Platform.Windows.Elevation.ShowWarning(LocalizationService.EarlyText(key));
            }
            finally
            {
                shown.ReleaseMutex();
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
        {
            Log.LogWarning(ex, "Could not show the launch problem");
        }
    }

    /// <summary>
    /// <c>--quit</c>: ask the running Filee (if any) to exit and wait for it. Exit code 1 when Filee is still running.
    /// </summary>
    private static int RunQuitCommand(SingleInstance instance)
    {
        using (instance)
        {
            var deadline = DateTime.UtcNow + QuitTimeout;
            if (!instance.IsFirst)
            {
                // A Filee that is still starting takes the request once it listens; then it has the rest to exit.
                instance.ForwardToFirst(["--quit"], QuitTimeout);
                var left = deadline - DateTime.UtcNow;
                if (!instance.WaitForFirstToExit(left > TimeSpan.FromSeconds(5) ? left : TimeSpan.FromSeconds(5)))
                    return 1;
            }
            // The mutex is released just before the process ends: wait for the end, so Filee.exe is no longer in use.
            return WaitForOtherProcessesOfThisExe(deadline) ? 0 : 1;
        }
    }

    private static bool WaitForOtherProcessesOfThisExe(DateTime deadline)
    {
        if (Environment.ProcessPath is not { } exe)
            return true;
        foreach (var process in OtherProcessesOfThisExe(exe))
        {
            using (process)
            {
                try
                {
                    var left = deadline - DateTime.UtcNow;
                    if (left <= TimeSpan.Zero || !process.WaitForExit(left))
                        return false;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    // Exited meanwhile.
                }
            }
        }
        return true;
    }

    /// <summary>Other running processes started from <paramref name="exe"/> (the caller disposes them).</summary>
    private static List<Process> OtherProcessesOfThisExe(string exe)
    {
        var found = new List<Process>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
        {
            try
            {
                if (process.Id != Environment.ProcessId
                    && string.Equals(process.MainModule?.FileName, exe, Filee.Core.Platform.FileSystemPaths.Comparison))
                {
                    found.Add(process);
                    continue;
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // Exited meanwhile, or another user's process we may not inspect.
            }
            process.Dispose();
        }
        return found;
    }

    /// <summary>Also used by the XAML previewer.</summary>
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>().UsePlatformDetect();
        if (OperatingSystem.IsWindows())
            builder = builder.With(WindowsRendering.Options(
                WindowsRendering.Normalize(Environment.GetEnvironmentVariable(WindowsRendering.Variable)),
                adapters => Log.LogInformation("Graphics adapters: {Adapters}", adapters)));
        builder = builder
#if DEBUG
            .WithDeveloperTools()
#endif
            .LogToTrace();
        AvaloniaLogForwarder.Install(Log);
        return builder;
    }
}
