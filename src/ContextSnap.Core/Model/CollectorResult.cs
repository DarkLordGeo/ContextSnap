namespace ContextSnap.Model;

public enum CollectorStatus
{
    /// <summary>The collector ran and found at least one item.</summary>
    Ok,

    /// <summary>The collector ran successfully but found nothing.</summary>
    Empty,

    /// <summary>The collector is turned off in settings.</summary>
    Disabled,

    /// <summary>The data source does not exist on this machine (for example, Git is not installed).</summary>
    Unavailable,

    /// <summary>The collector threw or timed out.</summary>
    Failed,
}

/// <summary>Non-generic view of a collector result, used for status reporting.</summary>
public interface ICollectorResult
{
    string Name { get; }
    CollectorStatus Status { get; }
    int Count { get; }
    int OmittedCount { get; }
    int ExcludedCount { get; }
    string? Error { get; }
    IReadOnlyList<string> Notes { get; }
    TimeSpan Elapsed { get; }
}

public sealed record CollectorResult<T>(
    string Name,
    CollectorStatus Status,
    IReadOnlyList<T> Items,
    string? Error = null,
    IReadOnlyList<string>? Notes = null,
    int OmittedCount = 0,
    int ExcludedCount = 0,
    TimeSpan Elapsed = default) : ICollectorResult
{
    public IReadOnlyList<string> Notes { get; init; } = Notes ?? [];

    public int Count => Items.Count;

    /// <summary>Total items found, including those dropped by the per-section item limit.</summary>
    public int TotalCount => Items.Count + OmittedCount;

    public static CollectorResult<T> Disabled(string name) => new(name, CollectorStatus.Disabled, []);

    public static CollectorResult<T> FromFailure(string name, CollectorStatus status, string error, TimeSpan elapsed = default) =>
        new(name, status, [], error, Elapsed: elapsed);
}
