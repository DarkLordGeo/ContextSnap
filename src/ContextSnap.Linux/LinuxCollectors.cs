using ContextSnap.Capture;
using ContextSnap.Collectors;
using ContextSnap.Collectors.Git;

namespace ContextSnap.Linux;

public static class LinuxCollectors
{
    public static CollectorSet CreateDefault() => new()
    {
        Windows = new LinuxWindowCollector(new X11WindowSource(), Environment.ProcessId),
        Processes = new ProcessCollector(new LinuxProcessSource()),
        Explorer = new WindowsOnlyCollector<Model.ExplorerFolder>("File Explorer folders"),
        Git = new GitCollector(new GitProcessRunner()),
        Desktop = DesktopCollector.CreateDefault(),
        BrowserTabs = new WindowsOnlyCollector<Model.BrowserTab>("Browser tabs"),
        SystemInfo = new LinuxSystemSummaryCollector(),
        ScreenText = new WindowsOnlyCollector<Model.WindowText>("Visible screen text"),
    };
}

/// <summary>
/// Stands in for a collector that exists only on Windows. <see cref="LinuxCommandLine.ApplyPlatformLimits"/>
/// turns these off, so they are never run; if one is, it reports itself as unavailable.
/// </summary>
public sealed class WindowsOnlyCollector<T>(string name) : ICollector<T>
{
    public string Name { get; } = name;

    public CollectorOutput<T> Collect(CollectorContext context, CancellationToken cancellationToken) =>
        throw new CollectorUnavailableException("Available only on Windows.");
}
