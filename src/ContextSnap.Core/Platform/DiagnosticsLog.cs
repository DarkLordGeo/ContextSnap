using System.Globalization;
using System.Text;
using ContextSnap.Privacy;

namespace ContextSnap.Platform;

/// <summary>Records problems for later diagnosis. Never receives captured content.</summary>
public interface IDiagnosticsLog
{
    void Warning(string message);

    void Error(string message, Exception exception);
}

/// <summary>
/// A small local text log (<c>%LOCALAPPDATA%\ContextSnap\logs\contextsnap.log</c>). It is written only
/// when something goes wrong, holds error types and messages (with secrets redacted), never snapshot
/// content, and is capped at about 256 KB plus one previous file. Logging failures are ignored.
/// </summary>
public sealed class DiagnosticsLog(string filePath, long maxBytes = 256 * 1024) : IDiagnosticsLog
{
    private readonly object _gate = new();

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ContextSnap", "logs", "contextsnap.log");

    public static DiagnosticsLog CreateDefault() => new(DefaultPath);

    public string FilePath { get; } = filePath;

    public void Warning(string message) => Write("WARN", message);

    public void Error(string message, Exception exception) =>
        Write("ERROR", $"{message}: {exception.GetType().Name}: {exception.Message.Split('\n', 2)[0].Trim()}");

    private void Write(string level, string message)
    {
        var line = string.Create(CultureInfo.InvariantCulture,
            $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} {level} {Redactor.Default.Redact(message)}{Environment.NewLine}");
        lock (_gate)
        {
            try
            {
                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var existing = new FileInfo(FilePath);
                if (existing.Exists && existing.Length > maxBytes)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);

                File.AppendAllText(FilePath, line, new UTF8Encoding(false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Diagnostics must never affect capture or copying.
            }
        }
    }
}
