using ContextSnap.Collectors;
using ContextSnap.Collectors.ScreenText;
using ContextSnap.Configuration;
using ContextSnap.Model;
using ContextSnap.Privacy;

namespace ContextSnap.Tests;

public class WindowFilterTests
{
    private static RawWindow Window(string title, string? process = "app", int pid = 100, string className = "AppWindow",
        bool cloaked = false, bool tool = false, bool owned = false, bool foreground = false) =>
        new(1, title, pid, process, className, cloaked, tool, owned, foreground);

    private static readonly WindowFilterOptions Options = new(["KeePassXC"], ["Incognito"], OwnProcessId: 999);

    [Fact]
    public void Select_KeepsOrdinaryWindowsInOrder()
    {
        var (windows, excluded) = WindowFilter.Select([Window("B", foreground: true), Window("A")], Options);

        Assert.Equal(["B", "A"], windows.Select(w => w.Title));
        Assert.True(windows[0].IsForeground);
        Assert.Equal(0, excluded);
    }

    [Fact]
    public void Select_DropsShellCloakedToolOwnedEmptyAndOwnWindows()
    {
        var (windows, _) = WindowFilter.Select(
        [
            Window("Program Manager", className: "Progman"),
            Window("Hidden UWP", cloaked: true),
            Window("Palette", tool: true),
            Window("Find dialog", owned: true),
            Window("   "),
            Window("Snapctx", pid: 999),
            Window("Keep me"),
        ], Options);

        Assert.Equal(["Keep me"], windows.Select(w => w.Title));
    }

    [Fact]
    public void Select_AppliesPrivacyExclusionsAndCountsThem()
    {
        var (windows, excluded) = WindowFilter.Select(
        [
            Window("Vault", process: "KeePassXC"),
            Window("New Incognito tab - Chrome", process: "chrome"),
            Window("Docs", process: "chrome"),
        ], Options);

        Assert.Equal(["Docs"], windows.Select(w => w.Title));
        Assert.Equal(2, excluded);
    }

    [Fact]
    public void Select_RemovesDuplicatesAndNamesUnknownProcesses()
    {
        var (windows, _) = WindowFilter.Select([Window("Same"), Window("Same"), Window("Orphan", process: null)], Options);

        Assert.Equal(2, windows.Count);
        Assert.Equal("unknown", windows[1].ProcessName);
    }
}

public class ProcessCollectorTests
{
    private sealed class FakeProcessSource(params (int Id, string Name, string? Path)[] processes) : IProcessSource
    {
        public int PathLookups { get; private set; }

        public IReadOnlyList<RawProcess> GetProcesses() => processes.Select(p => new RawProcess(p.Id, p.Name)).ToArray();

        public string? GetExecutablePath(int processId)
        {
            PathLookups++;
            return processes.First(p => p.Id == processId).Path;
        }
    }

    [Fact]
    public void Collect_GroupsRelevantProcessesAndIgnoresOthers()
    {
        var source = new FakeProcessSource(
            (3, "Code", @"C:\VS\Code.exe"), (1, "Code", @"C:\VS\Code.exe"), (2, "Code", null),
            (5, "node", @"C:\a\node.exe"), (6, "node", @"C:\b\node.exe"),
            (7, "svchost", @"C:\Windows\svchost.exe"));
        var settings = new AppSettings { DeveloperProcesses = ["Code", "node.exe"] };

        var output = new ProcessCollector(source).Collect(CollectorContext.For(settings), CancellationToken.None);

        Assert.Equal(3, output.Items.Count);
        var code = output.Items[0];
        Assert.Equal("Code", code.Name);
        Assert.Equal([1, 2, 3], code.ProcessIds);
        Assert.Equal(@"C:\VS\Code.exe", code.ExecutablePath);
        Assert.Equal(2, output.Items.Count(g => g.Name == "node"));
    }

