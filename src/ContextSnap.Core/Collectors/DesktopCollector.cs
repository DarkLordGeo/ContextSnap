using ContextSnap.Model;
using ContextSnap.Privacy;

namespace ContextSnap.Collectors;

/// <summary>Lists names and sizes of non-hidden Desktop entries. Never opens or reads files.</summary>
public sealed class DesktopCollector(Func<string?> desktopPathProvider) : ICollector<DesktopEntry>
{
    /// <summary>Hard cap on entries examined, independent of the output limit.</summary>
    public const int EnumerationCap = 2_000;

    public string Name => "Desktop items";

    public static DesktopCollector CreateDefault() => new(() =>
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolderOption.DoNotVerify));

    public CollectorOutput<DesktopEntry> Collect(CollectorContext context, CancellationToken cancellationToken)
    {
        var path = desktopPathProvider();
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            throw new CollectorUnavailableException("The Desktop folder was not found.");

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };

        var entries = new List<DesktopEntry>();
        var excluded = 0;
        var capped = false;

        foreach (var info in new DirectoryInfo(path).EnumerateFileSystemInfos("*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entries.Count >= EnumerationCap)
            {
                capped = true;
                break;
            }

            if (PrivacyRules.IsPathExcluded(info.FullName, context.Settings.ExcludedPathPrefixes))
            {
                excluded++;
                continue;
            }

            var isDirectory = info is DirectoryInfo;
            long? size = info is FileInfo file ? file.Length : null;
            entries.Add(new DesktopEntry(info.Name, isDirectory, size, info.LastWriteTime));
        }

        var notes = capped ? new[] { $"Only the first {EnumerationCap} Desktop entries were examined." } : null;
        var ordered = entries
            .OrderByDescending(e => e.LastModified)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new CollectorOutput<DesktopEntry>(ordered, notes, excluded);
    }
}
