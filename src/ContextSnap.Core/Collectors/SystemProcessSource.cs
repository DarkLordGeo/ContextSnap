using System.Diagnostics;
using ContextSnap.Platform;

namespace ContextSnap.Collectors;

/// <summary>Running processes on Windows; executable paths come from the process image (never command lines).</summary>
public sealed class SystemProcessSource : IProcessSource
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

    public string? GetExecutablePath(int processId) => ProcessInfoReader.GetImagePath(processId);
}
