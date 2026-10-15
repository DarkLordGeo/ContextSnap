namespace ContextSnap.Collectors;

/// <summary>Browsers recognized by both the browser-tab and the visible-text collectors.</summary>
public static class KnownBrowsers
{
    /// <summary>Process name (without .exe) to display name.</summary>
    public static IReadOnlyDictionary<string, string> Names { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = "Chrome",
        ["msedge"] = "Edge",
        ["brave"] = "Brave",
        ["vivaldi"] = "Vivaldi",
        ["opera"] = "Opera",
        ["firefox"] = "Firefox",
        ["arc"] = "Arc",
        ["thorium"] = "Thorium",
    };
}
