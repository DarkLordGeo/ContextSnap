using ContextSnap.Capture;
using ContextSnap.Collectors;
using ContextSnap.Configuration;
using ContextSnap.Model;
using ContextSnap.Notifications;
using ContextSnap.Output;
using ContextSnap.Platform;

namespace ContextSnap.Tests;

public class HotkeyCaptureWorkflowTests
{
    private static readonly WindowText Chat = new("chrome", "Chat - Google Chrome", true, ScreenTextSource.UiAutomation,
        "You said: deploy it with api_key=sk-live-123456", 47, 0, false, [], TimeSpan.Zero);

    private sealed class RecordingClipboard(bool succeed = true) : IClipboardWriter
    {
        public List<string> Writes { get; } = [];

        public ClipboardResult SetText(string text, bool excludeFromHistory)
        {
            Writes.Add(text);
            return succeed ? ClipboardResult.Ok : new ClipboardResult(false, "The clipboard is in use.");
        }
    }

    private sealed class RecordingNotifier : ICopyNotifier
    {
        public List<CopyNotificationContent> Shown { get; } = [];

        public void Show(CopyNotificationContent content) => Shown.Add(content);
    }

    private sealed class MemoryLog : IDiagnosticsLog
    {
        public List<string> Lines { get; } = [];

        public void Warning(string message) => Lines.Add("WARN " + message);

        public void Error(string message, Exception exception) => Lines.Add($"ERROR {message}: {exception.GetType().Name}");
    }

    private static SnapshotService Service(params WindowText[] screenText) => new(new CollectorSet
    {
        Windows = StubCollector<WindowInfo>.Returning("Open windows", new WindowInfo("chrome", "Chat - Google Chrome", 1, true)),
        Processes = StubCollector<ProcessGroup>.Returning("Developer tools"),
        Explorer = StubCollector<ExplorerFolder>.Returning("File Explorer folders"),
        Git = StubCollector<GitRepositoryInfo>.Returning("Git repositories"),
        Desktop = StubCollector<DesktopEntry>.Returning("Desktop items"),
        BrowserTabs = StubCollector<BrowserTab>.Returning("Browser tabs"),
        SystemInfo = StubCollector<SystemSummary>.Returning("System summary", new SystemSummary("Windows 11", "X64", 8, null)),
        ScreenText = StubCollector<WindowText>.Returning("Visible screen text", screenText),
    });

    private static (HotkeyCaptureWorkflow Workflow, RecordingClipboard Clipboard, RecordingNotifier Notifier, MemoryLog Log) Create(
        Func<AppSettings, Task<CaptureResult>> capture, bool clipboardWorks = true)
    {
        var clipboard = new RecordingClipboard(clipboardWorks);
        var notifier = new RecordingNotifier();
        var log = new MemoryLog();
        return (new HotkeyCaptureWorkflow(capture, new CopyService(clipboard, notifier), notifier, log), clipboard, notifier, log);
    }

    [Fact]
    public async Task CopiesTheCompleteMarkdownEvenWhenVisibleScreenTextIsIncluded()
    {
        var service = Service(Chat);
        var (workflow, clipboard, notifier, _) = Create(s => service.CaptureAsync(s));

        var outcome = await workflow.RunAsync(new AppSettings());

        Assert.Equal(HotkeyCaptureStatus.Copied, outcome.Status);
        var copied = Assert.Single(clipboard.Writes);
        Assert.Equal(outcome.Capture!.Markdown, copied);           // The full formatted snapshot, not extracted text.
        Assert.StartsWith("# Context Snapshot", copied);
        Assert.Contains("## Visible text", copied);
        Assert.Contains("## System context", copied);
        Assert.Contains("api_key=[REDACTED]", copied);              // Redaction still applies.
        Assert.DoesNotContain("sk-live-123456", copied);
        Assert.Equal("Includes visible text from 1 window.", Assert.Single(notifier.Shown).Message);
    }

    [Fact]
    public async Task OlderReviewSettingNoLongerPreventsCopying()
    {
        var service = Service(Chat);
        var (workflow, clipboard, _, _) = Create(s => service.CaptureAsync(s));

        await workflow.RunAsync(new AppSettings { HotkeyCopiesImmediately = false });

        Assert.Single(clipboard.Writes);
    }

