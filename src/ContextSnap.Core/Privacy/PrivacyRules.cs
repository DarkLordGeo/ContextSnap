namespace ContextSnap.Privacy;

/// <summary>Exclusion checks that drop whole items before they reach the snapshot.</summary>
public static class PrivacyRules
{
    /// <summary>The process name File Explorer windows run under, as checked against excluded programs.</summary>
    public const string FileExplorerProcessName = "explorer";

    /// <summary>
    /// True when the process matches an excluded program. An entry is either a program name
    /// (<c>Discord</c> or <c>Discord.exe</c>) or a full executable path (<c>C:\Apps\Discord.exe</c>).
    /// A path entry matches by name wherever the process's own path is unknown, so it never
    /// excludes less than a name entry would.
    /// </summary>
    public static bool IsProcessExcluded(string processName, IEnumerable<string> excludedProcesses) =>
        IsProcessExcluded(processName, executablePath: null, excludedProcesses);

    /// <summary>
    /// As <see cref="IsProcessExcluded(string, IEnumerable{string})"/>, but a path entry only matches a
    /// process whose known <paramref name="executablePath"/> is that same file.
    /// </summary>
    public static bool IsProcessExcluded(string processName, string? executablePath, IEnumerable<string> excludedProcesses)
    {
        var name = StripExe(processName);
        var path = NormalizeExecutablePath(executablePath);

        foreach (var entry in excludedProcesses)
        {
            if (string.IsNullOrWhiteSpace(entry))
                continue;

            if (!IsPathEntry(entry))
            {
                if (string.Equals(StripExe(entry.Trim()), name, StringComparison.OrdinalIgnoreCase))
                    return true;
                continue;
            }

            if (path is null)
            {
                if (string.Equals(ProgramNameOf(entry), name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (string.Equals(NormalizeExecutablePath(entry), path, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The program name of an exclusion entry: the file name without directories or ".exe".</summary>
    public static string ProgramNameOf(string entry)
    {
        var trimmed = entry.Trim();
        var separator = trimmed.LastIndexOfAny(['\\', '/']);
        return StripExe(separator >= 0 ? trimmed[(separator + 1)..] : trimmed);
    }

    /// <summary>True when the entry names a file by path rather than a program by name.</summary>
    public static bool IsPathEntry(string entry) => entry.IndexOfAny(['\\', '/']) >= 0;

    /// <summary>
    /// Turns a path chosen in a file picker into an exclusion entry (the program name, which keeps
    /// working when the program is updated or moved). Returns null with a reason for a path that is
    /// empty, not an .exe, or not an existing file.
    /// </summary>
    public static string? ExclusionEntryForExecutable(string? executablePath, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            error = "No program was chosen.";
            return null;
        }

        var path = executablePath.Trim();
        if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            error = "Choose a program file (.exe).";
            return null;
        }

        bool exists;
        try
        {
            exists = File.Exists(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            exists = false;
        }

        if (!exists)
        {
            error = "That program file was not found.";
            return null;
        }

        var name = ProgramNameOf(path);
        if (name.Length == 0)
        {
            error = "That file has no program name.";
            return null;
        }

        return name;
    }

    private static string? NormalizeExecutablePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        return path.Trim().Replace('/', '\\');
    }

    public static bool ContainsExcludedKeyword(string text, IEnumerable<string> keywords) =>
        keywords.Any(keyword => keyword.Length > 0 && text.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True when <paramref name="path"/> equals or lies under one of the prefixes. Prefixes may use
    /// environment variables (<c>%USERPROFILE%\Documents</c>) or <c>~</c> for the user profile.
    /// </summary>
    public static bool IsPathExcluded(string path, IEnumerable<string> excludedPrefixes)
    {
        var normalizedPath = NormalizePath(path);
        if (normalizedPath is null)
            return false;

        foreach (var prefix in excludedPrefixes)
        {
            var normalizedPrefix = NormalizePath(ExpandPrefix(prefix));
            if (normalizedPrefix is null)
                continue;

            if (normalizedPath.Equals(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
                return true;

            var withSeparator = normalizedPrefix.EndsWith('\\') ? normalizedPrefix : normalizedPrefix + "\\";
            if (normalizedPath.StartsWith(withSeparator, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    internal static string ExpandPrefix(string prefix)
    {
        var expanded = Environment.ExpandEnvironmentVariables(prefix.Trim());
        if (expanded == "~" || expanded.StartsWith("~\\", StringComparison.Ordinal) || expanded.StartsWith("~/", StringComparison.Ordinal))
            expanded = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + expanded[1..];
        return expanded;
    }

    private static string? NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var normalized = path.Trim().Replace('/', '\\');
        while (normalized.Length > 3 && normalized.EndsWith('\\'))
            normalized = normalized[..^1];
        return normalized;
    }

    private static string StripExe(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}
