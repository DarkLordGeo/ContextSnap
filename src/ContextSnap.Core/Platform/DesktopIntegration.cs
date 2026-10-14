using System.Runtime.InteropServices;

namespace ContextSnap.Platform;

/// <summary>A system-wide hotkey bound to a window. It only notifies the window; it never monitors input.</summary>
public sealed class GlobalHotkey(nint windowHandle) : IDisposable
{
    public const int Id = 0xC511;
    public const int WmHotkey = 0x0312;
    public const string DisplayText = "Ctrl+Alt+Shift+C";

    private const uint ModAlt = 0x1;
    private const uint ModControl = 0x2;
    private const uint ModShift = 0x4;
    private const uint ModNoRepeat = 0x4000;
    private const uint VirtualKeyC = 0x43;

    public bool IsRegistered { get; private set; }

    public bool Register()
    {
        if (!IsRegistered)
            IsRegistered = NativeMethods.RegisterHotKey(windowHandle, Id, ModControl | ModAlt | ModShift | ModNoRepeat, VirtualKeyC);
        return IsRegistered;
    }

    public void Unregister()
    {
        if (!IsRegistered)
            return;

        NativeMethods.UnregisterHotKey(windowHandle, Id);
        IsRegistered = false;
    }

    public void Dispose() => Unregister();
}

public static class WindowAttention
{
    private const uint FlashAll = 0x3;
    private const uint FlashUntilForeground = 0xC;

    /// <summary>Flashes the taskbar button until the window is brought to the foreground.</summary>
    public static void Flash(nint windowHandle)
    {
        var info = new NativeMethods.FlashWindowInfo
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.FlashWindowInfo>(),
            Window = windowHandle,
            Flags = FlashAll | FlashUntilForeground,
            Count = 3,
        };
        NativeMethods.FlashWindowEx(ref info);
    }
}

public static class ConsoleAttachment
{
    private const int AttachParentProcess = -1;
    private const int StdOutputHandle = -11;

    /// <summary>
    /// For a GUI-subsystem executable started from a terminal: attaches to the parent console when
    /// standard output is not already redirected to a file or pipe.
    /// </summary>
    /// <returns>True when output was already redirected (a script or pipe is consuming it).</returns>
    public static bool AttachToParentIfNeeded()
    {
        var handle = NativeMethods.GetStdHandle(StdOutputHandle);
        if (handle != 0 && handle != -1)
            return true;

        NativeMethods.AttachConsole(AttachParentProcess);
        return false;
    }
}
