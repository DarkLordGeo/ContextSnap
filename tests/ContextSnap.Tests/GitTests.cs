using System.Diagnostics;
using ContextSnap.Collectors;
using ContextSnap.Collectors.Git;
using ContextSnap.Configuration;
using ContextSnap.Platform;

namespace ContextSnap.Tests;

public class GitStatusParserTests
{
    [Fact]
    public void Parse_ReadsBranchUpstreamAndChanges()
    {
        var output = string.Join('\0',
            "# branch.oid 0123456789abcdef0123456789abcdef01234567",
            "# branch.head feature/x",
            "# branch.upstream origin/feature/x",
            "# branch.ab +2 -1",
            "1 M. N... 100644 100644 100644 aaa bbb src/staged file.cs",
            "1 .M N... 100644 100644 100644 aaa bbb README.md",
            "2 R. N... 100644 100644 100644 aaa bbb R100 new name.cs",
            "old name.cs",
            "u UU N... 100644 100644 100644 100644 aaa bbb ccc conflict.cs",
            "? notes.txt",
            "");

        var repo = GitStatusParser.Parse(@"D:\repo", output, maxListedChanges: 10);

        Assert.Equal("feature/x", repo.Branch);
        Assert.Equal("0123456", repo.HeadShortHash);
        Assert.Equal("origin/feature/x", repo.Upstream);
        Assert.Equal((2, 1), (repo.Ahead, repo.Behind));
        Assert.Equal(2, repo.Staged);
        Assert.Equal(1, repo.Unstaged);
        Assert.Equal(1, repo.Untracked);
        Assert.Equal(1, repo.Conflicted);
        Assert.Equal(5, repo.TotalChanges);
        Assert.Equal(["M ", " M", "R ", "UU", "??"], repo.Changes.Select(c => c.Code));
        Assert.Equal("src/staged file.cs", repo.Changes[0].Path);
        Assert.Equal("new name.cs", repo.Changes[2].Path);
        Assert.Equal("old name.cs", repo.Changes[2].OriginalPath);
    }

    [Fact]
    public void Parse_HandlesDetachedInitialAndLimit()
    {
        var detached = GitStatusParser.Parse("r", "# branch.oid abcdef1234\0# branch.head (detached)\0? a\0? b\0? c\0", 1);
        var initial = GitStatusParser.Parse("r", "# branch.oid (initial)\0# branch.head main\0", 10);

        Assert.True(detached.IsDetached);
        Assert.Equal("abcdef1", detached.HeadShortHash);
        Assert.Single(detached.Changes);
        Assert.Equal(3, detached.TotalChanges);
        Assert.True(initial.HasNoCommits);
        Assert.True(initial.IsClean);
    }

    [Fact]
    public void Parse_IgnoresMalformedAndTruncatedEntries()
    {
        var repo = GitStatusParser.Parse("r", "garbage\01 M.\02 R. too few\0# branch.ab nonsense\0", 10);

        Assert.Equal(0, repo.TotalChanges);
    }

    [Fact]
    public void ParseLog_SplitsUnitSeparatedFields()
    {
        var commits = GitStatusParser.ParseLog("abc1234\u001fFix bug\u001f2 hours ago\ndef5678\u001fInitial\u001f3 days ago\n");

        Assert.Equal(2, commits.Count);
        Assert.Equal(("abc1234", "Fix bug", "2 hours ago"), (commits[0].Hash, commits[0].Subject, commits[0].RelativeDate));
    }
}

