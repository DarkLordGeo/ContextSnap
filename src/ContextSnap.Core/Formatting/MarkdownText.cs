using System.Text;
using System.Text.RegularExpressions;

namespace ContextSnap.Formatting;

/// <summary>Helpers for writing untrusted text safely into Markdown.</summary>
public static partial class MarkdownText
{
    private const string EscapedCharacters = "\\`*_[]<>|~";

    /// <summary>
    /// Escapes Markdown control characters. Redaction placeholders such as <c>[REDACTED]</c> are kept
    /// readable because a bracketed word without a link target renders literally.
    /// </summary>
    public static string Escape(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var builder = new StringBuilder(text.Length + 8);
        var position = 0;
        foreach (Match token in Placeholder().Matches(text))
        {
            AppendEscaped(builder, text.AsSpan(position, token.Index - position));
            builder.Append(token.Value);
            position = token.Index + token.Length;
        }

        AppendEscaped(builder, text.AsSpan(position));
        return builder.ToString();
    }

    /// <summary>Wraps text in an inline code span, choosing a fence that cannot be closed early.</summary>
    public static string Code(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "` `";

        var longestRun = 0;
        var run = 0;
        foreach (var c in text)
        {
            run = c == '`' ? run + 1 : 0;
            longestRun = Math.Max(longestRun, run);
        }

        if (longestRun == 0)
            return "`" + text + "`";

        var fence = new string('`', longestRun + 1);
        return $"{fence} {text} {fence}";
    }

    /// <summary>A fence of backticks longer than any backtick run in <paramref name="text"/> (at least three).</summary>
    public static string Fence(string text)
    {
        var longest = 0;
        var run = 0;
        foreach (var c in text)
        {
            run = c == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        return new string('`', Math.Max(3, longest + 1));
    }

    /// <summary>Multi-line text for a fenced block: normalized line endings, no control characters except tabs.</summary>
    public static string Block(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.ReplaceLineEndings("\n"))
        {
            if (c == '\n' || c == '\t' || !char.IsControl(c))
                builder.Append(c);
        }

        return builder.ToString().Trim('\n');
    }

    /// <summary>Collapses whitespace and control characters to single spaces and truncates with an ellipsis.</summary>
    public static string SingleLine(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var builder = new StringBuilder(Math.Min(text.Length, maxLength + 1));
        var pendingSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
            if (builder.Length > maxLength)
                break;
        }

        if (builder.Length <= maxLength)
            return builder.ToString();

        var cut = Math.Max(0, maxLength - 1);
        if (cut > 0 && char.IsHighSurrogate(builder[cut - 1]))
            cut--;
        return builder.ToString(0, cut).TrimEnd() + "…";
    }

    private static void AppendEscaped(StringBuilder builder, ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            if (EscapedCharacters.Contains(c))
                builder.Append('\\');
            builder.Append(c);
        }
    }

    [GeneratedRegex(@"\[(?:REDACTED(?:_[A-Z]+)*|USER|HOST)\]")]
    private static partial Regex Placeholder();
}
