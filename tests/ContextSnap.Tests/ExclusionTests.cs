using ContextSnap.Capture;
using ContextSnap.Collectors;
using ContextSnap.Configuration;
using ContextSnap.Model;
using ContextSnap.Privacy;

namespace ContextSnap.Tests;

/// <summary>
/// Excluded programs, checked end to end: the real filtering collectors run over fake OS sources, so an
/// exclusion is proven to keep data out of the snapshot itself, not only out of the Markdown.
/// </summary>
public sealed class ExclusionTests : IDisposable
{
    private const string ExplorerPath = @"C:\Windows\explorer.exe";

    private readonly string _directory = Directory.CreateTempSubdirectory("ContextSnapExclusions").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class FakeWindowSource(params RawWindow[] windows) : IWindowSource
    {
        public IReadOnlyList<RawWindow> GetWindows() => windows;
    }

    private sealed class FakeProcessSource(params (int Id, string Name, string? Path)[] processes) : IProcessSource
    {
        public IReadOnlyList<RawProcess> GetProcesses() => processes.Select(p => new RawProcess(p.Id, p.Name)).ToArray();

        public string? GetExecutablePath(int processId) => processes.First(p => p.Id == processId).Path;
    }

    private sealed class FakeShellSource(params RawShellWindow[] windows) : IShellWindowSource
    {
        public int Reads { get; private set; }

        public IReadOnlyList<RawShellWindow> GetWindows(TimeSpan timeout)
        {
            Reads++;
            return windows;
        }
    }

    private static RawWindow Window(int pid, string process, string title, bool foreground = false) =>
        new(pid, title, pid, process, "AppWindow", IsCloaked: false, IsToolWindow: false, IsOwnedPopup: false, IsForeground: foreground);

    private sealed record Rig(SnapshotService Service, FakeShellSource Shell, StubCollector<GitRepositoryInfo> Git);

    /// <summary>A desktop with Discord, VS Code, and two File Explorer folders.</summary>
    private static Rig Desktop()
    {
        var windows = new FakeWindowSource(
            Window(10, "Discord", "Discord - #general", foreground: true),
            Window(20, "Code", "Program.cs - Shop - Visual Studio Code"),
            Window(30, "explorer", "Invoices"));
        var processes = new FakeProcessSource(
            (10, "Discord", @"C:\Users\dev\AppData\Local\Discord\app-1.0\Discord.exe"),
            (20, "Code", @"C:\Tools\VSCode\Code.exe"));
        var shell = new FakeShellSource(
            new RawShellWindow(ExplorerPath, @"D:\Work\Shop", "Shop", null),
            new RawShellWindow(ExplorerPath, @"D:\Finance\Invoices", "Invoices", null));
        var git = StubCollector<GitRepositoryInfo>.Returning("Git repositories");

        var collectors = new CollectorSet
        {
            Windows = new WindowCollector(windows, ownProcessId: 999),
            Processes = new ProcessCollector(processes),
            Explorer = new ExplorerCollector(shell),
            Git = git,
            Desktop = StubCollector<DesktopEntry>.Returning("Desktop items"),
            BrowserTabs = StubCollector<BrowserTab>.Returning("Browser tabs"),
            SystemInfo = StubCollector<SystemSummary>.Returning("System summary"),
            ScreenText = StubCollector<WindowText>.Returning("Visible screen text"),
        };
        return new Rig(new SnapshotService(collectors), shell, git);
    }

    private static AppSettings Settings(params string[] excluded) => new()
    {
        ExcludedProcesses = [.. excluded],
        DeveloperProcesses = ["Code", "Discord"],
        CaptureScreenText = false,
    };

