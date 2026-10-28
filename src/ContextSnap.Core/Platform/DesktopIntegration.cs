using System.Runtime.InteropServices;

namespace ContextSnap.Platform;

/// <summary>A system-wide hotkey bound to a window. It only notifies the window; it never monitors input.</summary>
public sealed class GlobalHotkey(nint windowHandle) : IDisposable
{
    public const int Id = 0xC511;
    public const int WmHotkey = 0x0312;

    private const uint ModNoRepeat = 0x4000;

    public bool IsRegistered { get; private set; }

    /// <summary>The shortcut currently registered, or null.</summary>
    public HotkeyGesture? Current { get; private set; }

    /// <summary>Registers <paramref name="gesture"/>, replacing any previous shortcut. False when another app owns it.</summary>
    public bool Register(HotkeyGesture gesture)
    {
        if (IsRegistered && gesture == Current)
            return true;

        Unregister();
        IsRegistered = NativeMethods.RegisterHotKey(windowHandle, Id, gesture.Modifiers | ModNoRepeat, gesture.VirtualKey);
        Current = IsRegistered ? gesture : null;
        return IsRegistered;
    }

    public void Unregister()
    {
        if (!IsRegistered)
            return;

        NativeMethods.UnregisterHotKey(windowHandle, Id);
        IsRegistered = false;
        Current = null;
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
