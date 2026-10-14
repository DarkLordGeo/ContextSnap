using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using ContextSnap.Platform;

namespace ContextSnap.Collectors.Git;

public sealed record GitCommandResult(int ExitCode, string Output, string Error, bool TimedOut, bool OutputTruncated = false)
{
    public bool Succeeded => !TimedOut && ExitCode == 0;
}

public interface IGitRunner
{
    bool IsAvailable { get; }

    GitCommandResult Run(string repositoryRoot, IReadOnlyList<string> arguments, TimeSpan timeout);
}

/// <summary>
/// Runs read-only Git commands. Arguments are passed as an argument list (no shell), the executable
/// is resolved to an absolute path, optional index locks and fsmonitor hooks are disabled, output is
/// bounded, and the process tree is killed on timeout.
/// </summary>
public sealed class GitProcessRunner : IGitRunner
{
    public const int MaxOutputCharacters = 512 * 1024;
    private const int MaxErrorCharacters = 4 * 1024;

    private static readonly string[] FallbackLocations =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe"),
    ];

    private static readonly string[] InheritedVariablesToRemove =
    [
        "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_OBJECT_DIRECTORY", "GIT_NAMESPACE",
        "GIT_EXTERNAL_DIFF", "GIT_PAGER", "GIT_EDITOR", "GIT_SSH", "GIT_SSH_COMMAND", "GIT_ASKPASS",
        "GIT_CONFIG_PARAMETERS", "GIT_CONFIG_COUNT",
    ];

    private readonly Lazy<string?> _gitPath;

    public GitProcessRunner(string? gitPath = null)
    {
        _gitPath = new Lazy<string?>(() => gitPath ?? ExecutableLocator.FindOnPath("git.exe", fallbackLocations: FallbackLocations));
    }

    public bool IsAvailable => _gitPath.Value is not null;

    /// <summary>Global options placed before every subcommand.</summary>
    internal static IReadOnlyList<string> BuildArguments(string repositoryRoot, IReadOnlyList<string> arguments) =>
    [
        "--no-pager",
        "--no-optional-locks",
        "-c", "core.fsmonitor=false",
        "-c", "log.showSignature=false",
        "-c", "color.ui=false",
        "-C", repositoryRoot,
        .. arguments,
    ];

    public GitCommandResult Run(string repositoryRoot, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        var gitPath = _gitPath.Value ?? throw new CollectorUnavailableException("Git was not found on PATH.");

        var startInfo = new ProcessStartInfo(gitPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            WorkingDirectory = repositoryRoot,
        };
        foreach (var argument in BuildArguments(repositoryRoot, arguments))
            startInfo.ArgumentList.Add(argument);

        foreach (var name in InheritedVariablesToRemove)
            startInfo.Environment.Remove(name);
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "never";

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new CollectorUnavailableException($"Git could not be started ({ex.NativeErrorCode}).");
        }

        process.StandardInput.Close();
        var output = ReadBoundedAsync(process.StandardOutput, MaxOutputCharacters);
        var error = ReadBoundedAsync(process.StandardError, MaxErrorCharacters);

        if (!process.WaitForExit(timeout))
        {
            TryKill(process);
            return new GitCommandResult(-1, string.Empty, string.Empty, TimedOut: true);
        }

        // Exit does not mean the pipes are drained. Never report a success with missing output: on a
        // busy machine that would show a repository with no branch or status and no error.
        try
        {
            if (!Task.WaitAll([output, error], timeout))
                return new GitCommandResult(-1, string.Empty, string.Empty, TimedOut: true);
        }
        catch (AggregateException)
        {
            return new GitCommandResult(-1, string.Empty, "Git output could not be read.", TimedOut: false);
        }

        var (text, truncated) = output.Result;
        var (errorText, _) = error.Result;
        return new GitCommandResult(process.ExitCode, text, errorText, TimedOut: false, truncated);
    }

    /// <summary>Keeps the first <paramref name="limit"/> characters and drains the rest so the child never blocks.</summary>
    private static async Task<(string Text, bool Truncated)> ReadBoundedAsync(StreamReader reader, int limit)
    {
        var builder = new StringBuilder();
        var buffer = new char[8192];
        var truncated = false;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
        {
            var take = Math.Min(read, limit - builder.Length);
            if (take > 0)
                builder.Append(buffer, 0, take);
            if (take < read)
                truncated = true;
        }

        return (builder.ToString(), truncated);
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        catch (Win32Exception)
        {
            // Could not terminate; the process is detached from us and will be reported as timed out.
        }
    }
}
