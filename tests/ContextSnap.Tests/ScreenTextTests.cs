using ContextSnap.Capture;
using ContextSnap.Collectors;
using ContextSnap.Collectors.ScreenText;
using ContextSnap.Configuration;
using ContextSnap.Formatting;
using ContextSnap.Model;
using ContextSnap.Platform;
using ContextSnap.Privacy;

namespace ContextSnap.Tests;

public class VisibleTextAssemblerTests
{
    private static readonly PixelRect View = new(0, 0, 1000, 800);

    private static UiaNode Node(string type, string? name, PixelRect bounds, params UiaNode[] children) =>
        new(type, name, null, bounds, false, false, null, children);

    private static PixelRect Row(int top, int left = 10, int width = 200) => new(left, top, left + width, top + 20);

    private static AssembledText Assemble(UiaNode root, bool includeOffscreen = false, int max = 10_000) =>
        VisibleTextAssembler.Assemble(root, new AssemblyOptions(View, includeOffscreen, max));

    [Fact]
    public void KeepsTreeOrderAndPrefersLeafTextOverContainerLabels()
    {
        var root = Node("Window", "App", View,
            Node("ListItem", "First item full label", Row(10), UiaNode.Leaf("Text", "First item", Row(10))),
            Node("Group", "Second", Row(40), UiaNode.Leaf("Text", "Second item", Row(40))),
            Node("Button", "OK", Row(80)));

        Assert.Equal("First item\nSecond item\nOK", Assemble(root).Text);
    }

    [Fact]
    public void JoinsFragmentsOnTheSameVisualLine()
    {
        var root = Node("Pane", null, View,
            UiaNode.Leaf("Text", "Read the", new PixelRect(10, 100, 80, 120)),
            UiaNode.Leaf("Hyperlink", "documentation", new PixelRect(84, 100, 190, 120)),
            UiaNode.Leaf("Text", ", then continue.", new PixelRect(190, 100, 300, 120)),
            UiaNode.Leaf("Text", "Next paragraph", new PixelRect(10, 140, 200, 160)));

        Assert.Equal("Read the documentation, then continue.\nNext paragraph", Assemble(root).Text);
    }

    [Fact]
    public void SkipsOffscreenOutsideAndTinyElements()
    {
        var root = Node("Pane", null, View,
            UiaNode.Leaf("Text", "Visible", Row(10)),
            UiaNode.Leaf("Text", "Scrolled away", Row(900), isOffscreen: true),
            UiaNode.Leaf("Text", "Below the window", Row(2000)),
            UiaNode.Leaf("Hyperlink", "Skip to content", new PixelRect(0, 0, 1, 1)),
            Node("Group", null, Row(1500) with { Top = 1500 }, UiaNode.Leaf("Text", "Hidden child", Row(1500))) with { IsOffscreen = true });

        Assert.Equal("Visible", Assemble(root).Text);
        Assert.Contains("Scrolled away", Assemble(root, includeOffscreen: true).Text);
    }

    [Fact]
    public void NeverReadsPasswordFieldsOrTheirChildren()
    {
        var password = new UiaNode("Edit", "Password", "hunter2", Row(50), false, IsPassword: true, null,
            [UiaNode.Leaf("Text", "hunter2", Row(50))]);
        var root = Node("Window", null, View, UiaNode.Leaf("Text", "Sign in", Row(10)), password);

        var result = Assemble(root);

        Assert.Equal("Sign in", result.Text);
        Assert.Equal(1, result.SkippedPasswordFields);
        Assert.DoesNotContain("hunter2", result.Text);
    }

    [Fact]
    public void InsideWebDocumentsSkipsButtonAndRegionLabels()
    {
        var document = Node("Document", "Page", View,
            UiaNode.Leaf("Text", "You said: hello", Row(10)),
            UiaNode.Leaf("Button", "Copy message", Row(40)),
            Node("Group", "Latest response", Row(70)),
            UiaNode.Leaf("Hyperlink", "Open docs", Row(100)));
        var root = Node("Window", null, View, document, UiaNode.Leaf("Button", "Close", Row(700)));

        Assert.Equal("You said: hello\nOpen docs\nClose", Assemble(root).Text);
    }

