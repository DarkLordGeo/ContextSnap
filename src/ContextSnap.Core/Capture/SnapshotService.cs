using System.Diagnostics;
using ContextSnap.Collectors;
using ContextSnap.Collectors.Git;
using ContextSnap.Collectors.ScreenText;
using ContextSnap.Configuration;
using ContextSnap.Formatting;
using ContextSnap.Model;
using ContextSnap.Privacy;

namespace ContextSnap.Capture;

public sealed class CollectorSet
{
    public required ICollector<WindowInfo> Windows { get; init; }
    public required ICollector<ProcessGroup> Processes { get; init; }
    public required ICollector<ExplorerFolder> Explorer { get; init; }
    public required ICollector<GitRepositoryInfo> Git { get; init; }
    public required ICollector<DesktopEntry> Desktop { get; init; }
    public required ICollector<BrowserTab> BrowserTabs { get; init; }
    public required ICollector<SystemSummary> SystemInfo { get; init; }
    public required ICollector<WindowText> ScreenText { get; init; }

    public static CollectorSet CreateDefault()
    {
        var windowSource = new Win32WindowSource();
        return new CollectorSet
        {
            Windows = new WindowCollector(windowSource, Environment.ProcessId),
            Processes = new ProcessCollector(new SystemProcessSource()),
            Explorer = new ExplorerCollector(new ShellComWindowSource()),
            Git = new GitCollector(new GitProcessRunner()),
            Desktop = DesktopCollector.CreateDefault(),
            BrowserTabs = new BrowserTabCollector(new UiAutomationBrowserTabSource(windowSource)),
            SystemInfo = new SystemSummaryCollector(),
            ScreenText = new ScreenTextCollector(windowSource, new UiaTextReader(), new WindowsOcrReader(), Environment.ProcessId),
        };
    }
}

/// <param name="ExtraProjectFolders">Additional folders to check for Git repositories, such as the CLI's working directory.</param>
public sealed record CaptureOptions(IReadOnlyList<string> ExtraProjectFolders)
{
    public static CaptureOptions Default { get; } = new([]);
}

public sealed record CaptureResult(ContextSnapshot Snapshot, string Markdown, int UntruncatedLength)
{
    public bool WasTruncated => UntruncatedLength > Markdown.Length;

    /// <summary>The character limit in effect for this capture.</summary>
    public int OutputLimit { get; init; }
}

/// <summary>
/// Orchestrates a capture: runs collectors in parallel with per-collector isolation and timeouts,
/// then redacts, formats, and applies the output limit. Never touches the clipboard.
/// </summary>
public sealed class SnapshotService(CollectorSet collectors, TimeProvider? timeProvider = null)
{
    private const int GitTimeoutMultiplier = 4;

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<CaptureResult> CaptureAsync(AppSettings settings, CaptureOptions? options = null, CancellationToken cancellationToken = default)
    {
        settings = settings.Clone();
        settings.Normalize();
        options ??= CaptureOptions.Default;

        var capturedAt = _time.GetLocalNow();
        var context = CollectorContext.For(settings);
        var timeout = TimeSpan.FromSeconds(settings.CollectorTimeoutSeconds);
        var perSection = settings.MaxItemsPerSection;

        // Started first: it reads the window the user is looking at right now.
        var screenText = RunAsync(collectors.ScreenText, settings.CaptureScreenText, context, settings.MaxScreenTextWindows,
            ScreenTextBudget(settings), cancellationToken);
        var windows = RunAsync(collectors.Windows, settings.CollectWindows, context, perSection, timeout, cancellationToken);
        var processes = RunAsync(collectors.Processes, settings.CollectProcesses, context, perSection, timeout, cancellationToken);
        var explorer = RunAsync(collectors.Explorer, settings.CollectExplorerFolders, context, perSection, timeout, cancellationToken);
        var desktop = RunAsync(collectors.Desktop, settings.CollectDesktop, context, settings.MaxDesktopItems, timeout, cancellationToken);
        var browser = RunAsync(collectors.BrowserTabs, settings.CollectBrowserTabs, context, perSection, timeout, cancellationToken);
        var system = RunAsync(collectors.SystemInfo, settings.CollectSystemSummary, context, perSection, timeout, cancellationToken);
        await Task.WhenAll(screenText, windows, processes, explorer, desktop, browser, system).ConfigureAwait(false);

        var gitContext = context with { ProjectCandidates = BuildProjectCandidates(settings, options, explorer.Result) };
        var git = await RunAsync(collectors.Git, settings.CollectGit, gitContext, settings.MaxRepositories,
            timeout * GitTimeoutMultiplier, cancellationToken).ConfigureAwait(false);

        var snapshot = new ContextSnapshot(
            capturedAt, windows.Result, processes.Result, explorer.Result, git, desktop.Result, browser.Result, system.Result, screenText.Result);

        var markdown = new MarkdownFormatter(new Redactor(RedactionOptions.FromSettings(settings))).Format(snapshot);
        var limited = MarkdownFormatter.LimitOutput(markdown, settings.MaxOutputCharacters);
        return new CaptureResult(snapshot, limited, markdown.Length) { OutputLimit = settings.MaxOutputCharacters };
    }

