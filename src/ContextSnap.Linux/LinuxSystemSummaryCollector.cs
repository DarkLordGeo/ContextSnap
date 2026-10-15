using System.Globalization;
using System.Runtime.InteropServices;
using ContextSnap.Collectors;
using ContextSnap.Model;

namespace ContextSnap.Linux;

/// <summary>
/// Distribution, kernel version, desktop, and hardware class only. No machine name, user name, or
/// network details.
/// </summary>
public sealed class LinuxSystemSummaryCollector(Func<string, string?>? readFile = null, Func<string, string?>? environment = null)
    : ICollector<SystemSummary>
{
    private readonly Func<string, string?> _readFile = readFile ?? TryReadFile;
    private readonly Func<string, string?> _environment = environment ?? Environment.GetEnvironmentVariable;

    public string Name => "System summary";

    public CollectorOutput<SystemSummary> Collect(CollectorContext context, CancellationToken cancellationToken)
    {
        var memory = ParseMemTotal(_readFile("/proc/meminfo")) ?? GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var summary = new SystemSummary(
            Describe(ParseOsRelease(_readFile("/etc/os-release") ?? _readFile("/usr/lib/os-release")),
                Environment.OSVersion.Version, _environment("XDG_CURRENT_DESKTOP"), _environment("XDG_SESSION_TYPE")),
            RuntimeInformation.OSArchitecture.ToString(),
            Environment.ProcessorCount,
            memory > 0 ? memory : null);

        return new CollectorOutput<SystemSummary>([summary]);
    }

    /// <summary>For example "Ubuntu 24.04.1 LTS (Linux 6.8.0, GNOME on Wayland)".</summary>
    public static string Describe(string? distribution, Version kernel, string? desktop, string? sessionType)
    {
        var details = new List<string> { $"Linux {kernel.Major}.{kernel.Minor}.{Math.Max(kernel.Build, 0)}" };
        var session = sessionType is "x11" ? "X11" : sessionType is "wayland" ? "Wayland" : null;
        var desktopName = string.IsNullOrWhiteSpace(desktop) ? null : desktop.Split(':')[^1].Trim();
        if (desktopName is not null || session is not null)
            details.Add(desktopName is not null && session is not null ? $"{desktopName} on {session}" : desktopName ?? session!);

        return $"{distribution ?? "Linux"} ({string.Join(", ", details)})";
    }

    /// <summary>PRETTY_NAME (or NAME) from os-release, unquoted.</summary>
    public static string? ParseOsRelease(string? content)
    {
        if (content is null)
            return null;

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in content.Split('\n'))
        {
            var parts = line.Trim().Split('=', 2);
            if (parts.Length == 2 && parts[0].Length > 0 && !parts[0].StartsWith('#'))
                values[parts[0]] = parts[1].Trim().Trim('"', '\'');
        }

        var name = values.GetValueOrDefault("PRETTY_NAME") is { Length: > 0 } pretty ? pretty : values.GetValueOrDefault("NAME");
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>MemTotal from /proc/meminfo, in bytes.</summary>
    public static long? ParseMemTotal(string? content)
    {
        var line = content?.Split('\n').FirstOrDefault(l => l.StartsWith("MemTotal:", StringComparison.Ordinal));
        var parts = line?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts is [_, var value, "kB"] && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var kilobytes)
            ? kilobytes * 1024
            : null;
    }

    private static string? TryReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