    [Fact]
    public void SkipsCaptionButtonsAndTabTitles()
    {
        var root = Node("Window", "Notepad", View,
            Node("TitleBar", "Notepad", Row(0), UiaNode.Leaf("Button", "Minimize", Row(0, 800, 40)), UiaNode.Leaf("Button", "Close", Row(0, 900, 40))),
            Node("Tab", null, Row(30), Node("TabItem", null, Row(30), UiaNode.Leaf("Text", "other-file-with-secret.txt", Row(30))), UiaNode.Leaf("TabItem", "notes.txt", Row(30, 220))),
            UiaNode.Leaf("Text", "Document body", Row(80)));

        Assert.Equal("Document body", Assemble(root).Text);
    }

    [Fact]
    public void UsesVisibleRangesForTextControlsAndRemovesConsecutiveDuplicates()
    {
        var editor = new UiaNode("Document", "Editor", null, Row(10), false, false, "line one\r\nline two\n\nline two", []);
        var root = Node("Window", null, View, editor, UiaNode.Leaf("Text", "line two", Row(500)));

        Assert.Equal("line one\nline two", Assemble(root).Text);
    }

    [Fact]
    public void EditValuesAreReadButEmptyEditsAreNot()
    {
        var root = Node("Window", null, View,
            new UiaNode("Edit", "Search", "my query", Row(10), false, false, null, []),
            new UiaNode("Edit", "Message", "", Row(40), false, false, null, []));

        Assert.Equal("my query", Assemble(root).Text);
    }

    [Fact]
    public void AppliesCharacterLimitAtLineBoundaries()
    {
        var root = Node("Pane", null, View, Enumerable.Range(0, 20).Select(i => UiaNode.Leaf("Text", $"Line number {i}", Row(i * 30))).ToArray());

        var result = Assemble(root, max: 41);

        Assert.True(result.Truncated);
        Assert.Equal("Line number 0\nLine number 1\nLine number 2", result.Text);
    }

    [Fact]
    public void Limit_CutsASingleOverlongLine()
    {
        var (text, truncated) = VisibleTextAssembler.Limit([new string('x', 50)], 10);

        Assert.True(truncated);
        Assert.Equal(10, text.Length);
        Assert.EndsWith("…", text);
    }

    [Fact]
    public void Limit_CutsAnOverlongLineAtAWordBoundarySoTokensAreNotSplit()
    {
        var token = "ghp_" + new string('A', 36);
        var (text, truncated) = VisibleTextAssembler.Limit([$"see {token} here"], 20);

        Assert.True(truncated);
        Assert.Equal("see…", text);
        Assert.DoesNotContain("ghp_", text);
    }

    [Fact]
    public void Normalize_RemovesInvisibleCharacters()
    {
        Assert.Equal("a b c", VisibleTextAssembler.Normalize("a​ ￼  b\t﻿c"));
    }
}

public class OcrLayoutTests
{
    private static OcrLine Line(string text, int left, int top, int right) => new(text, new PixelRect(left, top, right, top + 16));

    [Fact]
    public void Order_ReadsColumnsLeftToRightThenTopToBottom()
    {
        var lines = new[]
        {
            Line("Message two", 320, 60, 900),
            Line("Chat B", 10, 60, 200),
            Line("Message one", 320, 20, 880),
            Line("Chat A", 10, 20, 200),
        };

        Assert.Equal(["Chat A", "Chat B", "Message one", "Message two"], OcrLayout.Order(lines));
    }

    [Fact]
    public void Order_WideLineMergesColumnsIntoRowOrder()
    {
        var lines = new[] { Line("Left", 10, 40, 200), Line("Heading across", 10, 10, 900), Line("Right", 400, 40, 600) };

        Assert.Equal(["Heading across", "Left", "Right"], OcrLayout.Order(lines));
    }

    [Fact]
    public void Order_IgnoresEmptyAndZeroSizedLines()
    {
        Assert.Empty(OcrLayout.Order([new OcrLine("  ", new PixelRect(0, 0, 10, 10)), new OcrLine("x", default)]));
    }

    [Fact]
    public void MergeWithUia_SkipsTextAlreadyReadDespiteSmallDifferences()
    {
        var (lines, added) = OcrLayout.MergeWithUia(["Hello, World!", "Settings"], ["hello world", "SETTINGS", "x", "Only on screen"]);

        Assert.Equal(["Hello, World!", "Settings", "Only on screen"], lines);
        Assert.Equal("Only on screen".Length, added);
    }
}

