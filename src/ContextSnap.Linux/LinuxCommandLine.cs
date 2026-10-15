using ContextSnap.Capture;
using ContextSnap.Configuration;

namespace ContextSnap.Linux;

/// <summary>Command-line options and platform limits of the Linux app, which has no window: no arguments means capture.</summary>
public static class LinuxCommandLine
{
    public const string Usage = """
        ContextSnap for Linux — copy a Markdown snapshot of your desktop context to the clipboard.

        Usage:
          contextsnap             Capture, copy to the clipboard, show a notification, and exit.
          contextsnap --print     Capture and write the Markdown to standard output (no clipboard).
          contextsnap --version   Show the version.
          contextsnap --help      Show this help.

        Bind contextsnap to a keyboard shortcut in your desktop's keyboard settings.

        Collected: open windows (X11 and XWayland apps; needs wmctrl), running developer tools,
        Git repositories (the current directory and configured project folders), Desktop items,
        and a system summary. Visible screen text, browser tabs, and file manager folders are
        Windows only.

        Copying needs wl-clipboard (Wayland) or xclip or xsel (X11). Notifications use notify-send.
        Settings are read from ~/.config/ContextSnap/settings.json ($XDG_CONFIG_HOME if set).
        """;

    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
            return new CommandLineOptions(RunMode.Capture);

        var options = CommandLineOptions.Parse(args);
        // --startup opens the Windows app's window; there is no window here.
        return options.Mode == RunMode.Gui ? new CommandLineOptions(RunMode.Help, "Unknown option.") : options;
    }

    /// <summary>Turns off collectors that exist only on Windows, so they are reported as not collected.</summary>
    public static void ApplyPlatformLimits(AppSettings settings)
    {
        settings.CaptureScreenText = false;
        settings.CollectBrowserTabs = false;
        settings.IncludeBrowserUrls = false;
        settings.CollectExplorerFolders = false;
    }
}
