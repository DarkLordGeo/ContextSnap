using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ContextSnap.Collectors;
using ContextSnap.Collectors.ScreenText;
using ContextSnap.Configuration;
using ContextSnap.Model;
using Image = System.Windows.Controls.Image;

namespace ContextSnap.Tests;

/// <summary>
/// Windows integration tests: real UI Automation and real Windows OCR against windows this test
/// creates (shown without taking focus). Skipped on CI or non-interactive sessions, where no desktop exists.
/// </summary>
public class ScreenTextIntegrationTests
{
    private static bool CanRun => Environment.UserInteractive && Environment.GetEnvironmentVariable("CI") is null;

    private sealed class SingleWindow(RawWindow window) : IWindowSource
    {
        public IReadOnlyList<RawWindow> GetWindows() => [window];
    }

    [Fact]
    public void ReadsRealWindowTextThroughUiAutomationAndSkipsPasswords()
    {
        if (!CanRun)
            return;

        var output = WithWindow(() => new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "Integration heading visible to the user", FontSize = 16 },
                new TextBox { Text = "Value typed in a text box" },
                new PasswordBox { Password = "hunter2-secret" },
                new Button { Content = "Save changes" },
            },
        }, settings: new AppSettings { ScreenTextUseOcr = false });

        var window = Assert.Single(output.Items);
        Assert.Equal(ScreenTextSource.UiAutomation, window.Source);
        Assert.Contains("Integration heading visible to the user", window.Text);
        Assert.Contains("Value typed in a text box", window.Text);
        Assert.Contains("Save changes", window.Text);
        Assert.DoesNotContain("hunter2", window.Text);
    }

    [Fact]
    public void FallsBackToWindowsOcrForTextDrawnAsPixels()
    {
        if (!CanRun || new WindowsOcrReader().UnavailableReason is not null)
            return;

        var output = WithWindow(() => new Image { Source = RenderText("Quarterly revenue grew fifteen percent"), Stretch = Stretch.None },
            settings: new AppSettings());

        Assert.True(output.Items.Count == 1, "Notes: " + string.Join(" | ", output.Notes ?? []));
        var window = output.Items[0];
        Assert.True(window.Source.HasFlag(ScreenTextSource.Ocr), string.Join("; ", window.Warnings));
        Assert.Contains("revenue", window.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fifteen", window.Text, StringComparison.OrdinalIgnoreCase);
    }

    private static CollectorOutput<WindowText> WithWindow(Func<UIElement> content, AppSettings settings)
    {
        CollectorOutput<WindowText>? result = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var window = new Window
            {
                Title = "ContextSnap integration test",
                Width = 560, Height = 260, Left = 40, Top = 40,
                WindowStartupLocation = WindowStartupLocation.Manual,
                ShowActivated = false, ShowInTaskbar = false, Topmost = true,
                Background = Brushes.White,
                Content = content(),
            };
            try
            {
                window.Show();
                Pump(TimeSpan.FromMilliseconds(400)); // Let it render before reading.

                var handle = new WindowInteropHelper(window).Handle;
                var raw = new RawWindow(handle, window.Title, Environment.ProcessId, "testhost", "HwndWrapper", false, false, false, IsForeground: true);
                var collector = new ScreenTextCollector(new SingleWindow(raw), new UiaTextReader(), new WindowsOcrReader(), ownProcessId: -1);

                // UI Automation must not be called from the UI thread that hosts the window; read in the background while pumping.
                var read = Task.Run(() => collector.Collect(CollectorContext.For(settings), CancellationToken.None));
                while (!read.IsCompleted)
                    Pump(TimeSpan.FromMilliseconds(20));
                result = read.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                window.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Integration window timed out.");
        if (failure is not null)
            throw new Xunit.Sdk.XunitException(failure.ToString());
        return result!;
    }

    private static void Pump(TimeSpan duration)
    {
        var until = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < until)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            Thread.Sleep(10);
        }
    }

    /// <summary>Text as a bitmap: invisible to UI Automation, readable only by OCR.</summary>
    private static BitmapSource RenderText(string text)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, 540, 120));
            context.DrawText(new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 26, Brushes.Black, 1.0), new Point(16, 40));
        }

        var bitmap = new RenderTargetBitmap(540, 120, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
