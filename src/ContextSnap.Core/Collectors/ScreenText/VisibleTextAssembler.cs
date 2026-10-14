using System.Text;
using ContextSnap.Platform;

namespace ContextSnap.Collectors.ScreenText;

/// <summary>
/// A snapshot of one UI Automation element, detached from UI Automation so text assembly can be
/// tested without a desktop. Bounds are physical screen pixels.
/// </summary>
/// <param name="ControlType">Short control type name, such as "Text", "Edit", "Document", or "Button".</param>
/// <param name="VisibleText">Text from the element's visible ranges (TextPattern), when the element provides it.
/// Elements with visible text are not descended into.</param>
public sealed record UiaNode(
    string ControlType,
    string? Name,
    string? Value,
    PixelRect Bounds,
    bool IsOffscreen,
    bool IsPassword,
    string? VisibleText,
    IReadOnlyList<UiaNode> Children)
{
    public static UiaNode Leaf(string controlType, string? name, PixelRect bounds, bool isOffscreen = false) =>
        new(controlType, name, null, bounds, isOffscreen, false, null, []);
}

public sealed record AssemblyOptions(PixelRect ViewBounds, bool IncludeOffscreen, int MaxCharacters);

/// <param name="Characters">Length of <see cref="Text"/>.</param>
/// <param name="SkippedPasswordFields">Password controls whose content was deliberately not read.</param>
public sealed record AssembledText(string Text, bool Truncated, int SkippedPasswordFields)
{
    public int Characters => Text.Length;
}

/// <summary>
/// Turns an accessibility tree into readable visible text: skips hidden, off-screen, and password
/// elements, prefers leaf text over container labels so nothing is repeated, joins fragments that
/// sit on the same visual line, removes consecutive duplicates, and applies a character limit.
/// </summary>
public static class VisibleTextAssembler
{
    /// <summary>Element types whose name is only a label for their children or is not visible text.</summary>
    private static readonly HashSet<string> NonTextTypes = new(StringComparer.Ordinal)
    {
        "Window", "Pane", "TitleBar", "ScrollBar", "Thumb", "Separator", "ProgressBar", "ToolBar", "MenuBar",
        "Image", "Slider", "StatusBar", "Tab", "List", "Tree", "Table", "DataGrid", "Menu", "SemanticZoom", "AppBar",
        "TabItem", // Tab titles are navigation, and can name other open files or pages; their content is not shown.
    };

    /// <summary>
    /// Inside web documents, only these element types render their name as visible text. Buttons,
    /// groups, and regions there usually carry invisible accessibility labels ("Copy message").
    /// </summary>
    private static readonly HashSet<string> DocumentTextTypes = new(StringComparer.Ordinal)
    {
        "Text", "Edit", "Hyperlink", "Header", "HeaderItem", "ListItem", "DataItem", "TreeItem",
        "CheckBox", "RadioButton", "ComboBox",
    };

    /// <summary>Elements this small are "visually hidden" (for example, skip links) rather than visible text.</summary>
    private const int MinVisibleSize = 3;

    private readonly record struct Fragment(string Text, PixelRect Bounds, bool Mergeable);

    public static AssembledText Assemble(UiaNode root, AssemblyOptions options)
    {
        var fragments = new List<Fragment>();
        var passwords = 0;
        Visit(root, options, inDocument: false, fragments, ref passwords);
        var lines = JoinLines(fragments);
        var (text, truncated) = Limit(RemoveConsecutiveDuplicates(lines), options.MaxCharacters);
        return new AssembledText(text, truncated, passwords);
    }

    private static void Visit(UiaNode node, AssemblyOptions options, bool inDocument, List<Fragment> output, ref int passwords)
    {
        if (node.IsPassword)
        {
            passwords++;
            return; // Never read password controls, including their children.
        }

        if (node.ControlType is "TitleBar" or "TabItem")
            return; // Caption buttons are window chrome; tab titles (and their child text) name other files or pages.

        var visible = options.IncludeOffscreen || IsVisible(node, options.ViewBounds);
        if (!visible && !options.IncludeOffscreen && node.IsOffscreen)
            return; // Off-screen containers hold off-screen content.

        if (node.VisibleText is not null)
        {
            if (visible)
            {
                foreach (var line in SplitLines(node.VisibleText))
                    output.Add(new Fragment(line, node.Bounds, Mergeable: false));
            }

            return;
        }

        var insertAt = output.Count;
        var childrenInDocument = inDocument || node.ControlType == "Document";
        foreach (var child in node.Children)
            Visit(child, options, childrenInDocument, output, ref passwords);
        var hasChildText = output.Count > insertAt;

        if (!visible)
            return;

        if (inDocument && !DocumentTextTypes.Contains(node.ControlType))
            return;

        var text = OwnText(node, hasChildText);
        if (!string.IsNullOrWhiteSpace(text))
            output.Insert(insertAt, new Fragment(Normalize(text), node.Bounds, Mergeable: true));
    }

