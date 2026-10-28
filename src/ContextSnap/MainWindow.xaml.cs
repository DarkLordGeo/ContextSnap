using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ContextSnap.Capture;
using ContextSnap.Collectors.ScreenText;
using ContextSnap.Configuration;
using ContextSnap.Notifications;
using ContextSnap.Output;
using ContextSnap.Platform;

namespace ContextSnap;

public partial class MainWindow : Window
{
    public static readonly RoutedUICommand CaptureCommand = new("Capture snapshot", nameof(CaptureCommand), typeof(MainWindow));
    public static readonly RoutedUICommand CopyCommand = new("Copy to clipboard", nameof(CopyCommand), typeof(MainWindow));

    private const string CaptureLabel = "_Capture snapshot";

    private enum StatusKind { Neutral, Busy, Success, Warning, Error }

    private readonly SettingsStore _store;
    private readonly SnapshotService _service;
    private readonly CopyService _copy;
    private readonly HotkeyCaptureWorkflow _hotkeyCapture;
    private readonly DiagnosticsLog _diagnostics = DiagnosticsLog.CreateDefault();
    private readonly StartupRegistration _startup;

    private AppSettings _settings;
    private string? _savedSettingsJson;
    private CaptureResult? _lastResult;
    private bool _isCapturing;
    private GlobalHotkey? _hotkey;
    private HwndSource? _hwndSource;
    private bool? _appliedStartWithWindows;
    private NotificationAreaIcon? _trayIcon;
    private bool _exiting;
    private bool _closeHintShown;

    public MainWindow(SettingsStore store, SnapshotService service, IClipboardWriter clipboard, WpfCopyNotifier notifier,
        StartupRegistration startup)
    {
        _store = store;
        _service = service;
        _copy = new CopyService(clipboard, notifier);
        _hotkeyCapture = new HotkeyCaptureWorkflow(settings => _service.CaptureAsync(settings), _copy, notifier, _diagnostics);
        _startup = startup;
        notifier.Failed += error =>
            SetStatus(StatusKind.Warning, $"Copied, but the notification could not be shown ({error}).");

        InitializeComponent();

        var loaded = store.Load();
        _settings = loaded.Settings;
        // Only write when the user changes something; an unreadable file is left untouched until then.
        _savedSettingsJson = Serialize(_settings);
        SettingsPanel.DataContext = _settings;
        FirstRunText.Visibility = loaded.IsFirstRun ? Visibility.Visible : Visibility.Collapsed;
        SettingsPathText.Text = $"Changes are saved automatically to {store.FilePath}";
        UpdateEnabledCollectorsText();

        if (loaded.Error is not null)
            SetStatus(StatusKind.Warning, loaded.Error);
        else
            SetStatus(StatusKind.Neutral, "Ready. Press Capture snapshot (F5 or Ctrl+Enter) to begin.");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(WndProc);
        _hotkey = new GlobalHotkey(handle);
        ApplyHotkeySetting();
        ApplyTrayIconSetting();
        ApplyStartupSetting();
    }

    private string HotkeyText => HotkeyGesture.Parse(_settings.Hotkey)?.DisplayText ?? HotkeyGesture.DefaultText;

    /// <summary>The notification-area icon is opt-in; by default Snapctx runs with no icon at all.</summary>
    private void ApplyTrayIconSetting()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (_settings.ShowNotificationAreaIcon && _trayIcon is not { IsAdded: true } && handle != 0)
        {
            _trayIcon = new NotificationAreaIcon(handle, $"Snapctx — {HotkeyText} to capture");
            _trayIcon.Add();
        }
        else if (!_settings.ShowNotificationAreaIcon && _trayIcon is not null)
        {
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }

    /// <summary>Quits for real (Quit button, tray menu Exit, <c>--exit</c>, or Windows signing out).</summary>
    public void ExitApplication()
    {
        if (_exiting)
            return;
        _exiting = true;
        Close();
        Application.Current?.Shutdown();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Closing hides the window and keeps Snapctx running in the background while its shortcut works
        // (or its optional icon is shown). Opening Snapctx again shows this window; Quit or --exit ends it.
        if (BackgroundPolicy.KeepRunningAfterClose(_exiting, _hotkey is { IsRegistered: true }, _trayIcon is { IsAdded: true }))
        {
            e.Cancel = true;
            CommitPendingSettingsEdit();
            Hide();
            if (!_closeHintShown && _trayIcon is { IsAdded: true })
            {
                _closeHintShown = true;
                _trayIcon.ShowInfo("Snapctx is still running",
                    $"Press {HotkeyText} to capture. To quit, right-click the Snapctx icon and choose Exit.");
            }

            return;
        }

        if (!_exiting)
        {
            // Nothing would keep working in the background (shortcut off or unavailable), so closing quits.
            _exiting = true;
            Dispatcher.BeginInvoke(() => Application.Current?.Shutdown());
        }

        base.OnClosing(e);
    }

