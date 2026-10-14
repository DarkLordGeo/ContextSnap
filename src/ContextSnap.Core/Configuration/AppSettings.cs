using System.Text.Json;

namespace ContextSnap.Configuration;

public enum ScreenTextMode
{
    /// <summary>Only the window the user is working in.</summary>
    FocusedWindow,

    /// <summary>The focused window plus other visible application windows.</summary>
    AllVisibleWindows,
}

/// <summary>
/// User preferences. Defaults are deliberately conservative: browser tabs are off,
/// redaction is on, and output is bounded.
/// </summary>
public sealed class AppSettings
{
    public const int MinOutputCharacters = 1_000;
    public const int MaxOutputCharactersLimit = 200_000;
    public const int MaxListEntries = 200;
    public const int MaxListEntryLength = 260;

    public int Version { get; set; } = 1;

    // Collectors
    public bool CollectWindows { get; set; } = true;
    public bool CollectProcesses { get; set; } = true;
    public bool CollectExplorerFolders { get; set; } = true;
    public bool CollectGit { get; set; } = true;
    public bool CollectDesktop { get; set; } = true;
    public bool CollectSystemSummary { get; set; } = true;
    public bool CollectBrowserTabs { get; set; }
    public bool IncludeBrowserUrls { get; set; }

    // Visible screen text
    public bool CaptureScreenText { get; set; } = true;
    public ScreenTextMode ScreenTextMode { get; set; } = ScreenTextMode.FocusedWindow;
    public bool ScreenTextUseOcr { get; set; } = true;

    /// <summary>Also include accessible text that the app reports as scrolled out of view.</summary>
    public bool ScreenTextIncludeOffscreen { get; set; }

    public int MaxScreenTextCharactersPerWindow { get; set; } = 4_000;
    public int MaxScreenTextWindows { get; set; } = 4;
    public int ScreenTextTimeoutSeconds { get; set; } = 5;

    /// <summary>Process names or window-title keywords whose text is never read (in addition to the general exclusions).</summary>
    public List<string> ScreenTextExclusions { get; set; } = [];

    // Limits
    public int MaxOutputCharacters { get; set; } = 12_000;
    public int MaxItemsPerSection { get; set; } = 40;
    public int MaxDesktopItems { get; set; } = 40;
    public int MaxRepositories { get; set; } = 5;
    public int MaxChangedFilesPerRepository { get; set; } = 15;
    public int CollectorTimeoutSeconds { get; set; } = 8;

    // Privacy
    public bool RedactSecrets { get; set; } = true;
    public bool RedactEmails { get; set; } = true;
    public bool HideUserIdentity { get; set; } = true;
    public bool StripUrlQueryStrings { get; set; } = true;
    public bool IncludeExecutablePaths { get; set; } = true;
    public bool IncludeGitChangedFiles { get; set; } = true;
    public bool ExcludeFromClipboardHistory { get; set; } = true;

    public List<string> ExcludedProcesses { get; set; } = [.. DefaultExcludedProcesses];
    public List<string> ExcludedTitleKeywords { get; set; } = [.. DefaultExcludedTitleKeywords];
    public List<string> ExcludedPathPrefixes { get; set; } = [];
    public List<string> CustomRedactionTerms { get; set; } = [];
    public List<string> ProjectFolders { get; set; } = [];
    public List<string> DeveloperProcesses { get; set; } = [.. DefaultDeveloperProcesses];

    // Behavior
    public bool EnableGlobalHotkey { get; set; } = true;
    public bool CopyAfterCapture { get; set; }

    /// <summary>Adds a shortcut to the current user's Startup folder so ContextSnap opens minimized at sign-in.</summary>
    public bool StartWithWindows { get; set; } = true;

    /// <summary>Shows a small bottom-right notification after each copy. Never affects copying itself.</summary>
    public bool ShowCopyNotifications { get; set; } = true;

    public static IReadOnlyList<string> DefaultExcludedProcesses { get; } =
    [
        "KeePass", "KeePassXC", "1Password", "Bitwarden", "LastPass", "Dashlane",
        "Enpass", "RoboForm", "NordPass", "ProtonPass",
    ];

    public static IReadOnlyList<string> DefaultExcludedTitleKeywords { get; } =
    [
        "InPrivate", "Incognito", "Private Browsing", "Password",
    ];

    public static IReadOnlyList<string> DefaultDeveloperProcesses { get; } =
    [
        // Editors and IDEs
        "Code", "Code - Insiders", "Cursor", "Windsurf", "devenv", "rider64", "idea64",
        "pycharm64", "webstorm64", "clion64", "goland64", "phpstorm64", "rustrover64",
        "studio64", "sublime_text", "notepad++", "zed", "nvim", "vim", "emacs",
        // Terminals and shells
        "WindowsTerminal", "pwsh", "powershell", "cmd", "bash", "wsl", "mintty",
        // Toolchains and runtimes
        "git", "gh", "node", "dotnet", "python", "py", "java", "go", "cargo",
        "docker", "Docker Desktop", "ollama", "claude",
        // Creative tools
        "blender", "Unity", "Unity Hub", "UnrealEditor", "Godot",
    ];

    public AppSettings Clone()
    {
        var json = JsonSerializer.Serialize(this, SettingsStore.JsonOptions);
        return JsonSerializer.Deserialize<AppSettings>(json, SettingsStore.JsonOptions)!;
    }

    /// <summary>Clamps numbers into safe ranges and cleans list entries. Idempotent.</summary>
    public void Normalize()
    {
        MaxOutputCharacters = Math.Clamp(MaxOutputCharacters, MinOutputCharacters, MaxOutputCharactersLimit);
        MaxItemsPerSection = Math.Clamp(MaxItemsPerSection, 1, 500);
        MaxDesktopItems = Math.Clamp(MaxDesktopItems, 1, 500);
        MaxRepositories = Math.Clamp(MaxRepositories, 1, 20);
        MaxChangedFilesPerRepository = Math.Clamp(MaxChangedFilesPerRepository, 0, 200);
        CollectorTimeoutSeconds = Math.Clamp(CollectorTimeoutSeconds, 2, 60);
        MaxScreenTextCharactersPerWindow = Math.Clamp(MaxScreenTextCharactersPerWindow, 200, 50_000);
        MaxScreenTextWindows = Math.Clamp(MaxScreenTextWindows, 1, 12);
        ScreenTextTimeoutSeconds = Math.Clamp(ScreenTextTimeoutSeconds, 1, 30);
        if (!Enum.IsDefined(ScreenTextMode))
            ScreenTextMode = ScreenTextMode.FocusedWindow;

        ExcludedProcesses = CleanList(ExcludedProcesses);
        ExcludedTitleKeywords = CleanList(ExcludedTitleKeywords);
        ExcludedPathPrefixes = CleanList(ExcludedPathPrefixes);
        CustomRedactionTerms = CleanList(CustomRedactionTerms);
        ProjectFolders = CleanList(ProjectFolders);
        ScreenTextExclusions = CleanList(ScreenTextExclusions);
        DeveloperProcesses = CleanList(DeveloperProcesses);

        if (!CollectBrowserTabs)
            IncludeBrowserUrls = false;
    }

    private static List<string> CleanList(List<string>? values) =>
        (values ?? [])
            .Where(v => v is not null)
            .Select(v => v.Trim())
            .Where(v => v.Length > 0 && v.Length <= MaxListEntryLength)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxListEntries)
            .ToList();
}
