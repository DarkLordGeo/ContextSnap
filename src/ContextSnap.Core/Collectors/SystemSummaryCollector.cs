using System.Runtime.InteropServices;
using ContextSnap.Model;

namespace ContextSnap.Collectors;

/// <summary>OS version and hardware class only. No machine name, user name, or network details.</summary>
public sealed class SystemSummaryCollector : ICollector<SystemSummary>
{
    public string Name => "System summary";

    public CollectorOutput<SystemSummary> Collect(CollectorContext context, CancellationToken cancellationToken)
    {
        var version = Environment.OSVersion.Version;
        var name = version switch
        {
            { Major: 10, Build: >= 22000 } => "Windows 11",
            { Major: 10 } => "Windows 10",
            _ => "Windows",
        };

        var memory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var summary = new SystemSummary(
            $"{name} (build {version.Build})",
            RuntimeInformation.OSArchitecture.ToString(),
            Environment.ProcessorCount,
            memory > 0 ? memory : null);

        return new CollectorOutput<SystemSummary>([summary]);
    }
}