    /// <summary>The text an element contributes itself, or null when its children already carry it.</summary>
    private static string? OwnText(UiaNode node, bool hasChildText) => node.ControlType switch
    {
        "Text" => hasChildText ? null : node.Name,
        "Edit" => !string.IsNullOrWhiteSpace(node.Value) ? node.Value : null,
        "Document" => hasChildText ? null : node.Value,
        _ when NonTextTypes.Contains(node.ControlType) => null,
        _ => hasChildText ? null : node.Name,
    };

    private static bool IsVisible(UiaNode node, PixelRect view) =>
        !node.IsOffscreen && node.Bounds.Width >= MinVisibleSize && node.Bounds.Height >= MinVisibleSize && Intersects(node.Bounds, view);

    private static bool Intersects(PixelRect a, PixelRect b) =>
        a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;

    /// <summary>Joins fragments that continue the same visual line (for example, a link inside a sentence).</summary>
    private static List<string> JoinLines(List<Fragment> fragments)
    {
        var lines = new List<string>();
        Fragment? previous = null;
        var current = new StringBuilder();

        foreach (var fragment in fragments)
        {
            if (previous is { } prev && current.Length > 0 && prev.Mergeable && fragment.Mergeable && SameLine(prev.Bounds, fragment.Bounds))
            {
                if (NeedsSpace(current, fragment.Text))
                    current.Append(' ');
                current.Append(fragment.Text);
            }
            else
            {
                if (current.Length > 0)
                    lines.Add(current.ToString());
                current.Clear().Append(fragment.Text);
            }

            previous = fragment;
        }

        if (current.Length > 0)
            lines.Add(current.ToString());
        return lines;
    }

    internal static bool SameLine(PixelRect previous, PixelRect next)
    {
        if (previous.Height <= 0 || next.Height <= 0)
            return false;

        var tolerance = Math.Min(previous.Height, next.Height) / 2.0;
        var sameRow = Math.Abs((previous.Top + previous.Bottom) / 2.0 - (next.Top + next.Bottom) / 2.0) <= tolerance;
        var gap = next.Left - previous.Right;
        return sameRow && next.Left >= previous.Left && gap <= 3 * Math.Max(previous.Height, next.Height);
    }

    private static bool NeedsSpace(StringBuilder current, string next) =>
        !char.IsWhiteSpace(current[^1]) && next.Length > 0 && !".,;:!?)]}%".Contains(next[0]) && !"([{".Contains(current[^1]);

    private static IEnumerable<string> SplitLines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Select(Normalize)
            .Where(line => line.Length > 0);

    /// <summary>Collapses runs of spaces and removes control characters inside one line.</summary>
    internal static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        var space = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c) || IsInvisible(c))
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
                builder.Append(' ');
            space = false;
            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>Zero-width space, byte-order mark, and the object replacement character browsers use for embedded objects.</summary>
    private static bool IsInvisible(char c) => c is (char)0x200B or (char)0xFEFF or (char)0xFFFC;

    internal static List<string> RemoveConsecutiveDuplicates(IEnumerable<string> lines)
    {
        var result = new List<string>();
        foreach (var line in lines)
        {
            if (result.Count == 0 || !string.Equals(result[^1], line, StringComparison.Ordinal))
                result.Add(line);
        }

        return result;
    }

    /// <summary>Keeps whole lines up to the limit; a single overlong line is cut.</summary>
    internal static (string Text, bool Truncated) Limit(IReadOnlyList<string> lines, int maxCharacters)
    {
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            var needed = line.Length + (builder.Length > 0 ? 1 : 0);
            if (builder.Length + needed > maxCharacters)
            {
                if (builder.Length == 0)
                    builder.Append(line, 0, Math.Max(0, maxCharacters - 1)).Append('…');
                return (builder.ToString(), true);
            }

            if (builder.Length > 0)
                builder.Append('\n');
            builder.Append(line);
        }

        return (builder.ToString(), false);
    }
}
