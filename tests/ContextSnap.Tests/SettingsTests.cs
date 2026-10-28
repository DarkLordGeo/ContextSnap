using ContextSnap.Configuration;

namespace ContextSnap.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ContextSnapSettings").FullName;

    private string SettingsPath => Path.Combine(_directory, "nested", "settings.json");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Defaults_ArePrivacyConservative()
    {
        var settings = new AppSettings();

        Assert.False(settings.CollectBrowserTabs);
        Assert.False(settings.IncludeBrowserUrls);
        Assert.True(settings.RedactSecrets);
        Assert.True(settings.RedactEmails);
        Assert.True(settings.HideUserIdentity);
        Assert.True(settings.ExcludeFromClipboardHistory);
        Assert.True(settings.HotkeyCopiesImmediately);
        Assert.Equal(12_000, settings.MaxOutputCharacters);
        Assert.Contains("KeePassXC", settings.ExcludedProcesses);
        Assert.Contains("InPrivate", settings.ExcludedTitleKeywords);
    }

    [Fact]
    public void Load_OlderSettingsFilesGetOneStepShortcutCopying()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """{ "enableGlobalHotkey": true, "copyAfterCapture": false }""");

        var settings = new SettingsStore(SettingsPath).Load().Settings;

        Assert.True(settings.EnableGlobalHotkey);
        Assert.True(settings.HotkeyCopiesImmediately);
    }

    [Fact]
    public void Load_ReportsFirstRunWhenFileMissing()
    {
        var result = new SettingsStore(SettingsPath).Load();

        Assert.True(result.IsFirstRun);
        Assert.Null(result.Error);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var store = new SettingsStore(SettingsPath);
        var settings = new AppSettings { CollectGit = false, MaxOutputCharacters = 5_000, ProjectFolders = [@"D:\src\app"] };

        store.Save(settings);
        var loaded = store.Load();

        Assert.False(loaded.IsFirstRun);
        Assert.False(loaded.Settings.CollectGit);
        Assert.Equal(5_000, loaded.Settings.MaxOutputCharacters);
        Assert.Equal([@"D:\src\app"], loaded.Settings.ProjectFolders);
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void Load_FallsBackToDefaultsForInvalidJson()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "{ not json");

        var result = new SettingsStore(SettingsPath).Load();

        Assert.NotNull(result.Error);
        Assert.Equal(new AppSettings().MaxOutputCharacters, result.Settings.MaxOutputCharacters);
    }

    [Fact]
    public void Load_NormalizesOutOfRangeValuesAndNullLists()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """
            { "maxOutputCharacters": 5, "maxItemsPerSection": 100000, "excludedProcesses": null,
              "customRedactionTerms": [" a ", "A", "", "b"], "collectBrowserTabs": false, "includeBrowserUrls": true }
            """);

        var settings = new SettingsStore(SettingsPath).Load().Settings;

        Assert.Equal(AppSettings.MinOutputCharacters, settings.MaxOutputCharacters);
        Assert.Equal(500, settings.MaxItemsPerSection);
        Assert.Empty(settings.ExcludedProcesses);
        Assert.Equal(["a", "b"], settings.CustomRedactionTerms);
        Assert.False(settings.IncludeBrowserUrls);
    }

    [Fact]
    public void Clone_IsDeep()
    {
        var settings = new AppSettings();
        var clone = settings.Clone();

        clone.ExcludedProcesses.Add("other");

        Assert.DoesNotContain("other", settings.ExcludedProcesses);
    }
}

public sealed class SettingsMigrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "snapctx-settings-" + Guid.NewGuid().ToString("N"));

    public SettingsMigrationTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Load_ReadsTheSettingsFromBeforeTheRenameUntilTheFirstSave()
    {
        var current = Path.Combine(_directory, "Snapctx", "settings.json");
        var legacy = Path.Combine(_directory, "ContextSnap", "settings.json");
        new SettingsStore(legacy).Save(new AppSettings { CollectGit = false });
        var store = new SettingsStore(current, legacy);

        var loaded = store.Load();
        Assert.False(loaded.IsFirstRun);
        Assert.False(loaded.Settings.CollectGit);

        store.Save(loaded.Settings);
        Assert.True(File.Exists(current));
        Assert.True(File.Exists(legacy)); // the old file is never deleted
    }

    [Fact]
    public void Load_PrefersTheNewFileAndIsFirstRunWhenNeitherExists()
    {
        var current = Path.Combine(_directory, "Snapctx", "settings.json");
        var legacy = Path.Combine(_directory, "ContextSnap", "settings.json");
        Assert.True(new SettingsStore(current, legacy).Load().IsFirstRun);

        new SettingsStore(legacy).Save(new AppSettings { CollectGit = false });
        new SettingsStore(current).Save(new AppSettings { CollectGit = true });
        Assert.True(new SettingsStore(current, legacy).Load().Settings.CollectGit);
    }
}