    [Fact]
    public async Task OutputLimitStillApplies()
    {
        var longText = string.Join('\n', Enumerable.Range(0, 400).Select(i => $"Visible line {i} with some words"));
        var service = Service(Chat with { Text = longText, UiaCharacters = longText.Length });
        var (workflow, clipboard, notifier, _) = Create(s => service.CaptureAsync(s));

        await workflow.RunAsync(new AppSettings { MaxOutputCharacters = 2_000 });

        var copied = Assert.Single(clipboard.Writes);
        Assert.True(copied.Length <= 2_000);
        Assert.Contains("truncated", copied);
        Assert.Contains("2,000-character limit", notifier.Shown.Single().Warning);
    }

    [Fact]
    public async Task CaptureFailure_IsLoggedAndReportedWithoutCopyingOrThrowing()
    {
        var (workflow, clipboard, notifier, log) = Create(_ => throw new InvalidOperationException("UI Automation crashed"));

        var outcome = await workflow.RunAsync(new AppSettings());

        Assert.Equal(HotkeyCaptureStatus.CaptureFailed, outcome.Status);
        Assert.Empty(clipboard.Writes);
        Assert.Contains("ERROR Shortcut capture failed: InvalidOperationException", log.Lines);
        Assert.Equal(CopyNotificationKind.Error, Assert.Single(notifier.Shown).Kind);
    }

    [Fact]
    public async Task CaptureFailure_WithNotificationsOff_ShowsNothing()
    {
        var (workflow, _, notifier, log) = Create(_ => throw new InvalidOperationException("boom"));

        await workflow.RunAsync(new AppSettings { ShowCopyNotifications = false });

        Assert.Empty(notifier.Shown);
        Assert.Single(log.Lines);
    }

    [Fact]
    public async Task ClipboardFailure_IsLogged()
    {
        var service = Service();
        var (workflow, _, _, log) = Create(s => service.CaptureAsync(s), clipboardWorks: false);

        var outcome = await workflow.RunAsync(new AppSettings());

        Assert.Equal(HotkeyCaptureStatus.CopyFailed, outcome.Status);
        Assert.Contains("WARN Shortcut copy failed: The clipboard is in use.", log.Lines);
    }

    [Fact]
    public async Task RepeatedPresses_WhileCapturing_AreIgnoredThenWorkAgain()
    {
        var service = Service(Chat);
        var gate = new TaskCompletionSource();
        var calls = 0;
        var (workflow, clipboard, _, _) = Create(async s =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                await gate.Task; // First capture is still running.
            return await service.CaptureAsync(s);
        });

        var first = workflow.RunAsync(new AppSettings());
        var second = await workflow.RunAsync(new AppSettings());
        gate.SetResult();
        var firstOutcome = await first;
        var third = await workflow.RunAsync(new AppSettings());

        Assert.Equal(HotkeyCaptureStatus.Busy, second.Status);
        Assert.Equal(HotkeyCaptureStatus.Copied, firstOutcome.Status);
        Assert.Equal(HotkeyCaptureStatus.Copied, third.Status);
        Assert.Equal(2, clipboard.Writes.Count);
    }

    [Fact]
    public void WorkflowHasNoUserInterfaceDependency()
    {
        // Regression guard: the shortcut path lives in the UI-free core library, so it cannot open a window.
        var referenced = typeof(HotkeyCaptureWorkflow).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
        Assert.DoesNotContain("PresentationFramework", referenced);
    }
}

public sealed class DiagnosticsLogTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ContextSnapDiagnostics").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void WritesRedactedErrorsAndRotatesWhenLarge()
    {
        var path = Path.Combine(_directory, "logs", "contextsnap.log");
        var log = new DiagnosticsLog(path, maxBytes: 200);

        log.Error("Shortcut capture failed", new InvalidOperationException("token=abc123 leaked"));
        Assert.Contains("ERROR Shortcut capture failed: InvalidOperationException: token=[REDACTED] leaked", File.ReadAllText(path));

        for (var i = 0; i < 10; i++)
            log.Warning($"Shortcut copy failed: attempt {i}");

        Assert.True(File.Exists(path + ".old"));
        Assert.True(new FileInfo(path).Length < 400);
    }

    [Fact]
    public void LoggingFailuresAreIgnored()
    {
        var blocker = Path.Combine(_directory, "file-not-folder");
        File.WriteAllText(blocker, "x");
        var log = new DiagnosticsLog(Path.Combine(blocker, "contextsnap.log"));

        log.Warning("cannot be written"); // Must not throw.
    }
}
