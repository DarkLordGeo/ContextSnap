using ContextSnap.Collectors.ScreenText;
using ContextSnap.Configuration;
using ContextSnap.Notifications;
using ContextSnap.Output;
using ContextSnap.Platform;

namespace ContextSnap.Capture;

public enum HotkeyCaptureStatus
{
    Copied,
    CopyFailed,
    CaptureFailed,

    /// <summary>The shortcut was pressed while a previous capture was still running; the press was ignored.</summary>
    Busy,
}

public sealed record HotkeyCaptureOutcome(HotkeyCaptureStatus Status, CaptureResult? Capture, CopyOutcome? Copy, string? Error);

/// <summary>
/// The global-shortcut workflow: capture, then copy the complete Markdown (after redaction and output
/// limits) straight to the clipboard. It has no UI: it never opens a window or asks for review.
/// Failures are written to the diagnostics log and, if copy notifications are on, shown as a small notice.
/// </summary>
public sealed class HotkeyCaptureWorkflow(
    Func<AppSettings, Task<CaptureResult>> capture,
    CopyService copy,
    ICopyNotifier notifier,
    IDiagnosticsLog log)
{
    private int _running;

    public async Task<HotkeyCaptureOutcome> RunAsync(AppSettings settings)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
            return new HotkeyCaptureOutcome(HotkeyCaptureStatus.Busy, null, null, null);

        try
        {
            CaptureResult result;
            try
            {
                result = await capture(settings).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Capture runs third-party accessibility code; a failure must never reach the UI as a dialog.
                log.Error("Shortcut capture failed", ex);
                if (settings.ShowCopyNotifications)
                    TryNotify(CopyNotifications.ForFailure($"Capture failed ({ex.GetType().Name}). Details are in the Snapctx diagnostics log."));
                return new HotkeyCaptureOutcome(HotkeyCaptureStatus.CaptureFailed, null, null, $"{ex.GetType().Name}: {ex.Message}");
            }

            var outcome = copy.Copy(new CopyRequest(
                result.Markdown,
                settings.ExcludeFromClipboardHistory,
                result.WasTruncated,
                result.OutputLimit,
                settings.ShowCopyNotifications,
                ScreenTextSummary.CopyDetail(result.Snapshot.ScreenText)));

            if (!outcome.Succeeded)
                log.Warning($"Shortcut copy failed: {outcome.Error}");
            if (outcome.NotificationError is not null)
                log.Warning($"Copy notification could not be shown: {outcome.NotificationError}");

            return new HotkeyCaptureOutcome(
                outcome.Succeeded ? HotkeyCaptureStatus.Copied : HotkeyCaptureStatus.CopyFailed, result, outcome, outcome.Error);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private void TryNotify(CopyNotificationContent content)
    {
        try
        {
            notifier.Show(content);
        }
        catch (Exception ex)
        {
            log.Warning($"Failure notification could not be shown: {ex.GetType().Name}");
        }
    }
}
