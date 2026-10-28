using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace ContextSnap.Platform;

public sealed record ShortcutDefinition(
    string TargetPath,
    string Arguments,
    string WorkingDirectory,
    string Description,
    string IconPath);

/// <summary>Reads and writes .lnk files. Abstracted so tests never touch the real Startup folder.</summary>
public interface IShortcutStore
{
    bool Exists(string shortcutPath);

    /// <summary>The shortcut's contents, or null when the file does not exist.</summary>
    ShortcutDefinition? Read(string shortcutPath);

    void Write(string shortcutPath, ShortcutDefinition shortcut);

    void Delete(string shortcutPath);
}

/// <param name="FileName">Executable the shortcut starts.</param>
/// <param name="Arguments">Command-line arguments, already quoted.</param>
/// <param name="IsDevelopmentBuild">True when running from a <c>bin\Debug</c> or <c>bin\Release</c> build folder.</param>
public sealed record LaunchTarget(string FileName, string Arguments, string WorkingDirectory, bool IsDevelopmentBuild);

public sealed record LaunchTargetResult(LaunchTarget? Target, string? Error);

/// <summary>Works out which executable a startup shortcut should launch for the running copy of Snapctx.</summary>
public static class LaunchTargetResolver
{
    public const string ExecutableName = "Snapctx.exe";
    public const string AssemblyFileName = "Snapctx.dll";

    /// <summary>The launcher and assembly names before the rename from ContextSnap to Snapctx.</summary>
    public const string LegacyExecutableName = "ContextSnap.exe";
    public const string LegacyAssemblyFileName = "ContextSnap.dll";
    public const string StartupArgument = "--startup";

