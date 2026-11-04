using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ContextSnap;

/// <summary>
/// The app's few transitions: a short fade and rise, eased out like the website (cubic-bezier(.22,1,.36,1)).
/// Skipped when Windows animations are turned off.
/// </summary>
internal static class Motion
{
    private static readonly IEasingFunction EaseOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });

    /// <summary>False when the user turned off "Animation effects" in Windows.</summary>
    public static bool Enabled => SystemParameters.ClientAreaAnimation;

    public static void FadeIn(UIElement element, double offset = 8, int milliseconds = 180)
    {
        if (!Enabled)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(milliseconds);
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = EaseOut });

        var shift = new TranslateTransform();
        element.RenderTransform = shift;
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offset, 0, duration) { EasingFunction = EaseOut });
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

internal static class AppVersion
{
    /// <summary>The product version without build metadata, for example "1.0.0".</summary>
    public static string Display
    {
        get
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(AppVersion).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var version = informational ?? assembly.GetName().Version?.ToString(3) ?? "unknown";
            var plus = version.IndexOf('+');
            return plus >= 0 ? version[..plus] : version;
        }
    }
}