    [Fact]
    public void Collect_SkipsPathsWhenDisabledAndHonorsExclusions()
    {
        var source = new FakeProcessSource((1, "git", @"C:\git.exe"), (2, "KeePassXC", @"C:\kp.exe"));
        var settings = new AppSettings
        {
            DeveloperProcesses = ["git", "KeePassXC"],
            IncludeExecutablePaths = false,
        };

        var output = new ProcessCollector(source).Collect(CollectorContext.For(settings), CancellationToken.None);

        var git = Assert.Single(output.Items);
        Assert.Null(git.ExecutablePath);
        Assert.Equal(1, output.ExcludedCount);
        Assert.Equal(0, source.PathLookups);
    }
}

public class ExplorerFilterTests
{
    private const string Explorer = @"C:\Windows\explorer.exe";

    [Fact]
    public void Select_ReturnsSortedDistinctFileSystemFolders()
    {
        var selection = ExplorerFilter.Select(
        [
            new RawShellWindow(Explorer, @"D:\Work\Zeta", "Zeta", "file:///D:/Work/Zeta"),
            new RawShellWindow(Explorer, @"D:\Work\Alpha", "Alpha", null),
            new RawShellWindow(Explorer, @"d:\work\alpha\", "alpha", null),
            new RawShellWindow(@"C:\Program Files\Internet Explorer\iexplore.exe", null, "Site", "https://example.com"),
        ], []);

        Assert.Equal([@"D:\Work\Alpha", @"D:\Work\Zeta"], selection.Folders.Select(f => f.Path));
        Assert.Equal(0, selection.VirtualLocations);
    }

    [Fact]
    public void Select_CountsVirtualLocationsAndExclusions()
    {
        var selection = ExplorerFilter.Select(
        [
            new RawShellWindow(Explorer, "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", "This PC", ""),
            new RawShellWindow(Explorer, null, "Home", null),
            new RawShellWindow(Explorer, @"D:\Private\taxes", "taxes", null),
            new RawShellWindow(Explorer, @"D:\Public", "Public", null),
        ], [@"D:\Private"]);

        Assert.Equal([@"D:\Public"], selection.Folders.Select(f => f.Path));
        Assert.Equal(2, selection.VirtualLocations);
        Assert.Equal(1, selection.ExcludedCount);
    }

    [Fact]
    public void ResolvePath_FallsBackToFileUrl()
    {
        var path = ExplorerFilter.ResolvePath(new RawShellWindow(Explorer, null, "x", "file:///C:/Temp/My%20Folder"));

        Assert.Equal(@"C:\Temp\My Folder", path);
    }

    [Fact]
    public void Collect_PropagatesSourceFailureForOrchestratorToReport()
    {
        var collector = new ExplorerCollector(new ThrowingShellSource());

        Assert.Throws<TimeoutException>(() => collector.Collect(CollectorContext.For(new AppSettings()), CancellationToken.None));
    }

    private sealed class ThrowingShellSource : IShellWindowSource
    {
        public IReadOnlyList<RawShellWindow> GetWindows(TimeSpan timeout) => throw new TimeoutException("slow shell");
    }
}

public sealed class DesktopCollectorTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ContextSnapDesktop").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Collect_ListsVisibleEntriesNewestFirstWithoutReadingContents()
    {
        var old = Path.Combine(_directory, "old.txt");
        File.WriteAllText(old, "12345");
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-2));
        Directory.CreateDirectory(Path.Combine(_directory, "Projects"));
        var hidden = Path.Combine(_directory, "hidden.txt");
        File.WriteAllText(hidden, "x");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var output = new DesktopCollector(() => _directory).Collect(CollectorContext.For(new AppSettings()), CancellationToken.None);

        Assert.Equal(["Projects", "old.txt"], output.Items.Select(e => e.Name));
        Assert.True(output.Items[0].IsDirectory);
        Assert.Equal(5, output.Items[1].SizeBytes);
    }

    [Fact]
    public void Collect_HonorsExcludedPaths()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "Secret"));
        File.WriteAllText(Path.Combine(_directory, "a.txt"), "a");
        var settings = new AppSettings { ExcludedPathPrefixes = [Path.Combine(_directory, "Secret")] };

        var output = new DesktopCollector(() => _directory).Collect(CollectorContext.For(settings), CancellationToken.None);

        Assert.Equal(["a.txt"], output.Items.Select(e => e.Name));
        Assert.Equal(1, output.ExcludedCount);
    }

    [Fact]
    public void Collect_ReportsMissingFolderAsUnavailable()
    {
        var collector = new DesktopCollector(() => Path.Combine(_directory, "missing"));

        Assert.Throws<CollectorUnavailableException>(() => collector.Collect(CollectorContext.For(new AppSettings()), CancellationToken.None));
    }
}

