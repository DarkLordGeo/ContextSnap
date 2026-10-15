using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ContextSnap.Linux;

/// <summary>
/// <c>--install</c> and <c>--uninstall</c>: put contextsnap in ~/.local/bin and make Ctrl+Alt+Shift+C work
/// from login onwards. GNOME (X11 and Wayland) gets a custom keyboard shortcut, so the desktop itself
/// listens for the key and nothing has to stay running. Other X11 desktops get an autostart entry that
/// starts <c>contextsnap --background</c> at login. Other Wayland desktops cannot be set up automatically.
/// Only files and settings named after ContextSnap are created, changed, or removed.
/// </summary>
public sealed partial class LinuxSetup(TextWriter output, Func<string, string?>? environment = null)
{
    public const string GnomeSchema = "org.gnome.settings-daemon.plugins.media-keys";
    public const string GnomeKeybindingPath = "/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/contextsnap/";
    public const string GnomeBinding = "<Control><Alt><Shift>c";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly Func<string, string?> _environment = environment ?? Environment.GetEnvironmentVariable;

    private string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public string InstalledPath => Path.Combine(Home, ".local", "bin", "contextsnap");

    /// <summary>The XDG autostart folder ($XDG_CONFIG_HOME/autostart, usually ~/.config/autostart).</summary>
    public string AutostartFile => Path.Combine(
        _environment("XDG_CONFIG_HOME") is { Length: > 0 } config && Path.IsPathFullyQualified(config) ? config : Path.Combine(Home, ".config"),
        "autostart", "contextsnap.desktop");

    public bool IsGnome => (_environment("XDG_CURRENT_DESKTOP") ?? string.Empty)
        .Split(':').Any(d => d.Equals("GNOME", StringComparison.OrdinalIgnoreCase));

    public bool IsWayland => new X11WindowSource(_environment).IsWayland;

