using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace ContextSnap.Platform;

/// <summary>Creates and reads .lnk files with the built-in Windows Shell link object (IShellLinkW).</summary>
public sealed class ShellLinkShortcutStore : IShortcutStore
{
    private const int MaxPath = 1024;
    private const uint SlgpRawPath = 0x4;
    private const int ShowMinNoActive = 7;
    private const int StgmRead = 0;

    public bool Exists(string shortcutPath) => File.Exists(shortcutPath);

    public ShortcutDefinition? Read(string shortcutPath)
    {
        if (!File.Exists(shortcutPath))
            return null;

        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(shortcutPath, StgmRead);
            var buffer = new StringBuilder(MaxPath);

            link.GetPath(buffer, buffer.Capacity, 0, SlgpRawPath);
            var target = buffer.ToString();
            buffer.Clear();
            link.GetArguments(buffer, buffer.Capacity);
            var arguments = buffer.ToString();
            buffer.Clear();
            link.GetWorkingDirectory(buffer, buffer.Capacity);
            var workingDirectory = buffer.ToString();
            buffer.Clear();
            link.GetDescription(buffer, buffer.Capacity);
            var description = buffer.ToString();
            buffer.Clear();
            link.GetIconLocation(buffer, buffer.Capacity, out _);
            var icon = buffer.ToString();

            return new ShortcutDefinition(target, arguments, workingDirectory, description, icon);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    public void Write(string shortcutPath, ShortcutDefinition shortcut)
    {
        var directory = Path.GetDirectoryName(shortcutPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(shortcut.TargetPath);
            link.SetArguments(shortcut.Arguments);
            link.SetWorkingDirectory(shortcut.WorkingDirectory);
            link.SetDescription(shortcut.Description);
            link.SetIconLocation(shortcut.IconPath, 0);
            link.SetShowCmd(ShowMinNoActive);
            ((IPersistFile)link).Save(shortcutPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    public void Delete(string shortcutPath) => File.Delete(shortcutPath);

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxChars, nint findData, uint flags);
        void GetIDList(out nint idList);
        void SetIDList(nint idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxChars);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int maxChars);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int maxChars);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxChars, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
        void Resolve(nint window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