public class BrowserTabCollectorTests
{
    /// <summary>Like the UI Automation source, returns excluded browsers' windows without reading them.</summary>
    private sealed class FakeBrowserSource(params RawBrowserWindow[] windows) : IBrowserTabSource
    {
        public bool? RequestedUrls { get; private set; }
        public List<string> ReadProcesses { get; } = [];

        public IReadOnlyList<RawBrowserWindow> GetWindows(bool includeActiveUrl, Func<string, bool> isExcluded, CancellationToken cancellationToken)
        {
            RequestedUrls = includeActiveUrl;
            return windows.Select(w =>
            {
                if (isExcluded(w.ProcessName))
                    return w with { WindowTitle = string.Empty, Tabs = [], ActiveUrl = null };
                ReadProcesses.Add(w.ProcessName);
                return w;
            }).ToArray();
        }
    }

    [Fact]
    public void Collect_AssignsUrlOnlyToActiveTabWhenEnabled()
    {
        var source = new FakeBrowserSource(new RawBrowserWindow("Chrome", "chrome", "Docs - Google Chrome",
            [new RawBrowserTab("Docs", true), new RawBrowserTab("Mail", false)], "https://docs.test/page"));
        var settings = new AppSettings { CollectBrowserTabs = true, IncludeBrowserUrls = true };

        var output = new BrowserTabCollector(source).Collect(CollectorContext.For(settings), CancellationToken.None);

        Assert.True(source.RequestedUrls);
        Assert.Equal("https://docs.test/page", output.Items[0].Url);
        Assert.Null(output.Items[1].Url);
    }

    [Fact]
    public void Collect_SkipsPrivateWindowsAndExcludedTabs()
    {
        var source = new FakeBrowserSource(
            new RawBrowserWindow("Edge", "msedge", "New tab - [InPrivate] - Microsoft Edge", [new RawBrowserTab("Bank", true)], null),
            new RawBrowserWindow("Chrome", "chrome", "Docs", [new RawBrowserTab("Change password", false), new RawBrowserTab("Docs", true)], null));
        var settings = new AppSettings { CollectBrowserTabs = true };

        var output = new BrowserTabCollector(source).Collect(CollectorContext.For(settings), CancellationToken.None);

        Assert.Equal(["Docs"], output.Items.Select(t => t.Title));
        Assert.Equal(2, output.ExcludedCount);
        Assert.Null(output.Items[0].Url);
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("chrome.exe")]
    [InlineData("CHROME")]
    [InlineData("Chrome")]
    public void Collect_NeverReadsBrowsersInExcludedPrograms(string excludedEntry)
    {
        var source = new FakeBrowserSource(
            new RawBrowserWindow("Chrome", "chrome", "Bank - Google Chrome", [new RawBrowserTab("Bank", true), new RawBrowserTab("Mail", false)], "https://bank.test/"),
            new RawBrowserWindow("Edge", "msedge", "Docs - Microsoft Edge", [new RawBrowserTab("Docs", true)], "https://docs.test/"));
        var settings = new AppSettings { CollectBrowserTabs = true, IncludeBrowserUrls = true, ExcludedProcesses = [excludedEntry] };

        var output = new BrowserTabCollector(source).Collect(CollectorContext.For(settings), CancellationToken.None);

        Assert.Equal(["msedge"], source.ReadProcesses);
        Assert.Equal(["Docs"], output.Items.Select(t => t.Title));
        Assert.All(output.Items, t => Assert.Equal("Edge", t.Browser));
        Assert.Equal(1, output.ExcludedCount);
    }

