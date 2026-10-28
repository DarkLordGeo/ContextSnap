using ContextSnap.Configuration;
using ContextSnap.Platform;

namespace ContextSnap.Capture;

/// <summary>Decisions for running Snapctx as a background process.</summary>
public static class BackgroundPolicy
{
    /// <summary>
    /// Closing the window keeps Snapctx running (hidden) only while the global shortcut works or a
    /// notification-area icon offers a way back; otherwise closing quits, so nothing invisible and useless keeps running.
    /// </summary>
    public static bool KeepRunningAfterClose(bool exitRequested, bool hotkeyRegistered, bool trayIconShown) =>
        !exitRequested && (hotkeyRegistered || trayIconShown);

    /// <summary>A sign-in launch never shows a window; a launch the user started does.</summary>
    public static bool ShowWindowAtLaunch(bool startedAtSignIn) => !startedAtSignIn;
}

/// <summary>
/// Handles <c>--exit</c>, <c>--disable-startup</c>, and <c>--enable-startup</c>: explicit, scriptable controls
/// for the background process. Exit codes: 0 success, 1 failure.
/// </summary>
public sealed class BackgroundCommands(SettingsStore store, StartupRegistration startup, Func<bool> requestExitOfRunningInstance)
{
    public int Run(RunMode mode, TextWriter output, TextWriter error)
    {
        switch (mode)
        {
            case RunMode.Exit:
                if (requestExitOfRunningInstance())
                {
                    output.WriteLine("Asked the running Snapctx to quit. It unregisters its shortcut and exits.");
                    return 0;
                }

                output.WriteLine("Snapctx is not running.");
                return 0;

            case RunMode.EnableStartup:
            case RunMode.DisableStartup:
                var enable = mode == RunMode.EnableStartup;
                var loaded = store.Load();
                var settings = loaded.Settings;
                if (settings.StartWithWindows != enable)
                {
                    settings.StartWithWindows = enable;
                    try
                    {
                        store.Save(settings);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        error.WriteLine($"Settings could not be saved: {ex.Message}");
                        return 1;
                    }
                }

                var state = startup.Apply(enable);
                (state.Success ? output : error).WriteLine(state.Message);
                return state.Success ? 0 : 1;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Not a background command.");
        }
    }
}
