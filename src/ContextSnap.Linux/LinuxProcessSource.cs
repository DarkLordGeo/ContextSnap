using System.Diagnostics;
using ContextSnap.Collectors;

namespace ContextSnap.Linux;

/// <summary>Running processes from /proc. Only names and executable paths are read, never command lines or environments.</summary>
public sealed class LinuxProcessSource : IProcessSource
{
    public IReadOnlyList<RawProcess> GetProcesses()
    {
        var processes = new List<RawProcess>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    processes.Add(new RawProcess(process.Id, process.ProcessName));
                }
                catch (InvalidOperationException)
                {
                    // The process exited between enumeration and reading its name.
                }
            }
        }

        return processes;
    }

    /// <summary>The target of /proc/&lt;pid&gt;/exe; null for other users' processes, which the kernel does not reveal.</summary>
    public string? GetExecutablePath(int processId)
    {
        try
        {
            return new FileInfo($"/proc/{processId}/exe").LinkTarget;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The process name, or null when the process has exited.</summary>
    internal static string? ReadName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
