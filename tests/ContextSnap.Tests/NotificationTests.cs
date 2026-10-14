using ContextSnap.Capture;
using ContextSnap.Collectors;
using ContextSnap.Configuration;
using ContextSnap.Model;
using ContextSnap.Notifications;
using ContextSnap.Output;
using ContextSnap.Platform;

namespace ContextSnap.Tests;

public class TextStatisticsTests
{
    [Theory]
    [InlineData("", 0, 0, 0)]
    [InlineData("   \r\n\t", 6, 0, 6)]
    [InlineData("hello", 5, 1, 5)]
    [InlineData("# Title\r\n\r\n- **bold** item | x", 30, 4, 30)]
    [InlineData("- - | # ---", 11, 0, 11)]
    [InlineData("naïve café", 10, 2, 12)]
    [InlineData("C:\\Users\\a\\file.txt v2", 22, 2, 22)]
    public void Measure_CountsCharactersWordsAndUtf8Bytes(string text, int characters, int words, long bytes)
    {
        Assert.Equal(new TextStatistics(characters, words, bytes), TextStatistics.Measure(text));
    }
}

public class CopyNotificationContentTests
{
    [Fact]
    public void Success_ShowsFormattedStatistics()
    {
        var content = CopyNotifications.ForSuccess(new TextStatistics(8_420, 1_240, 8_420), wasTruncated: false, outputLimit: 12_000);

        Assert.Equal(CopyNotificationKind.Success, content.Kind);
        Assert.Equal("Context copied to clipboard", content.Title);
        Assert.Equal("8,420 characters · 1,240 words · 8.2 KB", string.Join(" · ", content.Stats));
        Assert.Null(content.Warning);
        Assert.Null(content.Message);
        Assert.Equal(TimeSpan.FromSeconds(4), content.Duration);
    }

    [Fact]
    public void Success_UsesSingularForms()
    {
        var content = CopyNotifications.ForSuccess(new TextStatistics(1, 1, 1), false, 12_000);

        Assert.Equal(["1 character", "1 word", "1 B"], content.Stats);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1_023, "1023 B")]
    [InlineData(1_024, "1 KB")]
    [InlineData(10_240, "10 KB")]
    [InlineData(1_572_864, "1.5 MB")]
    public void Statistics_FormatSizeEdgeCases(long bytes, string expected)
    {
        Assert.Equal(expected, CopyNotifications.FormatStatistics(new TextStatistics(0, 0, bytes))[2]);
    }

    [Fact]
    public void TruncatedSuccess_WarnsWithConfiguredLimitAndStaysLonger()
    {
        var content = CopyNotifications.ForSuccess(new TextStatistics(12_000, 1_500, 12_400), wasTruncated: true, outputLimit: 12_000);

        Assert.Equal(CopyNotificationKind.Success, content.Kind);
        Assert.Equal("Snapshot truncated to the 12,000-character limit.", content.Warning);
        Assert.Equal(TimeSpan.FromSeconds(5), content.Duration);
    }

    [Fact]
    public void TruncatedSuccess_WithoutKnownLimit()
    {
        Assert.Equal("Snapshot truncated by the output limit.", CopyNotifications.ForSuccess(default, true, 0).Warning);
    }

    [Fact]
    public void Failure_ShowsShortSingleLineError()
    {
        var content = CopyNotifications.ForFailure("The clipboard is busy.\r\nDetails: " + new string('x', 300));

        Assert.Equal(CopyNotificationKind.Error, content.Kind);
        Assert.Equal("Copy failed", content.Title);
        Assert.NotNull(content.Message);
        Assert.DoesNotContain('\n', content.Message);
        Assert.True(content.Message.Length <= 140);
        Assert.StartsWith("The clipboard is busy. Details:", content.Message);
        Assert.Empty(content.Stats);
        Assert.Equal(TimeSpan.FromSeconds(6), content.Duration);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Failure_HasFallbackMessage(string? error)
    {
        Assert.Equal("The clipboard could not be written. Try again.", CopyNotifications.ForFailure(error).Message);
    }

    [Fact]
    public void Empty_IsAWarningThatNeverClaimsACopy()
    {
        var content = CopyNotifications.ForEmpty();

        Assert.Equal(CopyNotificationKind.Warning, content.Kind);
        Assert.DoesNotContain("copied to", content.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(content.Stats);
    }

    [Fact]
    public void Durations_StayWithinReadableRange()
    {
        var all = new[]
        {
            CopyNotifications.ForSuccess(default, false, 1), CopyNotifications.ForSuccess(default, true, 1),
            CopyNotifications.ForEmpty(), CopyNotifications.ForFailure("x"),
        };

        Assert.All(all, c => Assert.InRange(c.Duration, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6)));
        Assert.True(CopyNotifications.ErrorDuration > CopyNotifications.SuccessDuration);
    }
}