    [Fact]
    public async Task ExcludedProgram_IsAbsentFromWindowsToolsAndMarkdown_OthersRemain()
    {
        var rig = Desktop();

        var result = await rig.Service.CaptureAsync(Settings("Discord.exe"));

        Assert.DoesNotContain(result.Snapshot.Windows.Items, w => w.ProcessName == "Discord");
        Assert.DoesNotContain(result.Snapshot.Processes.Items, p => p.Name == "Discord");
        Assert.Equal(1, result.Snapshot.Windows.ExcludedCount);
        Assert.Equal(1, result.Snapshot.Processes.ExcludedCount);
        Assert.DoesNotContain("Discord", result.Markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#general", result.Markdown);

        Assert.Contains(result.Snapshot.Windows.Items, w => w.ProcessName == "Code");
        Assert.Contains(result.Snapshot.Processes.Items, p => p.Name == "Code");
        Assert.Contains("Visual Studio Code", result.Markdown);
    }

    [Fact]
    public async Task ExcludedFileExplorer_IsNotRead_AndFeedsNoGitCandidates()
    {
        var rig = Desktop();

        var result = await rig.Service.CaptureAsync(Settings("explorer"));

        Assert.Equal(0, rig.Shell.Reads);
        Assert.Empty(result.Snapshot.ExplorerFolders.Items);
        Assert.DoesNotContain(result.Snapshot.Windows.Items, w => w.ProcessName == "explorer");
        Assert.DoesNotContain(rig.Git.LastContext!.ProjectCandidates, p => p.StartsWith(@"D:\", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(@"D:\Work\Shop", result.Markdown);
        Assert.DoesNotContain("Invoices", result.Markdown);
        Assert.Contains("Visual Studio Code", result.Markdown);
    }

    [Fact]
    public async Task ExcludedFileExplorer_ByFullPath_IsAlsoNotRead()
    {
        var rig = Desktop();

        var result = await rig.Service.CaptureAsync(Settings(ExplorerPath));

        Assert.Equal(0, rig.Shell.Reads);
        Assert.Empty(result.Snapshot.ExplorerFolders.Items);
        Assert.Empty(rig.Git.LastContext!.ProjectCandidates);
    }

    [Fact]
    public async Task IncludedFileExplorer_StillFeedsFoldersAndGitCandidates()
    {
        var rig = Desktop();

        var result = await rig.Service.CaptureAsync(Settings());

        Assert.Equal(1, rig.Shell.Reads);
        Assert.Equal([@"D:\Finance\Invoices", @"D:\Work\Shop"], result.Snapshot.ExplorerFolders.Items.Select(f => f.Path));
        Assert.Contains(@"D:\Work\Shop", rig.Git.LastContext!.ProjectCandidates);
    }

    [Fact]
    public void DefaultExclusions_RemainEffective()
    {
        var windows = new FakeWindowSource(Window(1, "KeePassXC", "Passwords.kdbx - KeePassXC"), Window(2, "Code", "Editor"));
        var processes = new FakeProcessSource((1, "KeePassXC", @"C:\kp\KeePassXC.exe"), (2, "Code", @"C:\Code.exe"));
        var settings = new AppSettings { DeveloperProcesses = ["KeePassXC", "Code"] };

        var (shown, excluded) = WindowFilter.Select(windows.GetWindows(), new WindowFilterOptions(settings.ExcludedProcesses, settings.ExcludedTitleKeywords, 999));
        var tools = new ProcessCollector(processes).Collect(CollectorContext.For(settings), CancellationToken.None);

        Assert.Equal(["Code"], shown.Select(w => w.ProcessName));
        Assert.Equal(1, excluded);
        Assert.Equal(["Code"], tools.Items.Select(p => p.Name));
    }

    [Fact]
    public async Task Exclusion_PersistsAcrossReload_AndRemovingItRestoresCollection()
    {
        var store = new SettingsStore(Path.Combine(_directory, "settings.json"));
        var settings = Settings("Discord");
        store.Save(settings);

        var reloaded = store.Load().Settings;
        Assert.Contains("Discord", reloaded.ExcludedProcesses);
        var excludedRun = await Desktop().Service.CaptureAsync(reloaded);
        Assert.DoesNotContain("Discord", excludedRun.Markdown, StringComparison.OrdinalIgnoreCase);

        reloaded.ExcludedProcesses.Remove("Discord");
        store.Save(reloaded);
        var restored = store.Load().Settings;
        Assert.DoesNotContain("Discord", restored.ExcludedProcesses);

        var includedRun = await Desktop().Service.CaptureAsync(restored);
        Assert.Contains(includedRun.Snapshot.Windows.Items, w => w.ProcessName == "Discord");
        Assert.Contains(includedRun.Snapshot.Processes.Items, p => p.Name == "Discord");
    }

    [Fact]
    public void ExistingNameEntries_LoadUnchanged()
    {
        var path = Path.Combine(_directory, "old.json");
        File.WriteAllText(path, """{ "excludedProcesses": ["KeePassXC", "Slack.exe", "  Discord  "] }""");

        var settings = new SettingsStore(path).Load().Settings;

        Assert.Equal(["KeePassXC", "Slack.exe", "Discord"], settings.ExcludedProcesses);
        Assert.True(PrivacyRules.IsProcessExcluded("slack", settings.ExcludedProcesses));
    }

    [Fact]
    public void PathEntry_MatchesOnlyThatExecutable_WhenThePathIsKnown()
    {
        var processes = new FakeProcessSource((1, "node", @"C:\work\node.exe"), (2, "node", @"C:\private\node.exe"));
        var settings = new AppSettings { DeveloperProcesses = ["node"], ExcludedProcesses = [@"c:/PRIVATE/node.exe"], IncludeExecutablePaths = false };

        var output = new ProcessCollector(processes).Collect(CollectorContext.For(settings), CancellationToken.None);

        var node = Assert.Single(output.Items);
        Assert.Equal([1], node.ProcessIds);
        Assert.Null(node.ExecutablePath); // read only to apply the exclusion, never reported
        Assert.Equal(1, output.ExcludedCount);
    }

    [Theory]
    [InlineData(null, true)] // path unknown (windows, screen text): matches by name, never excludes less
    [InlineData(@"C:\private\node.exe", true)]
    [InlineData(@"C:\work\node.exe", false)]
    public void PathEntry_FallsBackToNameWhenThePathIsUnknown(string? processPath, bool expected)
    {
        Assert.Equal(expected, PrivacyRules.IsProcessExcluded("node", processPath, [@"C:\private\node.exe"]));
    }

    [Theory]
    [InlineData(@"C:\Apps\Discord\Discord.exe", "Discord")]
    [InlineData("/usr/bin/code", "code")]
    [InlineData("Slack.EXE", "Slack")]
    public void ProgramNameOf_StripsFoldersAndExtension(string entry, string expected)
    {
        Assert.Equal(expected, PrivacyRules.ProgramNameOf(entry));
    }

    [Fact]
    public void ExclusionEntryForExecutable_AcceptsAnExistingExe()
    {
        var exe = Path.Combine(_directory, "My Tool.exe");
        File.WriteAllBytes(exe, [0x4D, 0x5A]);

        Assert.Equal("My Tool", PrivacyRules.ExclusionEntryForExecutable(exe, out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData(@"C:\definitely\missing\ghost.exe")]
    [InlineData("not a path\0.exe")]
    public void ExclusionEntryForExecutable_RejectsMissingOrInvalidPaths(string? path)
    {
        Assert.Null(PrivacyRules.ExclusionEntryForExecutable(path, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void ExclusionEntryForExecutable_RejectsNonExecutables()
    {
        var text = Path.Combine(_directory, "notes.txt");
        File.WriteAllText(text, "x");

        Assert.Null(PrivacyRules.ExclusionEntryForExecutable(text, out var error));
        Assert.Contains(".exe", error);
    }

    [Fact]
    public void ExplorerFilter_DropsEveryExplorerWindowWhenExplorerIsExcluded()
    {
        var selection = ExplorerFilter.Select(
            [new RawShellWindow(ExplorerPath, @"D:\A", "A", null), new RawShellWindow(ExplorerPath, @"D:\B", "B", null)],
            excludedPathPrefixes: [], excludedProcesses: ["Explorer.exe"]);

        Assert.Empty(selection.Folders);
        Assert.Equal(2, selection.ExcludedCount);
    }
}
