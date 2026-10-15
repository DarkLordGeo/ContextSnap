using System.Runtime.InteropServices;

namespace ContextSnap.Platform;

public enum TrayCommand
{
    None,
    Open,
    Capture,
    Exit,
}

/// <summary>
/// The ContextSnap icon in the Windows notification area (Shell_NotifyIcon, version 4). Clicks and the
/// context menu arrive as <see cref="CallbackMessage"/> on the owner window; pass them to
/// <see cref="HandleMessage"/>. The icon is re-added when Explorer restarts.
/// </summary>
public sealed class NotificationAreaIcon(nint windowHandle, string tooltip) : IDisposable
{
    public const int CallbackMessage = 0x8000 + 0x51; // WM_APP + 0x51

    private const int NimAdd = 0, NimModify = 1, NimDelete = 2, NimSetVersion = 4;
    private const int NifMessage = 0x1, NifIcon = 0x2, NifTip = 0x4, NifInfo = 0x10, NifShowTip = 0x80;
    private const int NotifyIconVersion4 = 4;
    private const int NiifInfo = 0x1, NiifRespectQuietTime = 0x80;
    private const int NinSelect = 0x400, NinKeySelect = 0x401, WmContextMenu = 0x7B;
    private const uint MfString = 0x0, MfSeparator = 0x800;
    private const uint TpmRightButton = 0x2, TpmReturnCmd = 0x100, TpmNoNotify = 0x80;
    private const uint IconId = 1;

    private static readonly int TaskbarCreated = RegisterWindowMessageW("TaskbarCreated");

    private nint _icon;
    private bool _added;

    public bool IsAdded => _added;

    /// <summary>Adds the icon. False when the notification area is unavailable (for example, Explorer is not running).</summary>
    public bool Add()
    {
        if (_icon == 0)
            _icon = LoadAppIcon();

        var data = Data(NifMessage | NifIcon | NifTip | NifShowTip);
        _added = ShellNotifyIconW(NimAdd, ref data);
        if (_added)
        {
            data.uVersionOrTimeout = NotifyIconVersion4;
            ShellNotifyIconW(NimSetVersion, ref data);
        }

        return _added;
    }

    /// <summary>A short system notification from the icon; never contains snapshot text.</summary>
    public void ShowInfo(string title, string text)
    {
        if (!_added)
            return;

        var data = Data(NifInfo);
        data.szInfoTitle = title;
        data.szInfo = text;
        data.dwInfoFlags = NiifInfo | NiifRespectQuietTime;
        ShellNotifyIconW(NimModify, ref data);
    }

    /// <summary>
    /// Handles the icon's callback and Explorer restarts. Returns the command the user chose, or
    /// <see cref="TrayCommand.None"/>; <paramref name="handled"/> is set for messages that belong to the icon.
    /// </summary>
    public TrayCommand HandleMessage(int message, nint wParam, nint lParam, string captureLabel, out bool handled)
    {
        handled = false;
        if (message == TaskbarCreated && TaskbarCreated != 0)
        {
            Add(); // Explorer restarted and lost its icons.
            return TrayCommand.None;
        }

        if (message != CallbackMessage)
            return TrayCommand.None;

        handled = true;
        return (int)(lParam & 0xFFFF) switch
        {
            NinSelect or NinKeySelect => TrayCommand.Open,
            WmContextMenu => ShowMenu((short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF), captureLabel),
            _ => TrayCommand.None,
        };
    }

    private TrayCommand ShowMenu(int x, int y, string captureLabel)
    {
        var menu = CreatePopupMenu();
        if (menu == 0)
            return TrayCommand.None;

        try
        {
            AppendMenuW(menu, MfString, (nuint)TrayCommand.Open, "&Open ContextSnap");
            AppendMenuW(menu, MfString, (nuint)TrayCommand.Capture, captureLabel);
            AppendMenuW(menu, MfSeparator, 0, null);
            AppendMenuW(menu, MfString, (nuint)TrayCommand.Exit, "E&xit");
            SetMenuDefaultItem(menu, (uint)TrayCommand.Open, 0);

            // Required so the menu closes when the user clicks elsewhere.
            SetForegroundWindow(windowHandle);
            var chosen = TrackPopupMenuEx(menu, TpmRightButton | TpmReturnCmd | TpmNoNotify, x, y, windowHandle, 0);
            PostMessageW(windowHandle, 0, 0, 0); // WM_NULL, per the TrackPopupMenu documentation.
            return Enum.IsDefined((TrayCommand)chosen) ? (TrayCommand)chosen : TrayCommand.None;
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = Data(0);
            ShellNotifyIconW(NimDelete, ref data);
            _added = false;
        }

        if (_icon != 0)
        {
            DestroyIcon(_icon);
            _icon = 0;
        }
    }

    private NotifyIconData Data(int flags) => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(),
        hWnd = windowHandle,
        uID = IconId,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = tooltip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    /// <summary>The small icon embedded in ContextSnap.exe, or the standard application icon.</summary>
    private static nint LoadAppIcon()
    {
        if (Environment.ProcessPath is { } path && ExtractIconExW(path, 0, 0, out var small, 1) > 0 && small != 0)
            return small;
        return CopyIcon(LoadIconW(0, 32512)); // IDI_APPLICATION; copied so DestroyIcon is always valid.
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public nint hWnd;
        public uint uID;
        public int uFlags;
        public int uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uVersionOrTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIconW(int message, ref NotifyIconData data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(string file, int index, nint large, out nint small, uint icons);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessageW(string name);

    [DllImport("user32.dll")]
    private static extern nint LoadIconW(nint instance, nint name);

    [DllImport("user32.dll")]
    private static extern nint CopyIcon(nint icon);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(nint menu, uint flags, nuint id, string? text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetMenuDefaultItem(nint menu, uint item, uint byPosition);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint window, nint parameters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint window, int message, nint wParam, nint lParam);
}
