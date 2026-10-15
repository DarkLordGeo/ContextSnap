using System.Globalization;

namespace ContextSnap.Platform;

/// <summary>
/// A global shortcut such as "Ctrl+Alt+Shift+C", parsed into RegisterHotKey modifiers and a virtual key.
/// At least one of Ctrl, Alt, or Win is required so ordinary typing is never intercepted.
/// </summary>
public sealed record HotkeyGesture(uint Modifiers, uint VirtualKey, string DisplayText)
{
    public const string DefaultText = "Ctrl+Alt+Shift+C";

    public const uint ModAlt = 0x1;
    public const uint ModControl = 0x2;
    public const uint ModShift = 0x4;
    public const uint ModWin = 0x8;

    public static HotkeyGesture Default { get; } = Parse(DefaultText)!;

    /// <summary>Returns null when the text is not a valid shortcut.</summary>
    public static HotkeyGesture? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        uint modifiers = 0;
        uint? key = null;
        string? keyName = null;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries))
        {
            switch (raw.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL":
                    modifiers |= ModControl;
                    continue;
                case "ALT":
                    modifiers |= ModAlt;
                    continue;
                case "SHIFT":
                    modifiers |= ModShift;
                    continue;
                case "WIN" or "WINDOWS":
                    modifiers |= ModWin;
                    continue;
            }

            if (key is not null || !TryKey(raw, out var virtualKey, out var name))
                return null; // Unknown token or a second key.
            key = virtualKey;
            keyName = name;
        }

        if (key is null || (modifiers & (ModControl | ModAlt | ModWin)) == 0)
            return null;

        var parts = new List<string>();
        if ((modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((modifiers & ModShift) != 0) parts.Add("Shift");
        if ((modifiers & ModWin) != 0) parts.Add("Win");
        parts.Add(keyName!);
        return new HotkeyGesture(modifiers, key.Value, string.Join('+', parts));
    }

    private static bool TryKey(string token, out uint virtualKey, out string name)
    {
        var upper = token.ToUpperInvariant();
        virtualKey = 0;
        name = upper;
        if (upper.Length == 1 && (char.IsAsciiLetterUpper(upper[0]) || char.IsAsciiDigit(upper[0])))
        {
            virtualKey = upper[0]; // VK codes for A–Z and 0–9 equal their ASCII values.
            return true;
        }

        if (upper.Length is >= 2 and <= 3 && upper[0] == 'F'
            && int.TryParse(upper.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + number - 1); // VK_F1 = 0x70.
            return true;
        }

        return false;
    }
}
