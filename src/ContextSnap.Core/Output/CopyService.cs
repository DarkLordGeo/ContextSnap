using ContextSnap.Notifications;

namespace ContextSnap.Output;

/// <param name="WasTruncated">The snapshot was shortened by the output limit when it was captured.</param>
/// <param name="ShowNotification">The user's "Copy notifications" setting.</param>
/// <param name="Detail">Optional extra line for the success notification (counts only, never content).</param>
public sealed record CopyRequest(string Text, bool ExcludeFromHistory, bool WasTruncated, int OutputLimit, bool ShowNotification, string? Detail = null);

public enum CopyStatus
{
    Copied,
    Empty,
    Failed,
}

/// <param name="NotificationError">Set when the notification could not be shown; the copy result is unaffected.</param>
public sealed record CopyOutcome(
    CopyStatus Status,
    string? Error,
    TextStatistics? Statistics,
    CopyNotificationContent Notification,
    string? NotificationError = null)
{
    public bool Succeeded => Status == CopyStatus.Copied;
}

/// <summary>
/// Copies text to the clipboard first, then (optionally) reports the result through a notifier.
/// The notification never delays the clipboard write and can never turn a successful copy into a failure.
/// </summary>
public sealed class CopyService(IClipboardWriter clipboard, ICopyNotifier notifier)
{
    public const string EmptyError = "Nothing to copy. Capture a snapshot first.";

    public CopyOutcome Copy(CopyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var outcome = WriteClipboard(request);
        if (!request.ShowNotification)
            return outcome;

        try
        {
            notifier.Show(outcome.Notification);
            return outcome;
        }
        catch (Exception ex)
        {
            // Notification failures are cosmetic; the clipboard already holds the result.
            return outcome with { NotificationError = $"{ex.GetType().Name}: {ex.Message}" };
        }
    }

    private CopyOutcome WriteClipboard(CopyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return new CopyOutcome(CopyStatus.Empty, EmptyError, null, CopyNotifications.ForEmpty());

        var result = clipboard.SetText(request.Text, request.ExcludeFromHistory);
        if (!result.Success)
            return new CopyOutcome(CopyStatus.Failed, result.Error, null, CopyNotifications.ForFailure(result.Error));

        var statistics = TextStatistics.Measure(request.Text);
        return new CopyOutcome(
            CopyStatus.Copied,
            null,
            statistics,
            CopyNotifications.ForSuccess(statistics, request.WasTruncated, request.OutputLimit, request.Detail));
    }
}
