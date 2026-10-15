using System.Runtime.InteropServices;

namespace ContextSnap.Linux;

/// <summary>
/// A system-wide X11 key grab (libX11 <c>XGrabKey</c> on the root window). Wayland does not let apps
/// grab keys, so this is used only in X11 sessions. Only the grabbed key combination is ever delivered;
/// no other keyboard input is seen.
/// </summary>
public sealed class X11Hotkey : IDisposable
{
    public const string DisplayText = "Ctrl+Alt+Shift+C";

    private const int KeyPress = 2;
    private const int BadAccess = 10;
    private const int GrabModeAsync = 1;
    private const uint ShiftMask = 1, LockMask = 2, ControlMask = 4, Mod1Mask = 8, Mod2Mask = 16;
    private const nint KeysymC = 0x63;
    private const int XEventSize = 192; // sizeof(XEvent) on 64-bit: 24 longs.

    private static readonly XErrorHandler ErrorHandler = OnError; // Kept alive for libX11.
    private static int _lastError;

    private readonly nint _display;
    private readonly nint _root;
    private readonly int _keycode;
    private readonly nint _event = Marshal.AllocHGlobal(XEventSize);

    private X11Hotkey(nint display, nint root, int keycode)
    {
        _display = display;
        _root = root;
        _keycode = keycode;
    }

    /// <summary>Caps Lock and Num Lock change the modifier state, so the combination is grabbed with each of them.</summary>
    internal static IReadOnlyList<uint> Modifiers { get; } =
        [.. new[] { 0u, LockMask, Mod2Mask, LockMask | Mod2Mask }.Select(extra => ControlMask | Mod1Mask | ShiftMask | extra)];

    /// <summary>Grabs Ctrl+Alt+Shift+C. Returns null with a reason when the display or the key is unavailable.</summary>
    public static (X11Hotkey? Hotkey, string? Error) Register()
    {
        nint display;
        try
        {
            display = XOpenDisplay(0);
        }
        catch (DllNotFoundException)
        {
            return (null, "libX11 was not found.");
        }

        if (display == 0)
            return (null, "Could not connect to the X11 display.");

        XSetErrorHandler(Marshal.GetFunctionPointerForDelegate(ErrorHandler));
        var root = XDefaultRootWindow(display);
        var keycode = XKeysymToKeycode(display, KeysymC);
        _lastError = 0;
        foreach (var modifiers in Modifiers)
            XGrabKey(display, keycode, modifiers, root, 0, GrabModeAsync, GrabModeAsync);
        XSync(display, 0);

        var hotkey = new X11Hotkey(display, root, keycode);
        if (_lastError == 0)
            return (hotkey, null);

        hotkey.Dispose();
        return (null, _lastError == BadAccess
            ? $"{DisplayText} is already used by another application or your desktop."
            : $"{DisplayText} could not be registered (X11 error {_lastError}).");
    }

    /// <summary>Blocks until the shortcut is pressed. Key repeats queued while the caller worked are discarded.</summary>
    public void WaitForPress()
    {
        while (XPending(_display) > 0)
            XNextEvent(_display, _event);

        while (true)
        {
            XNextEvent(_display, _event);
            if (Marshal.ReadInt32(_event) == KeyPress)
                return;
        }
    }

    public void Dispose()
    {
        foreach (var modifiers in Modifiers)
            XUngrabKey(_display, _keycode, modifiers, _root);
        XCloseDisplay(_display);
        Marshal.FreeHGlobal(_event);
    }

    private static int OnError(nint display, nint errorEvent)
    {
        // XErrorEvent: int type; Display*; XID resourceid; unsigned long serial; unsigned char error_code.
        _lastError = Marshal.ReadByte(errorEvent, 32);
        return 0;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int XErrorHandler(nint display, nint errorEvent);

    private const string Lib = "libX11.so.6";

    [DllImport(Lib)] private static extern nint XOpenDisplay(nint name);
    [DllImport(Lib)] private static extern int XCloseDisplay(nint display);
    [DllImport(Lib)] private static extern nint XDefaultRootWindow(nint display);
    [DllImport(Lib)] private static extern byte XKeysymToKeycode(nint display, nint keysym);
    [DllImport(Lib)] private static extern int XGrabKey(nint display, int keycode, uint modifiers, nint grabWindow, int ownerEvents, int pointerMode, int keyboardMode);
    [DllImport(Lib)] private static extern int XUngrabKey(nint display, int keycode, uint modifiers, nint grabWindow);
    [DllImport(Lib)] private static extern int XSync(nint display, int discard);
    [DllImport(Lib)] private static extern int XPending(nint display);
    [DllImport(Lib)] private static extern int XNextEvent(nint display, nint eventReturn);
    [DllImport(Lib)] private static extern nint XSetErrorHandler(nint handler);
}
