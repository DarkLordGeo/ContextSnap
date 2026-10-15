using System.Globalization;
using System.Text;
using ContextSnap.Collectors.ScreenText;
using ContextSnap.Model;
using ContextSnap.Privacy;

namespace ContextSnap.Formatting;

/// <summary>
/// Turns a <see cref="ContextSnapshot"/> into compact Markdown. Every captured string passes
/// through the <see cref="Redactor"/> and is then escaped or code-quoted, so redaction cannot be
/// bypassed by a section that forgets to call it.
/// </summary>
public sealed class MarkdownFormatter(Redactor redactor)
{
    public const int DefaultMaxCharacters = 12_000;
    public const string TruncationNotice = "\n\n_[Snapshot truncated by the output limit.]_\n";

    private const int MaxFieldLength = 240;
    private const int MaxListedProcessIds = 3;

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public string Format(ContextSnapshot snapshot)
    {
        var md = new StringBuilder();
        md.AppendLine("# Context Snapshot");
        md.AppendLine();
        md.AppendLine(Culture, $"_Captured {snapshot.CapturedAt:yyyy-MM-dd HH:mm} (UTC{snapshot.CapturedAt:zzz}) by ContextSnap. Sensitive values may be redacted._");

        var foreground = snapshot.Windows.Items.FirstOrDefault(w => w.IsForeground);
        if (foreground is not null)
        {
            md.AppendLine();
            md.AppendLine($"**Foreground window:** {Text(foreground.ProcessName)} — {Text(foreground.Title)}");
        }

        AppendScreenText(md, snapshot.ScreenText);

        md.AppendLine();
        md.AppendLine("## System context");

        AppendWindows(md, snapshot.Windows);
        AppendGit(md, snapshot.Git);
        AppendExplorer(md, snapshot.ExplorerFolders);
        AppendProcesses(md, snapshot.Processes);
        AppendBrowserTabs(md, snapshot.BrowserTabs);
        AppendDesktop(md, snapshot.Desktop);
        AppendSystem(md, snapshot.SystemInfo);

        var disabled = snapshot.AllResults
            .Where(r => r.Status == CollectorStatus.Disabled)
            .Select(r => r.Name)
            .ToArray();
        if (disabled.Length > 0)
        {
            md.AppendLine();
            md.AppendLine($"_Not collected (turned off): {string.Join(", ", disabled)}._");
        }

        return md.ToString();
    }