public class CopyServiceTests
{
    private const string SecretText = "Window: Bank login - api_key=abc123 https://private.example/path";

    private sealed class FakeClipboard(bool succeed = true, string? error = null) : IClipboardWriter
    {
        public List<string> Writes { get; } = [];

        public ClipboardResult SetText(string text, bool excludeFromHistory)
        {
            Writes.Add(text);
            return succeed ? ClipboardResult.Ok : new ClipboardResult(false, error);
        }
    }

    private sealed class RecordingNotifier : ICopyNotifier
    {
        public List<CopyNotificationContent> Shown { get; } = [];

        public void Show(CopyNotificationContent content) => Shown.Add(content);
    }

    private sealed class ThrowingNotifier : ICopyNotifier
    {
        public void Show(CopyNotificationContent content) => throw new InvalidOperationException("toast broke");
    }

    private sealed class FakeToastView : IToastView
    {
        public int Presented { get; private set; }
        public int Refreshed { get; private set; }
        public CopyNotificationContent? Current { get; private set; }
        public bool IsShowing { get; set; }

        public void Present(CopyNotificationContent content)
        {
            Presented++;
            Current = content;
            IsShowing = true;
        }

        public void Refresh(CopyNotificationContent content)
        {
            Refreshed++;
            Current = content;
        }
    }

    private static CopyRequest Request(string text, bool notify = true, bool truncated = false) =>
        new(text, ExcludeFromHistory: true, truncated, OutputLimit: 12_000, notify);

    [Fact]
    public void Success_WritesClipboardThenNotifies()
    {
        var clipboard = new FakeClipboard();
        var notifier = new RecordingNotifier();

        var outcome = new CopyService(clipboard, notifier).Copy(Request("one two three"));

        Assert.True(outcome.Succeeded);
        Assert.Equal(["one two three"], clipboard.Writes);
        var shown = Assert.Single(notifier.Shown);
        Assert.Equal(CopyNotificationKind.Success, shown.Kind);
        Assert.Equal("3 words", shown.Stats[1]);
    }

    [Fact]
    public void Success_PassesTruncationToNotification()
    {
        var notifier = new RecordingNotifier();

        new CopyService(new FakeClipboard(), notifier).Copy(Request("text", truncated: true));

        Assert.Contains("12,000-character limit", notifier.Shown[0].Warning);
    }

