using ContextSnap.Capture;
using ContextSnap.Collectors;
using ContextSnap.Configuration;
using ContextSnap.Model;
using ContextSnap.Output;

namespace ContextSnap.Tests;

internal sealed class StubCollector<T>(string name, Func<CollectorContext, CollectorOutput<T>> collect) : ICollector<T>
{
    public string Name => name;
    public CollectorContext? LastContext { get; private set; }

    public CollectorOutput<T> Collect(CollectorContext context, CancellationToken cancellationToken)
    {
        LastContext = context;
        return collect(context);
    }

    public static StubCollector<T> Returning(string name, params T[] items) => new(name, _ => new CollectorOutput<T>(items));
}

public class SnapshotServiceTests
{
    private static CollectorSet Collectors(
        ICollector<WindowInfo>? windows = null,
        ICollector<ExplorerFolder>? explorer = null,
        ICollector<GitRepositoryInfo>? git = null,
        ICollector<ProcessGroup>? processes = null) => new()
    {
        Windows = windows ?? StubCollector<WindowInfo>.Returning("Open windows", new WindowInfo("Code", "App", 1, true)),
        Processes = processes ?? StubCollector<ProcessGroup>.Returning("Developer tools"),
        Explorer = explorer ?? StubCollector<ExplorerFolder>.Returning("File Explorer folders"),
        Git = git ?? StubCollector<GitRepositoryInfo>.Returning("Git repositories"),
        Desktop = StubCollector<DesktopEntry>.Returning("Desktop items"),
        BrowserTabs = new StubCollector<BrowserTab>("Browser tabs", _ => throw new InvalidOperationException("must stay disabled")),
        SystemInfo = StubCollector<SystemSummary>.Returning("System summary", new SystemSummary("Windows 11", "X64", 4, null)),
        ScreenText = StubCollector<WindowText>.Returning("Visible screen text"),
    };

    [Fact]
    public async Task Capture_IsolatesFailingCollectors()
    {
        var service = new SnapshotService(Collectors(
            processes: new StubCollector<ProcessGroup>("Developer tools", _ => throw new UnauthorizedAccessException("Access is denied.")),
            git: new StubCollector<GitRepositoryInfo>("Git repositories", _ => throw new CollectorUnavailableException("Git was not found on PATH."))));

        var result = await service.CaptureAsync(new AppSettings());

        Assert.Equal(CollectorStatus.Ok, result.Snapshot.Windows.Status);
        Assert.Equal(CollectorStatus.Failed, result.Snapshot.Processes.Status);
        Assert.Equal("UnauthorizedAccessException: Access is denied.", result.Snapshot.Processes.Error);
        Assert.Equal(CollectorStatus.Unavailable, result.Snapshot.Git.Status);
        Assert.Equal(CollectorStatus.Empty, result.Snapshot.Desktop.Status);
        Assert.Equal(CollectorStatus.Disabled, result.Snapshot.BrowserTabs.Status);
        Assert.Contains("_Collection failed: UnauthorizedAccessException: Access is denied._", result.Markdown);
    }

    [Fact]
    public async Task Capture_TimesOutSlowCollector()
    {
        var slow = new StubCollector<ExplorerFolder>("File Explorer folders", _ =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(5));
            return new CollectorOutput<ExplorerFolder>([]);
        });
        var service = new SnapshotService(Collectors(explorer: slow));

        var started = DateTime.UtcNow;
        var result = await service.CaptureAsync(new AppSettings { CollectorTimeoutSeconds = 2 });

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(4.5));
        Assert.Equal(CollectorStatus.Failed, result.Snapshot.ExplorerFolders.Status);
        Assert.Contains("Timed out", result.Snapshot.ExplorerFolders.Error);
    }

    [Fact]
    public async Task Capture_AppliesItemLimitAndOutputLimit()
    {
        var many = Enumerable.Range(0, 50).Select(i => new WindowInfo("app", $"Window {i} " + new string('x', 80), i, false)).ToArray();
        var service = new SnapshotService(Collectors(windows: StubCollector<WindowInfo>.Returning("Open windows", many)));

        var result = await service.CaptureAsync(new AppSettings { MaxItemsPerSection = 30, MaxOutputCharacters = 1_000 });

        Assert.Equal(30, result.Snapshot.Windows.Count);
        Assert.Equal(20, result.Snapshot.Windows.OmittedCount);
        Assert.True(result.Markdown.Length <= 1_000);
        Assert.True(result.WasTruncated);
        Assert.Contains("truncated", result.Markdown);
    }

    [Fact]
    public async Task Capture_PassesProjectCandidatesToGitInPriorityOrder()
    {
        var git = StubCollector<GitRepositoryInfo>.Returning("Git repositories");
        var explorer = StubCollector<ExplorerFolder>.Returning("File Explorer folders",
            new ExplorerFolder(@"D:\explorer"), new ExplorerFolder(@"D:\private\x"));
        var service = new SnapshotService(Collectors(explorer: explorer, git: git));
        var settings = new AppSettings { ProjectFolders = [@"D:\configured"], ExcludedPathPrefixes = [@"D:\private"] };

        await service.CaptureAsync(settings, new CaptureOptions([@"D:\cwd"]));

        Assert.Equal([@"D:\configured", @"D:\cwd", @"D:\explorer"], git.LastContext!.ProjectCandidates);
    }

    [Fact]
    public async Task Capture_DoesNotRunDisabledCollectors()
    {
        var windows = new StubCollector<WindowInfo>("Open windows", _ => throw new InvalidOperationException("should not run"));
        var service = new SnapshotService(Collectors(windows: windows));

        var result = await service.CaptureAsync(new AppSettings { CollectWindows = false });

        Assert.Equal(CollectorStatus.Disabled, result.Snapshot.Windows.Status);
        Assert.Null(windows.LastContext);
        Assert.Contains("Not collected (turned off): Open windows", result.Markdown);
    }

    [Fact]
    public async Task Capture_PropagatesCallerCancellation()
    {
        var service = new SnapshotService(Collectors());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CaptureAsync(new AppSettings(), null, cancelled.Token));
    }

    [Fact]
    public async Task Capture_DoesNotMutateCallerSettings()
    {
        var settings = new AppSettings { MaxOutputCharacters = 5 };

        await new SnapshotService(Collectors()).CaptureAsync(settings);

        Assert.Equal(5, settings.MaxOutputCharacters);
    }
}