    [Fact]
    public void Collect_ExcludesBrowsersFromWindowsReturnedUnfiltered()
    {
        // Defense in depth: even if a source ignores the exclusion callback, nothing from the browser is kept.
        var source = new UnfilteredBrowserSource(
            new RawBrowserWindow("Firefox", "firefox", "Private - Mozilla Firefox", [new RawBrowserTab("Secret", true)], "https://secret.test/"));
        var settings = new AppSettings { CollectBrowserTabs = true, IncludeBrowserUrls = true, ExcludedProcesses = ["firefox"] };

        var output = new BrowserTabCollector(source).Collect(CollectorContext.For(settings), CancellationToken.None);

        Assert.Empty(output.Items);
        Assert.Equal(1, output.ExcludedCount);
    }

    private sealed class UnfilteredBrowserSource(params RawBrowserWindow[] windows) : IBrowserTabSource
    {
        public IReadOnlyList<RawBrowserWindow> GetWindows(bool includeActiveUrl, Func<string, bool> isExcluded, CancellationToken cancellationToken) => windows;
    }

    [Fact]
    public void BrowserListsAreShared()
    {
        Assert.True(ScreenTextTargetSelector.Browsers.SetEquals(KnownBrowsers.Names.Keys));
        foreach (var process in new[] { "chrome", "msedge", "brave", "vivaldi", "opera", "firefox", "arc", "thorium" })
            Assert.Contains(process, KnownBrowsers.Names.Keys);
    }

    [Theory]
    [InlineData("C++ Tutorial - YouTube - Memory usage - 399 MB", "C++ Tutorial - YouTube")]
    [InlineData("Song - YouTube - Audio playing - High memory usage - 1.3 GB", "Song - YouTube")]
    [InlineData("Plain title", "Plain title")]
    public void CleanTitle_RemovesChromiumStatusSuffixes(string input, string expected)
    {
        Assert.Equal(expected, BrowserTabCollector.CleanTitle(input));
    }
}

public class SystemSummaryCollectorTests
{
    [Fact]
    public void Collect_ReturnsOneSummaryWithoutIdentifyingNames()
    {
        var output = new SystemSummaryCollector().Collect(CollectorContext.For(new AppSettings()), CancellationToken.None);

        var summary = Assert.Single(output.Items);
        Assert.StartsWith("Windows", summary.OperatingSystem);
        Assert.True(summary.LogicalProcessors > 0);
        Assert.DoesNotContain(Environment.MachineName, summary.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}

public class PrivacyRulesTests
{
    [Theory]
    [InlineData(@"C:\Secret", @"C:\Secret", true)]
    [InlineData(@"C:\Secret\sub\file.txt", @"C:\Secret", true)]
    [InlineData(@"c:/secret/sub", @"C:\Secret\", true)]
    [InlineData(@"C:\SecretOther", @"C:\Secret", false)]
    [InlineData(@"D:\Secret", @"C:\Secret", false)]
    public void IsPathExcluded_MatchesOnDirectoryBoundaries(string path, string prefix, bool expected)
    {
        Assert.Equal(expected, PrivacyRules.IsPathExcluded(path, [prefix]));
    }

    [Fact]
    public void IsPathExcluded_ExpandsTildeAndEnvironmentVariables()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.True(PrivacyRules.IsPathExcluded(Path.Combine(profile, "Private", "a"), [@"~\Private"]));
        Assert.True(PrivacyRules.IsPathExcluded(Path.Combine(profile, "Private"), [@"%USERPROFILE%\Private"]));
    }

    [Theory]
    [InlineData("KeePassXC", true)]
    [InlineData("keepassxc.exe", true)]
    [InlineData("KeePass", false)]
    public void IsProcessExcluded_IgnoresCaseAndExtension(string name, bool expected)
    {
        Assert.Equal(expected, PrivacyRules.IsProcessExcluded(name, ["KeePassXC.exe"]));
    }
}
