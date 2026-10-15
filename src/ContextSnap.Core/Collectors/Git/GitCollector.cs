using System.Security;
using ContextSnap.Model;
using ContextSnap.Privacy;

namespace ContextSnap.Collectors.Git;

public static class RepositoryLocator
{
    private const int MaxDepth = 64;

    /// <summary>Walks up from <paramref name="startPath"/> to the nearest folder containing a <c>.git</c> directory or file.</summary>
    public static string? FindRoot(string startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath) || !Path.IsPathFullyQualified(startPath))
            return null;

        DirectoryInfo? directory;
        try
        {
            directory = new DirectoryInfo(Path.GetFullPath(startPath));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException or SecurityException)
        {
            return null;
        }

        for (var depth = 0; directory is not null && depth < MaxDepth; depth++, directory = directory.Parent)
        {
            var marker = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(marker) || File.Exists(marker))
                return directory.FullName;
        }

        return null;
    }

    /// <summary>Distinct repository roots for the candidates, in candidate order.</summary>
    public static IReadOnlyList<string> FindRoots(IEnumerable<string> candidates) =>
        candidates
            .Select(FindRoot)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

/// <summary>Collects read-only Git metadata (branch, status summary, recent commits) for detected project folders.</summary>
public sealed class GitCollector(IGitRunner runner) : ICollector<GitRepositoryInfo>
{
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);
    private const int RecentCommitCount = 3;

    public string Name => "Git repositories";

    public CollectorOutput<GitRepositoryInfo> Collect(CollectorContext context, CancellationToken cancellationToken)
    {
        var settings = context.Settings;
        var roots = RepositoryLocator.FindRoots(context.ProjectCandidates)
            .Where(root => !PrivacyRules.IsPathExcluded(root, settings.ExcludedPathPrefixes))
            .ToArray();
        if (roots.Length == 0)
            return new CollectorOutput<GitRepositoryInfo>([]);

        if (!runner.IsAvailable)
            throw new CollectorUnavailableException("Git was not found on PATH.");

        var maxChanges = settings.IncludeGitChangedFiles ? settings.MaxChangedFilesPerRepository : 0;
        var repositories = new List<GitRepositoryInfo>();
        foreach (var root in roots.Take(settings.MaxRepositories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            repositories.Add(ReadRepository(root, maxChanges));
        }

        var notes = new List<string>();
        if (roots.Length > settings.MaxRepositories)
            notes.Add($"{roots.Length - settings.MaxRepositories} more repositories were not inspected (repository limit).");

        return new CollectorOutput<GitRepositoryInfo>(repositories, notes);
    }

    private GitRepositoryInfo ReadRepository(string root, int maxChanges)
    {
        var status = runner.Run(
            root,
            ["status", "--porcelain=v2", "--branch", "-z", "--untracked-files=normal", "--ignore-submodules=all"],
            CommandTimeout);
        if (!status.Succeeded)
            return new GitRepositoryInfo(root) { Error = DescribeFailure("git status", status) };

        var repository = GitStatusParser.Parse(root, status.Output, maxChanges);
        if (repository.HasNoCommits)
            return repository;

        var log = runner.Run(root, ["log", $"-{RecentCommitCount}", "--no-color", "--format=%h%x1f%s%x1f%cr"], CommandTimeout);
        return log.Succeeded ? repository with { RecentCommits = GitStatusParser.ParseLog(log.Output) } : repository;
    }

    internal static string DescribeFailure(string command, GitCommandResult result)
    {
        if (result.TimedOut)
            return $"{command} timed out after {CommandTimeout.TotalSeconds:0} seconds.";

        if (result.Error.Contains("dubious ownership", StringComparison.OrdinalIgnoreCase)
            || result.Error.Contains("safe.directory", StringComparison.OrdinalIgnoreCase))
            return "Git refused to read this repository because it is owned by another user (safe.directory). ContextSnap does not override this check.";

        if (result.Error.Contains("not a git repository", StringComparison.OrdinalIgnoreCase))
            return "Not a Git repository.";

        var firstLine = result.Error
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return firstLine is null
            ? $"{command} exited with code {result.ExitCode}."
            : $"{command} exited with code {result.ExitCode}: {firstLine}";
    }
}
