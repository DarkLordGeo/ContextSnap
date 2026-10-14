using System.Globalization;
using ContextSnap.Model;

namespace ContextSnap.Collectors.Git;

/// <summary>Parses <c>git status --porcelain=v2 --branch -z</c> output.</summary>
public static class GitStatusParser
{
    public static GitRepositoryInfo Parse(string root, string output, int maxListedChanges)
    {
        string? oid = null, branch = null, upstream = null;
        bool detached = false, initial = false;
        int ahead = 0, behind = 0, staged = 0, unstaged = 0, untracked = 0, conflicted = 0, total = 0;
        var changes = new List<GitChange>();

        void AddChange(string code, string path, string? originalPath)
        {
            total++;
            if (changes.Count < maxListedChanges)
                changes.Add(new GitChange(code, path, originalPath));
        }

        var fields = output.Split('\0');
        for (var i = 0; i < fields.Length; i++)
        {
            var entry = fields[i];
            if (entry.Length < 2)
                continue;

            if (entry.StartsWith("# ", StringComparison.Ordinal))
            {
                ParseHeader(entry, ref oid, ref branch, ref upstream, ref detached, ref initial, ref ahead, ref behind);
                continue;
            }

            switch (entry[0])
            {
                case '1':
                {
                    var parts = entry.Split(' ', 9);
                    if (parts.Length < 9)
                        break;
                    CountXy(parts[1], ref staged, ref unstaged);
                    AddChange(DisplayCode(parts[1]), parts[8], null);
                    break;
                }
                case '2':
                {
                    var parts = entry.Split(' ', 10);
                    var original = i + 1 < fields.Length ? fields[++i] : null;
                    if (parts.Length < 10)
                        break;
                    CountXy(parts[1], ref staged, ref unstaged);
                    AddChange(DisplayCode(parts[1]), parts[9], original);
                    break;
                }
                case 'u':
                {
                    var parts = entry.Split(' ', 11);
                    if (parts.Length < 11)
                        break;
                    conflicted++;
                    AddChange(parts[1], parts[10], null);
                    break;
                }
                case '?':
                    untracked++;
                    AddChange("??", entry[2..], null);
                    break;
            }
        }

        return new GitRepositoryInfo(root)
        {
            Branch = branch,
            IsDetached = detached,
            HasNoCommits = initial,
            HeadShortHash = oid is { Length: >= 7 } ? oid[..7] : oid,
            Upstream = upstream,
            Ahead = ahead,
            Behind = behind,
            Staged = staged,
            Unstaged = unstaged,
            Untracked = untracked,
            Conflicted = conflicted,
            Changes = changes,
            TotalChanges = total,
        };
    }

    /// <summary>Parses <c>git log --format=%h%x1f%s%x1f%cr</c> output.</summary>
    public static IReadOnlyList<GitCommit> ParseLog(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('\u001f'))
            .Where(parts => parts.Length >= 2 && parts[0].Length > 0)
            .Select(parts => new GitCommit(parts[0], parts[1], parts.Length > 2 ? parts[2] : null))
            .ToArray();

    private static void ParseHeader(
        string entry, ref string? oid, ref string? branch, ref string? upstream,
        ref bool detached, ref bool initial, ref int ahead, ref int behind)
    {
        var parts = entry.Split(' ', 3);
        if (parts.Length < 3)
            return;

        var value = parts[2];
        switch (parts[1])
        {
            case "branch.oid":
                if (value == "(initial)")
                    initial = true;
                else
                    oid = value;
                break;
            case "branch.head":
                if (value == "(detached)")
                    detached = true;
                else
                    branch = value;
                break;
            case "branch.upstream":
                upstream = value;
                break;
            case "branch.ab":
                var counts = value.Split(' ');
                if (counts.Length == 2)
                {
                    int.TryParse(counts[0].TrimStart('+'), NumberStyles.Integer, CultureInfo.InvariantCulture, out ahead);
                    int.TryParse(counts[1].TrimStart('-'), NumberStyles.Integer, CultureInfo.InvariantCulture, out behind);
                }

                break;
        }
    }

    private static void CountXy(string xy, ref int staged, ref int unstaged)
    {
        if (xy.Length != 2)
            return;
        if (xy[0] != '.')
            staged++;
        if (xy[1] != '.')
            unstaged++;
    }

    /// <summary>Converts porcelain v2 <c>XY</c> (with '.' for unchanged) to the familiar short-status form.</summary>
    private static string DisplayCode(string xy) => xy.Replace('.', ' ');
}
