using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

internal static class Program
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private static int Main()
    {
        var markdown = new StringBuilder();
        markdown.AppendLine("# ContextSnap");
        markdown.AppendLine();
        markdown.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        markdown.AppendLine();

        markdown.AppendLine("## Visible windows");
        markdown.AppendLine();

        var windows = new List<(string Process, string Title)>();

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;

            var title = new StringBuilder(1024);
            GetWindowText(hWnd, title, title.Capacity);
            if (title.Length == 0) return true;

            GetWindowThreadProcessId(hWnd, out uint processId);

            try
            {
                var process = Process.GetProcessById((int)processId);
                windows.Add((process.ProcessName, title.ToString()));
            }
            catch (ArgumentException) { }

            return true;
        }, IntPtr.Zero);

        foreach (var window in windows)
            markdown.AppendLine($"- **{window.Process}**: {window.Title.Replace("|", "\\|")}");

        markdown.AppendLine();
        markdown.AppendLine("## Desktop items");
        markdown.AppendLine();

        var desktop = Environment.GetFolderPath(
            Environment.SpecialFolder.DesktopDirectory);

        foreach (var path in Directory.GetFileSystemEntries(desktop).OrderBy(Path.GetFileName))
        {
            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                    continue;

                var name = Path.GetFileName(path);
                if ((attributes & FileAttributes.Directory) != 0)
                    markdown.AppendLine($"- [Folder] {name}");
                else
                    markdown.AppendLine($"- [File] {name} ({new FileInfo(path).Length:N0} bytes)");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        var snapshot = markdown.ToString();
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(snapshot));

        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(
            $"$s=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{encoded}')); Set-Clipboard -Value $s");

        using var clipboard = Process.Start(start);
        if (clipboard is null) return 1;
        clipboard.WaitForExit();

        if (clipboard.ExitCode != 0)
        {
            Console.Error.WriteLine("Clipboard copy failed.");
            return 1;
        }

        Console.WriteLine($"Snapshot copied to clipboard. {windows.Count} windows captured.");
        Console.WriteLine($"Desktop: {desktop}");
        Console.WriteLine($"Markdown size: {Encoding.UTF8.GetByteCount(snapshot):N0} bytes");
        return 0;
    }
}
