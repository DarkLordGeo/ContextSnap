using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ContextSnap.Notifications;

/// <summary>A critically damped spring: a quick rise with a small (about 8%) overshoot that settles exactly at 1.</summary>
public sealed class SpringEase : EasingFunctionBase
{
    private const double Damping = 7;
    private const double Frequency = 9;
    private static readonly double EndValue = Curve(1);

    public SpringEase() => EasingMode = EasingMode.EaseIn; // EaseInCore is used as-is.

    protected override double EaseInCore(double normalizedTime) => Curve(normalizedTime) / EndValue;

    protected override Freezable CreateInstanceCore() => new SpringEase();

    private static double Curve(double t) => 1 - Math.Exp(-Damping * t) * Math.Cos(Frequency * t);
}

/// <summary>Honors the Windows "Animation effects" setting (Settings → Accessibility → Visual effects).</summary>
public static class MotionPreference
{
    /// <summary>Diagnostic override: set to 1 to force reduced motion, 0 to force full motion.</summary>
    public const string OverrideVariable = "SNAPCTX_REDUCED_MOTION";
    public const string LegacyOverrideVariable = "CONTEXTSNAP_REDUCED_MOTION";

    public static bool IsReduced => (Environment.GetEnvironmentVariable(OverrideVariable) ?? Environment.GetEnvironmentVariable(LegacyOverrideVariable)) switch
    {
        "1" => true,
        "0" => false,
        _ => !SystemParameters.ClientAreaAnimation,
    };
}

/// <summary>Toast colors for the Windows app theme (light or dark), or system colors in high contrast.</summary>
public sealed record ToastPalette(
    Brush Card, Brush Border, Brush Title, Brush Body, Brush Accent, Brush Glyph,
    Brush WarningBackground, Brush WarningText, Brush ProgressTrack,
    Brush ShadowOuter, Brush ShadowMiddle, Brush ShadowInner)
{
    /// <summary>Diagnostic override: "light" or "dark".</summary>
    public const string ThemeOverrideVariable = "SNAPCTX_TOAST_THEME";
    public const string LegacyThemeOverrideVariable = "CONTEXTSNAP_TOAST_THEME";

    public static ToastPalette For(CopyNotificationKind kind)
    {
        if (SystemParameters.HighContrast)
            return HighContrast();

        return UsesDarkTheme() ? Dark(kind) : Light(kind);
    }

    public static bool UsesDarkTheme()
    {
        switch ((Environment.GetEnvironmentVariable(ThemeOverrideVariable) ?? Environment.GetEnvironmentVariable(LegacyThemeOverrideVariable))?.ToLowerInvariant())
        {
            case "dark":
                return true;
            case "light":
                return false;
        }

        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }

    private static ToastPalette Light(CopyNotificationKind kind) => new(
        Card: Solid(0xF7, 0xFB, 0xFC, 0xFD),
        Border: Solid(0x1F, 0x00, 0x00, 0x00),
        Title: Solid(0xFF, 0x1A, 0x1D, 0x21),
        Body: Solid(0xFF, 0x5B, 0x63, 0x6B),
        Accent: kind switch
        {
            CopyNotificationKind.Success => Solid(0xFF, 0x1E, 0x8E, 0x3E),
            CopyNotificationKind.Error => Solid(0xFF, 0xC5, 0x22, 0x1F),
            _ => Solid(0xFF, 0xB0, 0x60, 0x00),
        },
        Glyph: Solid(0xFF, 0xFF, 0xFF, 0xFF),
        WarningBackground: Solid(0xFF, 0xFD, 0xF1, 0xDC),
        WarningText: Solid(0xFF, 0x7A, 0x43, 0x00),
        ProgressTrack: Solid(0x14, 0x00, 0x00, 0x00),
        ShadowOuter: Solid(0x0A, 0x00, 0x00, 0x00),
        ShadowMiddle: Solid(0x10, 0x00, 0x00, 0x00),
        ShadowInner: Solid(0x16, 0x00, 0x00, 0x00));

    private static ToastPalette Dark(CopyNotificationKind kind) => new(
        Card: Solid(0xF2, 0x2B, 0x2D, 0x31),
        Border: Solid(0x2E, 0xFF, 0xFF, 0xFF),
        Title: Solid(0xFF, 0xF2, 0xF3, 0xF5),
        Body: Solid(0xFF, 0xAE, 0xB4, 0xBC),
        Accent: kind switch
        {
            CopyNotificationKind.Success => Solid(0xFF, 0x3F, 0xC8, 0x6E),
            CopyNotificationKind.Error => Solid(0xFF, 0xFF, 0x6B, 0x6B),
            _ => Solid(0xFF, 0xF5, 0xB8, 0x3D),
        },
        Glyph: Solid(0xFF, 0x12, 0x14, 0x17),
        WarningBackground: Solid(0x33, 0xF5, 0xB8, 0x3D),
        WarningText: Solid(0xFF, 0xF8, 0xD3, 0x8A),
        ProgressTrack: Solid(0x1F, 0xFF, 0xFF, 0xFF),
        ShadowOuter: Solid(0x14, 0x00, 0x00, 0x00),
        ShadowMiddle: Solid(0x20, 0x00, 0x00, 0x00),
        ShadowInner: Solid(0x2C, 0x00, 0x00, 0x00));

    private static ToastPalette HighContrast() => new(
        SystemColors.WindowBrush, SystemColors.WindowTextBrush, SystemColors.WindowTextBrush, SystemColors.WindowTextBrush,
        SystemColors.HighlightBrush, SystemColors.HighlightTextBrush, SystemColors.WindowBrush, SystemColors.WindowTextBrush,
        SystemColors.WindowBrush, Brushes.Transparent, Brushes.Transparent, Brushes.Transparent);

    private static SolidColorBrush Solid(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// Shows copy notifications in the GUI process. Calls return immediately (work is queued to the UI
/// thread), and any toast failure is contained and reported through <see cref="Failed"/>.
/// </summary>
public sealed class WpfCopyNotifier(Dispatcher dispatcher) : ICopyNotifier
{
    private ToastWindow? _window;
    private ToastController? _controller;

    public event Action<string>? Failed;

    public void Show(CopyNotificationContent content) => dispatcher.InvokeAsync(() => Display(content));

    private void Display(CopyNotificationContent content)
    {
        try
        {
            if (_window is null)
            {
                _window = new ToastWindow();
                _controller = new ToastController(_window);
            }

            _controller!.Show(content);
        }
        catch (Exception ex)
        {
            // A broken toast must never affect copying; drop the window so the next copy starts fresh.
            Failed?.Invoke($"{ex.GetType().Name}: {ex.Message}");
            ResetWindow();
        }
    }

    private void ResetWindow()
    {
        var window = _window;
        _window = null;
        _controller = null;
        try
        {
            window?.Close();
        }
        catch (InvalidOperationException)
        {
            // The window was already closing.
        }
    }
}