    /// <summary>Called when another launch of Snapctx asks this instance to show itself.</summary>
    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Show();
        Activate();
        Topmost = true; // Nudge past focus-stealing prevention, then restore normal z-order.
        Topmost = false;
        Focus();

        // Windows refuses activation when the launching process did not own the foreground; flash instead.
        if (!IsActive)
            WindowAttention.Flash(new WindowInteropHelper(this).Handle);
    }

    protected override void OnClosed(EventArgs e)
    {
        CommitPendingSettingsEdit();
        _hotkey?.Dispose();
        _trayIcon?.Dispose();
        _hwndSource?.RemoveHook(WndProc);
        base.OnClosed(e);
    }

    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == GlobalHotkey.WmHotkey && wParam == GlobalHotkey.Id)
        {
            handled = true;
            _ = HotkeyCaptureAsync();
            return 0;
        }

        var command = _trayIcon?.HandleMessage(message, wParam, lParam, $"&Capture snapshot\t{HotkeyText}", out handled)
            ?? TrayCommand.None;
        switch (command)
        {
            case TrayCommand.Open:
                BringToFront();
                break;
            case TrayCommand.Capture:
                // Same as the shortcut: capture and copy, no window.
                _ = HotkeyCaptureAsync();
                break;
            case TrayCommand.Exit:
                Dispatcher.BeginInvoke(ExitApplication);
                break;
        }

        return 0;
    }

    private void OnCaptureCanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = !_isCapturing;

    private void OnCaptureExecuted(object sender, ExecutedRoutedEventArgs e) =>
        _ = CaptureAsync();

    private void OnCopyCanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = !_isCapturing && PreviewBox is { Text.Length: > 0 }; // Null while XAML is loading.

    private void OnCopyExecuted(object sender, ExecutedRoutedEventArgs e) => CopyPreview();

    private async Task CaptureAsync()
    {
        if (_isCapturing)
            return;

        CommitPendingSettingsEdit();
        _isCapturing = true;
        CaptureButton.Content = "Capturing…";
        SetStatus(StatusKind.Busy, "Capturing context…");
        CommandManager.InvalidateRequerySuggested();

        try
        {
            var result = await _service.CaptureAsync(_settings);
            _lastResult = result;
            ShowPreview(result);

            if (_settings.CopyAfterCapture)
                CopyPreview();
            else
                SetStatus(StatusKind.Success, "Snapshot captured. Review or edit it below, then copy it.");
        }
        catch (Exception ex)
        {
            // UI boundary: a capture bug must be reported, not crash the window.
            SetStatus(StatusKind.Error, $"Capture failed: {ex.Message}");
        }
        finally
        {
            _isCapturing = false;
            CaptureButton.Content = CaptureLabel;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>
    /// The global shortcut (and the optional icon's Capture): capture and copy the complete Markdown with no
    /// window, dialog, or review. The hidden window's preview is updated so opening Snapctx shows it.
    /// </summary>
    private async Task HotkeyCaptureAsync()
    {
        if (IsVisible)
            CommitPendingSettingsEdit();

        HotkeyCaptureOutcome outcome;
        try
        {
            outcome = await _hotkeyCapture.RunAsync(_settings.Clone());
        }
        catch (Exception ex)
        {
            // Defensive: the workflow already handles capture and copy failures.
            _diagnostics.Error("Shortcut workflow failed", ex);
            SetStatus(StatusKind.Error, $"Shortcut capture failed: {ex.Message}. See {_diagnostics.FilePath}.");
            return;
        }

        if (outcome.Capture is { } capture)
        {
            _lastResult = capture;
            ShowPreview(capture);
        }

        // Return the capture's temporary memory to Windows now, rather than holding it until the next capture.
        if (outcome.Status != HotkeyCaptureStatus.Busy)
            _ = Task.Run(MemoryRelief.ReleaseCaptureMemory);

        var time = DateTime.Now.ToString("T", CultureInfo.CurrentCulture);
        switch (outcome.Status)
        {
            case HotkeyCaptureStatus.Copied:
                SetStatus(StatusKind.Success, $"Shortcut copied {outcome.Capture!.Markdown.Length:N0} characters at {time}.");
                break;
            case HotkeyCaptureStatus.CopyFailed:
                SetStatus(StatusKind.Error, $"Shortcut capture at {time} could not be copied: {outcome.Error}");
                break;
            case HotkeyCaptureStatus.CaptureFailed:
                SetStatus(StatusKind.Error, $"Shortcut capture at {time} failed: {outcome.Error}. Details: {_diagnostics.FilePath}");
                break;
        }
    }

    private void OnOpenDiagnostics(object sender, RoutedEventArgs e)
    {
        var folder = System.IO.Path.GetDirectoryName(_diagnostics.FilePath)!;
        System.IO.Directory.CreateDirectory(folder);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { ArgumentList = { folder }, UseShellExecute = false });
    }

    private void ShowPreview(CaptureResult result)
    {
        PreviewBox.Text = result.Markdown;
        PreviewBox.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;
        PreviewBox.ScrollToHome();
        Tabs.SelectedIndex = 0;

        CollectorChips.ItemsSource = result.Snapshot.AllResults.Select(CollectorChip.From).ToArray();
        CollectorChips.Visibility = Visibility.Visible;
        UpdateMetaText();
    }

    /// <summary>Single copy path for the Copy button, capture-and-copy, and the global shortcut.</summary>
    private void CopyPreview()
    {
        var outcome = _copy.Copy(new CopyRequest(
            PreviewBox.Text,
            _settings.ExcludeFromClipboardHistory,
            _lastResult?.WasTruncated == true,
            _lastResult?.OutputLimit ?? _settings.MaxOutputCharacters,
            _settings.ShowCopyNotifications,
            ScreenTextSummary.CopyDetail(_lastResult?.Snapshot.ScreenText)));

        switch (outcome.Status)
        {
            case CopyStatus.Copied:
                var time = DateTime.Now.ToString("T", CultureInfo.CurrentCulture);
                SetStatus(StatusKind.Success, $"Copied {PreviewBox.Text.Length:N0} characters to the clipboard at {time}. Paste it into your AI chat.");
                break;
            case CopyStatus.Empty:
                SetStatus(StatusKind.Warning, CopyService.EmptyError);
                break;
            default:
                SetStatus(StatusKind.Error, $"Copy failed: {outcome.Error}");
                break;
        }

        if (outcome.NotificationError is not null)
            SetStatus(StatusKind.Warning, $"Copied, but the notification could not be shown ({outcome.NotificationError}).");
    }

    private void OnPreviewTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateMetaText();
        CommandManager.InvalidateRequerySuggested();
    }

    private void UpdateMetaText()
    {
        if (_lastResult is null)
        {
            MetaText.Text = string.Empty;
            return;
        }

        var parts = new List<string>
        {
            $"Captured {_lastResult.Snapshot.CapturedAt.ToLocalTime():T}",
            $"{PreviewBox.Text.Length:N0} / {_settings.MaxOutputCharacters:N0} characters",
        };
        if (_lastResult.WasTruncated)
            parts.Add("truncated");
        if (!string.Equals(PreviewBox.Text, _lastResult.Markdown, StringComparison.Ordinal))
            parts.Add("edited");
        MetaText.Text = string.Join(" · ", parts);
    }

    private void SetStatus(StatusKind kind, string message)
    {
        StatusText.Text = message;
        StatusDot.Fill = new SolidColorBrush(kind switch
        {
            StatusKind.Busy => Color.FromRgb(0x23, 0x57, 0xD9),
            StatusKind.Success => Color.FromRgb(0x1F, 0x88, 0x3D),
            StatusKind.Warning => Color.FromRgb(0xBF, 0x87, 0x00),
            StatusKind.Error => Color.FromRgb(0xCF, 0x22, 0x2E),
            _ => Color.FromRgb(0x8C, 0x95, 0x9F),
        });
    }

    // Settings

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.Primitives.ToggleButton)
            SaveSettings(); // Check boxes and radio buttons.
    }

    private void OnSettingsLostFocus(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox)
            SaveSettings();
    }

    private void OnQuit(object sender, RoutedEventArgs e)
    {
        CommitPendingSettingsEdit();
        ExitApplication();
    }

    private void OnRestoreDefaults(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "Restore all settings, exclusions, and lists to their defaults?",
            "Restore defaults", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        _settings = new AppSettings();
        RebindSettings();
        SaveSettings();
    }

    /// <summary>Pushes a half-finished text box edit into the settings before capturing or closing.</summary>
    private void CommitPendingSettingsEdit()
    {
        if (Keyboard.FocusedElement is TextBox box && SettingsPanel.IsAncestorOf(box))
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        SaveSettings();
    }

    private void SaveSettings()
    {
        var invalidHotkey = HotkeyGesture.Parse(_settings.Hotkey) is null ? _settings.Hotkey : null;
        var before = Serialize(_settings);
        _settings.Normalize();
        var after = Serialize(_settings);
        if (before != after)
            RebindSettings(); // Show clamped values.

        ApplyHotkeySetting();
        if (invalidHotkey is not null)
            SetStatus(StatusKind.Warning, $"\"{invalidHotkey}\" is not a valid shortcut, so the previous default was restored. Use Ctrl, Alt, or Win (optionally Shift) plus a letter, digit, or F1–F24.");
        ApplyTrayIconSetting();
        if (_settings.StartWithWindows != _appliedStartWithWindows)
            ApplyStartupSetting();
        UpdateEnabledCollectorsText();
        UpdateMetaText();

        if (after == _savedSettingsJson)
            return;

        try
        {
            _store.Save(_settings);
            _savedSettingsJson = after;
            SettingsPathText.Text = $"Saved at {DateTime.Now:T} to {_store.FilePath}";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            SetStatus(StatusKind.Error, $"Settings could not be saved: {ex.Message}");
        }
    }

    private void RebindSettings()
    {
        SettingsPanel.DataContext = null;
        SettingsPanel.DataContext = _settings;
    }

    private void ApplyHotkeySetting()
    {
        if (_hotkey is null)
            return;

        if (!_settings.EnableGlobalHotkey)
        {
            _hotkey.Unregister();
            HotkeyStatusText.Text = "Off.";
            HotkeyHint.Text = string.Empty;
            return;
        }

        var gesture = HotkeyGesture.Parse(_settings.Hotkey) ?? HotkeyGesture.Default;
        if (_hotkey.Register(gesture))
        {
            HotkeyStatusText.Text = $"Active: {gesture.DisplayText} works in any application while Snapctx runs in the background, also after this window is closed.";
            HotkeyHint.Text = $"Tip: press {gesture.DisplayText} in any application to capture and copy in one step.";
        }
        else
        {
            HotkeyStatusText.Text = $"Unavailable: another application is already using {gesture.DisplayText}. Choose another shortcut.";
            HotkeyHint.Text = string.Empty;
        }
    }

    /// <summary>Creates or removes the Startup-folder shortcut to match the setting and reports the outcome.</summary>
    private void ApplyStartupSetting()
    {
        _appliedStartWithWindows = _settings.StartWithWindows;
        var state = _startup.Apply(_settings.StartWithWindows);
        StartupStatusText.Text = state.Message;
        if (!state.Success)
            SetStatus(StatusKind.Error, state.Message);
        else if (state.NeedsAttention)
            SetStatus(StatusKind.Warning, state.Message);
    }

    private void UpdateEnabledCollectorsText()
    {
        var collectors = new (string Name, bool Enabled)[]
        {
            ("open windows", _settings.CollectWindows),
            ("Git repositories", _settings.CollectGit),
            ("Explorer folders", _settings.CollectExplorerFolders),
            ("developer tools", _settings.CollectProcesses),
            ("Desktop items", _settings.CollectDesktop),
            ("system summary", _settings.CollectSystemSummary),
            ("browser tabs", _settings.CollectBrowserTabs),
            (_settings.ScreenTextMode == ScreenTextMode.AllVisibleWindows ? "visible text of all windows" : "visible text of the focused window",
                _settings.CaptureScreenText),
        };
        var on = collectors.Where(c => c.Enabled).Select(c => c.Name).ToArray();
        var off = collectors.Where(c => !c.Enabled).Select(c => c.Name).ToArray();
        EnabledCollectorsText.Text =
            (on.Length > 0 ? $"Will collect: {string.Join(", ", on)}." : "All collectors are turned off.")
            + (off.Length > 0 ? $" Off: {string.Join(", ", off)}." : string.Empty);
    }

    private static string Serialize(AppSettings settings) => JsonSerializer.Serialize(settings);
}
