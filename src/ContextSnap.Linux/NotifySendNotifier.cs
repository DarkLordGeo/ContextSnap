using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using ContextSnap.Notifications;

namespace ContextSnap.Linux;

/// <summary>
/// Shows the copy notification through the desktop's notification service with <c>notify-send</c>.
/// The content holds only counts and fixed wording, never snapshot text.
/// </summary>
public sealed class NotifySendNotifier(string path) : ICopyNotifier
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    /// <summary>Null when notify-send is not installed or there is no graphical session.</summary>
    public static NotifySendNotifier? CreateIfAvailable() =>
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
            ? null
            : ToolRunner.Find("notify-send") is { } path ? new NotifySendNotifier(path) : null;

    public static IReadOnlyList<string> BuildArguments(CopyNotificationContent content)
    {
        var body = string.Join("\n", new[] { content.Message, string.Join(" · ", content.Stats), content.Warning }
            .Where(line => !string.IsNullOrWhiteSpace(line)));
        return
        [
            "--app-name=ContextSnap",
            "--icon=" + (content.Kind == CopyNotificationKind.Success ? "edit-copy" : "dialog-warning"),
            "--urgency=" + (content.Kind == CopyNotificationKind.Error ? "critical" : "low"),
            "--expire-time=" + ((int)content.Duration.TotalMilliseconds).ToString(CultureInfo.InvariantCulture),
            "--",
            content.Title,
            body,
        ];
    }

    public void Show(CopyNotificationContent content)
    {
        var start = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in BuildArguments(content))
            start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
            process.StandardInput.Close();
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException($"notify-send could not be started ({ex.NativeErrorCode}).", ex);
        }

        if (!process.WaitForExit(Timeout))
            ToolRunner.TryKill(process);
    }
}