public class ScreenTextTargetSelectorTests
{
    private static RawWindow Window(nint handle, string title, string process, bool foreground = false, bool minimized = false, bool cloaked = false, int pid = 10) =>
        new(handle, title, pid, process, "AppWindow", cloaked, false, false, foreground) { IsMinimized = minimized };

    private static TargetOptions Options(ScreenTextMode mode = ScreenTextMode.FocusedWindow, int max = 4, string[]? screenTextExclusions = null) =>
        new(mode, max, ["KeePassXC"], ["Incognito"], screenTextExclusions ?? [], OwnProcessId: 99);

    private static readonly PixelRect Bounds = new(0, 0, 800, 600);

    private static TargetSelection Select(RawWindow[] windows, TargetOptions options, double visible = 1) =>
        ScreenTextTargetSelector.Select(windows, options, _ => Bounds, (_, _) => visible);

    [Fact]
    public void FocusedMode_ReadsOnlyTheForegroundWindow()
    {
        var selection = Select([Window(1, "Docs", "chrome"), Window(2, "Editor", "Code", foreground: true)], Options());

        var target = Assert.Single(selection.Targets);
        Assert.Equal("Editor", target.Title);
        Assert.True(target.IsFocused);
        Assert.False(target.IsBrowser);
    }

    [Fact]
    public void WhenContextSnapIsInFront_UsesTheMostRecentOtherWindow()
    {
        var selection = Select([Window(1, "ContextSnap", "ContextSnap", foreground: true, pid: 99), Window(2, "Chat", "chrome")], Options());

        Assert.Equal("Chat", Assert.Single(selection.Targets).Title);
        Assert.True(selection.Targets[0].IsBrowser);
    }

    [Fact]
    public void AllWindowsMode_SkipsCoveredMinimizedAndCloakedWindowsAndHonorsLimit()
    {
        var windows = new[]
        {
            Window(1, "Focused", "Code", foreground: true), Window(2, "Minimized", "notepad", minimized: true),
            Window(3, "Cloaked", "app", cloaked: true), Window(4, "Second", "chrome"), Window(5, "Third", "claude"), Window(6, "Fourth", "cmd"),
        };

        var selection = Select(windows, Options(ScreenTextMode.AllVisibleWindows, max: 3));

        Assert.Equal(["Focused", "Second", "Third"], selection.Targets.Select(t => t.Title));
        Assert.Empty(Select(windows, Options(ScreenTextMode.AllVisibleWindows), visible: 0.01).Targets.Skip(1));
    }

    [Fact]
    public void Exclusions_MatchProcessesAndTitleKeywords()
    {
        var windows = new[]
        {
            Window(1, "Vault", "KeePassXC", foreground: true), Window(2, "New Incognito tab", "chrome"),
            Window(3, "Signal", "Signal"), Window(4, "Inbox - Outlook", "olk"), Window(5, "Notes", "notepad"),
        };

        var selection = Select(windows, Options(ScreenTextMode.AllVisibleWindows, screenTextExclusions: ["signal", "Outlook"]));

        Assert.Equal(["Notes"], selection.Targets.Select(t => t.Title));
        Assert.True(selection.Targets[0].IsFocused);
        Assert.Equal(4, selection.ExcludedCount);
    }
}

public class ScreenTextCollectorTests
{
    private static readonly PixelRect Bounds = new(0, 0, 800, 600);

    private sealed class FakeWindows(params RawWindow[] windows) : IWindowSource
    {
        public IReadOnlyList<RawWindow> GetWindows() => windows;
    }

    private sealed class FakeUia(Func<ScreenTextTarget, UiaReadResult> read) : IUiaTextReader
    {
        public UiaReadResult Read(ScreenTextTarget target, int maxCharacters, CancellationToken cancellationToken) => read(target);
    }

    private sealed class FakeOcr(Func<ScreenTextTarget, PixelRect, OcrReadResult>? read = null, string? unavailable = null) : IOcrTextReader
    {
        public int Calls { get; private set; }
        public PixelRect? LastRegion { get; private set; }
        public string? UnavailableReason => unavailable;

        public OcrReadResult Read(ScreenTextTarget target, PixelRect region, CancellationToken cancellationToken)
        {
            Calls++;
            LastRegion = region;
            return read?.Invoke(target, region) ?? new OcrReadResult([], null);
        }
    }

    private static RawWindow Window(nint handle, string title, string process, bool foreground = false) =>
        new(handle, title, 10, process, "AppWindow", false, false, false, foreground);

