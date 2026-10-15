using ContextSnap.Collectors;
using ContextSnap.Collectors.Git;
using ContextSnap.Collectors.ScreenText;

namespace ContextSnap.Capture;

public sealed partial class CollectorSet
{
    /// <summary>The Windows collectors (Win32, Shell COM, UI Automation, Windows OCR).</summary>
    public static CollectorSet CreateDefault()
    {
        var windowSource = new Win32WindowSource();
        return new CollectorSet
        {
            Windows = new WindowCollector(windowSource, Environment.ProcessId),
            Processes = new ProcessCollector(new SystemProcessSource()),
            Explorer = new ExplorerCollector(new ShellComWindowSource()),
            Git = new GitCollector(new GitProcessRunner()),
            Desktop = DesktopCollector.CreateDefault(),
            BrowserTabs = new BrowserTabCollector(new UiAutomationBrowserTabSource(windowSource)),
            SystemInfo = new SystemSummaryCollector(),
            ScreenText = new ScreenTextCollector(windowSource, new UiaTextReader(), new WindowsOcrReader(), Environment.ProcessId),
        };
    }
}
