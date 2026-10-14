namespace ContextSnap.Model;

public sealed record ContextSnapshot(
    DateTimeOffset CapturedAt,
    CollectorResult<WindowInfo> Windows,
    CollectorResult<ProcessGroup> Processes,
    CollectorResult<ExplorerFolder> ExplorerFolders,
    CollectorResult<GitRepositoryInfo> Git,
    CollectorResult<DesktopEntry> Desktop,
    CollectorResult<BrowserTab> BrowserTabs,
    CollectorResult<SystemSummary> SystemInfo,
    CollectorResult<WindowText> ScreenText)
{
    public IReadOnlyList<ICollectorResult> AllResults =>
        [ScreenText, Windows, Git, ExplorerFolders, Processes, BrowserTabs, Desktop, SystemInfo];
}

[Flags]
public enum ScreenTextSource
{
    None = 0,
    UiAutomation = 1,
    Ocr = 2,
}

/// <summary>Text read from one window. Held only in memory for the current snapshot.</summary>
/// <param name="Text">Visible text in reading order, already limited to the per-window character budget.</param>
/// <param name="UiaCharacters">Characters contributed by UI Automation.</param>
/// <param name="OcrCharacters">Characters contributed by OCR.</param>
public sealed record WindowText(
    string ProcessName,
    string Title,
    bool IsFocused,
    ScreenTextSource Source,
    string Text,
    int UiaCharacters,
    int OcrCharacters,
    bool Truncated,
    IReadOnlyList<string> Warnings,
    TimeSpan Elapsed);

public sealed record WindowInfo(string ProcessName, string Title, int ProcessId, bool IsForeground);

/// <summary>One or more running processes that share a name and executable.</summary>
public sealed record ProcessGroup(string Name, string? ExecutablePath, IReadOnlyList<int> ProcessIds);

public sealed record ExplorerFolder(string Path);

public sealed record DesktopEntry(string Name, bool IsDirectory, long? SizeBytes, DateTime LastModified);

public sealed record BrowserTab(string Browser, string Title, string? Url, bool IsActive);

public sealed record SystemSummary(
    string OperatingSystem,
    string Architecture,
    int LogicalProcessors,
    long? TotalMemoryBytes);

public sealed record GitChange(string Code, string Path, string? OriginalPath = null);

public sealed record GitCommit(string Hash, string Subject, string? RelativeDate);

public sealed record GitRepositoryInfo(string Root)
{
    public string? Branch { get; init; }
    public bool IsDetached { get; init; }
    public bool HasNoCommits { get; init; }
    public string? HeadShortHash { get; init; }
    public string? Upstream { get; init; }
    public int Ahead { get; init; }
    public int Behind { get; init; }
    public int Staged { get; init; }
    public int Unstaged { get; init; }
    public int Untracked { get; init; }
    public int Conflicted { get; init; }
    public IReadOnlyList<GitChange> Changes { get; init; } = [];
    public int TotalChanges { get; init; }
    public IReadOnlyList<GitCommit> RecentCommits { get; init; } = [];

    /// <summary>Set when Git could not read the repository; other fields are then unreliable.</summary>
    public string? Error { get; init; }

    public bool IsClean => Error is null && TotalChanges == 0;
}