    private static UiaReadResult Uia(string text, PixelRect? content = null) =>
        new(new UiaNode("Window", null, null, Bounds, false, false, null,
            text.Split('\n').Select((t, i) => UiaNode.Leaf("Text", t, new PixelRect(10, 10 + i * 30, 300, 30 + i * 30))).ToArray()), content, 5, null);

    private static UiaReadResult NoUia => new(null, null, 0, "UI Automation could not open this window.");

    private static OcrReadResult Ocr(params string[] lines) =>
        new(lines.Select((t, i) => new OcrLine(t, new PixelRect(10, 10 + i * 20, 400, 26 + i * 20))).ToArray(), null);

    private static ScreenTextCollector Collector(IUiaTextReader uia, IOcrTextReader ocr, params RawWindow[] windows) =>
        new(new FakeWindows(windows), uia, ocr, ownProcessId: 99, _ => Bounds, (_, _) => 1);

    private static CollectorOutput<WindowText> Collect(ScreenTextCollector collector, AppSettings? settings = null, CancellationToken token = default) =>
        collector.Collect(CollectorContext.For(settings ?? new AppSettings()), token);

    private static readonly string LongText = string.Join('\n', Enumerable.Range(0, 20).Select(i => $"Readable line {i} with enough words"));

    [Fact]
    public void SufficientUiaText_DoesNotRunOcr()
    {
        var ocr = new FakeOcr();

        var output = Collect(Collector(new FakeUia(_ => Uia(LongText)), ocr, Window(1, "Editor", "Code", foreground: true)));

        var window = Assert.Single(output.Items);
        Assert.Equal(ScreenTextSource.UiAutomation, window.Source);
        Assert.Equal(0, ocr.Calls);
        Assert.StartsWith("Readable line 0", window.Text);
    }

    [Fact]
    public void InsufficientUiaText_FallsBackToOcrAndMergesWithoutDuplicates()
    {
        var ocr = new FakeOcr((_, _) => Ocr("Title bar", "Message rendered by the app", "Another visible line"));

        var output = Collect(Collector(new FakeUia(_ => Uia("Title bar")), ocr, Window(1, "Custom app", "claude", foreground: true)));

        var window = Assert.Single(output.Items);
        Assert.Equal(ScreenTextSource.UiAutomation | ScreenTextSource.Ocr, window.Source);
        Assert.Equal("Title bar\nMessage rendered by the app\nAnother visible line", window.Text);
        Assert.Equal("Message rendered by the app".Length + "Another visible line".Length, window.OcrCharacters);
    }

    [Fact]
    public void OcrDisabled_IsNeverCalled()
    {
        var ocr = new FakeOcr((_, _) => Ocr("should not appear"));

        var output = Collect(Collector(new FakeUia(_ => Uia("short")), ocr, Window(1, "App", "app", foreground: true)),
            new AppSettings { ScreenTextUseOcr = false });

        Assert.Equal(0, ocr.Calls);
        Assert.Equal("short", Assert.Single(output.Items).Text);
    }

    [Fact]
    public void OcrUnavailable_KeepsUiaTextAndExplainsWhy()
    {
        var output = Collect(Collector(new FakeUia(_ => Uia("short")), new FakeOcr(unavailable: "OCR is unavailable: no language."),
            Window(1, "App", "app", foreground: true)));

        var window = Assert.Single(output.Items);
        Assert.Equal("short", window.Text);
        Assert.Contains("OCR is unavailable: no language.", window.Warnings);
    }

    [Fact]
    public void OcrFailure_DoesNotLoseUiaText()
    {
        var ocr = new FakeOcr((_, _) => throw new InvalidOperationException("capture broke"));

        var window = Assert.Single(Collect(Collector(new FakeUia(_ => Uia("short")), ocr, Window(1, "App", "app", foreground: true))).Items);

        Assert.Equal("short", window.Text);
        Assert.Contains("OCR failed (InvalidOperationException).", window.Warnings);
    }

    [Fact]
    public void UiaFailure_FallsBackToOcr()
    {
        var output = Collect(Collector(new FakeUia(_ => throw new InvalidOperationException()), new FakeOcr((_, _) => Ocr("From pixels")),
            Window(1, "Remote desktop", "AnyDesk", foreground: true)));

        var window = Assert.Single(output.Items);
        Assert.Equal(ScreenTextSource.Ocr, window.Source);
        Assert.Equal("From pixels", window.Text);
        Assert.Contains("UI Automation failed (InvalidOperationException).", window.Warnings);
    }

