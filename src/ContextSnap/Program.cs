using System.Text;
using System.Windows;
using ContextSnap.Capture;
using ContextSnap.Configuration;
using ContextSnap.Notifications;
using ContextSnap.Output;
using ContextSnap.Platform;

namespace ContextSnap;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var options = CommandLineOptions.Parse(args);
        if (options.Mode is RunMode.Exit or RunMode.EnableStartup or RunMode.DisableStartup)
            return RunBackgroundCommand(options.Mode);
        return options.Mode == RunMode.Gui && options.Error is null
            ? RunGui(options.StartMinimized)
            : RunHeadless(options);
    }

    /// <summary><c>--exit</c>, <c>--enable-startup</c>, <c>--disable-startup</c>: no window; output goes to the parent console, if any.</summary>
    private static int RunBackgroundCommand(RunMode mode)
    {
        ConsoleAttachment.AttachToParentIfNeeded();
        Console.OutputEncoding = new UTF8Encoding(false);
        using var instance = mode == RunMode.Exit ? SingleInstance.Acquire() : null;
        var commands = new BackgroundCommands(SettingsStore.CreateDefault(), StartupRegistration.CreateDefault(),
            () => instance?.RequestExitOfFirstInstance() ?? false);
        return commands.Run(mode, Console.Out, Console.Error);
    }

    private static int RunGui(bool startMinimized)
    {
        using var instance = SingleInstance.Acquire();
        if (!instance.IsFirstInstance)
        {
            // Already running: a manual launch brings the existing window forward; a sign-in launch stays silent.
            if (!startMinimized)
                instance.ActivateFirstInstance();
            return 0;
        }

        var store = SettingsStore.CreateDefault();
        var service = new SnapshotService(CollectorSet.CreateDefault());
        // The window can be hidden to the notification area, so the app ends only on Exit or sign-out.
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            // Last-resort boundary: report instead of terminating; the user can retry or close.
            MessageBox.Show($"An unexpected error occurred:\n\n{e.Exception.GetType().Name}: {e.Exception.Message}",
                "Snapctx", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };

        // Custom styling is skipped in high-contrast mode so system colors apply.
        if (!SystemParameters.HighContrast)
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Snapctx;component/Themes/Styles.xaml"),
            });
        }

        var notifier = new WpfCopyNotifier(app.Dispatcher);
        var window = new MainWindow(store, service, new Win32ClipboardWriter(), notifier, StartupRegistration.CreateDefault());
        instance.ListenForActivation(() => window.Dispatcher.BeginInvoke(window.BringToFront));
        instance.ListenForExit(() => window.Dispatcher.BeginInvoke(window.ExitApplication));
        app.SessionEnding += (_, _) => window.ExitApplication();

        // Creating the handle registers the shortcut (and the optional icon) without showing anything, so a
        // sign-in launch has no window, taskbar button, or console. Opening Snapctx again shows the window.
        new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
        if (BackgroundPolicy.ShowWindowAtLaunch(startMinimized))
            window.Show();

        return app.Run();
    }

    private static int RunHeadless(CommandLineOptions options)
    {
        // Scripts and pipes get no notification and no delay; interactive or shortcut launches do.
        var outputRedirected = ConsoleAttachment.AttachToParentIfNeeded();
        Console.OutputEncoding = new UTF8Encoding(false);

        var notifier = outputRedirected ? null : new DeferredCopyNotifier();
        var runner = new HeadlessRunner(
            new SnapshotService(CollectorSet.CreateDefault()),
            SettingsStore.CreateDefault(),
            new Win32ClipboardWriter(),
            notifier);

        int exitCode;
        try
        {
            exitCode = runner.RunAsync(options, Console.Out, Console.Error, Environment.CurrentDirectory)
                .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Snapctx failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }

        if (notifier?.Last is { } notification)
            ShowStandaloneToast(notification);
        return exitCode;
    }

    /// <summary>
    /// Shows the copy toast from a command-line run after the clipboard work (and its exit code) is final.
    /// The process exits as soon as the toast is dismissed.
    /// </summary>
    private static void ShowStandaloneToast(CopyNotificationContent content)
    {
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var toast = new ToastWindow();
            toast.Dismissed += (_, _) => app.Shutdown();

            // Safety net if rendering stalls, so a command-line run can never hang.
            var deadline = new System.Windows.Threading.DispatcherTimer { Interval = content.Duration + TimeSpan.FromSeconds(3) };
            deadline.Tick += (_, _) => app.Shutdown();
            deadline.Start();

            app.Dispatcher.InvokeAsync(() => toast.Present(content));
            app.Run();
        }
        catch (Exception ex)
        {
            // Cosmetic only: the copy already succeeded or failed and the exit code is unchanged.
            Console.Error.WriteLine($"Notification could not be shown: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
