using ContextSnap.Model;
using ContextSnap.Privacy;

namespace ContextSnap.Collectors;

/// <summary>A top-level window as reported by Windows, before filtering.</summary>
public sealed record RawWindow(
    nint Handle,
    string Title,
    int ProcessId,
    string? ProcessName,
    string ClassName,
    bool IsCloaked,
    bool IsToolWindow,
    bool IsOwnedPopup,
    bool IsForeground)
{
    public bool IsMinimized { get; init; }
}

public interface IWindowSource
{
    /// <summary>Visible top-level windows with a title, in z-order (topmost first).</summary>
    IReadOnlyList<RawWindow> GetWindows();
}

public sealed record WindowFilterOptions(
    IReadOnlyCollection<string> ExcludedProcesses,
    IReadOnlyCollection<string> ExcludedTitleKeywords,
    int OwnProcessId);

public static class WindowFilter
{
    private static readonly HashSet<string> ShellWindowClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
        "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland",
    };

    /// <summary>Keeps windows a user would see in Alt+Tab, drops excluded and duplicate entries.</summary>
    public static (IReadOnlyList<WindowInfo> Windows, int ExcludedCount) Select(IEnumerable<RawWindow> windows, WindowFilterOptions options)
    {
        var result = new List<WindowInfo>();
        var seen = new HashSet<(string, string)>();
        var excluded = 0;

        foreach (var window in windows)
        {
            var title = window.Title.Trim();
            if (title.Length == 0
                || window.IsCloaked
                || window.IsToolWindow
                || window.IsOwnedPopup
                || window.ProcessId == options.OwnProcessId
                || ShellWindowClasses.Contains(window.ClassName))
                continue;

            var processName = string.IsNullOrWhiteSpace(window.ProcessName) ? "unknown" : window.ProcessName;
            if (PrivacyRules.IsProcessExcluded(processName, options.ExcludedProcesses)
                || PrivacyRules.ContainsExcludedKeyword(title, options.ExcludedTitleKeywords))
            {
                excluded++;
                continue;
            }

            if (seen.Add((processName.ToLowerInvariant(), title)))
                result.Add(new WindowInfo(processName, title, window.ProcessId, window.IsForeground));
        }

        return (result, excluded);
    }
}

public sealed class WindowCollector(IWindowSource source, int ownProcessId) : ICollector<WindowInfo>
{
    public string Name => "Open windows";

    public CollectorOutput<WindowInfo> Collect(CollectorContext context, CancellationToken cancellationToken)
    {
        var settings = context.Settings;
        var options = new WindowFilterOptions(settings.ExcludedProcesses, settings.ExcludedTitleKeywords, ownProcessId);
        var (windows, excluded) = WindowFilter.Select(source.GetWindows(), options);
        return new CollectorOutput<WindowInfo>(windows, ExcludedCount: excluded);
    }
}
