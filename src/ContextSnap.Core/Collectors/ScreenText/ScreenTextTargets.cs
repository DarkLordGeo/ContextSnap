using System.Runtime.InteropServices;
using ContextSnap.Configuration;
using ContextSnap.Platform;
using ContextSnap.Privacy;

namespace ContextSnap.Collectors.ScreenText;

/// <param name="Bounds">Visible window frame in physical screen pixels.</param>
/// <param name="IsBrowser">Only the page content of browser windows is read (never tabs or the address bar).</param>
public sealed record ScreenTextTarget(
    nint Handle, string ProcessName, string Title, int ProcessId, bool IsFocused, PixelRect Bounds, bool IsBrowser)
{
    /// <summary>Chromium or Electron window (for example, VS Code, Claude, Slack): the page document holds the app's content.</summary>
    public bool IsChromium { get; init; }
}

public sealed record TargetOptions(
    ScreenTextMode Mode,
    int MaxWindows,
    IReadOnlyCollection<string> ExcludedProcesses,
    IReadOnlyCollection<string> ExcludedTitleKeywords,
    IReadOnlyCollection<string> ScreenTextExclusions,
    int OwnProcessId)
{
    /// <summary>Windows less visible than this (covered by others) are skipped in all-windows mode.</summary>
    public const double MinVisibleFraction = 0.05;

    public static TargetOptions FromSettings(AppSettings settings, int ownProcessId) => new(
        settings.ScreenTextMode, settings.MaxScreenTextWindows, settings.ExcludedProcesses,
        settings.ExcludedTitleKeywords, settings.ScreenTextExclusions, ownProcessId);
}

public sealed record TargetSelection(IReadOnlyList<ScreenTextTarget> Targets, int ExcludedCount);

public static class ScreenTextTargetSelector
{
    public static readonly IReadOnlySet<string> Browsers = new HashSet<string>(KnownBrowsers.Names.Keys, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
    };

    /// <summary>
    /// Picks the windows to read. The focused window is the foreground window, or, when ContextSnap
    /// itself is in front (the Capture button was clicked), the most recently used other window.
    /// </summary>
    /// <param name="windowsInZOrder">Top-level windows, topmost first.</param>
    /// <param name="visibleFraction">Share of a window not covered by other windows (0–1).</param>
    public static TargetSelection Select(
        IReadOnlyList<RawWindow> windowsInZOrder,
        TargetOptions options,
        Func<RawWindow, PixelRect> boundsOf,
        Func<RawWindow, PixelRect, double> visibleFraction)
    {
        var eligible = new List<(RawWindow Window, PixelRect Bounds)>();
        var excluded = 0;
        foreach (var window in windowsInZOrder)
        {
            if (string.IsNullOrWhiteSpace(window.Title) || window.IsCloaked || window.IsToolWindow || window.IsMinimized
                || window.ProcessId == options.OwnProcessId || ShellClasses.Contains(window.ClassName))
                continue;

            var process = window.ProcessName ?? "unknown";
            if (IsExcluded(process, window.Title, options))
            {
                excluded++;
                continue;
            }

            var bounds = boundsOf(window);
            if (bounds.Width > 0 && bounds.Height > 0)
                eligible.Add((window, bounds));
        }

        if (eligible.Count == 0)
            return new TargetSelection([], excluded);

        var focusedIndex = eligible.FindIndex(e => e.Window.IsForeground);
        if (focusedIndex < 0)
            focusedIndex = 0; // ContextSnap (or nothing) is in front: use the most recently used window.

        var targets = new List<ScreenTextTarget> { ToTarget(eligible[focusedIndex], isFocused: true) };
        if (options.Mode == ScreenTextMode.AllVisibleWindows)
        {
            for (var i = 0; i < eligible.Count && targets.Count < options.MaxWindows; i++)
            {
                if (i == focusedIndex)
                    continue;
                var (window, bounds) = eligible[i];
                if (visibleFraction(window, bounds) >= TargetOptions.MinVisibleFraction)
                    targets.Add(ToTarget(eligible[i], isFocused: false));
            }
        }

        return new TargetSelection(targets, excluded);
    }

    internal static bool IsExcluded(string processName, string title, TargetOptions options) =>
        PrivacyRules.IsProcessExcluded(processName, options.ExcludedProcesses)
        || PrivacyRules.ContainsExcludedKeyword(title, options.ExcludedTitleKeywords)
        || PrivacyRules.IsProcessExcluded(processName, options.ScreenTextExclusions)
        || PrivacyRules.ContainsExcludedKeyword(title, options.ScreenTextExclusions);

    private static ScreenTextTarget ToTarget((RawWindow Window, PixelRect Bounds) entry, bool isFocused)
    {
        var process = entry.Window.ProcessName ?? "unknown";
        return new ScreenTextTarget(entry.Window.Handle, process, entry.Window.Title.Trim(), entry.Window.ProcessId,
            isFocused, entry.Bounds, Browsers.Contains(process))
        {
            IsChromium = entry.Window.ClassName.StartsWith("Chrome_WidgetWin", StringComparison.Ordinal),
        };
    }
}

/// <summary>Window geometry helpers backed by DWM and hit-testing.</summary>
public static class WindowGeometry
{
    private const int DwmwaExtendedFrameBounds = 9;
    private const uint GaRoot = 2;
    private const int Samples = 10;

    /// <summary>The visible frame (without the invisible resize border), in physical pixels.</summary>
    public static PixelRect GetVisibleBounds(nint window)
    {
        if (DwmGetWindowAttribute(window, DwmwaExtendedFrameBounds, out var rect, Marshal.SizeOf<Rect>()) == 0)
            return new PixelRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return ScreenPlacement.GetWindowBounds(window);
    }

    /// <summary>Estimates how much of the window is not covered, by hit-testing a grid of points.</summary>
    public static double EstimateVisibleFraction(nint window, PixelRect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return 0;

        var hits = 0;
        for (var row = 0; row < Samples; row++)
        {
            for (var column = 0; column < Samples; column++)
            {
                var point = new Point
                {
                    X = bounds.Left + (int)((column + 0.5) * bounds.Width / Samples),
                    Y = bounds.Top + (int)((row + 0.5) * bounds.Height / Samples),
                };
                var hit = WindowFromPoint(point);
                if (hit != 0 && GetAncestor(hit, GaRoot) == window)
                    hits++;
            }
        }

        return hits / (double)(Samples * Samples);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out Rect value, int size);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);
}
