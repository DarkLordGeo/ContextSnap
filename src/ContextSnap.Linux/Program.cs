using System.Reflection;
using ContextSnap.Capture;
using ContextSnap.Configuration;

namespace ContextSnap.Linux;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args is ["--version"])
        {
            Console.WriteLine($"ContextSnap {Version}");
            return 0;
        }

        var runner = new HeadlessRunner(
            new SnapshotService(LinuxCollectors.CreateDefault()),
            SettingsStore.CreateDefault(),
            new LinuxClipboardWriter(),
            NotifySendNotifier.CreateIfAvailable(),
            LinuxCommandLine.Usage,
            LinuxCommandLine.ApplyPlatformLimits);

        try
        {
            return args switch
            {
                ["--background"] => BackgroundMode.Run(runner, Console.Out, Console.Error),
                ["--install"] => new LinuxSetup(Console.Out).Install(Environment.ProcessPath!),
                ["--uninstall"] => new LinuxSetup(Console.Out).Uninstall(),
                _ => runner.RunAsync(LinuxCommandLine.Parse(args), Console.Out, Console.Error, Environment.CurrentDirectory)
                    .GetAwaiter().GetResult(),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ContextSnap failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static string Version =>
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "unknown";
}
