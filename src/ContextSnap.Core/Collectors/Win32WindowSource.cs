using System.Text;
using ContextSnap.Platform;

namespace ContextSnap.Collectors;

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
