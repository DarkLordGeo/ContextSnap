using System.Runtime.InteropServices;

namespace ContextSnap.Platform;

/// <summary>A rectangle in physical screen pixels.</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

/// <summary>Positions small notification windows in physical pixels so per-monitor DPI and taskbars are respected.</summary>
public static class ScreenPlacement
{
    private static readonly nint TopmostWindow = -1;
    private const uint NoSize = 0x0001;
    private const uint NoActivate = 0x0010;
    private const uint NoOwnerZOrder = 0x0200;

    /// <summary>
    /// Bottom-right position for a window of <paramref name="width"/>×<paramref name="height"/> inside
    /// <paramref name="workArea"/> (the monitor area excluding taskbars), clamped so it never leaves the work area.
    /// </summary>
    public static (int X, int Y) BottomRight(PixelRect workArea, int width, int height, int margin)
    {
        var x = workArea.Right - width - margin;
        var y = workArea.Bottom - height - margin;
        return (Math.Max(workArea.Left, x), Math.Max(workArea.Top, y));
    }

    /// <summary>Work area of the monitor showing the foreground window, or the monitor under the cursor.</summary>
    public static PixelRect GetActiveWorkArea()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        var monitor = foreground != 0
            ? NativeMethods.MonitorFromWindow(foreground, NativeMethods.MonitorDefaultToNearest)
            : NativeMethods.GetCursorPos(out var cursor)
                ? NativeMethods.MonitorFromPoint(cursor, NativeMethods.MonitorDefaultToNearest)
                : NativeMethods.MonitorFromPoint(default, NativeMethods.MonitorDefaultToPrimary);

        var info = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (monitor == 0 || !NativeMethods.GetMonitorInfo(monitor, ref info))
            throw new InvalidOperationException("The active monitor could not be determined.");

        return new PixelRect(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom);
    }

    /// <summary>Moves (without resizing or activating) and keeps the window above normal windows.</summary>
    public static void MoveTopmost(nint window, int x, int y) =>
        NativeMethods.SetWindowPos(window, TopmostWindow, x, y, 0, 0, NoSize | NoActivate | NoOwnerZOrder);

    public static PixelRect GetWindowBounds(nint window) =>
        NativeMethods.GetWindowRect(window, out var rect)
            ? new PixelRect(rect.Left, rect.Top, rect.Right, rect.Bottom)
            : default;

    /// <summary>DPI scale factor (1.0 = 96 DPI) of the monitor the window is on.</summary>
    public static double GetScale(nint window)
    {
        var dpi = NativeMethods.GetDpiForWindow(window);
        return dpi > 0 ? dpi / 96.0 : 1.0;
    }

    /// <summary>Marks a window as a non-activating tool window: it never takes focus or appears in Alt+Tab.</summary>
    public static void MakeNonActivating(nint window)
    {
        var style = NativeMethods.GetWindowLong(window, NativeMethods.GwlExStyle);
        NativeMethods.SetWindowLong(window, NativeMethods.GwlExStyle,
            style | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow);
    }
}