    /// <summary>
    /// Truncates at a line boundary when possible and appends a visible notice. When the cut falls inside
    /// a fenced code block (such as visible screen text), the block is closed first, so the notice is never
    /// part of captured content and the result never has an unbalanced fence.
    /// </summary>
    public static string LimitOutput(string markdown, int maxCharacters = DefaultMaxCharacters)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCharacters);

        if (markdown.Length <= maxCharacters)
            return markdown;

        if (maxCharacters <= TruncationNotice.Length)
            return TruncationNotice[..maxCharacters];

        var budget = maxCharacters - TruncationNotice.Length;
        while (true)
        {
            var head = Head(markdown, budget);
            var (fence, fenceLineStart) = OpenFence(head);
            if (fence is null)
                return head + TruncationNotice;

            // Nothing of the block would remain: end before its opening line instead of leaving it empty.
            if (head.IndexOf('\n', fenceLineStart) < 0)
                return markdown[..fenceLineStart].TrimEnd() + TruncationNotice;

            var closing = "\n" + fence;
            if (head.Length + closing.Length + TruncationNotice.Length <= maxCharacters)
                return head + closing + TruncationNotice;

            // Make room for the closing fence. If even that is not possible, end before the open block.
            var smaller = maxCharacters - TruncationNotice.Length - closing.Length;
            if (smaller <= 0 || smaller >= budget)
                return markdown[..fenceLineStart].TrimEnd() + TruncationNotice;
            budget = smaller;
        }
    }

    /// <summary>The first <paramref name="budget"/> characters, cut back to a line break in the second half when there is one.</summary>
    private static string Head(string markdown, int budget)
    {
        var cut = budget;
        var lastNewline = markdown.LastIndexOf('\n', cut - 1);
        if (lastNewline > cut / 2)
            cut = lastNewline;
        if (char.IsHighSurrogate(markdown[cut - 1]))
            cut--;
        return markdown[..cut].TrimEnd();
    }

    /// <summary>
    /// The backtick fence of a code block left open at the end of <paramref name="text"/>, and where its line
    /// starts; null when every block is closed. Fences start at the beginning of a line, as the formatter writes them.
    /// </summary>
    internal static (string? Fence, int LineStart) OpenFence(string text)
    {
        string? open = null;
        var openLineStart = 0;
        var lineStart = 0;
        while (lineStart <= text.Length)
        {
            var lineEnd = text.IndexOf('\n', lineStart);
            if (lineEnd < 0)
                lineEnd = text.Length;
            var line = text.AsSpan(lineStart, lineEnd - lineStart).TrimEnd('\r');
            var run = 0;
            while (run < line.Length && line[run] == '`')
                run++;

            if (open is null)
            {
                if (run >= 3)
                {
                    open = new string('`', run);
                    openLineStart = lineStart;
                }
            }
            else if (run >= open.Length && line[run..].Trim().IsEmpty)
            {
                open = null;
            }

            lineStart = lineEnd + 1;
        }

        return (open, openLineStart);
    }

    /// <summary>Visible text goes first because it is usually the most useful context.</summary>
    private void AppendScreenText(StringBuilder md, CollectorResult<WindowText> result)
    {
        if (result.Status == CollectorStatus.Disabled)
            return;

        md.AppendLine();
        md.AppendLine("## Visible text");
        md.AppendLine();

        switch (result.Status)
        {
            case CollectorStatus.Empty:
                md.AppendLine("_No visible text could be extracted; the system context below is still included._");
                AppendNotes(md, result);
                return;
            case CollectorStatus.Unavailable:
            case CollectorStatus.Failed:
                md.AppendLine($"_Visible text could not be read: {Text(result.Error ?? "unknown error")}_");
                AppendNotes(md, result);
                return;
        }

        md.AppendLine($"_Read on this PC from the screen ({Text(ScreenTextSummary.Describe(result.Items))}). Sensitive values may be redacted._");

        foreach (var window in result.Items.Where(w => w.IsFocused))
            AppendWindowText(md, window, $"### Active window: {Text(window.ProcessName)} — {Text(window.Title)}");

        var others = result.Items.Where(w => !w.IsFocused).ToArray();
        if (others.Length > 0)
        {
            md.AppendLine();
            md.AppendLine("## Other visible windows");
            foreach (var window in others)
                AppendWindowText(md, window, $"### {Text(window.ProcessName)} — {Text(window.Title)}");
        }

        if (result.OmittedCount > 0)
        {
            md.AppendLine();
            md.AppendLine(Culture, $"_{result.OmittedCount} more window(s) not read (window limit)._");
        }

        AppendNotes(md, result);
    }

    private void AppendWindowText(StringBuilder md, WindowText window, string heading)
    {
        md.AppendLine();
        md.AppendLine(heading);
        md.AppendLine();

        var details = $"Source: {ScreenTextSummary.SourceLabel(window.Source)} · {window.Text.Length.ToString("N0", Culture)} characters";
        if (window.Truncated)
            details += " · shortened to the per-window limit";
        md.AppendLine($"_{details}_");

        foreach (var warning in window.Warnings)
        {
            md.AppendLine();
            md.AppendLine($"_Note: {Text(warning)}_");
        }

        if (window.Text.Length == 0)
            return;

        var body = MarkdownText.Block(redactor.Redact(window.Text));
        var fence = MarkdownText.Fence(body);
        md.AppendLine();
        md.AppendLine(fence + "text");
        md.AppendLine(body);
        md.AppendLine(fence);
    }

    private void AppendWindows(StringBuilder md, CollectorResult<WindowInfo> result)
    {
        if (!StartSection(md, result, "Open windows", ", most recently used first", "No visible application windows found."))
            return;

        foreach (var window in result.Items)
        {
            var marker = window.IsForeground ? " _(foreground)_" : string.Empty;
            md.AppendLine($"- **{Text(window.ProcessName)}**: {Text(window.Title)}{marker}");
        }

        EndSection(md, result);
    }

    private void AppendGit(StringBuilder md, CollectorResult<GitRepositoryInfo> result)
    {
        if (!StartSection(md, result, "Git repositories", string.Empty,
                "No Git repositories found in open File Explorer folders or configured project folders."))
            return;

        var first = true;
        foreach (var repo in result.Items)
        {
            if (!first)
                md.AppendLine();
            first = false;
            md.AppendLine($"#### {Text(LastSegment(repo.Root))} — {Code(repo.Root)}");
            md.AppendLine();

            if (repo.Error is not null)
            {
                md.AppendLine($"- _Could not read repository: {Text(repo.Error)}_");
                continue;
            }

            md.AppendLine($"- {DescribeBranch(repo)}");
            md.AppendLine($"- Working tree: {DescribeWorkingTree(repo)}");

            if (repo.Changes.Count > 0)
            {
                md.AppendLine("- Changed files:");
                foreach (var change in repo.Changes)
                {
                    var original = change.OriginalPath is null ? string.Empty : $" ← {Code(change.OriginalPath)}";
                    md.AppendLine($"  - {MarkdownText.Code(change.Code)} {Code(change.Path)}{original}");
                }

                var hidden = repo.TotalChanges - repo.Changes.Count;
                if (hidden > 0)
                    md.AppendLine(Culture, $"  - _…and {hidden} more._");
            }

            if (repo.RecentCommits.Count > 0)
            {
                md.AppendLine("- Recent commits:");
                foreach (var commit in repo.RecentCommits)
                {
                    var date = string.IsNullOrWhiteSpace(commit.RelativeDate) ? string.Empty : $" ({Text(commit.RelativeDate)})";
                    md.AppendLine($"  - {Code(commit.Hash)} {Text(commit.Subject)}{date}");
                }
            }
        }

        EndSection(md, result);
    }

    private void AppendExplorer(StringBuilder md, CollectorResult<ExplorerFolder> result)
    {
        if (!StartSection(md, result, "File Explorer folders", string.Empty, "No open File Explorer folders found."))
            return;

        foreach (var folder in result.Items)
            md.AppendLine($"- {Code(folder.Path)}");

        EndSection(md, result);
    }

    private void AppendProcesses(StringBuilder md, CollectorResult<ProcessGroup> result)
    {
        if (!StartSection(md, result, "Developer tools running", string.Empty, "None of the configured developer tools are running."))
            return;

        foreach (var group in result.Items)
        {
            var ids = group.ProcessIds;
            var count = ids.Count switch
            {
                1 => $" (PID {ids[0].ToString(Culture)})",
                <= MaxListedProcessIds => $" ×{ids.Count} (PIDs {string.Join(", ", ids.Select(i => i.ToString(Culture)))})",
                _ => $" ×{ids.Count}",
            };
            var path = group.ExecutablePath is null ? string.Empty : $" — {Code(group.ExecutablePath)}";
            md.AppendLine($"- **{Text(group.Name)}**{count}{path}");
        }

        EndSection(md, result);
    }

    private void AppendBrowserTabs(StringBuilder md, CollectorResult<BrowserTab> result)
    {
        if (!StartSection(md, result, "Browser tabs", string.Empty, "No browser tabs found in supported browsers."))
            return;

        foreach (var tab in result.Items)
        {
            var active = tab.IsActive ? " _(active)_" : string.Empty;
            var url = string.IsNullOrWhiteSpace(tab.Url) ? string.Empty : $" — {Url(tab.Url)}";
            md.AppendLine($"- **{Text(tab.Browser)}**: {Text(tab.Title)}{active}{url}");
        }

        EndSection(md, result);
    }

    private void AppendDesktop(StringBuilder md, CollectorResult<DesktopEntry> result)
    {
        if (!StartSection(md, result, "Desktop items", ", most recently modified first", "The Desktop folder is empty."))
            return;

        foreach (var entry in result.Items)
        {
            if (entry.IsDirectory)
                md.AppendLine($"- {Code(entry.Name + "/")}");
            else if (entry.SizeBytes is { } size)
                md.AppendLine($"- {Code(entry.Name)} ({FormatSize(size)})");
            else
                md.AppendLine($"- {Code(entry.Name)}");
        }

        EndSection(md, result);
    }

    private void AppendSystem(StringBuilder md, CollectorResult<SystemSummary> result)
    {
        if (!StartSection(md, result, "System", null, "No system information available."))
            return;

        foreach (var system in result.Items)
        {
            var parts = new List<string>
            {
                Text(system.OperatingSystem),
                Text(system.Architecture),
                $"{system.LogicalProcessors.ToString(Culture)} logical processors",
            };
            if (system.TotalMemoryBytes is { } memory)
                parts.Add($"{FormatSize(memory)} memory");
            md.AppendLine($"- {string.Join(" · ", parts)}");
        }

        EndSection(md, result);
    }

    /// <summary>Writes the heading and, for non-OK results, the status line. Returns true when items should be written.</summary>
    private bool StartSection<T>(StringBuilder md, CollectorResult<T> result, string title, string? countSuffix, string emptyMessage)
    {
        if (result.Status == CollectorStatus.Disabled)
            return false;

        md.AppendLine();
        md.AppendLine(result.Status == CollectorStatus.Ok && countSuffix is not null
            ? $"### {title} ({result.TotalCount.ToString(Culture)}{countSuffix})"
            : $"### {title}");
        md.AppendLine();

        switch (result.Status)
        {
            case CollectorStatus.Ok:
                return true;
            case CollectorStatus.Empty:
                md.AppendLine($"_{emptyMessage}_");
                break;
            case CollectorStatus.Unavailable:
                md.AppendLine($"_Unavailable: {Text(result.Error ?? "data source not found")}_");
                break;
            default:
                md.AppendLine($"_Collection failed: {Text(result.Error ?? "unknown error")}_");
                break;
        }

        AppendNotes(md, result);
        return false;
    }

    private void EndSection(StringBuilder md, ICollectorResult result)
    {
        if (result.OmittedCount > 0)
            md.AppendLine(Culture, $"- _…and {result.OmittedCount} more (item limit reached)._");
        AppendNotes(md, result);
    }

    private void AppendNotes(StringBuilder md, ICollectorResult result)
    {
        foreach (var note in result.Notes)
        {
            md.AppendLine();
            md.AppendLine($"_{Text(note)}_");
        }
    }

    private string DescribeBranch(GitRepositoryInfo repo)
    {
        if (repo.IsDetached)
            return $"HEAD: detached at {Code(repo.HeadShortHash ?? "unknown")}";

        var branch = $"Branch: {Code(repo.Branch ?? "unknown")}";
        if (repo.HasNoCommits)
            return branch + " (no commits yet)";

        if (repo.Upstream is null)
            return branch + " (no upstream)";

        var sync = (repo.Ahead, repo.Behind) switch
        {
            (0, 0) => "up to date",
            (var a, 0) => $"ahead {a}",
            (0, var b) => $"behind {b}",
            var (a, b) => $"ahead {a}, behind {b}",
        };
        return $"{branch} → {Code(repo.Upstream)} ({sync})";
    }

    private static string DescribeWorkingTree(GitRepositoryInfo repo)
    {
        if (repo.TotalChanges == 0)
            return "clean";

        var parts = new List<string>();
        if (repo.Staged > 0) parts.Add($"{repo.Staged} staged");
        if (repo.Unstaged > 0) parts.Add($"{repo.Unstaged} unstaged");
        if (repo.Untracked > 0) parts.Add($"{repo.Untracked} untracked");
        if (repo.Conflicted > 0) parts.Add($"{repo.Conflicted} conflicted");
        return string.Join(", ", parts);
    }

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes.ToString(Culture)} B"
            : $"{value.ToString(value >= 10 ? "0" : "0.#", Culture)} {units[unit]}";
    }

    private static string LastSegment(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }

    private string Text(string? value) =>
        MarkdownText.Escape(MarkdownText.SingleLine(redactor.Redact(value), MaxFieldLength));

    private string Code(string? value) =>
        MarkdownText.Code(MarkdownText.SingleLine(redactor.Redact(value), MaxFieldLength));

    private string Url(string? value) =>
        MarkdownText.Code(MarkdownText.SingleLine(redactor.RedactUrl(value), MaxFieldLength));
}
