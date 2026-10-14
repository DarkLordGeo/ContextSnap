using System.Globalization;
using System.Text;
using ContextSnap.Platform;

namespace ContextSnap.Collectors.ScreenText;

/// <summary>One recognized line with its bounding box in image pixels.</summary>
public sealed record OcrLine(string Text, PixelRect Bounds);

/// <summary>Orders OCR lines into reading order and combines OCR with UI Automation text.</summary>
public static class OcrLayout
{
    /// <summary>
    /// Groups lines into columns (lines whose horizontal extents overlap end up in the same column),
    /// then reads columns left to right and each column top to bottom. A layout such as a sidebar next
    /// to a conversation therefore reads as two blocks instead of interleaved rows.
    /// </summary>
    public static IReadOnlyList<string> Order(IEnumerable<OcrLine> lines)
    {
        var items = lines
            .Where(l => !string.IsNullOrWhiteSpace(l.Text) && l.Bounds.Width > 0 && l.Bounds.Height > 0)
            .ToList();
        if (items.Count == 0)
            return [];

        var columns = new List<(int Left, int Right, List<OcrLine> Lines)>();
        foreach (var line in items.OrderBy(l => l.Bounds.Left))
        {
            var column = columns.FindIndex(c => line.Bounds.Left < c.Right && c.Left < line.Bounds.Right);
            if (column < 0)
            {
                columns.Add((line.Bounds.Left, line.Bounds.Right, [line]));
            }
            else
            {
                var c = columns[column];
                c.Lines.Add(line);
                columns[column] = (Math.Min(c.Left, line.Bounds.Left), Math.Max(c.Right, line.Bounds.Right), c.Lines);
            }
        }

        // Widening a column can make it overlap a neighbour; merge until stable.
        columns = MergeOverlapping(columns);

        return columns
            .OrderBy(c => c.Left)
            .SelectMany(c => c.Lines.OrderBy(l => l.Bounds.Top).ThenBy(l => l.Bounds.Left))
            .Select(l => VisibleTextAssembler.Normalize(l.Text))
            .Where(t => t.Length > 0)
            .ToArray();
    }

    private static List<(int Left, int Right, List<OcrLine> Lines)> MergeOverlapping(List<(int Left, int Right, List<OcrLine> Lines)> columns)
    {
        var sorted = columns.OrderBy(c => c.Left).ToList();
        var merged = new List<(int Left, int Right, List<OcrLine> Lines)>();
        foreach (var column in sorted)
        {
            if (merged.Count > 0 && column.Left < merged[^1].Right)
            {
                var last = merged[^1];
                last.Lines.AddRange(column.Lines);
                merged[^1] = (last.Left, Math.Max(last.Right, column.Right), last.Lines);
            }
            else
            {
                merged.Add(column);
            }
        }

        return merged;
    }

    /// <summary>
    /// Adds OCR lines that UI Automation did not already provide. Comparison ignores case,
    /// whitespace, and punctuation so small recognition differences do not create duplicates.
    /// </summary>
    public static (IReadOnlyList<string> Lines, int AddedCharacters) MergeWithUia(IReadOnlyList<string> uiaLines, IReadOnlyList<string> ocrLines)
    {
        var known = Canonical(string.Join(' ', uiaLines));
        var result = new List<string>(uiaLines);
        var added = 0;
        foreach (var line in ocrLines)
        {
            var canonical = Canonical(line);
            // Fragments under two letters/digits are usually icons or misread glyphs, not text.
            if (canonical.Length < 2 || known.Contains(canonical, StringComparison.Ordinal))
                continue;
            result.Add(line);
            known += canonical;
            added += line.Length;
        }

        return (result, added);
    }

    /// <summary>Lowercase letters and digits only.</summary>
    internal static string Canonical(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(c))
                builder.Append(char.ToLower(c, CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
