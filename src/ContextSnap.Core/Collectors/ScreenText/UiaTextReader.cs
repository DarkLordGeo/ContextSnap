using System.Runtime.InteropServices;
using System.Windows.Automation;
using ContextSnap.Platform;

namespace ContextSnap.Collectors.ScreenText;

/// <param name="Root">Detached tree to assemble text from, or null when nothing could be read.</param>
/// <param name="ContentBounds">For browsers, the page area (used to crop OCR so browser UI is never read).</param>
public sealed record UiaReadResult(UiaNode? Root, PixelRect? ContentBounds, int NodesVisited, string? Warning);

public interface IUiaTextReader
{
    UiaReadResult Read(ScreenTextTarget target, int maxCharacters, CancellationToken cancellationToken);
}

/// <summary>
/// Reads a window's accessibility tree through Windows UI Automation. Read-only: it never focuses,
/// scrolls, clicks, or changes anything. Each element is fetched with its properties in a single
/// cached call. For browsers only the page document is read, never tabs, address bar, or extensions.
/// </summary>
public sealed class UiaTextReader : IUiaTextReader
{
    public const int MaxNodes = 6_000;
    private const int MaxDepth = 80;

    public UiaReadResult Read(ScreenTextTarget target, int maxCharacters, CancellationToken cancellationToken)
    {
        var request = CreateRequest(); // Not shared: cache requests are not documented as thread-safe.
        AutomationElement root;
        try
        {
            using (request.Activate())
                root = AutomationElement.FromHandle(target.Handle);
        }
        catch (Exception ex) when (IsElementFailure(ex))
        {
            return new UiaReadResult(null, null, 0, "UI Automation could not open this window.");
        }

        var state = new WalkState(request, maxCharacters, cancellationToken);
        if (!target.IsBrowser && !target.IsChromium)
        {
            var tree = Build(root, 0, state);
            return new UiaReadResult(tree, null, state.Visited, state.Warning);
        }

        // Browsers: find page documents without reading the browser's own UI, then read only those.
        var documents = new List<AutomationElement>();
        FindDocuments(root, 0, documents, state);
        var pages = documents.Select(d => Build(d, 0, state)).OfType<UiaNode>().ToArray();
        if (pages.Length == 0 && !target.IsBrowser)
        {
            // An Electron app that does not expose a document: read the whole window (OCR may follow).
            state.Visited = 0;
            return new UiaReadResult(Build(root, 0, state), null, state.Visited, state.Warning);
        }

        if (pages.Length == 0)
            return new UiaReadResult(null, null, state.Visited, state.Warning ?? "No page content was exposed by the browser.");

        var page = pages.OrderByDescending(p => (long)p.Bounds.Width * p.Bounds.Height).First();
        var container = new UiaNode("Pane", null, null, page.Bounds, false, false, null, pages);
        return new UiaReadResult(container, page.Bounds, state.Visited, state.Warning);
    }

    private sealed class WalkState(CacheRequest request, int maxCharacters, CancellationToken cancellationToken)
    {
        public int Visited;
        public CacheRequest Request { get; } = request;
        public string? Warning;
        public int MaxCharacters { get; } = maxCharacters;

