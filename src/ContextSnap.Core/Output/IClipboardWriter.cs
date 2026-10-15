namespace ContextSnap.Output;

public sealed record ClipboardResult(bool Success, string? Error)
{
    public static ClipboardResult Ok { get; } = new(true, null);
}

public interface IClipboardWriter
{
    ClipboardResult SetText(string text, bool excludeFromHistory);
}
