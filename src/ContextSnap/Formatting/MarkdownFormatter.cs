namespace ContextSnap.Formatting;

public static class MarkdownFormatter
{
    public static string LimitOutput(string markdown, int maxCharacters = 12000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCharacters);

        if (markdown.Length <= maxCharacters)
            return markdown;

        const string suffix = "\n\n[Snapshot truncated due to output limit.]";
        if (maxCharacters <= suffix.Length)
            return suffix[..maxCharacters];

        return markdown[..(maxCharacters - suffix.Length)] + suffix;
    }
}
