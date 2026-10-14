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
        return options.Mode == RunMode.Gui && options.Error is null
            ? RunGui(options.StartMinimized)
            : RunHeadless(options);
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
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) =>
        {
            // Last-resort boundary: report instead of terminating; the user can retry or close.
            MessageBox.Show($"An unexpected error occurred:\n\n{e.Exception.GetType().Name}: {e.Exception.Message}",
                "ContextSnap", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };

        // Custom styling is skipped in high-contrast mode so system colors apply.
        if (!SystemParameters.HighContrast)
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/ContextSnap;component/Themes/Styles.xaml"),
            });
        }

        var notifier = new WpfCopyNotifier(app.Dispatcher);
        var window = new MainWindow(store, service, new Win32ClipboardWriter(), notifier, StartupRegistration.CreateDefault(), startMinimized);
        instance.ListenForActivation(() => window.Dispatcher.BeginInvoke(window.BringToFront));
        return app.Run(window);
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
            Console.Error.WriteLine($"ContextSnap failed: {ex.GetType().Name}: {ex.Message}");
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
