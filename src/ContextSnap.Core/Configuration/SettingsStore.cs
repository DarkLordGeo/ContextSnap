using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContextSnap.Configuration;

public sealed record SettingsLoadResult(AppSettings Settings, bool IsFirstRun, string? Error);

/// <summary>
/// Reads and writes <see cref="AppSettings"/> as JSON in the user's roaming AppData folder. Until the first
/// save, settings are read from <paramref name="legacyFilePath"/> when only that file exists (the folder
/// used before the rename from ContextSnap to Snapctx), so existing settings carry over.
/// </summary>
public sealed class SettingsStore(string filePath, string? legacyFilePath = null)
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Snapctx",
        "settings.json");

    /// <summary>Where ContextSnap, the product's previous name, kept its settings.</summary>
    public static string LegacyDefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Snapctx",
        "settings.json");

    public string FilePath { get; } = filePath;

    public static SettingsStore CreateDefault() => new(DefaultPath, LegacyDefaultPath);

    public SettingsLoadResult Load()
    {
        var source = FilePath;
        if (!File.Exists(source))
        {
            if (legacyFilePath is null || !File.Exists(legacyFilePath))
                return new SettingsLoadResult(new AppSettings(), IsFirstRun: true, Error: null);
            source = legacyFilePath; // left in place; the next save writes the new file
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(source), JsonOptions)
                ?? new AppSettings();
            settings.Normalize();
            return new SettingsLoadResult(settings, IsFirstRun: false, Error: null);
        }
        catch (JsonException ex)
        {
            return Fallback($"Settings file is not valid JSON (line {ex.LineNumber + 1}); defaults are in use.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fallback($"Settings file could not be read ({ex.GetType().Name}); defaults are in use.");
        }
    }

    /// <summary>Writes atomically via a temporary file. Throws <see cref="IOException"/> or
    /// <see cref="UnauthorizedAccessException"/> on failure.</summary>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporary, FilePath, overwrite: true);
    }

    private static SettingsLoadResult Fallback(string error) =>
        new(new AppSettings(), IsFirstRun: false, error);
}
