using ContextSnap.Capture;
using ContextSnap.Collectors;
using ContextSnap.Configuration;
using ContextSnap.Linux;
using ContextSnap.Notifications;
using ContextSnap.Output;

namespace ContextSnap.Tests;

/// <summary>A fact that needs Linux (/proc, X11 tools). It is reported as skipped elsewhere.</summary>
public sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute()
    {
        if (!OperatingSystem.IsLinux())
            Skip = "Needs Linux.";
    }
}

/// <summary>A fact that needs an X11 display and xclip, for example under xvfb-run. Skipped otherwise.</summary>
public sealed class X11ClipboardFactAttribute : FactAttribute
{
    public X11ClipboardFactAttribute()
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            Skip = "Needs an X11 display (DISPLAY is not set).";
        else if (ToolRunner.Find("xclip") is null)
            Skip = "Needs xclip.";
    }
}

public class X11WindowSourceTests
{
    private const string WmctrlOutput = """
        0x01e00003  0 2345   gnome-terminal-server.Gnome-terminal  myhost ~/projects — bash
        0x02400007  0 3456   code.Code             myhost main.cs - Snapctx - Visual Studio Code
        0x00a0000c -1 1200   xfdesktop.Xfdesktop   myhost Desktop
        0x03000001  0 0      N/A                   myhost Untitled X client
        0x03200004  0 4567   firefox.firefox       myhost N/A

        not a window line
        """;

    private static string? Name(int pid) => pid switch
    {
        2345 => "gnome-terminal-server",
        3456 => "code",
        _ => null,
    };