public sealed class HeadlessRunnerTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ContextSnapCli").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class FakeClipboard(bool succeed = true) : IClipboardWriter
    {
        public string? Text { get; private set; }
        public bool? ExcludedFromHistory { get; private set; }

        public ClipboardResult SetText(string text, bool excludeFromHistory)
        {
            Text = text;
            ExcludedFromHistory = excludeFromHistory;
            return succeed ? ClipboardResult.Ok : new ClipboardResult(false, "busy");
        }
    }

    private HeadlessRunner Runner(IClipboardWriter clipboard)
    {
        var collectors = new CollectorSet
        {
            Windows = StubCollector<WindowInfo>.Returning("Open windows", new WindowInfo("Code", "App", 1, false)),
            Processes = StubCollector<ProcessGroup>.Returning("Developer tools"),
            Explorer = StubCollector<ExplorerFolder>.Returning("File Explorer folders"),
            Git = StubCollector<GitRepositoryInfo>.Returning("Git repositories"),
            Desktop = StubCollector<DesktopEntry>.Returning("Desktop items"),
            BrowserTabs = StubCollector<BrowserTab>.Returning("Browser tabs"),
            SystemInfo = StubCollector<SystemSummary>.Returning("System summary"),
            ScreenText = StubCollector<WindowText>.Returning("Visible screen text"),
        };
        return new HeadlessRunner(new SnapshotService(collectors), new SettingsStore(Path.Combine(_directory, "settings.json")), clipboard);
    }

    [Theory]
    [InlineData(new string[0], RunMode.Gui, false)]
    [InlineData(new[] { "--capture" }, RunMode.Capture, false)]
    [InlineData(new[] { "-P" }, RunMode.Print, false)]
    [InlineData(new[] { "/?" }, RunMode.Help, false)]
    [InlineData(new[] { "--bogus" }, RunMode.Help, true)]
    [InlineData(new[] { "--capture", "--print" }, RunMode.Help, true)]
    public void Parse_RecognizesOptions(string[] args, RunMode mode, bool hasError)
    {
        var options = CommandLineOptions.Parse(args);

        Assert.Equal(mode, options.Mode);
        Assert.Equal(hasError, options.Error is not null);
    }

    [Fact]
    public async Task Capture_CopiesMarkdownAndPrintsSummary()
    {
        var clipboard = new FakeClipboard();
        var output = new StringWriter();

        var exit = await Runner(clipboard).RunAsync(new CommandLineOptions(RunMode.Capture), output, new StringWriter(), _directory);

        Assert.Equal(0, exit);
        Assert.StartsWith("# Context Snapshot", clipboard.Text);
        Assert.True(clipboard.ExcludedFromHistory);
        Assert.Contains("Snapshot copied to the clipboard", output.ToString());
        Assert.Contains("Open windows: 1", output.ToString());
        Assert.Contains("Browser tabs: off", output.ToString());
    }

    [Fact]
    public async Task Capture_ReturnsFailureWhenClipboardUnavailable()
    {
        var error = new StringWriter();

        var exit = await Runner(new FakeClipboard(succeed: false)).RunAsync(new CommandLineOptions(RunMode.Capture), new StringWriter(), error, _directory);

        Assert.Equal(1, exit);
        Assert.Contains("Clipboard copy failed: busy", error.ToString());
    }

    [Fact]
    public async Task Print_WritesMarkdownWithoutTouchingClipboard()
    {
        var clipboard = new FakeClipboard();
        var output = new StringWriter();

        var exit = await Runner(clipboard).RunAsync(new CommandLineOptions(RunMode.Print), output, new StringWriter(), _directory);

        Assert.Equal(0, exit);
        Assert.Null(clipboard.Text);
        Assert.Contains("- **Code**: App", output.ToString());
    }

    [Fact]
    public async Task Help_WithErrorReturnsUsageExitCode()
    {
        var exit = await Runner(new FakeClipboard()).RunAsync(new CommandLineOptions(RunMode.Help, "Unknown option."), new StringWriter(), new StringWriter(), _directory);

        Assert.Equal(2, exit);
    }
}