    public int Install(string currentExecutable)
    {
        // A development build is an apphost next to contextsnap.dll; copying only the apphost would break.
        if (typeof(LinuxSetup).Assembly.Location.Length > 0)
        {
            output.WriteLine("Run --install from the downloaded single-file build (or a dotnet publish -p:PublishSingleFile=true build).");
            return 2;
        }

        StopBackground();
        var target = CopyExecutable(currentExecutable);

        if (IsGnome && ToolRunner.Find("gsettings") is { } gsettings)
        {
            if (!SetGnomeShortcut(gsettings, target))
            {
                output.WriteLine("Could not add the GNOME keyboard shortcut (gsettings failed).");
                return 1;
            }

            output.WriteLine($"Added the GNOME keyboard shortcut {X11Hotkey.DisplayText} → {target}.");
            output.WriteLine("GNOME listens for it from login onwards, so nothing has to keep running. Change the key in Settings → Keyboard → Custom Shortcuts.");
        }
        else if (!IsWayland)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AutostartFile)!);
            File.WriteAllText(AutostartFile, DesktopEntry(target));
            output.WriteLine($"Added {AutostartFile}: ContextSnap starts in the background when you log in.");
            if (!StartBackground(target))
                return 1;
            output.WriteLine($"Started. Press {X11Hotkey.DisplayText} to copy a snapshot.");
        }
        else
        {
            output.WriteLine("Your Wayland desktop does not let apps register a global shortcut, so it cannot be set up automatically.");
            output.WriteLine($"Add one in your desktop's keyboard settings (for example KDE: System Settings → Shortcuts → Add Command) that runs: {target}");
        }

        if (!(_environment("PATH") ?? string.Empty).Split(':').Contains(Path.GetDirectoryName(target)))
            output.WriteLine($"Note: {Path.GetDirectoryName(target)} is not on your PATH; the shortcut uses the full path, so it still works.");
        return 0;
    }

    public int Uninstall()
    {
        var stopped = StopBackground();
        if (File.Exists(AutostartFile))
        {
            File.Delete(AutostartFile);
            output.WriteLine($"Removed {AutostartFile}.");
        }

        if (ToolRunner.Find("gsettings") is { } gsettings && RemoveGnomeShortcut(gsettings))
            output.WriteLine("Removed the GNOME keyboard shortcut.");

        if (File.Exists(InstalledPath))
        {
            File.Delete(InstalledPath);
            output.WriteLine($"Removed {InstalledPath}.");
        }

        if (stopped)
            output.WriteLine("Stopped the background instance.");
        output.WriteLine($"ContextSnap is uninstalled. Your settings were kept in {Path.GetDirectoryName(Configuration.SettingsStore.DefaultPath)}.");
        return 0;
    }

    /// <summary>Copies the running executable to ~/.local/bin, replacing an older copy atomically.</summary>
    private string CopyExecutable(string currentExecutable)
    {
        var target = InstalledPath;
        if (string.Equals(Path.GetFullPath(currentExecutable), target, StringComparison.Ordinal))
            return target;

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + ".new";
        File.Copy(currentExecutable, temporary, overwrite: true);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        File.Move(temporary, target, overwrite: true); // A running copy keeps its old file.
        output.WriteLine($"Installed {target}.");
        return target;
    }

    private bool StartBackground(string target)
    {
        var start = new ProcessStartInfo(target)
        {
            UseShellExecute = false,
            // Not read: the background instance must not hold this terminal or pipe open.
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("--background");
        var process = Process.Start(start)!;
        process.StandardInput.Close();
        if (!process.WaitForExit(TimeSpan.FromSeconds(1)) || process.ExitCode == 0)
            return true;

        output.WriteLine($"The background instance did not start: {process.StandardError.ReadLine()}");
        return false;
    }

    /// <summary>Stops a running background instance, identified by its pid file and process name.</summary>
    private static bool StopBackground()
    {
        try
        {
            if (!File.Exists(InstanceLock.PidFile)
                || !int.TryParse(File.ReadAllText(InstanceLock.PidFile).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
                || pid == Environment.ProcessId)
                return false;

            using var process = Process.GetProcessById(pid);
            if (process.ProcessName != "contextsnap")
                return false; // Stale pid file: the number now belongs to another program.
            process.Kill();
            process.WaitForExit(Timeout);
            File.Delete(InstanceLock.PidFile); // A killed instance cannot remove it itself.
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static bool SetGnomeShortcut(string gsettings, string target)
    {
        var list = ToolRunner.ReadOutput(gsettings, ["get", GnomeSchema, "custom-keybindings"], Timeout);
        if (list is null)
            return false;

        var paths = ParseStringArray(list);
        if (!paths.Contains(GnomeKeybindingPath))
            paths.Add(GnomeKeybindingPath);

        var relocatable = $"{GnomeSchema}.custom-keybinding:{GnomeKeybindingPath}";
        return Set(gsettings, relocatable, "name", GVariantString("ContextSnap"))
            && Set(gsettings, relocatable, "command", GVariantString(Quote(target)))
            && Set(gsettings, relocatable, "binding", GVariantString(GnomeBinding))
            && Set(gsettings, GnomeSchema, "custom-keybindings", FormatStringArray(paths));
    }

    private static bool RemoveGnomeShortcut(string gsettings)
    {
        var list = ToolRunner.ReadOutput(gsettings, ["get", GnomeSchema, "custom-keybindings"], Timeout);
        if (list is null)
            return false;

        var paths = ParseStringArray(list);
        if (!paths.Remove(GnomeKeybindingPath))
            return false;

        var relocatable = $"{GnomeSchema}.custom-keybinding:{GnomeKeybindingPath}";
        foreach (var key in new[] { "name", "command", "binding" })
            ToolRunner.ReadOutput(gsettings, ["reset", relocatable, key], Timeout);
        return Set(gsettings, GnomeSchema, "custom-keybindings", FormatStringArray(paths));
    }

    private static bool Set(string gsettings, string schema, string key, string value) =>
        ToolRunner.ReadOutput(gsettings, ["set", schema, key, value], Timeout) is not null;

    /// <summary>Parses a GVariant string array as printed by gsettings, for example <c>['/a/', '/b/']</c> or <c>@as []</c>.</summary>
    public static List<string> ParseStringArray(string value) =>
        GVariantItem().Matches(value).Select(m => Regex.Unescape(m.Groups[1].Value)).ToList();

    public static string FormatStringArray(IEnumerable<string> items)
    {
        var list = items.Select(GVariantString).ToArray();
        return list.Length == 0 ? "@as []" : $"[{string.Join(", ", list)}]";
    }

    public static string GVariantString(string value) =>
        "'" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "'";

    /// <summary>
    /// The autostart entry. The path is quoted as the Desktop Entry specification requires for Exec,
    /// then escaped as a desktop-file string, so a path with spaces or special characters stays one argument.
    /// </summary>
    public static string DesktopEntry(string executable) => $"""
        [Desktop Entry]
        Type=Application
        Name=ContextSnap
        Comment=Copy a Markdown snapshot of your desktop context with {X11Hotkey.DisplayText}
        Exec={Quote(executable).Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "%%", StringComparison.Ordinal)} --background
        Terminal=false
        NoDisplay=true
        X-GNOME-Autostart-enabled=true

        """;

    /// <summary>Double-quotes a path for a desktop Exec line or a GNOME shortcut command.</summary>
    public static string Quote(string path)
    {
        if (path.Contains('\n') || path.Contains('\r'))
            throw new ArgumentException("The path contains a line break.", nameof(path));

        var builder = new StringBuilder("\"");
        foreach (var c in path)
        {
            if (c is '"' or '`' or '$' or '\\')
                builder.Append('\\');
            builder.Append(c);
        }

        return builder.Append('"').ToString();
    }

    [GeneratedRegex(@"'((?:[^'\\]|\\.)*)'")]
    private static partial Regex GVariantItem();
}
