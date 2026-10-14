using System.Text;
using ContextSnap.Model;
using ContextSnap.Platform;
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

/// <summary>Enumerates top-level windows through user32. Windows can disappear mid-enumeration; such entries are skipped.</summary>
public sealed class Win32WindowSource : IWindowSource
{
    private const int MaxTitleLength = 1024;

    public IReadOnlyList<RawWindow> GetWindows()
    {
        var handles = new List<nint>();
        NativeMethods.EnumWindows((handle, _) =>
        {
            handles.Add(handle);
            return true;
        }, 0);

        var foreground = NativeMethods.GetForegroundWindow();
        var processNames = new Dictionary<uint, string?>();
        var windows = new List<RawWindow>();

        foreach (var handle in handles)
        {
            if (!NativeMethods.IsWindowVisible(handle))
                continue;

            var title = GetTitle(handle);
            if (string.IsNullOrWhiteSpace(title))
                continue;

            NativeMethods.GetWindowThreadProcessId(handle, out var processId);
            if (processId == 0)
                continue; // Window was destroyed.

            if (!processNames.TryGetValue(processId, out var processName))
            {
                processName = ProcessInfoReader.GetProcessName((int)processId);
                processNames[processId] = processName;
            }

            var exStyle = NativeMethods.GetWindowLong(handle, NativeMethods.GwlExStyle);
            var isOwned = NativeMethods.GetWindow(handle, NativeMethods.GwOwner) != 0
                && (exStyle & NativeMethods.WsExAppWindow) == 0;

            windows.Add(new RawWindow(
                handle,
                title,
                (int)processId,
                processName,
                GetClassName(handle),
                IsCloaked(handle),
                (exStyle & NativeMethods.WsExToolWindow) != 0,
                isOwned,
                handle == foreground)
            {
                IsMinimized = NativeMethods.IsIconic(handle),
            });
        }

        return windows;
    }

    private static string GetTitle(nint handle)
    {
        var length = NativeMethods.GetWindowTextLength(handle);
        if (length <= 0)
            return string.Empty;

        var buffer = new StringBuilder(Math.Min(length, MaxTitleLength) + 1);
        NativeMethods.GetWindowText(handle, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string GetClassName(nint handle)
    {
        var buffer = new StringBuilder(256);
        return NativeMethods.GetClassName(handle, buffer, buffer.Capacity) > 0 ? buffer.ToString() : string.Empty;
    }

    private static bool IsCloaked(nint handle) =>
        NativeMethods.DwmGetWindowAttribute(handle, NativeMethods.DwmwaCloaked, out var cloaked, sizeof(int)) == 0
        && cloaked != 0;
}
