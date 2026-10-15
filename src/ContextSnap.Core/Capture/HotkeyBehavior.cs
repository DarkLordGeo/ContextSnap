using ContextSnap.Collectors.ScreenText;
using ContextSnap.Model;

namespace ContextSnap.Capture;

/// <summary>What the global shortcut does with a fresh snapshot.</summary>
public static class HotkeyBehavior
{
    /// <summary>
    /// A snapshot that includes visible screen text is shown for review before anything is copied, unless
    /// the user turned on one-step copying. Snapshots without screen text are copied immediately.
    /// </summary>
    public static bool CopiesImmediately(bool hotkeyCopiesImmediately, CollectorResult<WindowText> screenText) =>
        hotkeyCopiesImmediately || screenText is not { Status: CollectorStatus.Ok, Count: > 0 };
}
