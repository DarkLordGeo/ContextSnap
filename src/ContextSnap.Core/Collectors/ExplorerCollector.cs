using System.Runtime.InteropServices;
using ContextSnap.Model;
using ContextSnap.Platform;
using ContextSnap.Privacy;
using Microsoft.CSharp.RuntimeBinder;

namespace ContextSnap.Collectors;

/// <summary>One entry from the Shell's window list (File Explorer windows and tabs).</summary>
public sealed record RawShellWindow(string? ApplicationPath, string? FolderPath, string? LocationName, string? LocationUrl);

public interface IShellWindowSource
{
    IReadOnlyList<RawShellWindow> GetWindows(TimeSpan timeout);
}

public sealed record ExplorerSelection(IReadOnlyList<ExplorerFolder> Folders, int VirtualLocations, int ExcludedCount);

public static class ExplorerFilter
{
    public static ExplorerSelection Select(IEnumerable<RawShellWindow> windows, IReadOnlyCollection<string> excludedPathPrefixes)
    {
        var folders = new List<ExplorerFolder>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var virtualLocations = 0;
        var excluded = 0;

        foreach (var window in windows)
        {
            if (!IsFileExplorer(window.ApplicationPath))
                continue;

            var path = ResolvePath(window);
            if (path is null)
            {
                virtualLocations++;
                continue;
            }

            if (PrivacyRules.IsPathExcluded(path, excludedPathPrefixes))
            {
                excluded++;
                continue;
            }

            if (seen.Add(path.TrimEnd('\\')))
                folders.Add(new ExplorerFolder(path));
        }

        return new ExplorerSelection(
            folders.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
            virtualLocations,
            excluded);
    }

    /// <summary>Only windows hosted by explorer.exe; the Shell list can also contain legacy browser windows.</summary>
    internal static bool IsFileExplorer(string? applicationPath) =>
        applicationPath is not null
        && string.Equals(Path.GetFileName(applicationPath), "explorer.exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>A file-system path, or null for virtual locations such as Home, This PC, or Recycle Bin.</summary>
    internal static string? ResolvePath(RawShellWindow window)
    {
        if (IsFileSystemPath(window.FolderPath))
            return window.FolderPath;

        if (Uri.TryCreate(window.LocationUrl, UriKind.Absolute, out var uri) && uri.IsFile && IsFileSystemPath(uri.LocalPath))
            return uri.LocalPath;

        return null;
    }

    private static bool IsFileSystemPath(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && !path.StartsWith("::", StringComparison.Ordinal)
        && Path.IsPathFullyQualified(path);
}

public sealed class ExplorerCollector(IShellWindowSource source) : ICollector<ExplorerFolder>
{
    public string Name => "File Explorer folders";

    public CollectorOutput<ExplorerFolder> Collect(CollectorContext context, CancellationToken cancellationToken)
    {
        var settings = context.Settings;
        var windows = source.GetWindows(TimeSpan.FromSeconds(settings.CollectorTimeoutSeconds));
        var selection = ExplorerFilter.Select(windows, settings.ExcludedPathPrefixes);

        var notes = new List<string>();
        if (selection.VirtualLocations > 0)
            notes.Add($"{selection.VirtualLocations} Explorer window(s) showing virtual locations (such as Home or This PC) were omitted.");

        return new CollectorOutput<ExplorerFolder>(selection.Folders, notes, selection.ExcludedCount);
    }
}

/// <summary>
/// Reads open File Explorer windows through the documented Shell.Application automation object
/// (IShellWindows). Runs on a dedicated STA thread and releases every COM object it touches.
/// </summary>
public sealed class ShellComWindowSource : IShellWindowSource
{
    public IReadOnlyList<RawShellWindow> GetWindows(TimeSpan timeout) => StaThread.Run(ReadWindows, timeout);

    private static IReadOnlyList<RawShellWindow> ReadWindows()
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application", throwOnError: false)
            ?? throw new CollectorUnavailableException("The Windows Shell automation object (Shell.Application) is not registered.");

        object? shell = null;
        object? windows = null;
        try
        {
            shell = Activator.CreateInstance(shellType)
                ?? throw new CollectorUnavailableException("The Windows Shell automation object could not be created.");
            windows = ((dynamic)shell).Windows();
            int count = ((dynamic)windows!).Count;

            var result = new List<RawShellWindow>(count);
            for (var index = 0; index < count; index++)
            {
                var window = ReadWindow(windows!, index);
                if (window is not null)
                    result.Add(window);
            }

            return result;
        }
        finally
        {
            Release(windows);
            Release(shell);
        }
    }

    private static RawShellWindow? ReadWindow(object windows, int index)
    {
        object? window = null;
        try
        {
            window = ((dynamic)windows).Item(index);
            if (window is null)
                return null;

            dynamic item = window;
            return new RawShellWindow(
                TryRead(() => (string?)item.FullName),
                TryRead(() => (string?)item.Document.Folder.Self.Path),
                TryRead(() => (string?)item.LocationName),
                TryRead(() => (string?)item.LocationURL));
        }
        catch (COMException)
        {
            return null; // The window closed during enumeration.
        }
        finally
        {
            Release(window);
        }
    }

    private static string? TryRead(Func<string?> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is COMException or RuntimeBinderException or InvalidCastException or NullReferenceException)
        {
            return null; // Property unavailable for this window type (for example, a non-folder view).
        }
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
            Marshal.FinalReleaseComObject(comObject);
    }
}