        public bool Exhausted()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Visited < MaxNodes)
                return false;
            Warning ??= $"The accessibility tree is very large; only the first {MaxNodes:N0} elements were read.";
            return true;
        }
    }

    private static UiaNode? Build(AutomationElement element, int depth, WalkState state)
    {
        if (state.Exhausted())
            return null;
        state.Visited++;

        try
        {
            var controlType = element.Cached.ControlType?.ProgrammaticName?.Replace("ControlType.", string.Empty, StringComparison.Ordinal) ?? "Custom";
            var isPassword = element.Cached.IsPassword;
            var bounds = ToPixels(element.Cached.BoundingRectangle);
            var isOffscreen = element.Cached.IsOffscreen;
            if (isPassword)
                return new UiaNode(controlType, null, null, bounds, isOffscreen, true, null, []);

            var children = new List<UiaNode>();
            if (depth < MaxDepth && !(isOffscreen && bounds.Width == 0))
            {
                var walker = TreeWalker.ControlViewWalker;
                for (var child = walker.GetFirstChild(element, state.Request); child is not null; child = walker.GetNextSibling(child, state.Request))
                {
                    var node = Build(child, depth + 1, state);
                    if (node is not null)
                        children.Add(node);
                    if (state.Exhausted())
                        break;
                }
            }

            var value = ReadValue(element);
            var visibleText = children.Count == 0 && !isOffscreen ? ReadVisibleRanges(element, state.MaxCharacters) : null;
            return new UiaNode(controlType, element.Cached.Name, value, bounds, isOffscreen, false, visibleText, children);
        }
        catch (Exception ex) when (IsElementFailure(ex))
        {
            return null; // The element disappeared or the app stopped responding to this element.
        }
    }

    private static void FindDocuments(AutomationElement element, int depth, List<AutomationElement> documents, WalkState state)
    {
        if (depth > 20 || state.Exhausted())
            return;
        state.Visited++;

        try
        {
            if (element.Cached.ControlType == ControlType.Document)
            {
                documents.Add(element);
                return; // Frames inside the page are read as part of the document.
            }

            var walker = TreeWalker.ControlViewWalker;
            for (var child = walker.GetFirstChild(element, state.Request); child is not null; child = walker.GetNextSibling(child, state.Request))
                FindDocuments(child, depth + 1, documents, state);
        }
        catch (Exception ex) when (IsElementFailure(ex))
        {
            // Skip this branch.
        }
    }

    private static string? ReadValue(AutomationElement element)
    {
        if (!element.Cached.IsPassword
            && element.GetCachedPropertyValue(AutomationElement.IsValuePatternAvailableProperty) is true
            && element.GetCachedPropertyValue(ValuePattern.ValueProperty) is string value)
            return value;
        return null;
    }

    /// <summary>Text currently scrolled into view, for controls that implement the Text pattern without child elements.</summary>
    private static string? ReadVisibleRanges(AutomationElement element, int maxCharacters)
    {
        if (element.GetCachedPropertyValue(AutomationElement.IsTextPatternAvailableProperty) is not true)
            return null;

        try
        {
            var pattern = (TextPattern)element.GetCurrentPattern(TextPattern.Pattern);
            var parts = new List<string>();
            var remaining = maxCharacters;
            foreach (var range in pattern.GetVisibleRanges())
            {
                if (remaining <= 0)
                    break;
                var text = range.GetText(remaining);
                parts.Add(text);
                remaining -= text.Length;
            }

            var joined = string.Join('\n', parts);
            return string.IsNullOrWhiteSpace(joined) ? null : joined;
        }
        catch (Exception ex) when (IsElementFailure(ex))
        {
            return null;
        }
    }

    private static PixelRect ToPixels(System.Windows.Rect rect) =>
        rect.IsEmpty || double.IsInfinity(rect.Width)
            ? default
            : new PixelRect((int)Math.Round(rect.Left), (int)Math.Round(rect.Top), (int)Math.Round(rect.Right), (int)Math.Round(rect.Bottom));

    private static bool IsElementFailure(Exception ex) =>
        ex is ElementNotAvailableException or InvalidOperationException or COMException or TimeoutException or ArgumentException;

    private static CacheRequest CreateRequest()
    {
        var request = new CacheRequest { TreeFilter = Automation.ControlViewCondition };
        foreach (var property in new AutomationProperty[]
                 {
                     AutomationElement.NameProperty, AutomationElement.ControlTypeProperty,
                     AutomationElement.BoundingRectangleProperty, AutomationElement.IsOffscreenProperty,
                     AutomationElement.IsPasswordProperty, AutomationElement.IsTextPatternAvailableProperty,
                     AutomationElement.IsValuePatternAvailableProperty, ValuePattern.ValueProperty,
                 })
            request.Add(property);
        return request;
    }
}