    [Fact]
    public void Browser_OcrIsLimitedToThePageArea()
    {
        var page = new PixelRect(0, 120, 800, 600);
        var ocr = new FakeOcr((_, _) => Ocr("Page text"));

        Collect(Collector(new FakeUia(_ => Uia("x", page)), ocr, Window(1, "Chat - Google Chrome", "chrome", foreground: true)));
        Assert.Equal(page, ocr.LastRegion);

        var noPage = new FakeOcr((_, _) => Ocr("Address bar text"));
        var output = Collect(Collector(new FakeUia(_ => NoUia), noPage, Window(1, "Chat - Google Chrome", "chrome", foreground: true)));
        Assert.Equal(0, noPage.Calls);
        Assert.Empty(output.Items);
        Assert.Contains(output.Notes!, n => n.Contains("address bar are never read"));
    }

    [Fact]
    public void PerWindowCharacterLimit_TruncatesAndFlags()
    {
        var output = Collect(Collector(new FakeUia(_ => Uia(LongText)), new FakeOcr(), Window(1, "Editor", "Code", foreground: true)),
            new AppSettings { MaxScreenTextCharactersPerWindow = 200 });

        var window = Assert.Single(output.Items);
        Assert.True(window.Truncated);
        Assert.True(window.Text.Length <= 200);
    }

    [Fact]
    public void AllWindows_GroupsFocusedFirstAndDropsEmptyOthers()
    {
        var uia = new FakeUia(t => t.Title switch
        {
            "Chat" => Uia(LongText),
            "Editor" => Uia(LongText.Replace("Readable", "Code")),
            _ => NoUia,
        });
        var collector = Collector(uia, new FakeOcr(), Window(1, "Editor", "Code"), Window(2, "Chat", "chrome", foreground: true), Window(3, "Blank", "blank"));

        var output = Collect(collector, new AppSettings { ScreenTextMode = ScreenTextMode.AllVisibleWindows });

        Assert.Equal(["Chat", "Editor"], output.Items.Select(w => w.Title));
        Assert.True(output.Items[0].IsFocused);
        Assert.Contains("1 other visible window(s) had no readable text.", output.Notes!);
    }

    [Fact]
    public void HungWindow_TimesOutWithoutBlockingOthers()
    {
        var uia = new FakeUia(t =>
        {
            if (t.Title == "Hung")
                Thread.Sleep(TimeSpan.FromSeconds(10));
            return Uia(LongText);
        });
        var collector = Collector(uia, new FakeOcr(), Window(1, "Hung", "app", foreground: true), Window(2, "Fine", "notepad"));
        var started = DateTime.UtcNow;

        var output = Collect(collector, new AppSettings { ScreenTextMode = ScreenTextMode.AllVisibleWindows, ScreenTextTimeoutSeconds = 1 });

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
        var hung = output.Items.Single(w => w.Title == "Hung");
        Assert.Contains(hung.Warnings, w => w.StartsWith("Timed out after 1 s"));
        Assert.NotEmpty(output.Items.Single(w => w.Title == "Fine").Text);
    }

    [Fact]
    public void CallerCancellation_Propagates()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            Collect(Collector(new FakeUia(_ => Uia(LongText)), new FakeOcr(), Window(1, "App", "app", foreground: true)), token: cancelled.Token));
    }

    [Fact]
    public void NothingReadable_ReturnsEmptyWithReasons()
    {
        var output = Collect(Collector(new FakeUia(_ => NoUia), new FakeOcr(), Window(1, "App", "app", foreground: true)));

        Assert.Empty(output.Items);
        Assert.Contains(output.Notes!, n => n.Contains("UI Automation could not open this window."));
    }

    [Fact]
    public void ExcludedOnlyWindows_ReturnEmptyWithPrivacyNote()
    {
        var output = Collect(Collector(new FakeUia(_ => Uia(LongText)), new FakeOcr(), Window(1, "Vault", "KeePassXC", foreground: true)));

        Assert.Empty(output.Items);
        Assert.Equal(1, output.ExcludedCount);
        Assert.Contains("The windows on screen are excluded by your privacy settings.", output.Notes!);
    }

    [Theory]
    [InlineData(0, true, 1920, 1032, true)]
    [InlineData(199, true, 1920, 1032, true)]
    [InlineData(200, true, 1920, 1032, false)]
    [InlineData(51, true, 420, 180, false)] // Small dialog with complete accessible text.
    [InlineData(10, true, 420, 180, true)]
    [InlineData(100, true, 1000, 600, true)] // 0.6 MP: threshold 120.
    [InlineData(0, false, 1920, 1032, false)]
    public void ShouldRunOcr_ScalesThresholdWithWindowArea(int uiaCharacters, bool enabled, int width, int height, bool expected)
    {
        Assert.Equal(expected, ScreenTextCollector.ShouldRunOcr(uiaCharacters, enabled, new PixelRect(0, 0, width, height)));
    }
}

