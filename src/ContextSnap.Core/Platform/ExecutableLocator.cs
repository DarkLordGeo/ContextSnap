namespace ContextSnap.Platform;

/// <summary>
/// Resolves an executable to an absolute path using only absolute PATH entries, so a binary planted
/// in the current directory can never be picked up.
/// </summary>
public static class ExecutableLocator
{
    public static string? FindOnPath(string fileName, string? pathVariable = null, IEnumerable<string>? fallbackLocations = null)
    {
        var path = pathVariable ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var directory = entry.Trim('"');
            if (!Path.IsPathFullyQualified(directory))
                continue;

            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return fallbackLocations?.FirstOrDefault(File.Exists);
    }
}
