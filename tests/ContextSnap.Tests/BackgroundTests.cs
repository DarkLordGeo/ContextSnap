using ContextSnap.Capture;
using ContextSnap.Configuration;
using ContextSnap.Platform;

namespace ContextSnap.Tests;

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Shift+C", HotkeyGesture.ModControl | HotkeyGesture.ModAlt | HotkeyGesture.ModShift, 0x43u, "Ctrl+Alt+Shift+C")]
    [InlineData("shift+alt+ctrl+c", HotkeyGesture.ModControl | HotkeyGesture.ModAlt | HotkeyGesture.ModShift, 0x43u, "Ctrl+Alt+Shift+C")]
    [InlineData("Control + Win + F9", HotkeyGesture.ModControl | HotkeyGesture.ModWin, 0x78u, "Ctrl+Win+F9")]
    [InlineData("Alt+7", HotkeyGesture.ModAlt, 0x37u, "Alt+7")]
    [InlineData("Win+F24", HotkeyGesture.ModWin, 0x87u, "Win+F24")]
    public void Parse_AcceptsModifierPlusKey(string text, uint modifiers, uint key, string display)
    {
        var gesture = HotkeyGesture.Parse(text);

        Assert.NotNull(gesture);
        Assert.Equal(modifiers, gesture.Modifiers);
        Assert.Equal(key, gesture.VirtualKey);
        Assert.Equal(display, gesture.DisplayText);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("C")]                // No modifier: would capture ordinary typing.
    [InlineData("Shift+C")]          // Shift alone is not enough.
    [InlineData("Ctrl+Alt")]         // No key.
    [InlineData("Ctrl+C+V")]         // Two keys.
    [InlineData("Ctrl+Space")]       // Unsupported key name.
    [InlineData("Ctrl+F25")]
    public void Parse_RejectsInvalidShortcuts(string? text)
    {
        Assert.Null(HotkeyGesture.Parse(text));
    }

    [Fact]
    public void Default_IsCtrlAltShiftC()
    {
        Assert.Equal("Ctrl+Alt+Shift+C", HotkeyGesture.Default.DisplayText);
    }
}

public class BackgroundPolicyTests
{
    [Theory]
    [InlineData(false, true, false, true)]   // Shortcut works: keep running hidden.
    [InlineData(false, false, true, true)]   // Optional icon offers a way back.
    [InlineData(false, false, false, false)] // Nothing would work in the background: quit.
    [InlineData(true, true, true, false)]    // Quit / --exit / sign-out.
    public void KeepRunningAfterClose(bool exitRequested, bool hotkey, bool tray, bool expected)
    {
        Assert.Equal(expected, BackgroundPolicy.KeepRunningAfterClose(exitRequested, hotkey, tray));
    }

    [Fact]
    public void SignInLaunch_ShowsNoWindow()
    {
        Assert.False(BackgroundPolicy.ShowWindowAtLaunch(startedAtSignIn: true));
        Assert.True(BackgroundPolicy.ShowWindowAtLaunch(startedAtSignIn: false));
    }

    [Theory]
    [InlineData("--exit", RunMode.Exit)]
    [InlineData("/exit", RunMode.Exit)]
    [InlineData("--enable-startup", RunMode.EnableStartup)]
    [InlineData("--disable-startup", RunMode.DisableStartup)]
    public void ParsesBackgroundCommands(string argument, RunMode mode)
    {
        var options = CommandLineOptions.Parse([argument]);

        Assert.Equal(mode, options.Mode);
        Assert.Null(options.Error);
    }

    [Fact]
    public void StartupArgument_StillMeansSilentBackgroundStart()
    {
        var options = CommandLineOptions.Parse(["--startup"]);

        Assert.Equal(RunMode.Gui, options.Mode);
        Assert.True(options.StartMinimized);
    }
}

