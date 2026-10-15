using System.Globalization;
using ContextSnap.Model;

namespace ContextSnap.Collectors.ScreenText;

/// <summary>Short user-facing summaries of screen text results.</summary>
public static class ScreenTextSummary
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    /// <summary>For example: "UI Automation 2,340 characters · OCR 680 characters · 3 windows · 1 warning".</summary>
    public static string Describe(IReadOnlyList<WindowText> windows)
    {
        var parts = new List<string>();
        var uia = windows.Sum(w => w.UiaCharacters);
        var ocr = windows.Sum(w => w.OcrCharacters);
        if (uia > 0)
            parts.Add($"UI Automation {uia.ToString("N0", Culture)} characters");
        if (ocr > 0)
            parts.Add($"OCR {ocr.ToString("N0", Culture)} characters");
        parts.Add(windows.Count == 1 ? "1 window" : $"{windows.Count.ToString(Culture)} windows");
        var warnings = windows.Sum(w => w.Warnings.Count);
        if (warnings > 0)
            parts.Add(warnings == 1 ? "1 warning" : $"{warnings.ToString(Culture)} warnings");
        return string.Join(" · ", parts);
    }

    /// <summary>Notification line telling the user that screen text is part of what they copied.</summary>
    public static string? CopyDetail(CollectorResult<WindowText>? result)
    {
        if (result is not { Status: CollectorStatus.Ok, Count: > 0 })
            return null;
        return result.Count == 1
            ? "Includes visible text from 1 window."
            : $"Includes visible text from {result.Count.ToString(Culture)} windows.";
    }

    public static string SourceLabel(ScreenTextSource source) => source switch
    {
        ScreenTextSource.UiAutomation => "UI Automation",
        ScreenTextSource.Ocr => "OCR",
        ScreenTextSource.UiAutomation | ScreenTextSource.Ocr => "UI Automation + OCR",
        _ => "none",
    };
}
