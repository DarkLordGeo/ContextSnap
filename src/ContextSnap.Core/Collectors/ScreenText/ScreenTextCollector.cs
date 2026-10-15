using System.Diagnostics;
using System.Globalization;
using ContextSnap.Configuration;
using ContextSnap.Model;
using ContextSnap.Platform;

namespace ContextSnap.Collectors.ScreenText;

/// <summary>
/// Reads the text visible in application windows: UI Automation first, then on-device OCR when an
/// app exposes too little. Each window has its own timeout; one slow or failing window never blocks
/// the others or the rest of the snapshot. Nothing read here is logged or written to disk.
/// </summary>
public sealed class ScreenTextCollector(
    IWindowSource windows,
    IUiaTextReader uia,
    IOcrTextReader ocr,
    int ownProcessId,
    Func<RawWindow, PixelRect>? boundsOf = null,
    Func<RawWindow, PixelRect, double>? visibleFraction = null) : ICollector<WindowText>
{
    /// <summary>
    /// OCR is tried when UI Automation returns fewer than this many characters per megapixel of window,
    /// clamped to 20–200, so a small dialog with complete accessible text is not re-read with OCR.
    /// </summary>
    public const int MinUiaCharactersBeforeOcr = 200;
    private const int MinOcrThreshold = 20;
    private const int MaxParallelWindows = 3;

    private readonly Func<RawWindow, PixelRect> _boundsOf = boundsOf ?? (w => WindowGeometry.GetVisibleBounds(w.Handle));
    private readonly Func<RawWindow, PixelRect, double> _visibleFraction = visibleFraction ?? ((w, b) => WindowGeometry.EstimateVisibleFraction(w.Handle, b));

    public string Name => "Visible screen text";

    public CollectorOutput<WindowText> Collect(CollectorContext context, CancellationToken cancellationToken)
    {
        var settings = context.Settings;
        var selection = ScreenTextTargetSelector.Select(
            windows.GetWindows(), TargetOptions.FromSettings(settings, ownProcessId), _boundsOf, _visibleFraction);

        var notes = new List<string>();
        if (selection.Targets.Count == 0)
        {
            notes.Add(selection.ExcludedCount > 0
                ? "The windows on screen are excluded by your privacy settings."
                : "No application window was available to read.");
            return new CollectorOutput<WindowText>([], notes, selection.ExcludedCount);
        }

        var timeout = TimeSpan.FromSeconds(settings.ScreenTextTimeoutSeconds);
        var results = new WindowText[selection.Targets.Count];
        Parallel.For(0, selection.Targets.Count,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelWindows, CancellationToken = cancellationToken },
            i => results[i] = ReadWithTimeout(selection.Targets[i], settings, timeout, cancellationToken));

        // Keep the focused window even when empty (its warnings explain why); drop other empty windows.
        var kept = results.Where(r => r.IsFocused || r.Text.Length > 0).ToArray();
        var emptyOthers = results.Length - kept.Length;
        if (emptyOthers > 0)
            notes.Add($"{emptyOthers} other visible window(s) had no readable text.");

        return kept.All(r => r.Text.Length == 0)
            ? new CollectorOutput<WindowText>([], notes.Concat(kept.SelectMany(r => r.Warnings.Select(w => $"{r.ProcessName}: {w}"))).ToArray(), selection.ExcludedCount)
            : new CollectorOutput<WindowText>(kept, notes, selection.ExcludedCount);
    }

    private WindowText ReadWithTimeout(ScreenTextTarget target, AppSettings settings, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var windowCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        windowCancellation.CancelAfter(timeout);
        var started = Stopwatch.StartNew();
        var timedOut = string.Create(CultureInfo.InvariantCulture, $"Timed out after {timeout.TotalSeconds:0} s; the app did not respond in time.");

        // UI Automation calls cannot be interrupted, so a hung app is abandoned rather than waited on.
        // Each read gets its own thread and is waited on synchronously, so a hung app never holds a
        // thread-pool thread and the timeout does not depend on the pool having a free thread.
        var read = Task.Factory.StartNew(() => ReadWindow(target, settings, windowCancellation.Token),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        if (Task.WaitAny([read], (int)timeout.TotalMilliseconds, cancellationToken) < 0)
            return Empty(target, started.Elapsed, timedOut);

        try
        {
            return read.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Empty(target, started.Elapsed, timedOut);
        }
    }

    internal WindowText ReadWindow(ScreenTextTarget target, AppSettings settings, CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var warnings = new List<string>();
        var maxCharacters = settings.MaxScreenTextCharactersPerWindow;

        IReadOnlyList<string> uiaLines = [];
        PixelRect? contentBounds = null;
        try
        {
            var read = uia.Read(target, maxCharacters * 2, cancellationToken);
            contentBounds = read.ContentBounds;
            if (read.Warning is not null)
                warnings.Add(read.Warning);
            if (read.Root is not null)
            {
                var assembled = VisibleTextAssembler.Assemble(read.Root,
                    new AssemblyOptions(contentBounds ?? target.Bounds, settings.ScreenTextIncludeOffscreen, maxCharacters * 2));
                uiaLines = Lines(assembled.Text);
                if (assembled.SkippedPasswordFields > 0)
                    warnings.Add($"{assembled.SkippedPasswordFields} password field(s) were skipped.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Accessibility providers are third-party code; any failure falls back to OCR.
            warnings.Add($"UI Automation failed ({ex.GetType().Name}).");
        }

        var uiaCharacters = uiaLines.Sum(l => l.Length);
        IReadOnlyList<string> lines = uiaLines;
        var ocrCharacters = 0;

        if (ShouldRunOcr(uiaCharacters, settings.ScreenTextUseOcr, contentBounds ?? target.Bounds))
        {
            var (ocrLines, warning) = ReadOcr(target, contentBounds, cancellationToken);
            if (warning is not null)
                warnings.Add(warning);
            if (ocrLines.Count > 0)
                (lines, ocrCharacters) = OcrLayout.MergeWithUia(uiaLines, ocrLines);
        }

        var (text, truncated) = VisibleTextAssembler.Limit(lines, maxCharacters);
        var source = (uiaCharacters > 0 ? ScreenTextSource.UiAutomation : ScreenTextSource.None)
                     | (ocrCharacters > 0 ? ScreenTextSource.Ocr : ScreenTextSource.None);
        if (text.Length == 0 && warnings.Count == 0)
            warnings.Add("No text was found in the visible part of this window.");

        return new WindowText(target.ProcessName, target.Title, target.IsFocused, source, text,
            Math.Min(uiaCharacters, text.Length), Math.Min(ocrCharacters, text.Length), truncated, warnings, started.Elapsed);
    }

    internal static bool ShouldRunOcr(int uiaCharacters, bool ocrEnabled, PixelRect area)
    {
        if (!ocrEnabled)
            return false;
        var megapixels = Math.Max(0, (long)area.Width * area.Height) / 1_000_000.0;
        var threshold = Math.Clamp((int)Math.Round(MinUiaCharactersBeforeOcr * megapixels), MinOcrThreshold, MinUiaCharactersBeforeOcr);
        return uiaCharacters < threshold;
    }

    private (IReadOnlyList<string> Lines, string? Warning) ReadOcr(ScreenTextTarget target, PixelRect? contentBounds, CancellationToken cancellationToken)
    {
        if (ocr.UnavailableReason is { } reason)
            return ([], reason);

        if (target.IsBrowser && contentBounds is null)
            return ([], "OCR skipped: the page area could not be located, and browser tabs and address bar are never read.");

        try
        {
            var result = ocr.Read(target, contentBounds ?? target.Bounds, cancellationToken);
            return (OcrLayout.Order(result.Lines), result.Warning);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // OCR depends on graphics capture and a WinRT engine; a failure must not lose UI Automation text.
            return ([], $"OCR failed ({ex.GetType().Name}).");
        }
    }

    private static WindowText Empty(ScreenTextTarget target, TimeSpan elapsed, string warning) =>
        new(target.ProcessName, target.Title, target.IsFocused, ScreenTextSource.None, string.Empty, 0, 0, false, [warning], elapsed);

    private static string[] Lines(string text) =>
        text.Length == 0 ? [] : text.Split('\n');
}
