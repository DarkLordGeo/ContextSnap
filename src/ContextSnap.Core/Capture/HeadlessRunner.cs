using System.Globalization;
using ContextSnap.Collectors.ScreenText;
using ContextSnap.Configuration;
using ContextSnap.Model;
using ContextSnap.Notifications;
using ContextSnap.Output;

namespace ContextSnap.Capture;

public enum RunMode
{
    Gui,
    Capture,
    Print,
    Help,

    /// <summary>Ask the running background instance to quit.</summary>
    Exit,
    EnableStartup,
    DisableStartup,
}

/// <param name="StartMinimized">Set by <c>--startup</c> (used by the sign-in shortcut): start in the background without a window and stay quiet if already running.</param>
public sealed record CommandLineOptions(RunMode Mode, string? Error = null, bool StartMinimized = false)
{
    public const string Usage = """
        ContextSnap — capture a Markdown snapshot of your current desktop context.

        Usage:
          ContextSnap                Open the application window.
          ContextSnap --capture      Capture, copy to the clipboard, print a summary, and exit.
          ContextSnap --print        Capture and write the Markdown to standard output (no clipboard).
          ContextSnap --startup      Start in the background: no window, taskbar button, or icon (used at sign-in).
          ContextSnap --exit         Quit the running background instance.
          ContextSnap --disable-startup   Stop starting ContextSnap when you sign in.
          ContextSnap --enable-startup    Start ContextSnap in the background when you sign in.
          ContextSnap --help         Show this help.

        Settings are read from %APPDATA%\ContextSnap\settings.json. In --capture and --print modes,
        the current directory is also checked for a Git repository.
        """;

    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
            return new CommandLineOptions(RunMode.Gui);
        if (args.Count > 1)
            return new CommandLineOptions(RunMode.Help, "Only one option can be used at a time.");

        return args[0].ToLowerInvariant() switch
        {
            "--capture" or "-c" or "/capture" => new CommandLineOptions(RunMode.Capture),
            "--startup" or "/startup" => new CommandLineOptions(RunMode.Gui, StartMinimized: true),
            "--print" or "-p" or "/print" => new CommandLineOptions(RunMode.Print),
            "--help" or "-h" or "-?" or "/?" => new CommandLineOptions(RunMode.Help),
            "--exit" or "/exit" => new CommandLineOptions(RunMode.Exit),
            "--enable-startup" => new CommandLineOptions(RunMode.EnableStartup),
            "--disable-startup" => new CommandLineOptions(RunMode.DisableStartup),
            _ => new CommandLineOptions(RunMode.Help, "Unknown option."),
        };
    }
}

/// <summary>Command-line capture without a window. Exit codes: 0 success, 1 failure, 2 usage error.</summary>
/// <param name="notifier">Receives the copy notification in <c>--capture</c> mode; null when notifications must not be shown (for example, redirected output).</param>
/// <param name="usage">Help text; defaults to <see cref="CommandLineOptions.Usage"/>.</param>
/// <param name="applyPlatformLimits">Turns off, after settings are loaded, collectors this platform does not have.</param>
public sealed class HeadlessRunner(
    SnapshotService service,
    SettingsStore store,
    IClipboardWriter clipboard,
    ICopyNotifier? notifier = null,
    string? usage = null,
    Action<AppSettings>? applyPlatformLimits = null)
{
    /// <param name="currentDirectory">Also checked for a Git repository; null for shortcut captures, which have no meaningful one.</param>
    public async Task<int> RunAsync(CommandLineOptions options, TextWriter output, TextWriter error, string? currentDirectory)
    {
        if (options.Mode is RunMode.Help or RunMode.Gui)
        {
            if (options.Error is not null)
                await error.WriteLineAsync(options.Error);
            await output.WriteLineAsync(usage ?? CommandLineOptions.Usage);
            return options.Error is null ? 0 : 2;
        }

        var loaded = store.Load();
        if (loaded.Error is not null)
            await error.WriteLineAsync($"Warning: {loaded.Error}");

        applyPlatformLimits?.Invoke(loaded.Settings);
        var result = await service.CaptureAsync(loaded.Settings, currentDirectory is null ? CaptureOptions.Default : new CaptureOptions([currentDirectory]));

        if (options.Mode == RunMode.Print)
        {
            await output.WriteAsync(result.Markdown);
            return 0;
        }

        var settings = loaded.Settings;
        var copy = new CopyService(clipboard, notifier ?? NullCopyNotifier.Instance).Copy(new CopyRequest(
            result.Markdown,
            settings.ExcludeFromClipboardHistory,
            result.WasTruncated,
            result.OutputLimit,
            ShowNotification: notifier is not null && settings.ShowCopyNotifications,
            ScreenTextSummary.CopyDetail(result.Snapshot.ScreenText)));
        if (!copy.Succeeded)
        {
            await error.WriteLineAsync($"Clipboard copy failed: {copy.Error}");
            return 1;
        }

        await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
            $"Snapshot copied to the clipboard ({result.Markdown.Length:N0} characters{(result.WasTruncated ? ", truncated by the output limit" : string.Empty)})."));
        await output.WriteLineAsync("  " + Summarize(result.Snapshot));
        return 0;
    }

    public static string Summarize(ContextSnapshot snapshot) =>
        string.Join(" · ", snapshot.AllResults.Select(r => $"{r.Name}: {DescribeStatus(r)}"));

    public static string DescribeStatus(ICollectorResult result) => result.Status switch
    {
        CollectorStatus.Ok => (result.Count + result.OmittedCount).ToString(CultureInfo.InvariantCulture),
        CollectorStatus.Empty => "none",
        CollectorStatus.Disabled => "off",
        CollectorStatus.Unavailable => "unavailable",
        _ => "failed",
    };
}