    [Fact]
    public void Parse_ReadsTitlesAndProcessesAndDropsTheHost()
    {
        var windows = X11WindowSource.Parse(WmctrlOutput, RootProperties.None, Name);

        Assert.Equal(4, windows.Count);
        Assert.Equal("~/projects — bash", windows[0].Title);
        Assert.Equal("gnome-terminal-server", windows[0].ProcessName);
        Assert.Equal(2345, windows[0].ProcessId);
        Assert.Equal((nint)0x02400007, windows[1].Handle);
        Assert.DoesNotContain(windows, w => w.Title.Contains("myhost", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_SkipsUntitledWindowsAndMarksDesktopShellWindows()
    {
        var windows = X11WindowSource.Parse(WmctrlOutput, RootProperties.None, Name);

        Assert.DoesNotContain(windows, w => w.Title == "N/A");
        Assert.True(windows.Single(w => w.Title == "Desktop").IsToolWindow);
        // No _NET_WM_PID and no WM_CLASS: the process is unknown.
        Assert.Null(windows.Single(w => w.Title == "Untitled X client").ProcessName);
    }

    [Fact]
    public void Parse_UsesWmClassWhenTheProcessIsUnknown()
    {
        var windows = X11WindowSource.Parse(WmctrlOutput, RootProperties.None, _ => null);

        Assert.Equal("code", windows.Single(w => w.Title.EndsWith("Visual Studio Code", StringComparison.Ordinal)).ProcessName);
    }

    [Fact]
    public void Parse_OrdersActiveWindowFirstThenTopmost()
    {
        var root = X11WindowSource.ParseRootProperties("""
            _NET_ACTIVE_WINDOW(WINDOW): window id # 0x2400007
            _NET_CLIENT_LIST_STACKING(WINDOW): window id # 0xa0000c, 0x1e00003, 0x3000001, 0x2400007
            """);

        var windows = X11WindowSource.Parse(WmctrlOutput, root, Name);

        Assert.Equal(0x2400007, root.ActiveWindow);
        Assert.True(windows[0].IsForeground);
        Assert.Equal((nint)0x2400007, windows[0].Handle);
        Assert.Equal(["Untitled X client", "~/projects — bash", "Desktop"], windows.Skip(1).Select(w => w.Title));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("_NET_ACTIVE_WINDOW:  not found.")]
    [InlineData("_NET_ACTIVE_WINDOW(WINDOW): window id # 0x0")]
    public void ParseRootProperties_ToleratesMissingValues(string? output)
    {
        var root = X11WindowSource.ParseRootProperties(output);

        Assert.Null(root.ActiveWindow);
        Assert.Empty(root.StackingTopmostFirst);
    }

    [Fact]
    public void Excluded_And_OwnWindows_AreFilteredLikeOnWindows()
    {
        var windows = X11WindowSource.Parse("""
            0x01  0 10  keepassxc.KeePassXC  host Passwords.kdbx - KeePassXC
            0x02  0 11  xterm.XTerm          host notes
            0x03  0 99  contextsnap.x        host own window
            """, RootProperties.None, pid => pid == 10 ? "KeePassXC" : "xterm");
        var settings = new AppSettings();

        var (selected, excluded) = WindowFilter.Select(windows,
            new WindowFilterOptions(settings.ExcludedProcesses, settings.ExcludedTitleKeywords, OwnProcessId: 99));

        Assert.Equal("notes", Assert.Single(selected).Title);
        Assert.Equal(1, excluded);
    }

    [Fact]
    public void GetWindows_WithoutDisplay_IsUnavailable()
    {
        var source = new X11WindowSource(name => name == "WAYLAND_DISPLAY" ? "wayland-0" : null);

        var error = Assert.Throws<CollectorUnavailableException>(() => source.GetWindows());
        Assert.Contains("XWayland", error.Message, StringComparison.Ordinal);
        Assert.True(source.IsWayland);
    }
}

public class LinuxSystemSummaryTests
{
    [Fact]
    public void ParseOsRelease_PrefersPrettyName()
    {
        Assert.Equal("Ubuntu 24.04.1 LTS", LinuxSystemSummaryCollector.ParseOsRelease("""
            NAME="Ubuntu"
            # comment
            PRETTY_NAME="Ubuntu 24.04.1 LTS"
            VERSION_ID="24.04"
            """));
        Assert.Equal("Arch Linux", LinuxSystemSummaryCollector.ParseOsRelease("NAME='Arch Linux'\n"));
        Assert.Null(LinuxSystemSummaryCollector.ParseOsRelease("ID=x\n"));
        Assert.Null(LinuxSystemSummaryCollector.ParseOsRelease(null));
    }

    [Fact]
    public void ParseMemTotal_ReadsKilobytes()
    {
        Assert.Equal(16_303_492L * 1024, LinuxSystemSummaryCollector.ParseMemTotal("MemTotal:       16303492 kB\nMemFree: 1 kB\n"));
        Assert.Null(LinuxSystemSummaryCollector.ParseMemTotal("MemFree: 1 kB\n"));
        Assert.Null(LinuxSystemSummaryCollector.ParseMemTotal(null));
    }

    [Theory]
    [InlineData("Fedora Linux 41", "GNOME", "wayland", "Fedora Linux 41 (Linux 6.11.4, GNOME on Wayland)")]
    [InlineData("Ubuntu 24.04 LTS", "ubuntu:GNOME", "x11", "Ubuntu 24.04 LTS (Linux 6.11.4, GNOME on X11)")]
    [InlineData(null, null, "tty", "Linux (Linux 6.11.4)")]
    [InlineData("Debian GNU/Linux 12", "XFCE", null, "Debian GNU/Linux 12 (Linux 6.11.4, XFCE)")]
    public void Describe_IncludesDistributionKernelAndDesktop(string? distribution, string? desktop, string? session, string expected)
    {
        Assert.Equal(expected, LinuxSystemSummaryCollector.Describe(distribution, new Version(6, 11, 4), desktop, session));
    }

    [Fact]
    public void Collect_UsesOnlyOsReleaseMemInfoAndDesktopVariables()
    {
        var files = new Dictionary<string, string>
        {
            ["/etc/os-release"] = "PRETTY_NAME=\"Test OS 1\"\n",
            ["/proc/meminfo"] = "MemTotal: 2048 kB\n",
        };
        var collector = new LinuxSystemSummaryCollector(files.GetValueOrDefault,
            name => name == "XDG_CURRENT_DESKTOP" ? "KDE" : name == "XDG_SESSION_TYPE" ? "wayland" : null);

        var summary = Assert.Single(collector.Collect(CollectorContext.For(new AppSettings()), CancellationToken.None).Items);

        Assert.StartsWith("Test OS 1 (Linux ", summary.OperatingSystem, StringComparison.Ordinal);
        Assert.EndsWith(", KDE on Wayland)", summary.OperatingSystem, StringComparison.Ordinal);
        Assert.Equal(2048L * 1024, summary.TotalMemoryBytes);
    }
}

public class LinuxClipboardTests
{
    private static LinuxClipboardWriter Writer(Dictionary<string, string> environment, params string[] tools) =>
        new(environment.GetValueOrDefault, name => tools.Contains(name) ? "/usr/bin/" + name : null);

    [Fact]
    public void Choose_PrefersWlCopyOnWayland()
    {
        var (tool, error) = Writer(new() { ["WAYLAND_DISPLAY"] = "wayland-0", ["DISPLAY"] = ":0" }, "wl-copy", "xclip").Choose();

        Assert.Null(error);
        Assert.Equal("wl-copy", tool!.Name);
        Assert.Equal("/usr/bin/wl-copy", tool.Path);
    }

    [Fact]
    public void Choose_FallsBackToXclipThenXsel()
    {
        Assert.Equal("xclip", Writer(new() { ["WAYLAND_DISPLAY"] = "wayland-0", ["DISPLAY"] = ":0" }, "xclip", "xsel").Choose().Tool!.Name);
        Assert.Equal("xsel", Writer(new() { ["DISPLAY"] = ":0" }, "xsel").Choose().Tool!.Name);
        Assert.Equal(["-selection", "clipboard", "-in"], Writer(new() { ["DISPLAY"] = ":0" }, "xclip").Choose().Tool!.Arguments);
    }

    [Fact]
    public void Choose_ExplainsWhatIsMissing()
    {
        Assert.Equal(LinuxClipboardWriter.NoSessionError, Writer(new(), "wl-copy", "xclip").Choose().Error);
        Assert.Equal(LinuxClipboardWriter.NoToolError, Writer(new() { ["DISPLAY"] = ":0" }).Choose().Error);
        Assert.Contains("wl-clipboard", Writer(new() { ["WAYLAND_DISPLAY"] = "wayland-0" }, "xclip").Choose().Error, StringComparison.Ordinal);
    }

    [Fact]
    public void SetText_WithoutTool_FailsWithoutRunningAnything()
    {
        var result = Writer(new()).SetText("text", excludeFromHistory: true);

        Assert.False(result.Success);
        Assert.Equal(LinuxClipboardWriter.NoSessionError, result.Error);
    }

    [X11ClipboardFact]
    public void SetText_CopiesUnicodeTextThroughXclip()
    {
        const string text = "# Context Snapshot\n\n- naïve — 日本語 ✓\n";

        var result = new LinuxClipboardWriter(name => name == "WAYLAND_DISPLAY" ? null : Environment.GetEnvironmentVariable(name)).SetText(text, true);

        Assert.True(result.Success, result.Error);
        Assert.Equal(text, ToolRunner.ReadOutput(ToolRunner.Find("xclip")!, ["-selection", "clipboard", "-out"], TimeSpan.FromSeconds(5)));
    }
}

public class LinuxCommandLineTests
{
    [Fact]
    public void NoArguments_Captures()
    {
        Assert.Equal(RunMode.Capture, LinuxCommandLine.Parse([]).Mode);
        Assert.Equal(RunMode.Print, LinuxCommandLine.Parse(["--print"]).Mode);
        Assert.Equal(RunMode.Capture, LinuxCommandLine.Parse(["--capture"]).Mode);
    }

    [Fact]
    public void StartupOption_IsRejected()
    {
        var options = LinuxCommandLine.Parse(["--startup"]);

        Assert.Equal(RunMode.Help, options.Mode);
        Assert.NotNull(options.Error);
    }

    [Fact]
    public void ApplyPlatformLimits_TurnsOffWindowsOnlyCollectors()
    {
        var settings = new AppSettings { CollectBrowserTabs = true, IncludeBrowserUrls = true };

        LinuxCommandLine.ApplyPlatformLimits(settings);

        Assert.False(settings.CaptureScreenText);
        Assert.False(settings.CollectBrowserTabs);
        Assert.False(settings.IncludeBrowserUrls);
        Assert.False(settings.CollectExplorerFolders);
        Assert.True(settings.CollectWindows);
        Assert.True(settings.RedactSecrets);
    }

    [Fact]
    public async Task Help_ShowsTheLinuxUsage()
    {
        var output = new StringWriter();
        var runner = new HeadlessRunner(new SnapshotService(LinuxCollectors.CreateDefault()), new SettingsStore("unused.json"),
            new RecordingClipboard(), usage: LinuxCommandLine.Usage);

        var exitCode = await runner.RunAsync(LinuxCommandLine.Parse(["--help"]), output, TextWriter.Null, Environment.CurrentDirectory);

        Assert.Equal(0, exitCode);
        Assert.Contains("snapctx --print", output.ToString(), StringComparison.Ordinal);
    }

    [LinuxFact]
    public async Task Capture_CopiesRedactedMarkdownAndReportsWindowsOnlySectionsAsOff()
    {
        var directory = Directory.CreateTempSubdirectory("contextsnap-linux-");
        try
        {
            // Browser tabs on and a project folder that holds a secret-looking name: the limits and redaction still apply.
            var store = new SettingsStore(Path.Combine(directory.FullName, "settings.json"));
            store.Save(new AppSettings { CollectBrowserTabs = true, CollectDesktop = false });
            var clipboard = new RecordingClipboard();
            var runner = new HeadlessRunner(new SnapshotService(LinuxCollectors.CreateDefault()), store, clipboard,
                usage: LinuxCommandLine.Usage, applyPlatformLimits: LinuxCommandLine.ApplyPlatformLimits);
            var output = new StringWriter();

            var exitCode = await runner.RunAsync(LinuxCommandLine.Parse([]), output, TextWriter.Null, directory.FullName);

            Assert.Equal(0, exitCode);
            var markdown = Assert.IsType<string>(clipboard.Text);
            Assert.StartsWith("# Context Snapshot", markdown, StringComparison.Ordinal);
            Assert.Contains("_Not collected (turned off): Visible screen text, File Explorer folders, Browser tabs, Desktop items._", markdown, StringComparison.Ordinal);
            Assert.Contains("### System", markdown, StringComparison.Ordinal);
            Assert.Contains("Snapshot copied to the clipboard", output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class RecordingClipboard : IClipboardWriter
    {
        public string? Text { get; private set; }

        public ClipboardResult SetText(string text, bool excludeFromHistory)
        {
            Text = text;
            return ClipboardResult.Ok;
        }
    }
}

public class NotifySendTests
{
    [Fact]
    public void BuildArguments_SeparatesOptionsFromTextAndUsesOnlyNotificationContent()
    {
        var content = CopyNotifications.ForSuccess(new TextStatistics(1200, 150, 1200), wasTruncated: true, outputLimit: 1000);

        var arguments = NotifySendNotifier.BuildArguments(content);

        var separator = arguments.ToList().IndexOf("--");
        Assert.True(separator > 0);
        Assert.All(arguments.Take(separator), a => Assert.StartsWith("--", a, StringComparison.Ordinal));
        Assert.Equal(CopyNotifications.SuccessTitle, arguments[separator + 1]);
        Assert.Contains("1,200 characters", arguments[separator + 2], StringComparison.Ordinal);
        Assert.Contains("--urgency=low", arguments);
    }

    [Fact]
    public void BuildArguments_MarksFailuresCritical()
    {
        var arguments = NotifySendNotifier.BuildArguments(CopyNotifications.ForFailure("xclip failed"));

        Assert.Contains("--urgency=critical", arguments);
        Assert.Contains("--icon=dialog-warning", arguments);
    }
}

/// <summary>A fact that needs an X11 display, for example under xvfb-run. Skipped otherwise.</summary>
public sealed class X11FactAttribute : FactAttribute
{
    public X11FactAttribute()
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            Skip = "Needs an X11 display (DISPLAY is not set).";
    }
}

public class X11HotkeyTests
{
    [Fact]
    public void Modifiers_CoverCapsLockAndNumLock()
    {
        Assert.Equal(4, X11Hotkey.Modifiers.Distinct().Count());
        Assert.All(X11Hotkey.Modifiers, m => Assert.Equal(4u | 8u | 1u, m & (4u | 8u | 1u))); // Control, Alt, Shift always.
    }

    [X11Fact]
    public void Register_IsExclusive()
    {
        var (first, error) = X11Hotkey.Register();
        Assert.True(first is not null, error);
        using (first)
        {
            var (second, secondError) = X11Hotkey.Register();
            second?.Dispose();

            Assert.Null(second);
            Assert.Contains("already used", secondError, StringComparison.Ordinal);
        }

        // Released on dispose, so it can be registered again.
        var (again, againError) = X11Hotkey.Register();
        Assert.True(again is not null, againError);
        again.Dispose();
    }
}

public class LinuxSetupTests
{
    private static LinuxSetup Setup(Dictionary<string, string> environment) => new(TextWriter.Null, environment.GetValueOrDefault);

    [Theory]
    [InlineData("@as []", new string[0])]
    [InlineData("['/a/custom0/']", new[] { "/a/custom0/" })]
    [InlineData("['/a/', '/b/it\\'s/']", new[] { "/a/", "/b/it's/" })]
    public void ParseStringArray_ReadsGsettingsOutput(string output, string[] expected)
    {
        Assert.Equal(expected, LinuxSetup.ParseStringArray(output));
    }

    [Fact]
    public void FormatStringArray_RoundTrips()
    {
        string[] items = ["/a/", "/b/it's/", @"/c\d/"];

        Assert.Equal("@as []", LinuxSetup.FormatStringArray([]));
        Assert.Equal(items, LinuxSetup.ParseStringArray(LinuxSetup.FormatStringArray(items)));
    }

    [Fact]
    public void Quote_EscapesShellSpecialCharacters()
    {
        Assert.Equal("\"/home/a b/.local/bin/contextsnap\"", LinuxSetup.Quote("/home/a b/.local/bin/contextsnap"));
        Assert.Equal("\"/x/\\$HOME\\`id\\`\\\"\\\\/c\"", LinuxSetup.Quote("/x/$HOME`id`\"\\/c"));
        Assert.Throws<ArgumentException>(() => LinuxSetup.Quote("/x\n/y"));
    }

    [Fact]
    public void DesktopEntry_StartsTheBackgroundInstance()
    {
        var entry = LinuxSetup.DesktopEntry("/home/a b/100%/contextsnap");

        Assert.StartsWith("[Desktop Entry]\n", entry, StringComparison.Ordinal);
        Assert.Contains("\nExec=\"/home/a b/100%%/contextsnap\" --background\n", entry, StringComparison.Ordinal);
        Assert.Contains("\nType=Application\n", entry, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopEntry_DoublesBackslashesForTheDesktopFileFormat()
    {
        Assert.Contains("Exec=\"/x/\\\\$y\" --background", LinuxSetup.DesktopEntry("/x/$y"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ubuntu:GNOME", true)]
    [InlineData("GNOME", true)]
    [InlineData("KDE", false)]
    [InlineData("GNOME-Flashback:Unity", false)]
    public void IsGnome_MatchesTheDesktopName(string desktop, bool expected)
    {
        Assert.Equal(expected, Setup(new() { ["XDG_CURRENT_DESKTOP"] = desktop }).IsGnome);
    }

    [LinuxFact] // Linux paths: "/cfg" is not an absolute path on Windows.
    public void AutostartFile_HonorsAnAbsoluteXdgConfigHome()
    {
        Assert.Equal("/cfg/autostart/contextsnap.desktop", Setup(new() { ["XDG_CONFIG_HOME"] = "/cfg" }).AutostartFile);
        Assert.EndsWith("/.config/autostart/contextsnap.desktop", Setup(new() { ["XDG_CONFIG_HOME"] = "relative" }).AutostartFile, StringComparison.Ordinal);
    }

    [Fact]
    public void Usage_DescribesInstall()
    {
        Assert.Contains("snapctx --install", LinuxCommandLine.Usage, StringComparison.Ordinal);
        Assert.Contains("snapctx --uninstall", LinuxCommandLine.Usage, StringComparison.Ordinal);
    }
}