public class ScreenTextFormattingTests
{
    private static WindowText Text(string title, bool focused, string text, ScreenTextSource source = ScreenTextSource.UiAutomation, params string[] warnings) =>
        new("chrome", title, focused, source, text, source.HasFlag(ScreenTextSource.UiAutomation) ? text.Length : 0,
            source.HasFlag(ScreenTextSource.Ocr) ? text.Length : 0, false, warnings, TimeSpan.Zero);

    private static string Format(CollectorResult<WindowText> screenText) =>
        new MarkdownFormatter(Redactor.Default).Format(MarkdownFormatterTests.EmptySnapshot() with { ScreenText = screenText });

    [Fact]
    public void WritesVisibleTextBeforeSystemContextWithSourcesAndRedaction()
    {
        var result = new CollectorResult<WindowText>("Visible screen text", CollectorStatus.Ok,
        [
            Text("Chat - Google Chrome", true, "You said: deploy it\napi_key=sk-live-123456\nuse ```code``` here"),
            Text("Claude", false, "Answer text", ScreenTextSource.Ocr, "1 password field(s) were skipped."),
        ]);

        var md = Format(result);

        Assert.True(md.IndexOf("## Visible text", StringComparison.Ordinal) < md.IndexOf("## System context", StringComparison.Ordinal));
        Assert.Contains("### Active window: chrome — Chat - Google Chrome", md);
        Assert.Contains("_Source: UI Automation · ", md);
        Assert.Contains("````text\nYou said: deploy it\napi_key=[REDACTED]", md.ReplaceLineEndings("\n"));
        Assert.DoesNotContain("sk-live-123456", md);
        Assert.Contains("## Other visible windows", md);
        Assert.Contains("_Source: OCR · 11 characters_", md);
        Assert.Contains("_Note: 1 password field(s) were skipped._", md);
        Assert.Contains("UI Automation", md);
    }

    [Fact]
    public void ReportsEmptyResultWithoutFailingTheSnapshot()
    {
        var md = Format(new CollectorResult<WindowText>("Visible screen text", CollectorStatus.Empty, [], Notes: ["App: OCR is unavailable."]));

        Assert.Contains("_No visible text could be extracted; the system context below is still included._", md);
        Assert.Contains("_App: OCR is unavailable._", md);
        Assert.Contains("## System context", md);
    }

    [Fact]
    public void Summary_DescribesMethodsWindowsAndWarnings()
    {
        var windows = new[]
        {
            Text("A", true, new string('a', 2_340)),
            Text("B", false, new string('b', 680), ScreenTextSource.Ocr, "warning"),
        };

        Assert.Equal("UI Automation 2,340 characters · OCR 680 characters · 2 windows · 1 warning", ScreenTextSummary.Describe(windows));
    }

    [Fact]
    public void CopyDetail_OnlyWhenTextWasCaptured()
    {
        var ok = new CollectorResult<WindowText>("Visible screen text", CollectorStatus.Ok, [Text("A", true, "x")]);

        Assert.Equal("Includes visible text from 1 window.", ScreenTextSummary.CopyDetail(ok));
        Assert.Null(ScreenTextSummary.CopyDetail(CollectorResult<WindowText>.Disabled("Visible screen text")));
    }
}

public class ScreenTextPipelineTests
{
    private static CollectorSet Collectors(ICollector<WindowText> screenText) => new()
    {
        Windows = StubCollector<WindowInfo>.Returning("Open windows"),
        Processes = StubCollector<ProcessGroup>.Returning("Developer tools"),
        Explorer = StubCollector<ExplorerFolder>.Returning("File Explorer folders"),
        Git = StubCollector<GitRepositoryInfo>.Returning("Git repositories"),
        Desktop = StubCollector<DesktopEntry>.Returning("Desktop items"),
        BrowserTabs = StubCollector<BrowserTab>.Returning("Browser tabs"),
        SystemInfo = StubCollector<SystemSummary>.Returning("System summary"),
        ScreenText = screenText,
    };

