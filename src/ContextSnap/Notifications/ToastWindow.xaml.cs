using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ContextSnap.Platform;

namespace ContextSnap.Notifications;

/// <summary>
/// A non-activating, always-on-top toast in the bottom-right of the active monitor's work area.
/// One instance is reused: new content updates it in place and restarts its countdown.
/// All motion uses render transforms and opacity, which WPF composes cheaply on the GPU.
/// </summary>
public partial class ToastWindow : Window, IToastView
{
    private const double EntryOffset = 26;
    private const double DismissOffset = 10;
    private const double StatOffset = 6;
    private const double CheckDashLength = 8;
    private const int ScreenMarginDip = 4;

    private int _generation;
    private bool _isShowing;
    private nint _handle;

    public ToastWindow()
    {
        InitializeComponent();
        CardContent.SizeChanged += (_, e) => CardContent.Clip = new RectangleGeometry(new Rect(e.NewSize), 11, 11);
        SizeChanged += (_, _) =>
        {
            if (_isShowing)
                PlaceOnActiveScreen(moveToMonitorFirst: false); // Keep the bottom edge anchored when content height changes.
        };
        Root.MouseLeftButtonUp += (_, _) => Dismiss(_generation);
    }

    /// <summary>Raised after the toast has faded out and hidden itself.</summary>
    public event EventHandler? Dismissed;

