using System.Globalization;
using System.Windows.Media;
using System.Windows.Data;
using ContextSnap.Capture;
using ContextSnap.Collectors.ScreenText;
using ContextSnap.Model;

namespace ContextSnap;

/// <summary>Binds a list of strings to a multi-line text box, one entry per line.</summary>
public sealed class LinesConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is IEnumerable<string> lines ? string.Join(Environment.NewLine, lines) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as string ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
}

/// <summary>Binds a radio button to one value of an enum property (the value name is the converter parameter).</summary>
public sealed class EnumMatchConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter as string, StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is string name ? Enum.Parse(targetType, name) : Binding.DoNothing;
}

/// <summary>A small status badge for one collector in the status bar.</summary>
public sealed record CollectorChip(string Label, string Details, Brush Background, Brush Foreground)
{
    // The site's neutral palette: collected sections read as quiet tags; only problems get color.
    private static readonly Brush OkBackground = Freeze(new SolidColorBrush(Color.FromRgb(0xF1, 0xEF, 0xEB)));
    private static readonly Brush OkForeground = Freeze(new SolidColorBrush(Color.FromRgb(0x48, 0x46, 0x4A)));
    private static readonly Brush NeutralBackground = Freeze(new SolidColorBrush(Color.FromArgb(0x0F, 0x1D, 0x1D, 0x1F)));
    private static readonly Brush NeutralForeground = Freeze(new SolidColorBrush(Color.FromRgb(0x6B, 0x69, 0x66)));
    private static readonly Brush WarnBackground = Freeze(new SolidColorBrush(Color.FromRgb(0xFB, 0xF1, 0xDD)));
    private static readonly Brush WarnForeground = Freeze(new SolidColorBrush(Color.FromRgb(0x8A, 0x5A, 0x00)));
    private static readonly Brush ErrorBackground = Freeze(new SolidColorBrush(Color.FromRgb(0xFB, 0xE9, 0xE7)));
    private static readonly Brush ErrorForeground = Freeze(new SolidColorBrush(Color.FromRgb(0xA1, 0x2A, 0x1F)));

    public static CollectorChip From(ICollectorResult result)
    {
        var label = result is CollectorResult<WindowText> { Status: CollectorStatus.Ok } screenText
            ? $"Screen text: {ScreenTextSummary.Describe(screenText.Items)}"
            : $"{result.Name}: {HeadlessRunner.DescribeStatus(result)}";
        var details = new List<string> { $"{result.Name} — {result.Status}" };
        if (result.Error is not null)
            details.Add(result.Error);
        if (result.OmittedCount > 0)
            details.Add($"{result.OmittedCount} more not shown (item limit).");
        if (result.ExcludedCount > 0)
            details.Add($"{result.ExcludedCount} excluded by your privacy rules.");
        details.AddRange(result.Notes);
        if (result.Status != CollectorStatus.Disabled)
            details.Add($"Took {result.Elapsed.TotalMilliseconds:N0} ms.");

        var (background, foreground) = result.Status switch
        {
            CollectorStatus.Ok => (OkBackground, OkForeground),
            CollectorStatus.Failed => (ErrorBackground, ErrorForeground),
            CollectorStatus.Unavailable => (WarnBackground, WarnForeground),
            _ => (NeutralBackground, NeutralForeground),
        };
        return new CollectorChip(label, string.Join(Environment.NewLine, details), background, foreground);
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