    /// <summary>Per-window timeouts are enforced inside the collector; this is only a backstop for the whole collector.</summary>
    internal static TimeSpan ScreenTextBudget(AppSettings settings)
    {
        var batches = (int)Math.Ceiling(settings.MaxScreenTextWindows / 3.0);
        var seconds = settings.ScreenTextMode == ScreenTextMode.FocusedWindow ? 1 : batches;
        return TimeSpan.FromSeconds(settings.ScreenTextTimeoutSeconds * seconds + 3);
    }

    /// <summary>Configured folders first, then caller-supplied folders, then open Explorer folders.</summary>
    internal static IReadOnlyList<string> BuildProjectCandidates(AppSettings settings, CaptureOptions options, CollectorResult<ExplorerFolder> explorer) =>
        settings.ProjectFolders.Select(PrivacyRules.ExpandPrefix)
            .Concat(options.ExtraProjectFolders)
            .Concat(explorer.Items.Select(f => f.Path))
            .Where(path => !string.IsNullOrWhiteSpace(path) && !PrivacyRules.IsPathExcluded(path, settings.ExcludedPathPrefixes))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>
    /// Runs one collector in isolation. Collector exceptions become a Failed or Unavailable result;
    /// only cancellation requested by the caller propagates.
    /// </summary>
    internal static async Task<CollectorResult<T>> RunAsync<T>(
        ICollector<T> collector, bool enabled, CollectorContext context, int maxItems, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!enabled)
            return CollectorResult<T>.Disabled(collector.Name);

        var stopwatch = Stopwatch.StartNew();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            // Collectors block (COM, Win32, child processes) and a hung one is abandoned after its
            // timeout, so each gets its own thread. The timeout is also waited for on a dedicated
            // thread rather than by a timer: a timer callback needs a free thread-pool thread, so a
            // busy pool would let a slow collector run past its timeout.
            var work = Task.Factory.StartNew(() => collector.Collect(context, timeoutSource.Token), timeoutSource.Token,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
            var finished = await Task.Factory.StartNew(
                    () => Task.WaitAny([work], (int)timeout.TotalMilliseconds, cancellationToken) >= 0,
                    CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)
                .ConfigureAwait(false);
            if (!finished)
                throw new TimeoutException();

            var output = await work.ConfigureAwait(false);

            var items = output.Items;
            var omitted = Math.Max(0, items.Count - maxItems);
            if (omitted > 0)
                items = items.Take(maxItems).ToArray();

            return new CollectorResult<T>(
                collector.Name,
                items.Count == 0 ? CollectorStatus.Empty : CollectorStatus.Ok,
                items,
                Error: null,
                output.Notes,
                omitted,
                output.ExcludedCount,
                stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            return CollectorResult<T>.FromFailure(collector.Name, CollectorStatus.Failed,
                $"Timed out after {timeout.TotalSeconds:0} seconds.", stopwatch.Elapsed);
        }
        catch (CollectorUnavailableException ex)
        {
            return CollectorResult<T>.FromFailure(collector.Name, CollectorStatus.Unavailable, ex.Message, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            // Collectors sit on unreliable OS boundaries (COM, Win32, child processes). One failing
            // collector must not prevent the rest of the snapshot.
            return CollectorResult<T>.FromFailure(collector.Name, CollectorStatus.Failed, Describe(ex), stopwatch.Elapsed);
        }
    }

    private static string Describe(Exception ex)
    {
        var message = ex.Message.Split('\n', 2)[0].Trim();
        return string.IsNullOrEmpty(message) ? ex.GetType().Name : $"{ex.GetType().Name}: {message}";
    }
}
