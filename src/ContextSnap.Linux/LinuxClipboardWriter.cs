using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using ContextSnap.Output;

namespace ContextSnap.Linux;

/// <param name="Path">Absolute path of the tool.</param>
public sealed record ClipboardTool(string Name, string Path, IReadOnlyList<string> Arguments);

/// <summary>
/// Copies text with the desktop's clipboard tool: wl-copy on Wayland, otherwise xclip or xsel on X11.
/// Text is passed on standard input, never as an argument, so it never appears in a process list.
/// </summary>
public sealed class LinuxClipboardWriter(Func<string, string?>? environment = null, Func<string, string?>? find = null) : IClipboardWriter
{
    public const string NoSessionError = "No graphical session was found (neither WAYLAND_DISPLAY nor DISPLAY is set).";
    public const string NoToolError = "No clipboard tool was found. Install wl-clipboard (Wayland) or xclip (X11).";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly Func<string, string?> _environment = environment ?? Environment.GetEnvironmentVariable;
    private readonly Func<string, string?> _find = find ?? ToolRunner.Find;

    /// <summary>
    /// The tool to use, or an error. Clipboard-history exclusion is a Windows feature and is not requested here.
    /// </summary>
    public (ClipboardTool? Tool, string? Error) Choose()
    {
        var wayland = !string.IsNullOrEmpty(_environment("WAYLAND_DISPLAY"));
        var x11 = !string.IsNullOrEmpty(_environment("DISPLAY"));
        if (!wayland && !x11)
            return (null, NoSessionError);

        if (wayland && _find("wl-copy") is { } wlCopy)
            return (new ClipboardTool("wl-copy", wlCopy, ["--type", "text/plain;charset=utf-8"]), null);
        if (x11 && _find("xclip") is { } xclip)
            return (new ClipboardTool("xclip", xclip, ["-selection", "clipboard", "-in"]), null);
        if (x11 && _find("xsel") is { } xsel)
            return (new ClipboardTool("xsel", xsel, ["--clipboard", "--input"]), null);

        return (null, wayland && !x11 ? "wl-copy was not found. Install wl-clipboard." : NoToolError);
    }

    public ClipboardResult SetText(string text, bool excludeFromHistory)
    {
        var (tool, error) = Choose();
        if (tool is null)
            return new ClipboardResult(false, error);

        var start = new ProcessStartInfo(tool.Path)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            // Redirected but not read: these tools stay in the background to serve the clipboard, and must
            // not hold the caller's terminal or pipe open.
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
        };
        foreach (var argument in tool.Arguments)
            start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
            process.StandardInput.Write(text);
            process.StandardInput.Close();
        }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        {
            return new ClipboardResult(false, $"{tool.Name} could not be run ({ex.GetType().Name}).");
        }

        if (!process.WaitForExit(Timeout))
        {
            ToolRunner.TryKill(process);
            return new ClipboardResult(false, $"{tool.Name} did not respond in time.");
        }

        if (process.ExitCode != 0)
            return new ClipboardResult(false, $"{tool.Name} failed (exit code {process.ExitCode}): {ReadError(process)}");

        return ClipboardResult.Ok;
    }

    private static string ReadError(Process process)
    {
        var read = process.StandardError.ReadToEndAsync();
        var message = read.Wait(TimeSpan.FromSeconds(1)) ? read.Result.Split('\n', 2)[0].Trim() : string.Empty;
        return message.Length > 0 ? message : "no details";
    }
}
