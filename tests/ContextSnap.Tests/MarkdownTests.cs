using ContextSnap.Formatting;
using ContextSnap.Model;
using ContextSnap.Privacy;

namespace ContextSnap.Tests;

public class MarkdownTextTests
{
    [Theory]
    [InlineData("a*b_c", @"a\*b\_c")]
    [InlineData("x | y", @"x \| y")]
    [InlineData("<script>", @"\<script\>")]
    [InlineData("[link](url)", @"\[link\](url)")]
    [InlineData(@"back\slash", @"back\\slash")]
    [InlineData("key [REDACTED] and [USER]", "key [REDACTED] and [USER]")]
    public void Escape_EscapesMarkdownButKeepsPlaceholders(string input, string expected)
    {
        Assert.Equal(expected, MarkdownText.Escape(input));
    }

    [Theory]
    [InlineData("plain", "`plain`")]
    [InlineData("a`b", "`` a`b ``")]
    [InlineData("a``b", "``` a``b ```")]
    [InlineData("", "` `")]
    public void Code_ChoosesSafeFence(string input, string expected)
    {
        Assert.Equal(expected, MarkdownText.Code(input));
    }

    [Fact]
    public void SingleLine_CollapsesWhitespaceAndTruncates()
    {
        Assert.Equal("a b c", MarkdownText.SingleLine("  a\r\n b\t\tc  ", 50));
        Assert.Equal("abcd…", MarkdownText.SingleLine("abcdefghij", 5));
    }

    [Fact]
    public void SingleLine_DoesNotSplitSurrogatePairs()
    {
        var result = MarkdownText.SingleLine("ab😀😀😀", 4);

        Assert.False(result.Any(char.IsHighSurrogate) && !result.Any(char.IsLowSurrogate));
        Assert.EndsWith("…", result);
    }
}

public class OutputLimitTests
{
    [Fact]
    public void LimitOutput_TruncatesLongMarkdown()
    {
        var result = MarkdownFormatter.LimitOutput(new string('x', 100), 40);

        Assert.True(result.Length <= 40);
        Assert.Contains("truncated", result);
    }

    [Fact]
    public void LimitOutput_LeavesShortMarkdownUnchanged()
    {
        Assert.Equal("# Hello", MarkdownFormatter.LimitOutput("# Hello"));
    }

    [Fact]
    public void LimitOutput_CutsAtLineBoundary()
    {
        var lines = string.Join('\n', Enumerable.Range(0, 200).Select(i => $"- line number {i}"));

        var result = MarkdownFormatter.LimitOutput(lines, 500);

        Assert.True(result.Length <= 500);
        Assert.EndsWith(MarkdownFormatter.TruncationNotice, result);
        var body = result[..^MarkdownFormatter.TruncationNotice.Length];
        Assert.Matches(@"- line number \d+$", body);
    }

    [Fact]
    public void LimitOutput_RejectsInvalidLimit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MarkdownFormatter.LimitOutput("x", 0));
    }
}

