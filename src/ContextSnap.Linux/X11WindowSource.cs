using System.Globalization;
using System.Text.RegularExpressions;
using ContextSnap.Collectors;

namespace ContextSnap.Linux;

/// <summary>
/// Lists top-level windows through the window manager (EWMH) with <c>wmctrl</c>. Wayland has no common way
/// for an app to list other apps' windows, so on Wayland only X11 apps running through XWayland appear.
/// Window titles and process names are read; nothing else about the windows is.
/// </summary>
public sealed partial class X11WindowSource(Func<string, string?>? environment = null, Func<int, string?>? processName = null) : IWindowSource
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    /// <summary>WM_CLASS instance names of desktop backgrounds, panels, and docks, which are not app windows.</summary>
    private static readonly HashSet<string> ShellInstances = new(StringComparer.OrdinalIgnoreCase)
    {
        "xfdesktop", "xfce4-panel", "plasmashell", "desktop_window", "nemo-desktop", "lxpanel", "mate-panel",
        "polybar", "tint2", "plank", "cairo-dock", "conky", "gnome-shell", "budgie-panel", "waybar",
    };

    private readonly Func<string, string?> _environment = environment ?? Environment.GetEnvironmentVariable;
    private readonly Func<int, string?> _processName = processName ?? LinuxProcessSource.ReadName;

    /// <summary>Shown with the window list on Wayland, where only XWayland apps can be listed.</summary>
    public const string WaylandNote = "On Wayland only X11 (XWayland) apps are listed.";

    public bool IsWayland =>
        !string.IsNullOrEmpty(_environment("WAYLAND_DISPLAY"))
        || string.Equals(_environment("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<RawWindow> GetWindows()
    {
        if (string.IsNullOrEmpty(_environment("DISPLAY")))
        {
            throw new CollectorUnavailableException(IsWayland
                ? "Listing windows needs XWayland (DISPLAY is not set)."
                : "No graphical session was found (DISPLAY is not set).");
        }

        var wmctrl = ToolRunner.Find("wmctrl")
            ?? throw new CollectorUnavailableException("Install wmctrl to list open windows.");
        var list = ToolRunner.ReadOutput(wmctrl, ["-l", "-p", "-x"], Timeout)
            ?? throw new InvalidOperationException("wmctrl could not list windows (is an EWMH window manager running?).");

        var root = ToolRunner.Find("xprop") is { } xprop
            ? ToolRunner.ReadOutput(xprop, ["-root", "_NET_ACTIVE_WINDOW", "_NET_CLIENT_LIST_STACKING"], Timeout)
            : null;

        return Parse(list, ParseRootProperties(root), _processName);
    }

    /// <summary>
    /// Parses <c>wmctrl -l -p -x</c> output: id, desktop, pid, WM_CLASS, host, title. The host is discarded,
    /// and windows without a title (shown as "N/A") are skipped. Windows are returned topmost first.
    /// </summary>
    public static IReadOnlyList<RawWindow> Parse(string output, RootProperties root, Func<int, string?> processName)
    {
        var windows = new List<RawWindow>();
        var names = new Dictionary<int, string?>();
        foreach (var line in output.Split('\n'))
        {
            var match = WmctrlLine().Match(line.TrimEnd('\r'));
            if (!match.Success || !long.TryParse(match.Groups["id"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
                continue;

            var pid = int.Parse(match.Groups["pid"].Value, CultureInfo.InvariantCulture);
            if (pid > 0 && !names.TryGetValue(pid, out _))
                names[pid] = processName(pid);
            var name = pid > 0 ? names[pid] : null;

            var title = match.Groups["title"].Value;
            if (title is "N/A")
                continue;

            var wmClass = match.Groups["class"].Value;
            var instance = wmClass.Split('.', 2)[0];
            windows.Add(new RawWindow(
                (nint)id,
                title,
                pid,
                name ?? (instance.Length > 0 && wmClass != "N/A" ? instance : null),
                wmClass,
                IsCloaked: false,
                IsToolWindow: ShellInstances.Contains(instance),
                IsOwnedPopup: false,
                IsForeground: id == root.ActiveWindow));
        }

        // Topmost first, like the Windows z-order list; the active window leads even if the stacking order is unknown.
        var position = root.StackingTopmostFirst.Select((id, index) => (id, index)).ToDictionary(p => p.id, p => p.index);
        return windows
            .OrderByDescending(w => w.IsForeground)
            .ThenBy(w => position.GetValueOrDefault(w.Handle, int.MaxValue))
            .ToArray();
    }

    /// <summary>
    /// Parses <c>xprop -root _NET_ACTIVE_WINDOW _NET_CLIENT_LIST_STACKING</c>, for example
    /// <c>_NET_ACTIVE_WINDOW(WINDOW): window id # 0x3a00003</c>.
    /// </summary>
    public static RootProperties ParseRootProperties(string? output)
    {
        long? active = null;
        IReadOnlyList<nint> stacking = [];
        foreach (var line in (output ?? string.Empty).Split('\n'))
        {
            var ids = WindowId().Matches(line)
                .Select(m => long.TryParse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id) ? id : 0)
                .Where(id => id != 0)
                .ToArray();
            if (line.StartsWith("_NET_ACTIVE_WINDOW(", StringComparison.Ordinal) && ids.Length > 0)
                active = ids[0];
            else if (line.StartsWith("_NET_CLIENT_LIST_STACKING(", StringComparison.Ordinal))
                stacking = ids.Reverse().Select(id => (nint)id).Distinct().ToArray(); // xprop lists bottom to top.
        }

        return new RootProperties(active, stacking);
    }

    [GeneratedRegex(@"^0x(?<id>[0-9a-fA-F]+)\s+-?\d+\s+(?<pid>\d+)\s+(?<class>\S+)\s+\S+(?: (?<title>.*))?$")]
    private static partial Regex WmctrlLine();

    [GeneratedRegex(@"0x([0-9a-fA-F]+)")]
    private static partial Regex WindowId();
}

/// <param name="ActiveWindow">The focused window, if the window manager reports one.</param>
/// <param name="StackingTopmostFirst">Window ids from top to bottom of the stacking order; empty when unknown.</param>
public sealed record RootProperties(long? ActiveWindow, IReadOnlyList<nint> StackingTopmostFirst)
{
    public static RootProperties None { get; } = new(null, []);
}

/// <summary>Adds the Wayland note to the window list; filtering is shared with Windows.</summary>
public sealed class LinuxWindowCollector(X11WindowSource source, int ownProcessId) : ICollector<Model.WindowInfo>
{
    private readonly WindowCollector _inner = new(source, ownProcessId);

    public string Name => _inner.Name;

    public CollectorOutput<Model.WindowInfo> Collect(CollectorContext context, CancellationToken cancellationToken)
    {
        var output = _inner.Collect(context, cancellationToken);
        return source.IsWayland ? output with { Notes = [.. output.Notes ?? [], X11WindowSource.WaylandNote] } : output;
    }
}