    public bool IsShowing => _isShowing;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;
        ScreenPlacement.MakeNonActivating(_handle);
    }

    public void Present(CopyNotificationContent content) => Display(content, entering: true);

    public void Refresh(CopyNotificationContent content) => Display(content, entering: !IsVisible);

    private void Display(CopyNotificationContent content, bool entering)
    {
        var generation = ++_generation;
        var reduced = MotionPreference.IsReduced;
        var palette = ToastPalette.For(content.Kind);

        ApplyPalette(palette);
        ApplyContent(content);

        if (entering)
        {
            Set(this, OpacityProperty, 0);
            Set(RootTranslate, TranslateTransform.XProperty, 0);
            Set(RootTranslate, TranslateTransform.YProperty, reduced ? 0 : EntryOffset);
            if (!IsVisible)
                Show(); // Shown fully transparent; ShowActivated=false and WS_EX_NOACTIVATE keep focus where it is.
            PlaceOnActiveScreen(moveToMonitorFirst: true);

            Animate(this, OpacityProperty, 0, 1, reduced ? 150 : 200, Ease.Out);
            if (!reduced)
                Animate(RootTranslate, TranslateTransform.YProperty, EntryOffset, 0, 560, new SpringEase());
        }
        else
        {
            // Already on screen (possibly fading out): bring it back and update in place.
            PlaceOnActiveScreen(moveToMonitorFirst: false);
            Animate(this, OpacityProperty, null, 1, 120, Ease.Out);
            Animate(RootTranslate, TranslateTransform.YProperty, null, 0, 160, Ease.Out);
            if (!reduced)
                Pulse();
        }

        AnimateIcon(content.Kind, reduced, entering);
        AnimateDetails(reduced, entering);
        if (content.Kind == CopyNotificationKind.Error && !reduced)
            Shake(entering ? 260 : 0);

        StartCountdown(content.Duration, generation);
        _isShowing = true;
        Announce();
    }

    private void ApplyContent(CopyNotificationContent content)
    {
        TitleText.Text = content.Title;

        MessageText.Text = content.Message ?? string.Empty;
        MessageText.Visibility = string.IsNullOrEmpty(content.Message) ? Visibility.Collapsed : Visibility.Visible;

        StatsPanel.Children.Clear();
        for (var i = 0; i < content.Stats.Count; i++)
        {
            StatsPanel.Children.Add(new TextBlock
            {
                Text = i == 0 ? content.Stats[i] : "·  " + content.Stats[i],
                Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0),
                Foreground = MessageText.Foreground,
                RenderTransform = new TranslateTransform(),
            });
        }

        StatsPanel.Visibility = content.Stats.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        WarningText.Text = content.Warning ?? string.Empty;
        WarningRow.Visibility = string.IsNullOrEmpty(content.Warning) ? Visibility.Collapsed : Visibility.Visible;

        CheckMark.Visibility = content.Kind == CopyNotificationKind.Success ? Visibility.Visible : Visibility.Collapsed;
        CrossMark.Visibility = content.Kind == CopyNotificationKind.Error ? Visibility.Visible : Visibility.Collapsed;
        AlertMark.Visibility = content.Kind == CopyNotificationKind.Warning ? Visibility.Visible : Visibility.Collapsed;

        AutomationProperties.SetName(this, string.Join(". ", new[] { content.Title, content.Message, string.Join(", ", content.Stats), content.Warning }
            .Where(s => !string.IsNullOrEmpty(s))));
    }

    private void ApplyPalette(ToastPalette palette)
    {
        Card.Background = palette.Card;
        Card.BorderBrush = palette.Border;
        ShadowOuter.Background = palette.ShadowOuter;
        ShadowMiddle.Background = palette.ShadowMiddle;
        ShadowInner.Background = palette.ShadowInner;
        TitleText.Foreground = palette.Title;
        MessageText.Foreground = palette.Body;
        IconDisc.Fill = palette.Accent;
        CheckMark.Stroke = palette.Glyph;
        CrossMark.Stroke = palette.Glyph;
        AlertMark.Stroke = palette.Glyph;
        WarningRow.Background = palette.WarningBackground;
        WarningText.Foreground = palette.WarningText;
        ProgressTrack.Background = palette.ProgressTrack;
        ProgressBar.Background = palette.Accent;
    }

    private void AnimateIcon(CopyNotificationKind kind, bool reduced, bool entering)
    {
        var begin = entering ? 90 : 0;
        if (reduced)
        {
            Set(IconScale, ScaleTransform.ScaleXProperty, 1);
            Set(IconScale, ScaleTransform.ScaleYProperty, 1);
            Set(CheckMark, Shape.StrokeDashOffsetProperty, 0);
            Set(CrossScale, ScaleTransform.ScaleXProperty, 1);
            Set(CrossScale, ScaleTransform.ScaleYProperty, 1);
            Set(AlertMark, OpacityProperty, 1);
            return;
        }

        var pop = new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut };
        Animate(IconScale, ScaleTransform.ScaleXProperty, 0.55, 1, 340, pop, begin);
        Animate(IconScale, ScaleTransform.ScaleYProperty, 0.55, 1, 340, pop, begin);

        switch (kind)
        {
            case CopyNotificationKind.Success:
                // Draws the tick from its short leg to its tip.
                Set(CheckMark, Shape.StrokeDashOffsetProperty, CheckDashLength);
                Animate(CheckMark, Shape.StrokeDashOffsetProperty, CheckDashLength, 0, 300, Ease.Out, begin + 150);
                break;
            case CopyNotificationKind.Error:
                Set(CrossScale, ScaleTransform.ScaleXProperty, 0.3);
                Set(CrossScale, ScaleTransform.ScaleYProperty, 0.3);
                Animate(CrossScale, ScaleTransform.ScaleXProperty, 0.3, 1, 260, pop, begin + 120);
                Animate(CrossScale, ScaleTransform.ScaleYProperty, 0.3, 1, 260, pop, begin + 120);
                break;
            default:
                Set(AlertMark, OpacityProperty, 0);
                Animate(AlertMark, OpacityProperty, 0, 1, 220, Ease.Out, begin + 120);
                break;
        }
    }

    /// <summary>Statistics and the warning line slide up and fade in one after another.</summary>
    private void AnimateDetails(bool reduced, bool entering)
    {
        var start = entering ? 230 : 40;
        var index = 0;
        foreach (var child in StatsPanel.Children.OfType<TextBlock>().Append(WarningText))
        {
            var translate = child.RenderTransform as TranslateTransform ?? new TranslateTransform();
            child.RenderTransform = translate;
            if (reduced)
            {
                Set(child, OpacityProperty, 1);
                Set(translate, TranslateTransform.YProperty, 0);
                continue;
            }

            var begin = start + index++ * 70;
            Set(child, OpacityProperty, 0);
            Set(translate, TranslateTransform.YProperty, StatOffset);
            Animate(child, OpacityProperty, 0, 1, 240, Ease.Out, begin);
            Animate(translate, TranslateTransform.YProperty, StatOffset, 0, 300, Ease.Out, begin);
        }
    }

    private void Pulse()
    {
        var settle = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut };
        Animate(BodyScale, ScaleTransform.ScaleXProperty, 0.97, 1, 240, settle);
        Animate(BodyScale, ScaleTransform.ScaleYProperty, 0.97, 1, 240, settle);
        Animate(Body, OpacityProperty, 0.55, 1, 180, Ease.Out);
    }

    /// <summary>A short, damped horizontal shake for errors.</summary>
    private void Shake(double beginMs)
    {
        var shake = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(beginMs) };
        double[] offsets = [0, -7, 6, -4, 3, -1.5, 0];
        for (var i = 0; i < offsets.Length; i++)
            shake.KeyFrames.Add(new EasingDoubleKeyFrame(offsets[i], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(i * 55)), Ease.InOut));
        RootTranslate.BeginAnimation(TranslateTransform.XProperty, shake);
    }

    private void StartCountdown(TimeSpan duration, int generation)
    {
        Set(ProgressScale, ScaleTransform.ScaleXProperty, 1);
        Animate(ProgressScale, ScaleTransform.ScaleXProperty, 1, 0, duration.TotalMilliseconds, easing: null,
            completed: (_, _) => Dismiss(generation));
    }

    private void Dismiss(int generation)
    {
        // A newer notification supersedes this countdown.
        if (generation != _generation || !_isShowing)
            return;

        var reduced = MotionPreference.IsReduced;
        Animate(this, OpacityProperty, null, 0, reduced ? 150 : 180, Ease.In, completed: (_, _) =>
        {
            if (generation != _generation)
                return;
            Hide();
            _isShowing = false;
            Dismissed?.Invoke(this, EventArgs.Empty);
        });
        if (!reduced)
            Animate(RootTranslate, TranslateTransform.YProperty, null, DismissOffset, 180, Ease.In);
    }

    private void PlaceOnActiveScreen(bool moveToMonitorFirst)
    {
        if (_handle == 0)
            return;

        var work = ScreenPlacement.GetActiveWorkArea();
        if (moveToMonitorFirst)
        {
            // Moving onto the monitor first lets WPF adopt that monitor's DPI before the final size is measured.
            ScreenPlacement.MoveTopmost(_handle, work.Left, work.Top);
            UpdateLayout();
        }

        var bounds = ScreenPlacement.GetWindowBounds(_handle);
        var margin = (int)Math.Round(ScreenMarginDip * ScreenPlacement.GetScale(_handle));
        var (x, y) = ScreenPlacement.BottomRight(work, bounds.Width, bounds.Height, margin);
        if (x != bounds.Left || y != bounds.Top)
            ScreenPlacement.MoveTopmost(_handle, x, y);
    }

    private void Announce()
    {
        var peer = UIElementAutomationPeer.FromElement(TitleText) ?? UIElementAutomationPeer.CreatePeerForElement(TitleText);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private static void Set(IAnimatable target, DependencyProperty property, double value)
    {
        target.BeginAnimation(property, null); // Release any held animation value first.
        ((DependencyObject)target).SetValue(property, value);
    }

    private static void Animate(
        IAnimatable target, DependencyProperty property, double? from, double to, double milliseconds,
        IEasingFunction? easing, double beginMilliseconds = 0, EventHandler? completed = null)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(milliseconds),
            BeginTime = TimeSpan.FromMilliseconds(beginMilliseconds),
            EasingFunction = easing,
        };
        if (from.HasValue)
            animation.From = from.Value;
        if (completed is not null)
            animation.Completed += completed;
        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static class Ease
    {
        public static readonly IEasingFunction Out = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
        public static readonly IEasingFunction In = Frozen(new CubicEase { EasingMode = EasingMode.EaseIn });
        public static readonly IEasingFunction InOut = Frozen(new SineEase { EasingMode = EasingMode.EaseInOut });

        private static IEasingFunction Frozen(EasingFunctionBase easing)
        {
            easing.Freeze();
            return easing;
        }
    }
}
