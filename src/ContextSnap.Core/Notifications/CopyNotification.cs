using System.Globalization;
using System.Text;
using ContextSnap.Formatting;

namespace ContextSnap.Notifications;

public enum CopyNotificationKind
{
    Success,
    Warning,
    Error,
}

/// <summary>
/// Everything a copy notification displays. Built only from counts and fixed wording, never from
/// snapshot text, so captured titles, paths, URLs, or secrets cannot appear in a notification.
/// </summary>
/// <param name="Stats">Short statistics such as "8,420 characters", shown in order.</param>
/// <param name="Warning">Highlighted secondary line, for example a truncation notice.</param>
public sealed record CopyNotificationContent(
    CopyNotificationKind Kind,
    string Title,
    string? Message,
    IReadOnlyList<string> Stats,
    string? Warning,
    TimeSpan Duration);

/// <summary>Character, word, and UTF-8 byte counts of copied text.</summary>
public readonly record struct TextStatistics(int Characters, int Words, long Bytes)
{
    /// <summary>
    /// Words are whitespace-separated tokens containing at least one letter or digit, so Markdown
    /// markers such as "-", "#", or "|" are not counted.
    /// </summary>
    public static TextStatistics Measure(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var words = 0;
        var inToken = false;
        var tokenHasWordCharacter = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (inToken && tokenHasWordCharacter)
                    words++;
                inToken = false;
                tokenHasWordCharacter = false;
                continue;
            }

            inToken = true;
            tokenHasWordCharacter |= char.IsLetterOrDigit(c);
        }

        if (inToken && tokenHasWordCharacter)
            words++;

        return new TextStatistics(text.Length, words, Encoding.UTF8.GetByteCount(text));
    }
}

/// <summary>Builds notification content for each copy outcome.</summary>
public static class CopyNotifications
{
    public static readonly TimeSpan SuccessDuration = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan TruncatedDuration = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan EmptyDuration = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan ErrorDuration = TimeSpan.FromSeconds(6);

    public const string SuccessTitle = "Context copied to clipboard";
    public const string ErrorTitle = "Copy failed";
    public const string EmptyTitle = "Nothing copied";

    private const int MaxErrorLength = 140;
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static CopyNotificationContent ForSuccess(TextStatistics statistics, bool wasTruncated, int outputLimit, string? detail = null)
    {
        string? warning = null;
        if (wasTruncated)
        {
            warning = outputLimit > 0
                ? $"Snapshot truncated to the {outputLimit.ToString("N0", Culture)}-character limit."
                : "Snapshot truncated by the output limit.";
        }

        return new CopyNotificationContent(
            CopyNotificationKind.Success,
            SuccessTitle,
            Message: string.IsNullOrWhiteSpace(detail) ? null : MarkdownText.SingleLine(detail, MaxErrorLength),
            FormatStatistics(statistics),
            warning,
            wasTruncated ? TruncatedDuration : SuccessDuration);
    }

    public static CopyNotificationContent ForFailure(string? error)
    {
        var message = MarkdownText.SingleLine(error, MaxErrorLength);
        return new CopyNotificationContent(
            CopyNotificationKind.Error,
            ErrorTitle,
            message.Length > 0 ? message : "The clipboard could not be written. Try again.",
            [],
            Warning: null,
            ErrorDuration);
    }

    public static CopyNotificationContent ForEmpty() => new(
        CopyNotificationKind.Warning,
        EmptyTitle,
        "The snapshot is empty. Capture a snapshot first.",
        [],
        Warning: null,
        EmptyDuration);

    public static IReadOnlyList<string> FormatStatistics(TextStatistics statistics) =>
    [
        Count(statistics.Characters, "character"),
        Count(statistics.Words, "word"),
        MarkdownFormatter.FormatSize(statistics.Bytes),
    ];

    private static string Count(int value, string noun) =>
        $"{value.ToString("N0", Culture)} {noun}{(value == 1 ? string.Empty : "s")}";
}

/// <summary>Displays copy notifications. Implementations must return quickly and never block clipboard work.</summary>
public interface ICopyNotifier
{
    void Show(CopyNotificationContent content);
}

public sealed class NullCopyNotifier : ICopyNotifier
{
    public static NullCopyNotifier Instance { get; } = new();

    public void Show(CopyNotificationContent content)
    {
    }
}

/// <summary>Keeps the most recent notification so it can be shown later (used by the command-line mode).</summary>
public sealed class DeferredCopyNotifier : ICopyNotifier
{
    public CopyNotificationContent? Last { get; private set; }

    public void Show(CopyNotificationContent content) => Last = content;
}

/// <summary>A single on-screen notification surface.</summary>
public interface IToastView
{
    /// <summary>True from the moment the toast appears until its dismissal has finished.</summary>
    bool IsShowing { get; }

    /// <summary>Shows the toast with its entrance animation.</summary>
    void Present(CopyNotificationContent content);

    /// <summary>Updates the toast already on screen in place and restarts its timer.</summary>
    void Refresh(CopyNotificationContent content);
}

/// <summary>Ensures repeated copies update one toast instead of stacking new ones.</summary>
public sealed class ToastController(IToastView view) : ICopyNotifier
{
    public void Show(CopyNotificationContent content)
    {
        if (view.IsShowing)
            view.Refresh(content);
        else
            view.Present(content);
    }
}
