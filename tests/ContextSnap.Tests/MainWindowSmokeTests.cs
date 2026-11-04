using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using ContextSnap.Capture;
using ContextSnap.Configuration;
using ContextSnap.Model;
using ContextSnap.Notifications;
using ContextSnap.Output;
using ContextSnap.Platform;

namespace ContextSnap.Tests;

/// <summary>
/// Builds the real main window with the real theme (without showing it) and lays out every page, so XAML,
/// style, template, and resource mistakes fail here rather than at launch. Shortcut registration, the tray
/// icon, and the Startup folder are not touched: those start only once the window gets a handle.
/// </summary>
public sealed class MainWindowSmokeTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ContextSnapUi").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A WPF background thread can briefly hold a file; the temp folder is cleaned up later.
        }
    }

    private sealed class NoShortcuts : IShortcutStore
    {
        public bool Exists(string shortcutPath) => false;
        public ShortcutDefinition? Read(string shortcutPath) => null;
        public void Write(string shortcutPath, ShortcutDefinition shortcut) { }
        public void Delete(string shortcutPath) { }
    }

    private sealed class NoClipboard : IClipboardWriter
    {
        public ClipboardResult SetText(string text, bool excludeFromHistory) => ClipboardResult.Ok;
    }

    internal static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "The UI thread did not finish.");
        if (failure is not null)
            throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    internal static MainWindow CreateWindow(string directory, AppSettings? settings = null)
    {
        var store = new SettingsStore(Path.Combine(directory, "settings.json"));
        if (settings is not null)
            store.Save(settings);

        var collectors = new CollectorSet
        {
            Windows = StubCollector<WindowInfo>.Returning("Open windows", new WindowInfo("Code", "Program.cs - Shop", 1, true)),
            Processes = StubCollector<ProcessGroup>.Returning("Developer tools"),
            Explorer = StubCollector<ExplorerFolder>.Returning("File Explorer folders"),
            Git = StubCollector<GitRepositoryInfo>.Returning("Git repositories"),
            Desktop = StubCollector<DesktopEntry>.Returning("Desktop items"),
            BrowserTabs = StubCollector<BrowserTab>.Returning("Browser tabs"),
            SystemInfo = StubCollector<SystemSummary>.Returning("System summary", new SystemSummary("Windows 11", "X64", 4, null)),
            ScreenText = StubCollector<WindowText>.Returning("Visible screen text"),
        };
        var target = new LaunchTarget(@"D:\Apps\ContextSnap\Snapctx.exe", "--startup", @"D:\Apps\ContextSnap", false);
        var startup = new StartupRegistration(new NoShortcuts(), () => Path.Combine(directory, "Startup"), () => new LaunchTargetResult(target, null));

        var window = new MainWindow(store, new SnapshotService(collectors), new NoClipboard(),
            new WpfCopyNotifier(Dispatcher.CurrentDispatcher), startup);

        _ = System.IO.Packaging.PackUriHelper.UriSchemePack; // Registers pack:// without an Application.
        var styles = (ResourceDictionary)Application.LoadComponent(new Uri("/Snapctx;component/Themes/Styles.xaml", UriKind.Relative));
        window.Resources.MergedDictionaries.Add(styles);
        return window;
    }

    internal static void Layout(MainWindow window)
    {
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(1040, 720));
        root.Arrange(new Rect(0, 0, 1040, 720));
        root.UpdateLayout();
    }

    internal static T Find<T>(MainWindow window, string name) where T : class =>
        window.FindName(name) as T ?? throw new Xunit.Sdk.XunitException($"Element '{name}' not found.");

    [Fact]
    public void MainWindow_BuildsWithTheTheme_AndLaysOutEveryPage()
    {
        RunOnSta(() =>
        {
            var window = CreateWindow(_directory);
            Assert.Equal("ContextSnap", window.Title);
            Layout(window);

            foreach (var (nav, page) in new[] { ("NavPrivacy", "PrivacyPage"), ("NavSettings", "SettingsPage"), ("NavCapture", "CapturePage") })
            {
                Find<ToggleButton>(window, nav).IsChecked = true;
                Layout(window);
                Assert.Equal(Visibility.Visible, Find<UIElement>(window, page).Visibility);
                Assert.True(Find<FrameworkElement>(window, page).ActualWidth > 0, $"{page} was not laid out.");
            }

            // The theme's templates were applied (a missing resource would leave the default chrome).
            var capture = Find<Button>(window, "CaptureButton");
            Assert.NotNull(capture.Template.FindName("Chrome", capture));
            Assert.Equal(Visibility.Collapsed, Find<UIElement>(window, "PrivacyPage").Visibility);
        });
    }

    [Fact]
    public void CapturePage_ShowsShortcutAndEmptyStateBeforeTheFirstCapture()
    {
        RunOnSta(() =>
        {
            var window = CreateWindow(_directory);
            Layout(window);

            Assert.Equal("No capture yet", Find<TextBlock>(window, "LastCaptureText").Text);
            Assert.Equal(Visibility.Visible, Find<UIElement>(window, "EmptyState").Visibility);
            Assert.Equal(Visibility.Collapsed, Find<UIElement>(window, "PreviewBox").Visibility);
            Assert.Equal(["Ctrl", "Alt", "Shift", "C"], (IEnumerable<string>)Find<ItemsControl>(window, "ShortcutKeys").ItemsSource);
        });
    }
}
