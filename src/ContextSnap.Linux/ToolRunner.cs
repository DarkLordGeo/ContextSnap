using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using ContextSnap.Platform;

namespace ContextSnap.Linux;

/// <summary>
/// Runs a small desktop tool (wmctrl, xprop) and reads its output. The tool is resolved to an absolute
/// path from PATH, arguments are passed as a list (no shell), output is bounded, and a tool that does not
/// finish in time is killed.
/// </summary>
internal static class ToolRunner
{
    public const int MaxOutputCharacters = 256 * 1024;

    public static string? Find(string name) => ExecutableLocator.FindOnPath(name);

    /// <summary>Standard output, or null when the tool failed, timed out, or could not be started.</summary>
    /// <remarks>The tool runs in a UTF-8 locale; otherwise X11 tools print "N/A" for non-ASCII window titles.</remarks>
    public static string? ReadOutput(string path, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        var start = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        start.Environment["LC_ALL"] = "C.UTF-8";

        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            return null;
        }

        process.StandardInput.Close();
        var output = ReadBoundedAsync(process.StandardOutput);
        _ = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout) || !output.Wait(timeout))
        {
            TryKill(process);
            return null;
        }

        return process.ExitCode == 0 ? output.Result : null;
    }

    internal static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already exited, or could not be stopped; either way it is no longer waited on.
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        var builder = new StringBuilder();
        var buffer = new char[8192];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
            builder.Append(buffer, 0, Math.Min(read, MaxOutputCharacters - builder.Length));
        return builder.ToString();
    }
}
