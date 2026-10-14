using System.Text.RegularExpressions;
using System.Windows.Automation;
using ContextSnap.Model;
using ContextSnap.Privacy;

namespace ContextSnap.Collectors;

public sealed record RawBrowserTab(string Title, bool IsSelected);

public sealed record RawBrowserWindow(string Browser, string WindowTitle, IReadOnlyList<RawBrowserTab> Tabs, string? ActiveUrl);

public interface IBrowserTabSource
{
    IReadOnlyList<RawBrowserWindow> GetWindows(bool includeActiveUrl, CancellationToken cancellationToken);
}

/// <summary>
/// Opt-in. Reads tab titles from the browser's own tab strip through UI Automation, the same
/// accessibility interface screen readers use. Does not touch browser profiles, history, cookies,
/// or remote-debugging ports. Only the active tab's address bar can be read, so URLs are limited to it.
/// </summary>
public sealed partial class BrowserTabCollector(IBrowserTabSource source) : ICollector<BrowserTab>
{
    public string Name => "Browser tabs";

    public CollectorOutput<BrowserTab> Collect(CollectorContext context, CancellationToken cancellationToken)
    {
        var settings = context.Settings;
        var tabs = new List<BrowserTab>();
        var excluded = 0;

        foreach (var window in source.GetWindows(settings.IncludeBrowserUrls, cancellationToken))
        {
            // Private/incognito windows are skipped as a whole via the window title keywords.
            if (PrivacyRules.ContainsExcludedKeyword(window.WindowTitle, settings.ExcludedTitleKeywords))
            {
                excluded += Math.Max(1, window.Tabs.Count);
                continue;
            }

            foreach (var tab in window.Tabs)
            {
                if (string.IsNullOrWhiteSpace(tab.Title))
                    continue;

                if (PrivacyRules.ContainsExcludedKeyword(tab.Title, settings.ExcludedTitleKeywords))
                {
                    excluded++;
                    continue;
                }

                var url = tab.IsSelected && settings.IncludeBrowserUrls ? window.ActiveUrl : null;
                tabs.Add(new BrowserTab(window.Browser, CleanTitle(tab.Title), url, tab.IsSelected));
            }
        }

        return new CollectorOutput<BrowserTab>(tabs, ExcludedCount: excluded);
    }

    /// <summary>Removes status text that Chromium appends to accessible tab names (memory use, audio state).</summary>
    internal static string CleanTitle(string title)
    {
        var cleaned = title.Trim();
        string previous;
        do
        {
            previous = cleaned;
            cleaned = TabStatusSuffix().Replace(cleaned, string.Empty).TrimEnd();
        }
        while (cleaned != previous && cleaned.Length > 0);

        return cleaned.Length > 0 ? cleaned : title.Trim();
    }

    [GeneratedRegex(@"\s+-\s+(?:(?:High\s+)?memory usage\s+-\s+[\d.,]+\s*[KMGT]?B|Audio playing|Tab muted|Muted|Camera in use|Microphone in use|Network error|Crashed)$", RegexOptions.IgnoreCase)]
    private static partial Regex TabStatusSuffix();
}

public sealed class UiAutomationBrowserTabSource(IWindowSource windows) : IBrowserTabSource
{
    private const int MaxDepth = 24;
    private const int MaxNodesPerWindow = 4_000;

    private static readonly Dictionary<string, string> SupportedBrowsers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = "Chrome",
        ["msedge"] = "Edge",
        ["brave"] = "Brave",
        ["vivaldi"] = "Vivaldi",
        ["opera"] = "Opera",
        ["firefox"] = "Firefox",
    };

    public IReadOnlyList<RawBrowserWindow> GetWindows(bool includeActiveUrl, CancellationToken cancellationToken)
    {
        var result = new List<RawBrowserWindow>();
        foreach (var window in windows.GetWindows())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (window.IsCloaked || window.IsToolWindow || window.IsOwnedPopup
                || window.ProcessName is null || !SupportedBrowsers.TryGetValue(window.ProcessName, out var browser))
                continue;

            try
            {
                var root = AutomationElement.FromHandle(window.Handle);
                var tabs = new List<RawBrowserTab>();
                string? url = null;
                Walk(root, includeActiveUrl, tabs, ref url, cancellationToken);
                if (tabs.Count > 0)
                    result.Add(new RawBrowserWindow(browser, window.Title, tabs, url));
            }
            catch (ElementNotAvailableException)
            {
                // The window closed while it was being read.
            }
        }

        return result;
    }

    /// <summary>
    /// Depth-first walk of the browser's own UI. Web page content (Document elements) is never entered,
    /// which keeps the walk fast and prevents page widgets from being mistaken for tabs.
    /// </summary>
    private static void Walk(AutomationElement root, bool findUrl, List<RawBrowserTab> tabs, ref string? url, CancellationToken cancellationToken)
    {
        var walker = TreeWalker.ControlViewWalker;
        var stack = new Stack<(AutomationElement Element, int Depth)>();
        stack.Push((root, 0));
        var visited = 0;

        while (stack.Count > 0 && visited++ < MaxNodesPerWindow)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (element, depth) = stack.Pop();
            ControlType type;
            try
            {
                type = element.Current.ControlType;
            }
            catch (ElementNotAvailableException)
            {
                continue;
            }

            if (type == ControlType.Document)
                continue;

            if (type == ControlType.TabItem)
            {
                tabs.Add(new RawBrowserTab(element.Current.Name, IsSelected(element)));
                continue;
            }

            if (findUrl && url is null && type == ControlType.Edit
                && element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
            {
                url = ((ValuePattern)pattern).Current.Value;
                continue;
            }

            if (depth >= MaxDepth)
                continue;

            // Push children in reverse so they are visited in document order.
            var children = new List<AutomationElement>();
            for (var child = walker.GetFirstChild(element); child is not null; child = walker.GetNextSibling(child))
                children.Add(child);
            for (var i = children.Count - 1; i >= 0; i--)
                stack.Push((children[i], depth + 1));
        }
    }

    private static bool IsSelected(AutomationElement element) =>
        element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern)
        && ((SelectionItemPattern)pattern).Current.IsSelected;
}
