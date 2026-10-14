namespace ContextSnap.Privacy;

/// <summary>Exclusion checks that drop whole items before they reach the snapshot.</summary>
public static class PrivacyRules
{
    public static bool IsProcessExcluded(string processName, IEnumerable<string> excludedProcesses)
    {
        var name = StripExe(processName);
        return excludedProcesses.Any(excluded =>
            string.Equals(StripExe(excluded), name, StringComparison.OrdinalIgnoreCase));
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
