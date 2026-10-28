using ContextSnap.Capture;

namespace ContextSnap.Linux;

/// <summary>
/// <c>snapctx --background</c>: stays running (started at login by the autostart entry) and captures,
/// copies, and notifies each time Ctrl+Alt+Shift+C is pressed. X11 sessions only. One instance per user.
/// </summary>
public static class BackgroundMode
{
    public static int Run(HeadlessRunner runner, TextWriter output, TextWriter error)
    {
        if (new X11WindowSource().IsWayland)
        {
            error.WriteLine("The background shortcut needs an X11 session: Wayland does not let apps register global shortcuts. "
                + "Run snapctx --install to set up the shortcut through your desktop instead.");
            return 1;
        }

        using var instance = InstanceLock.TryAcquire();
        if (instance is null)
        {
            output.WriteLine("Snapctx is already running in the background.");
            return 0;
        }

        var (hotkey, problem) = X11Hotkey.Register();
        if (hotkey is null)
        {
            error.WriteLine($"Snapctx cannot listen for {X11Hotkey.DisplayText}: {problem}");
            return 1;
        }

        using (hotkey)
        {
            output.WriteLine($"Snapctx is running. Press {X11Hotkey.DisplayText} to copy a snapshot.");
            while (true)
            {
                hotkey.WaitForPress();
                try
                {
                    runner.RunAsync(new CommandLineOptions(RunMode.Capture), output, error, currentDirectory: null).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    // One failed capture must not stop the shortcut from working.
                    error.WriteLine($"Capture failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
    }
}

/// <summary>
/// Keeps one background instance per user: an exclusive lock on a file in the user's runtime directory,
/// released automatically when the process exits. The process ID is written next to it so
/// <c>--uninstall</c> can stop the instance.
/// </summary>
public sealed class InstanceLock : IDisposable
{
    private readonly FileStream _lock;

    private InstanceLock(FileStream lockFile) => _lock = lockFile;

    public static string Directory => Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } runtime
        ? runtime
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "snapctx");

    public static string PidFile => Path.Combine(Directory, "snapctx.pid");

    /// <summary>The pid file of a background instance from before the rename from contextsnap to snapctx.</summary>
    public static string LegacyPidFile => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } runtime
            ? runtime
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "snapctx"),
        "contextsnap.pid");

    public static InstanceLock? TryAcquire()
    {
        System.IO.Directory.CreateDirectory(Directory);
        try
        {
            // FileShare.None takes an exclusive advisory lock (flock) on Linux.
            var stream = new FileStream(Path.Combine(Directory, "snapctx.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            File.WriteAllText(PidFile, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return new InstanceLock(stream);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        try
        {
            File.Delete(PidFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Stale pid files are checked before use.
        }

        _lock.Dispose();
    }
}