    /// <summary>
    /// Prefers the native launcher (Snapctx.exe) next to the application, which exists for both
    /// <c>dotnet run</c> builds and published builds. Falls back to <c>dotnet.exe Snapctx.dll</c>
    /// when the app was started through the dotnet host and no launcher exists.
    /// </summary>
    public static LaunchTargetResult Resolve(string? processPath, string baseDirectory, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            return new LaunchTargetResult(null, "The application folder could not be determined.");

        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
        var isDevelopment = IsDevelopmentBuild(directory);

        var launcher = Path.Combine(directory, ExecutableName);
        if (fileExists(launcher))
            return new LaunchTargetResult(new LaunchTarget(launcher, StartupArgument, directory, isDevelopment), null);

        var assembly = Path.Combine(directory, AssemblyFileName);
        if (processPath is not null
            && string.Equals(Path.GetFileName(processPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase)
            && fileExists(processPath)
            && fileExists(assembly))
        {
            return new LaunchTargetResult(
                new LaunchTarget(processPath, $"\"{assembly}\" {StartupArgument}", directory, isDevelopment), null);
        }

        return new LaunchTargetResult(null, $"The Snapctx executable was not found in {directory}.");
    }

    public static LaunchTargetResult ResolveCurrent() =>
        Resolve(Environment.ProcessPath, AppContext.BaseDirectory, File.Exists);

    internal static bool IsDevelopmentBuild(string directory)
    {
        var withSeparator = Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar;
        return withSeparator.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase)
            || withSeparator.Contains(@"\bin\Release\", StringComparison.OrdinalIgnoreCase);
    }
}

/// <param name="IsRegistered">Whether Snapctx's shortcut is present in the Startup folder afterwards.</param>
/// <param name="Success">False when the requested change could not be made.</param>
/// <param name="NeedsAttention">The change worked, but the user should know something (for example, Windows has the item turned off).</param>
public sealed record StartupState(bool IsRegistered, bool Success, string Message, string? ShortcutPath, bool NeedsAttention = false);

/// <summary>
/// Adds or removes Snapctx's shortcut in the current user's Startup folder. No administrator
/// rights, registry Run keys, scheduled tasks, or services are used. Only a shortcut named
/// <see cref="ShortcutFileName"/> that points at Snapctx is ever modified or deleted.
/// </summary>
public sealed class StartupRegistration(
    IShortcutStore store,
    Func<string?> startupFolderProvider,
    Func<LaunchTargetResult> launchTargetProvider,
    Func<string, bool?>? isDisabledByWindows = null)
{
    public const string ShortcutFileName = "Snapctx.lnk";
    public const string ShortcutDescription = "Snapctx: capture desktop context for AI chats";

    /// <summary>The shortcut ContextSnap (the previous name) created. Replaced on enable, removed on disable.</summary>
    public const string LegacyShortcutFileName = "ContextSnap.lnk";

    public static StartupRegistration CreateDefault() => new(
        new ShellLinkShortcutStore(),
        () => Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        LaunchTargetResolver.ResolveCurrent,
        StartupApproval.IsDisabledByWindows);

    public string? ShortcutPath
    {
        get
        {
            var folder = startupFolderProvider();
            return string.IsNullOrWhiteSpace(folder) ? null : Path.Combine(folder, ShortcutFileName);
        }
    }

    private string? LegacyShortcutPath
    {
        get
        {
            var folder = startupFolderProvider();
            return string.IsNullOrWhiteSpace(folder) ? null : Path.Combine(folder, LegacyShortcutFileName);
        }
    }

    /// <summary>
    /// Removes ContextSnap.lnk when it points at this app (under either name), so an upgraded install never
    /// starts twice at sign-in. A same-named shortcut for another program is left alone.
    /// </summary>
    private bool RemoveLegacyShortcut()
    {
        if (LegacyShortcutPath is not { } legacy || store.Read(legacy) is not { } existing || !IsOwnShortcut(existing))
            return false;
        store.Delete(legacy);
        return true;
    }

    /// <summary>Makes the Startup folder match <paramref name="enabled"/>. Never throws for expected OS failures.</summary>
    public StartupState Apply(bool enabled)
    {
        var path = ShortcutPath;
        if (path is null)
            return new StartupState(false, false, "The Windows Startup folder could not be found.", null);

        try
        {
            return enabled ? Enable(path) : Disable(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException
                                       or COMException or ArgumentException or InvalidCastException)
        {
            var action = enabled ? "create" : "remove";
            return new StartupState(SafeExists(path), false,
                $"Could not {action} the startup shortcut ({ex.GetType().Name}: {ex.Message})", path);
        }
    }

    private StartupState Enable(string path)
    {
        var resolved = launchTargetProvider();
        if (resolved.Target is not { } target)
            return new StartupState(SafeExists(path), false, $"Start with Windows is unavailable: {resolved.Error}", path);

        var desired = new ShortcutDefinition(target.FileName, target.Arguments, target.WorkingDirectory, ShortcutDescription, target.FileName);
        var existing = store.Read(path);
        if (existing is not null && !IsOwnShortcut(existing))
        {
            return new StartupState(false, false,
                $"{path} already exists and points to another program, so it was left unchanged.", path, NeedsAttention: true);
        }

        if (existing is null || !Matches(existing, desired))
            store.Write(path, desired);
        RemoveLegacyShortcut();

        var message = $"Snapctx starts in the background when you sign in (no window, taskbar button, or icon). Shortcut: {path} → {target.FileName}";
        var needsAttention = false;
        if (target.IsDevelopmentBuild)
        {
            message += " (development build: the shortcut follows whichever copy of Snapctx you open last).";
        }

        if (isDisabledByWindows?.Invoke(ShortcutFileName) == true)
        {
            message += " Windows currently has this item turned off under Settings → Apps → Startup; turn it on there for it to run.";
            needsAttention = true;
        }

        return new StartupState(true, true, message, path, needsAttention);
    }

    private StartupState Disable(string path)
    {
        var removedLegacy = RemoveLegacyShortcut();
        var existing = store.Read(path);
        if (existing is null)
        {
            return new StartupState(false, true, removedLegacy
                ? "Snapctx does not start with Windows. The startup shortcut was removed."
                : "Snapctx does not start with Windows.", path);
        }

        if (!IsOwnShortcut(existing))
        {
            return new StartupState(true, false,
                $"{path} points to another program, so it was not removed.", path, NeedsAttention: true);
        }

        store.Delete(path);
        return new StartupState(false, true, "Snapctx does not start with Windows. The startup shortcut was removed.", path);
    }

    /// <summary>Guards against modifying a same-named shortcut that belongs to something else.</summary>
    /// Recognizes this app under its current and its previous name.
    internal static bool IsOwnShortcut(ShortcutDefinition shortcut)
    {
        var target = Path.GetFileName(shortcut.TargetPath);
        return string.Equals(target, LaunchTargetResolver.ExecutableName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(target, LaunchTargetResolver.LegacyExecutableName, StringComparison.OrdinalIgnoreCase)
            || shortcut.Arguments.Contains(LaunchTargetResolver.AssemblyFileName, StringComparison.OrdinalIgnoreCase)
            || shortcut.Arguments.Contains(LaunchTargetResolver.LegacyAssemblyFileName, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool Matches(ShortcutDefinition existing, ShortcutDefinition desired) =>
        SamePath(existing.TargetPath, desired.TargetPath)
        && SamePath(existing.WorkingDirectory, desired.WorkingDirectory)
        && string.Equals(existing.Arguments, desired.Arguments, StringComparison.Ordinal);

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    private bool SafeExists(string path)
    {
        try
        {
            return store.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }
}

/// <summary>Reads (never writes) whether the user turned the item off in Windows' Startup apps settings.</summary>
public static class StartupApproval
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    /// <returns>True if disabled, false if enabled, null if Windows has no record yet.</returns>
    public static bool? IsDisabledByWindows(string shortcutFileName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            if (key?.GetValue(shortcutFileName) is not byte[] { Length: > 0 } value)
                return null;

            // Windows stores an odd first byte (for example 0x03) for disabled items and an even one (0x02) for enabled.
            return (value[0] & 1) == 1;
        }
        catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
