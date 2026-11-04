using System.Diagnostics;
using System.IO;
using ContextSnap.Collectors;
using ContextSnap.Configuration;
using ContextSnap.Privacy;

namespace ContextSnap;

/// <summary>One row of the Excluded applications list. <see cref="Entry"/> is the stored settings value.</summary>
public sealed record ExcludedAppItem(string Entry, string DisplayName, string Detail, bool IsDefault)
{
    public string Monogram => DisplayName.Length > 0 ? char.ToUpperInvariant(DisplayName[0]).ToString() : "?";
    public string RemoveLabel => $"Remove {DisplayName} from excluded applications";
}

/// <summary>An application with a visible window, offered by "Choose running app".</summary>
public sealed record RunningApp(string ProgramName, string DisplayName, string? ExecutablePath)
{
    public string Detail => ExecutablePath ?? ProgramName + ".exe";
}

/// <summary>
/// Presentation helpers for the Excluded applications list. The list itself is
/// <see cref="AppSettings.ExcludedProcesses"/>: the UI edits that setting and nothing else, so what is shown
/// is exactly what the collectors enforce.
/// </summary>
public static class ExcludedApplications
{
    /// <summary>Rows for the stored entries, in stored order. <paramref name="describe"/> resolves a friendly name.</summary>
    public static IReadOnlyList<ExcludedAppItem> Describe(IEnumerable<string> entries, Func<string, string?> describe)
    {
        var defaults = new HashSet<string>(AppSettings.DefaultExcludedProcesses, StringComparer.OrdinalIgnoreCase);
        return entries
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(entry =>
            {
                var program = PrivacyRules.ProgramNameOf(entry);
                var name = describe(entry) is { Length: > 0 } friendly ? friendly : program;
                var detail = PrivacyRules.IsPathEntry(entry) ? entry.Trim() : program + ".exe";
                return new ExcludedAppItem(entry, name, detail, defaults.Contains(program));
            })
            .ToArray();
    }

    /// <summary>
    /// Adds <paramref name="entry"/> unless an existing entry already covers it. Returns false (and leaves the
    /// list unchanged) when it is already excluded.
    /// </summary>
    public static bool TryAdd(List<string> excluded, string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
            return false;
        var program = PrivacyRules.ProgramNameOf(entry);
        if (excluded.Any(e => !PrivacyRules.IsPathEntry(e) && string.Equals(PrivacyRules.ProgramNameOf(e), program, StringComparison.OrdinalIgnoreCase))
            || excluded.Any(e => string.Equals(e.Trim(), entry.Trim(), StringComparison.OrdinalIgnoreCase)))
            return false;
        excluded.Add(entry.Trim());
        return true;
    }

    /// <summary>Removes exactly the stored entry. Returns its former index, or -1.</summary>
    public static int Remove(List<string> excluded, string entry)
    {
        var index = excluded.FindIndex(e => string.Equals(e, entry, StringComparison.Ordinal));
        if (index >= 0)
            excluded.RemoveAt(index);
        return index;
    }

    /// <summary>
    /// Distinct applications with a visible window (the same Alt+Tab-style windows a capture lists), minus
    /// ContextSnap itself and anything already excluded. Window titles are never part of the result.
    /// </summary>
    public static IReadOnlyList<RunningApp> ListRunning(
        IEnumerable<RawWindow> windows, Func<int, string?> executablePath, IReadOnlyCollection<string> excluded, int ownProcessId)
    {
        var (visible, _) = WindowFilter.Select(windows, new WindowFilterOptions([], [], ownProcessId));
        var result = new List<RunningApp>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var window in visible)
        {
            if (window.ProcessName == "unknown" || !seen.Add(window.ProcessName))
                continue;
            var path = executablePath(window.ProcessId);
            if (PrivacyRules.IsProcessExcluded(window.ProcessName, path, excluded))
                continue;
            result.Add(new RunningApp(window.ProcessName, FriendlyName(path) ?? window.ProcessName, path));
        }

        return result.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    /// <summary>The program's own description ("Visual Studio Code"), or null when unreadable.</summary>
    public static string? FriendlyName(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            return null;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            var name = info.FileDescription?.Trim();
            if (string.IsNullOrEmpty(name))
                name = info.ProductName?.Trim();
            return string.IsNullOrEmpty(name) || name.Length > 80 ? null : name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