    [Fact]
    public async Task Snapshot_IncludesScreenTextAndCopyNotificationMentionsIt()
    {
        var screen = StubCollector<WindowText>.Returning("Visible screen text",
            new WindowText("notepad", "notes.txt", true, ScreenTextSource.UiAutomation, "Meeting notes: password: hunter2", 32, 0, false, [], TimeSpan.Zero));
        var result = await new SnapshotService(Collectors(screen)).CaptureAsync(new AppSettings());

        Assert.Contains("Meeting notes: password: [REDACTED]", result.Markdown);
        Assert.DoesNotContain("hunter2", result.Markdown);

        var notifier = new DeferredNotifier();
        var outcome = new Output.CopyService(new OkClipboard(), notifier).Copy(new Output.CopyRequest(
            result.Markdown, true, result.WasTruncated, result.OutputLimit, true, ScreenTextSummary.CopyDetail(result.Snapshot.ScreenText)));

        Assert.True(outcome.Succeeded);
        Assert.Equal("Includes visible text from 1 window.", notifier.Last!.Message);
        Assert.DoesNotContain("Meeting", notifier.Last.Message);
    }

    [Fact]
    public async Task DisabledScreenText_IsNotRunAndSnapshotStillWorks()
    {
        var screen = new StubCollector<WindowText>("Visible screen text", _ => throw new InvalidOperationException("must not run"));

        var result = await new SnapshotService(Collectors(screen)).CaptureAsync(new AppSettings { CaptureScreenText = false });

        Assert.Equal(CollectorStatus.Disabled, result.Snapshot.ScreenText.Status);
        Assert.Null(screen.LastContext);
        Assert.DoesNotContain("## Visible text", result.Markdown);
    }

    [Fact]
    public async Task FailingScreenText_DoesNotFailTheSnapshot()
    {
        var screen = new StubCollector<WindowText>("Visible screen text", _ => throw new InvalidOperationException("boom"));

        var result = await new SnapshotService(Collectors(screen)).CaptureAsync(new AppSettings());

        Assert.Equal(CollectorStatus.Failed, result.Snapshot.ScreenText.Status);
        Assert.Contains("_Visible text could not be read: InvalidOperationException: boom_", result.Markdown);
        Assert.Contains("## System context", result.Markdown);
    }

    private sealed class OkClipboard : Output.IClipboardWriter
    {
        public Output.ClipboardResult SetText(string text, bool excludeFromHistory) => Output.ClipboardResult.Ok;
    }

    private sealed class DeferredNotifier : Notifications.ICopyNotifier
    {
        public Notifications.CopyNotificationContent? Last { get; private set; }

        public void Show(Notifications.CopyNotificationContent content) => Last = content;
    }
}

public sealed class ScreenTextSettingsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ContextSnapScreenSettings").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void OldSettingsFilesGetSafeDefaults()
    {
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, """{ "Version": 1, "CollectBrowserTabs": true, "MaxOutputCharacters": 12000 }""");

        var settings = new SettingsStore(path).Load().Settings;

        Assert.True(settings.CaptureScreenText);
        Assert.Equal(ScreenTextMode.FocusedWindow, settings.ScreenTextMode);
        Assert.True(settings.ScreenTextUseOcr);
        Assert.False(settings.ScreenTextIncludeOffscreen);
        Assert.Equal(4_000, settings.MaxScreenTextCharactersPerWindow);
        Assert.True(settings.CollectBrowserTabs);
    }

    [Fact]
    public void ModeIsStoredByNameAndLimitsAreClamped()
    {
        var store = new SettingsStore(Path.Combine(_directory, "settings.json"));
        store.Save(new AppSettings { ScreenTextMode = ScreenTextMode.AllVisibleWindows, MaxScreenTextWindows = 500, MaxScreenTextCharactersPerWindow = 5 });

        Assert.Contains("\"AllVisibleWindows\"", File.ReadAllText(store.FilePath));
        var loaded = store.Load().Settings;
        Assert.Equal(ScreenTextMode.AllVisibleWindows, loaded.ScreenTextMode);
        Assert.Equal(12, loaded.MaxScreenTextWindows);
        Assert.Equal(200, loaded.MaxScreenTextCharactersPerWindow);
    }
}