public class MarkdownFormatterTests
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 10, 14, 9, 30, 0, TimeSpan.FromHours(4));

    internal static ContextSnapshot EmptySnapshot() => new(
        CapturedAt,
        CollectorResult<WindowInfo>.Disabled("Open windows"),
        CollectorResult<ProcessGroup>.Disabled("Developer tools"),
        CollectorResult<ExplorerFolder>.Disabled("File Explorer folders"),
        CollectorResult<GitRepositoryInfo>.Disabled("Git repositories"),
        CollectorResult<DesktopEntry>.Disabled("Desktop items"),
        CollectorResult<BrowserTab>.Disabled("Browser tabs"),
        CollectorResult<SystemSummary>.Disabled("System summary"),
        CollectorResult<WindowText>.Disabled("Visible screen text"));

    private static CollectorResult<T> Ok<T>(string name, params T[] items) => new(name, CollectorStatus.Ok, items);

    private static string Format(ContextSnapshot snapshot, Redactor? redactor = null) =>
        new MarkdownFormatter(redactor ?? Redactor.Default).Format(snapshot);

    [Fact]
    public void Format_WritesHeaderAndForegroundWindow()
    {
        var snapshot = EmptySnapshot() with
        {
            Windows = Ok("Open windows",
                new WindowInfo("Code", "Program.cs - App", 10, IsForeground: true),
                new WindowInfo("chrome", "Docs | Site", 11, IsForeground: false)),
        };

        var md = Format(snapshot);

        Assert.StartsWith("# Context Snapshot", md);
        Assert.Contains("2026-10-14 09:30 (UTC+04:00)", md);
        Assert.Contains("**Foreground window:** Code — Program.cs - App", md);
        Assert.Contains("## Open windows (2, most recently used first)", md);
        Assert.Contains("- **Code**: Program.cs - App _(foreground)_", md);
        Assert.Contains(@"- **chrome**: Docs \| Site", md);
    }

    [Fact]
    public void Format_RedactsEveryField()
    {
        var snapshot = EmptySnapshot() with
        {
            Windows = Ok("Open windows", new WindowInfo("pwsh", "export API_KEY=supersecret", 1, false)),
            ExplorerFolders = Ok("File Explorer folders", new ExplorerFolder(@"C:\keys\password=hunter2")),
            Desktop = Ok("Desktop items", new DesktopEntry("mail me@example.com.txt", false, 10, DateTime.Now)),
        };

        var md = Format(snapshot);

        Assert.DoesNotContain("supersecret", md);
        Assert.DoesNotContain("hunter2", md);
        Assert.DoesNotContain("me@example.com", md);
        Assert.Contains("API\\_KEY=[REDACTED]", md);
    }

    [Fact]
    public void Format_DistinguishesEmptyFailedUnavailableAndDisabled()
    {
        var snapshot = EmptySnapshot() with
        {
            ExplorerFolders = new CollectorResult<ExplorerFolder>("File Explorer folders", CollectorStatus.Empty, []),
            Processes = CollectorResult<ProcessGroup>.FromFailure("Developer tools", CollectorStatus.Failed, "Access denied"),
            Git = CollectorResult<GitRepositoryInfo>.FromFailure("Git repositories", CollectorStatus.Unavailable, "Git was not found on PATH."),
        };

        var md = Format(snapshot);

        Assert.Contains("_No open File Explorer folders found._", md);
        Assert.Contains("_Collection failed: Access denied_", md);
        Assert.Contains("_Unavailable: Git was not found on PATH._", md);
        Assert.Contains("_Not collected (turned off): Visible screen text, Open windows, Browser tabs, Desktop items, System summary._", md);
        Assert.DoesNotContain("## Open windows", md);
    }

    [Fact]
    public void Format_ReportsOmittedItemsAndNotes()
    {
        var snapshot = EmptySnapshot() with
        {
            ExplorerFolders = new CollectorResult<ExplorerFolder>("File Explorer folders", CollectorStatus.Ok,
                [new ExplorerFolder(@"D:\src")], Notes: ["1 virtual location omitted."], OmittedCount: 4),
        };

        var md = Format(snapshot);

        Assert.Contains("## File Explorer folders (5)", md);
        Assert.Contains(@"- `D:\src`", md);
        Assert.Contains("- _…and 4 more (item limit reached)._", md);
        Assert.Contains("_1 virtual location omitted._", md);
    }

    [Fact]
    public void Format_WritesGitRepository()
    {
        var repo = new GitRepositoryInfo(@"D:\src\app")
        {
            Branch = "main",
            Upstream = "origin/main",
            Ahead = 2,
            Staged = 1,
            Unstaged = 1,
            Untracked = 1,
            TotalChanges = 4,
            Changes = [new GitChange("M ", "src/a.cs"), new GitChange("R ", "b.cs", "old_b.cs"), new GitChange("??", "new.txt")],
            RecentCommits = [new GitCommit("abc1234", "Fix *bug*", "2 hours ago")],
        };
        var snapshot = EmptySnapshot() with { Git = Ok("Git repositories", repo) };

        var md = Format(snapshot);

        Assert.Contains(@"### app — `D:\src\app`", md);
        Assert.Contains("- Branch: `main` → `origin/main` (ahead 2)", md);
        Assert.Contains("- Working tree: 1 staged, 1 unstaged, 1 untracked", md);
        Assert.Contains("  - `M ` `src/a.cs`", md);
        Assert.Contains("  - `R ` `b.cs` ← `old_b.cs`", md);
        Assert.Contains("  - _…and 1 more._", md);
        Assert.Contains(@"  - `abc1234` Fix \*bug\* (2 hours ago)", md);
    }

    [Fact]
    public void Format_WritesGitEdgeCases()
    {
        var detached = new GitRepositoryInfo(@"D:\a") { IsDetached = true, HeadShortHash = "1234567" };
        var empty = new GitRepositoryInfo(@"D:\b") { Branch = "main", HasNoCommits = true };
        var broken = new GitRepositoryInfo(@"D:\c") { Error = "git status timed out" };
        var snapshot = EmptySnapshot() with { Git = Ok("Git repositories", detached, empty, broken) };

        var md = Format(snapshot);

        Assert.Contains("- HEAD: detached at `1234567`", md);
        Assert.Contains("- Working tree: clean", md);
        Assert.Contains("- Branch: `main` (no commits yet)", md);
        Assert.Contains("- _Could not read repository: git status timed out_", md);
    }

    [Fact]
    public void Format_WritesProcessesDesktopBrowserAndSystem()
    {
        var snapshot = EmptySnapshot() with
        {
            Processes = Ok("Developer tools",
                new ProcessGroup("Code", @"C:\Code\Code.exe", [1, 2, 3, 4]),
                new ProcessGroup("pwsh", null, [42])),
            Desktop = Ok("Desktop items",
                new DesktopEntry("Projects", true, null, DateTime.Now),
                new DesktopEntry("notes.txt", false, 2048, DateTime.Now)),
            BrowserTabs = Ok("Browser tabs", new BrowserTab("Chrome", "Docs", "https://x.test/a?token=1", true)),
            SystemInfo = Ok("System summary", new SystemSummary("Windows 11 (build 26200)", "X64", 8, 16L * 1024 * 1024 * 1024)),
        };

        var md = Format(snapshot);

        Assert.Contains(@"- **Code** ×4 — `C:\Code\Code.exe`", md);
        Assert.Contains("- **pwsh** (PID 42)", md);
        Assert.Contains("- `Projects/`", md);
        Assert.Contains("- `notes.txt` (2 KB)", md);
        Assert.Contains("- **Chrome**: Docs _(active)_ — `https://x.test/a`", md);
        Assert.Contains("- Windows 11 (build 26200) · X64 · 8 logical processors · 16 GB memory", md);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(157L * 1024 * 1024, "157 MB")]
    public void FormatSize_UsesBinaryUnits(long bytes, string expected)
    {
        Assert.Equal(expected, MarkdownFormatter.FormatSize(bytes));
    }
}
