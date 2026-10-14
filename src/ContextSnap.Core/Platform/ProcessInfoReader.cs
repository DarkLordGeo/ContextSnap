using System.Diagnostics;
using System.Text;

namespace ContextSnap.Platform;

/// <summary>Reads process image paths with the least privileged access right. Never reads command lines.</summary>
internal static class ProcessInfoReader
{
    public static string? GetImagePath(int processId)
    {
        if (processId <= 0)
            return null;

        var handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, (uint)processId);
        if (handle == 0)
            return null;

        try
        {
            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            return NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    public static string? GetProcessName(int processId)
    {
        var path = GetImagePath(processId);
        if (path is not null)
            return Path.GetFileNameWithoutExtension(path);

        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return null; // Process exited.
        }
        catch (InvalidOperationException)
        {
            return null; // Process exited while reading.
        }
    }
}