public sealed class BackgroundCommandsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ContextSnapBackground").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class MemoryShortcuts : IShortcutStore
    {
        public Dictionary<string, ShortcutDefinition> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool Exists(string shortcutPath) => Files.ContainsKey(shortcutPath);
        public ShortcutDefinition? Read(string shortcutPath) => Files.GetValueOrDefault(shortcutPath);
        public void Write(string shortcutPath, ShortcutDefinition shortcut) => Files[shortcutPath] = shortcut;
        public void Delete(string shortcutPath) => Files.Remove(shortcutPath);
    }

    private (BackgroundCommands Commands, SettingsStore Store, MemoryShortcuts Shortcuts) Create(Func<bool>? exit = null)
    {
        var store = new SettingsStore(Path.Combine(_directory, "settings.json"));
        var shortcuts = new MemoryShortcuts();
        var target = new LaunchTarget(@"D:\Apps\ContextSnap\ContextSnap.exe", LaunchTargetResolver.StartupArgument, @"D:\Apps\ContextSnap", false);
        var startup = new StartupRegistration(shortcuts, () => Path.Combine(_directory, "Startup"), () => new LaunchTargetResult(target, null));
        return (new BackgroundCommands(store, startup, exit ?? (() => false)), store, shortcuts);
    }

    [Fact]
    public void DisableThenEnableStartup_UpdatesSettingAndShortcut()
    {
        var (commands, store, shortcuts) = Create();

        Assert.Equal(0, commands.Run(RunMode.EnableStartup, new StringWriter(), new StringWriter()));
        var shortcut = Assert.Single(shortcuts.Files).Value;
        Assert.Equal("--startup", shortcut.Arguments);

        Assert.Equal(0, commands.Run(RunMode.DisableStartup, new StringWriter(), new StringWriter()));
        Assert.Empty(shortcuts.Files);
        Assert.False(store.Load().Settings.StartWithWindows);

        Assert.Equal(0, commands.Run(RunMode.EnableStartup, new StringWriter(), new StringWriter()));
        Assert.Single(shortcuts.Files);
        Assert.True(store.Load().Settings.StartWithWindows);
    }

    [Theory]
    [InlineData(true, "Asked the running Snapctx to quit")]
    [InlineData(false, "Snapctx is not running.")]
    public void Exit_ReportsWhetherAnInstanceWasRunning(bool running, string expected)
    {
        var (commands, _, _) = Create(() => running);
        var output = new StringWriter();

        Assert.Equal(0, commands.Run(RunMode.Exit, output, new StringWriter()));
        Assert.Contains(expected, output.ToString());
    }
}

public class SingleInstanceExitTests
{
    [Fact]
    public void ExitRequest_ReachesTheRunningInstanceOnce()
    {
        var name = "ContextSnapExitTest-" + Guid.NewGuid().ToString("N");
        using var exited = new ManualResetEventSlim();

        using var first = SingleInstance.Acquire(name);
        first.ListenForExit(exited.Set);

        using (var second = SingleInstance.Acquire(name))
        {
            Assert.False(second.IsFirstInstance);
            Assert.True(second.RequestExitOfFirstInstance());
        }

        Assert.True(exited.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ExitRequest_WithNothingRunning_ReturnsFalse()
    {
        using var only = SingleInstance.Acquire("ContextSnapExitTest-" + Guid.NewGuid().ToString("N"));

        Assert.True(only.IsFirstInstance);
        Assert.False(only.RequestExitOfFirstInstance());
    }
}

public class GlobalHotkeyRegistrationTests
{
    /// <summary>
    /// Real RegisterHotKey on a dedicated thread with an unusual combination: registration, idempotent
    /// re-registration, conflict reporting (a second owner fails instead of throwing), and cleanup.
    /// </summary>
    [InteractiveDesktopFact]
    public void RegistersReportsConflictsAndUnregisters()
    {
        var gesture = HotkeyGesture.Parse("Ctrl+Alt+Shift+Win+F23")!;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var first = new GlobalHotkey(0);
                Assert.True(first.Register(gesture));
                Assert.True(first.Register(gesture));
                Assert.Equal(gesture, first.Current);

                using (var second = new GlobalHotkey(0))
                {
                    Assert.False(second.Register(gesture));
                    Assert.False(second.IsRegistered);
                }

                first.Unregister();
                Assert.False(first.IsRegistered);
                Assert.Null(first.Current);

                using var again = new GlobalHotkey(0);
                Assert.True(again.Register(gesture)); // Freed after cleanup.
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (failure is not null)
            throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}

public class BackgroundSettingsTests
{
    [Fact]
    public void Defaults_HaveNoTrayIconAndTheStandardShortcut()
    {
        var settings = new AppSettings();

        Assert.False(settings.ShowNotificationAreaIcon);
        Assert.Equal("Ctrl+Alt+Shift+C", settings.Hotkey);
        Assert.True(settings.StartWithWindows);
        Assert.True(settings.EnableGlobalHotkey);
    }

    [Theory]
    [InlineData("ctrl+win+f9", "Ctrl+Win+F9")]
    [InlineData("not a shortcut", "Ctrl+Alt+Shift+C")]
    [InlineData("", "Ctrl+Alt+Shift+C")]
    public void Normalize_CanonicalizesOrResetsTheShortcut(string input, string expected)
    {
        var settings = new AppSettings { Hotkey = input };

        settings.Normalize();

        Assert.Equal(expected, settings.Hotkey);
    }
}
