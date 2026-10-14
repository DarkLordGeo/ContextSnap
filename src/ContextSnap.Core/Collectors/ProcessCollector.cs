using System.Diagnostics;
using ContextSnap.Model;
using ContextSnap.Platform;
using ContextSnap.Privacy;

namespace ContextSnap.Collectors;

public sealed record RawProcess(int Id, string Name);

public interface IProcessSource
{
    IReadOnlyList<RawProcess> GetProcesses();

    /// <summary>Executable path, or null when access is denied or the process exited.</summary>
    string? GetExecutablePath(int processId);
}

public static class ProcessGrouper
{
    /// <summary>
    /// Merges processes with the same name and executable. A process whose path is unknown joins the
    /// first known path for that name, so access-denied helpers do not produce duplicate entries.
    /// </summary>
    public static IReadOnlyList<ProcessGroup> Group(IEnumerable<(RawProcess Process, string? Path)> processes)
    {
        var list = processes.ToList();
        var firstKnownPath = list
            .Where(p => p.Path is not null)
            .GroupBy(p => p.Process.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Path, StringComparer.OrdinalIgnoreCase);

        return list
            .Select(p => (p.Process, Path: p.Path ?? firstKnownPath.GetValueOrDefault(p.Process.Name)))
            .GroupBy(p => (Name: p.Process.Name.ToLowerInvariant(), Path: p.Path?.ToLowerInvariant()))
            .Select(g => new ProcessGroup(
                g.First().Process.Name,
                g.First().Path,
                g.Select(p => p.Process.Id).Order().ToArray()))
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

/// <summary>Lists running developer tools from a configurable allow-list. Command lines are never read.</summary>
public sealed class ProcessCollector(IProcessSource source) : ICollector<ProcessGroup>
{
    public string Name => "Developer tools";

    public CollectorOutput<ProcessGroup> Collect(CollectorContext context, CancellationToken cancellationToken)
    {
        var settings = context.Settings;
        var relevant = new HashSet<string>(settings.DeveloperProcesses.Select(StripExe), StringComparer.OrdinalIgnoreCase);
        var excluded = 0;
        var matches = new List<(RawProcess, string?)>();

        foreach (var process in source.GetProcesses())
        {
            if (!relevant.Contains(process.Name))
                continue;

            if (PrivacyRules.IsProcessExcluded(process.Name, settings.ExcludedProcesses))
            {
                excluded++;
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var path = settings.IncludeExecutablePaths ? source.GetExecutablePath(process.Id) : null;
            if (path is not null && PrivacyRules.IsPathExcluded(path, settings.ExcludedPathPrefixes))
                path = null;
            matches.Add((process, path));
        }

        return new CollectorOutput<ProcessGroup>(ProcessGrouper.Group(matches), ExcludedCount: excluded);
    }

    private static string StripExe(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}

public sealed class SystemProcessSource : IProcessSource
{
    public IReadOnlyList<RawProcess> GetProcesses()
    {
        var processes = new List<RawProcess>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    processes.Add(new RawProcess(process.Id, process.ProcessName));
                }
                catch (InvalidOperationException)
                {
                    // The process exited between enumeration and reading its name.
                }
            }
        }

        return processes;
    }

    public string? GetExecutablePath(int processId) => ProcessInfoReader.GetImagePath(processId);
}