    [Fact]
    public void ClipboardFailure_ReportsErrorNotification()
    {
        var notifier = new RecordingNotifier();

        var outcome = new CopyService(new FakeClipboard(succeed: false, error: "The clipboard is in use."), notifier).Copy(Request("text"));

        Assert.False(outcome.Succeeded);
        Assert.Equal(CopyStatus.Failed, outcome.Status);
        Assert.Equal("The clipboard is in use.", outcome.Error);
        Assert.Equal(CopyNotificationKind.Error, notifier.Shown.Single().Kind);
        Assert.Equal("The clipboard is in use.", notifier.Shown.Single().Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n ")]
    public void EmptySnapshot_DoesNotTouchClipboardAndWarns(string text)
    {
        var clipboard = new FakeClipboard();
        var notifier = new RecordingNotifier();

        var outcome = new CopyService(clipboard, notifier).Copy(Request(text));

        Assert.Equal(CopyStatus.Empty, outcome.Status);
        Assert.Empty(clipboard.Writes);
        Assert.Equal(CopyNotificationKind.Warning, notifier.Shown.Single().Kind);
    }

    [Fact]
    public void DisabledNotifications_StillCopy()
    {
        var clipboard = new FakeClipboard();
        var notifier = new RecordingNotifier();

        var outcome = new CopyService(clipboard, notifier).Copy(Request("text", notify: false));

        Assert.True(outcome.Succeeded);
        Assert.Single(clipboard.Writes);
        Assert.Empty(notifier.Shown);
    }

    [Fact]
    public void NotificationFailure_KeepsSuccessfulCopySuccessful()
    {
        var clipboard = new FakeClipboard();

        var outcome = new CopyService(clipboard, new ThrowingNotifier()).Copy(Request("text"));

        Assert.True(outcome.Succeeded);
        Assert.Single(clipboard.Writes);
        Assert.Equal("InvalidOperationException: toast broke", outcome.NotificationError);
    }

    [Fact]
    public void Notification_NeverContainsSnapshotText()
    {
        var notifier = new RecordingNotifier();

        new CopyService(new FakeClipboard(), notifier).Copy(Request(SecretText, truncated: true));
        new CopyService(new FakeClipboard(false, "busy"), notifier).Copy(Request(SecretText));

        foreach (var content in notifier.Shown)
        {
            var visible = string.Join("\n", new[] { content.Title, content.Message, content.Warning }.Concat(content.Stats));
            foreach (var fragment in new[] { "Bank", "api_key", "abc123", "private.example", "Window" })
                Assert.DoesNotContain(fragment, visible);
        }
    }

    [Fact]
    public void RepeatedCopies_UpdateOneToast()
    {
        var view = new FakeToastView();
        var service = new CopyService(new FakeClipboard(), new ToastController(view));

        service.Copy(Request("first"));
        service.Copy(Request("second copy"));
        service.Copy(Request(""));

        Assert.Equal(1, view.Presented);
        Assert.Equal(2, view.Refreshed);
        Assert.Equal(CopyNotificationKind.Warning, view.Current!.Kind);

        view.IsShowing = false; // Dismissed.
        service.Copy(Request("third"));
        Assert.Equal(2, view.Presented);
    }
}

public class ScreenPlacementTests
{
    [Fact]
    public void BottomRight_SitsAboveBottomTaskbar()
    {
        var work = new PixelRect(0, 0, 1920, 1032); // 48 px taskbar at the bottom.

        Assert.Equal((1920 - 404 - 4, 1032 - 150 - 4), ScreenPlacement.BottomRight(work, 404, 150, 4));
    }

    [Fact]
    public void BottomRight_RespectsOffsetWorkAreasOnOtherMonitors()
    {
        var leftMonitorWithTopTaskbar = new PixelRect(-2560, 60, 0, 1440);

        Assert.Equal((-404 - 6, 1440 - 150 - 6), ScreenPlacement.BottomRight(leftMonitorWithTopTaskbar, 404, 150, 6));
    }

    [Fact]
    public void BottomRight_ClampsWhenWindowIsLargerThanWorkArea()
    {
        var tiny = new PixelRect(100, 50, 400, 200);

        Assert.Equal((100, 50), ScreenPlacement.BottomRight(tiny, 500, 300, 4));
    }
}

public sealed class HeadlessNotificationTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ContextSnapCliToast").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class OkClipboard : IClipboardWriter
    {
        public ClipboardResult SetText(string text, bool excludeFromHistory) => ClipboardResult.Ok;
    }

    private HeadlessRunner Runner(ICopyNotifier? notifier, AppSettings? settings = null)
    {
        var store = new SettingsStore(Path.Combine(_directory, "settings.json"));
        if (settings is not null)
            store.Save(settings);

        var collectors = new CollectorSet
        {
            Windows = StubCollector<WindowInfo>.Returning("Open windows", new WindowInfo("Code", "Secret title", 1, false)),
            Processes = StubCollector<ProcessGroup>.Returning("Developer tools"),
            Explorer = StubCollector<ExplorerFolder>.Returning("File Explorer folders"),
            Git = StubCollector<GitRepositoryInfo>.Returning("Git repositories"),
            Desktop = StubCollector<DesktopEntry>.Returning("Desktop items"),
            BrowserTabs = StubCollector<BrowserTab>.Returning("Browser tabs"),
            SystemInfo = StubCollector<SystemSummary>.Returning("System summary"),
            ScreenText = StubCollector<WindowText>.Returning("Visible screen text"),
        };
        return new HeadlessRunner(new SnapshotService(collectors), store, new OkClipboard(), notifier);
    }

    [Fact]
    public async Task Capture_ProducesSuccessNotification()
    {
        var notifier = new DeferredCopyNotifier();

        await Runner(notifier).RunAsync(new CommandLineOptions(RunMode.Capture), new StringWriter(), new StringWriter(), _directory);

        Assert.Equal(CopyNotificationKind.Success, notifier.Last!.Kind);
        Assert.DoesNotContain("Secret title", string.Join(" ", notifier.Last.Stats));
    }

    [Fact]
    public async Task Print_AndDisabledSetting_ProduceNoNotification()
    {
        var printNotifier = new DeferredCopyNotifier();
        await Runner(printNotifier).RunAsync(new CommandLineOptions(RunMode.Print), new StringWriter(), new StringWriter(), _directory);

        var disabledNotifier = new DeferredCopyNotifier();
        await Runner(disabledNotifier, new AppSettings { ShowCopyNotifications = false })
            .RunAsync(new CommandLineOptions(RunMode.Capture), new StringWriter(), new StringWriter(), _directory);

        Assert.Null(printNotifier.Last);
        Assert.Null(disabledNotifier.Last);
    }

    [Fact]
    public void Settings_CopyNotificationsOnByDefault()
    {
        Assert.True(new AppSettings().ShowCopyNotifications);
    }
}