public sealed class GitCollectorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ContextSnapGit").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class FakeGitRunner(bool available = true) : IGitRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];
        public Func<IReadOnlyList<string>, GitCommandResult> Handler { get; set; } = _ => new GitCommandResult(0, string.Empty, string.Empty, false);

        public bool IsAvailable => available;

        public GitCommandResult Run(string repositoryRoot, IReadOnlyList<string> arguments, TimeSpan timeout)
        {
            Calls.Add(arguments);
            return Handler(arguments);
        }
    }

    private string CreateRepo(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(path, ".git"));
        Directory.CreateDirectory(Path.Combine(path, "src", "deep"));
        return path;
    }

    private static CollectorContext Context(params string[] candidates) => new(new AppSettings(), candidates);

    [Fact]
    public void Collect_FindsRepositoryFromNestedFolderAndDeduplicates()
    {
        var repo = CreateRepo("app");
        var runner = new FakeGitRunner
        {
            Handler = args => args[0] == "status"
                ? new GitCommandResult(0, "# branch.oid 1234567abc\0# branch.head main\0", string.Empty, false)
                : new GitCommandResult(0, "1234567\u001fFirst\u001fnow\n", string.Empty, false),
        };

        var output = new GitCollector(runner).Collect(Context(Path.Combine(repo, "src", "deep"), repo), CancellationToken.None);

        var info = Assert.Single(output.Items);
        Assert.Equal(repo, info.Root);
        Assert.Equal("main", info.Branch);
        Assert.Equal("First", Assert.Single(info.RecentCommits).Subject);
        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public void Collect_ReturnsEmptyWithoutRunningGitWhenNoRepositories()
    {
        var runner = new FakeGitRunner(available: false);

        var output = new GitCollector(runner).Collect(Context(_root), CancellationToken.None);

        Assert.Empty(output.Items);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void Collect_ReportsMissingGitAsUnavailable()
    {
        var repo = CreateRepo("app");

        Assert.Throws<CollectorUnavailableException>(() =>
            new GitCollector(new FakeGitRunner(available: false)).Collect(Context(repo), CancellationToken.None));
    }

    [Fact]
    public void Collect_RecordsPerRepositoryFailuresWithoutFailingOthers()
    {
        var bad = CreateRepo("bad");
        var good = CreateRepo("good");
        var runner = new FakeGitRunner();
        runner.Handler = args => runner.Calls.Count == 1
            ? new GitCommandResult(128, string.Empty, "fatal: detected dubious ownership in repository", false)
            : new GitCommandResult(0, "# branch.head main\0", string.Empty, false);

        var output = new GitCollector(runner).Collect(Context(bad, good), CancellationToken.None);

        Assert.Equal(2, output.Items.Count);
        Assert.Contains("safe.directory", output.Items[0].Error);
        Assert.Null(output.Items[1].Error);
    }

    [Fact]
    public void Collect_HonorsRepositoryLimitAndChangedFileSetting()
    {
        var repos = Enumerable.Range(0, 3).Select(i => CreateRepo($"r{i}")).ToArray();
        var runner = new FakeGitRunner
        {
            Handler = _ => new GitCommandResult(0, "# branch.head main\0? secret-plan.txt\0", string.Empty, false),
        };
        var context = new CollectorContext(new AppSettings { MaxRepositories = 2, IncludeGitChangedFiles = false }, repos);

        var output = new GitCollector(runner).Collect(context, CancellationToken.None);

        Assert.Equal(2, output.Items.Count);
        Assert.All(output.Items, r => Assert.Empty(r.Changes));
        Assert.All(output.Items, r => Assert.Equal(1, r.Untracked));
        Assert.Contains("1 more repositories", Assert.Single(output.Notes!));
    }

    [Fact]
    public void DescribeFailure_ReportsTimeout()
    {
        var message = GitCollector.DescribeFailure("git status", new GitCommandResult(-1, "", "", TimedOut: true));

        Assert.Contains("timed out", message);
    }

    [Fact]
    public void RepositoryLocator_IgnoresRelativeAndMissingPaths()
    {
        Assert.Null(RepositoryLocator.FindRoot("relative\\path"));
        Assert.Null(RepositoryLocator.FindRoot(""));
        Assert.Null(RepositoryLocator.FindRoot(Path.Combine(_root, "does-not-exist")));
    }
}

public class GitProcessRunnerTests
{
    [Fact]
    public void BuildArguments_DisablesLocksHooksAndPagerBeforeSubcommand()
    {
        var args = GitProcessRunner.BuildArguments(@"D:\repo", ["status"]);

        Assert.Equal("status", args[^1]);
        Assert.Contains("--no-optional-locks", args);
        Assert.Contains("--no-pager", args);
        Assert.Contains("core.fsmonitor=false", args);
        Assert.Contains("log.showSignature=false", args);
        Assert.Equal(@"D:\repo", args[args.ToList().IndexOf("-C") + 1]);
    }

    [Fact]
    public void ExecutableLocator_IgnoresRelativePathEntries()
    {
        var directory = Directory.CreateTempSubdirectory("ContextSnapPath").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "tool.exe"), string.Empty);

            Assert.Null(ExecutableLocator.FindOnPath("tool.exe", "." + Path.PathSeparator + "relative"));
            Assert.Equal(Path.Combine(directory, "tool.exe"), ExecutableLocator.FindOnPath("tool.exe", "relative" + Path.PathSeparator + $"\"{directory}\""));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FilterOverrides_DisablesEveryConfiguredFilter()
    {
        var overrides = GitProcessRunner.FilterOverrides(
            "filter.evil.clean\necho x; cat\0filter.lfs.process\ngit-lfs filter-process\0filter.My Name.required\ntrue\0filter.evil.smudge\ncat\0")!;

        foreach (var name in new[] { "evil", "lfs", "My Name" })
        {
            Assert.Contains($"filter.{name}.clean=", overrides);
            Assert.Contains($"filter.{name}.smudge=", overrides);
            Assert.Contains($"filter.{name}.process=", overrides);
            Assert.Contains($"filter.{name}.required=false", overrides);
        }
        Assert.Equal(3 * 8, overrides.Count);
        Assert.All(overrides.Where((_, i) => i % 2 == 0), option => Assert.Equal("-c", option));
    }

    [Fact]
    public void FilterOverrides_HandlesNoFiltersAndRefusesNamesThatCannotBeOverridden()
    {
        Assert.Empty(GitProcessRunner.FilterOverrides(string.Empty)!);
        Assert.Null(GitProcessRunner.FilterOverrides("filter.a=b.clean\necho x\0"));
    }

    /// <summary>
    /// A repository's own config can define filter drivers that Git runs as shell commands while reading
    /// files. Capturing context from such a repository must not run them, including inside submodules.
    /// </summary>
    [GitFact]
    public void Collect_DoesNotRunRepositoryFiltersOrEnterSubmodules()
    {
        var directory = Directory.CreateTempSubdirectory("ContextSnapMaliciousGit").FullName;
        var marker = Path.Combine(directory, "filter-ran.marker");
        try
        {
            var sub = Path.Combine(directory, "sub");
            Directory.CreateDirectory(sub);
            RunGit(sub, "init", "--quiet", "--initial-branch=main");
            File.WriteAllText(Path.Combine(sub, "s.txt"), "s");
            File.WriteAllText(Path.Combine(sub, ".gitattributes"), "* filter=subevil\n");
            RunGit(sub, "add", ".");
            Commit(sub, "Submodule");

            RunGit(directory, "init", "--quiet", "--initial-branch=main");
            File.WriteAllText(Path.Combine(directory, "a.txt"), "a");
            File.WriteAllText(Path.Combine(directory, "b.txt"), "b");
            File.WriteAllText(Path.Combine(directory, ".gitattributes"), "a.txt filter=evil\n");
            RunGit(directory, "add", ".gitattributes", "a.txt", "b.txt");
            Commit(directory, "First commit");
            RunGit(directory, "update-index", "--add", "--cacheinfo", $"160000,{RunGitOutput(sub, "rev-parse", "HEAD").Trim()},sub");
            File.WriteAllText(Path.Combine(directory, ".gitmodules"), "[submodule \"sub\"]\n\tpath = sub\n\turl = ./sub\n");
            RunGit(directory, "add", ".gitmodules");
            Commit(directory, "Add submodule");

            // Malicious configuration, added after the commits: filters in .git/config, .git/info/attributes,
            // and the submodule's own config, each appending to the marker file when Git runs it.
            RunGit(directory, "config", "filter.evil.clean", "echo clean >> filter-ran.marker; cat");
            RunGit(directory, "config", "filter.evil.required", "true");
            Directory.CreateDirectory(Path.Combine(directory, ".git", "info"));
            File.WriteAllText(Path.Combine(directory, ".git", "info", "attributes"), "b.txt filter=proc\n");
            RunGit(directory, "config", "filter.proc.process", "sh -c \"echo process >> filter-ran.marker; exit 1\"");
            RunGit(sub, "config", "filter.subevil.clean", "echo submodule >> ../filter-ran.marker; cat");
            // Newer timestamps make the files stat-dirty, so Git has to re-read (and filter) their contents.
            foreach (var file in new[] { "a.txt", "b.txt", Path.Combine("sub", "s.txt") })
                File.SetLastWriteTimeUtc(Path.Combine(directory, file), DateTime.UtcNow.AddMinutes(5));

            var output = new GitCollector(new GitProcessRunner()).Collect(new CollectorContext(new AppSettings(), [directory]), CancellationToken.None);

            Assert.False(File.Exists(marker), "A repository filter ran during capture: " + (File.Exists(marker) ? File.ReadAllText(marker) : ""));
            var repo = Assert.Single(output.Items);
            Assert.Null(repo.Error);
            Assert.Equal("main", repo.Branch);
            Assert.Equal("Add submodule", repo.RecentCommits[0].Subject);

            // Control: a plain `git status` does run the repository's filter, so the fixture is really malicious.
            RunGitUnchecked(directory, "status");
            Assert.True(File.Exists(marker));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    /// <summary>Integration test against a throwaway repository; skipped when Git is not installed.</summary>
    [GitFact]
    public void Collect_ReadsRealTemporaryRepository()
    {
        var runner = new GitProcessRunner();
        var directory = Directory.CreateTempSubdirectory("ContextSnapRealGit").FullName;
        try
        {
            RunGit(directory, "init", "--quiet", "--initial-branch=trunk");
            File.WriteAllText(Path.Combine(directory, "a.txt"), "a");
            RunGit(directory, "add", "a.txt");
            RunGit(directory, "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false",
                "commit", "--quiet", "-m", "First commit");
            File.WriteAllText(Path.Combine(directory, "a.txt"), "changed");
            File.WriteAllText(Path.Combine(directory, "new file.txt"), "n");

            var output = new GitCollector(runner).Collect(new CollectorContext(new AppSettings(), [directory]), CancellationToken.None);

            var repo = Assert.Single(output.Items);
            Assert.Null(repo.Error);
            Assert.Equal("trunk", repo.Branch);
            Assert.Equal(1, repo.Unstaged);
            Assert.Equal(1, repo.Untracked);
            Assert.Contains(repo.Changes, c => c.Path == "new file.txt");
            Assert.Equal("First commit", Assert.Single(repo.RecentCommits).Subject);
            Assert.False(File.Exists(Path.Combine(directory, ".git", "index.lock")));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static void DeleteDirectory(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(directory, recursive: true);
    }

    private static void Commit(string directory, string message) =>
        RunGit(directory, "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false",
            "commit", "--quiet", "-m", message);

    private static void RunGit(string directory, params string[] args) => Assert.Equal(0, StartGit(directory, args).ExitCode);

    private static string RunGitOutput(string directory, params string[] args)
    {
        var (exitCode, output) = StartGit(directory, args);
        Assert.Equal(0, exitCode);
        return output;
    }

    private static void RunGitUnchecked(string directory, params string[] args) => StartGit(directory, args);

    private static (int ExitCode, string Output) StartGit(string directory, string[] args)
    {
        var start = new ProcessStartInfo(ExecutableLocator.FindOnPath(OperatingSystem.IsWindows() ? "git.exe" : "git")!)
        {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, output.GetAwaiter().GetResult());
    }
}
