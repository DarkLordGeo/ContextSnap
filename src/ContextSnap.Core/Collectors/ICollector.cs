using ContextSnap.Configuration;

namespace ContextSnap.Collectors;

/// <summary>Inputs shared by every collector during one capture.</summary>
public sealed record CollectorContext(AppSettings Settings, IReadOnlyList<string> ProjectCandidates)
{
    public static CollectorContext For(AppSettings settings) => new(settings, []);
}

/// <param name="Items">Collected items, already filtered by privacy exclusions.</param>
/// <param name="Notes">Short user-facing notes, such as how many entries were skipped and why.</param>
/// <param name="ExcludedCount">Items dropped because they matched a privacy exclusion.</param>
public sealed record CollectorOutput<T>(
    IReadOnlyList<T> Items,
    IReadOnlyList<string>? Notes = null,
    int ExcludedCount = 0);

/// <summary>
/// Gathers one kind of context. Implementations must be read-only and may throw;
/// the <see cref="Capture.SnapshotService"/> isolates failures per collector.
/// </summary>
public interface ICollector<T>
{
    string Name { get; }

    CollectorOutput<T> Collect(CollectorContext context, CancellationToken cancellationToken);
}

/// <summary>Thrown when a data source does not exist on this machine, as opposed to failing.</summary>
public sealed class CollectorUnavailableException(string message) : Exception(message);
