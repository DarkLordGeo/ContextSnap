using System.Runtime.InteropServices;
using ContextSnap.Capture;
using ContextSnap.Configuration;
using ContextSnap.Platform;

namespace ContextSnap.Tests;

/// <summary>All tests use temporary folders or in-memory fakes; the real Startup folder is never touched.</summary>
public class LaunchTargetResolverTests
{
    private const string Published = @"D:\Apps\ContextSnap";
    private const string DebugBuild = @"D:\src\ContextSnap\src\ContextSnap\bin\Debug\net10.0-windows";

    private static Func<string, bool> Files(params string[] existing) =>
        path => existing.Contains(path, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Resolve_PrefersLauncherNextToApplication()
    {
        var result = LaunchTargetResolver.Resolve(@"D:\Apps\ContextSnap\Snapctx.exe", Published + @"\", Files(@"D:\Apps\ContextSnap\Snapctx.exe"));

        var target = Assert.IsType<LaunchTarget>(result.Target);
        Assert.Equal(@"D:\Apps\ContextSnap\Snapctx.exe", target.FileName);
        Assert.Equal("--startup", target.Arguments);
        Assert.Equal(Published, target.WorkingDirectory);
        Assert.False(target.IsDevelopmentBuild);
    }

    [Fact]
    public void Resolve_UsesLauncherEvenWhenStartedThroughDotnetRun()
    {
        var launcher = DebugBuild + @"\Snapctx.exe";

        var result = LaunchTargetResolver.Resolve(@"C:\Program Files\dotnet\dotnet.exe", DebugBuild, Files(launcher, @"C:\Program Files\dotnet\dotnet.exe"));

        Assert.Equal(launcher, result.Target!.FileName);
        Assert.True(result.Target.IsDevelopmentBuild);
    }

    [Fact]
    public void Resolve_FallsBackToDotnetHostWhenLauncherIsMissing()
    {
        const string dotnet = @"C:\Program Files\dotnet\dotnet.exe";
        var assembly = Published + @"\Snapctx.dll";

        var result = LaunchTargetResolver.Resolve(dotnet, Published, Files(dotnet, assembly));

        Assert.Equal(dotnet, result.Target!.FileName);
        Assert.Equal($"\"{assembly}\" --startup", result.Target.Arguments);
        Assert.Equal(Published, result.Target.WorkingDirectory);
    }

    [Theory]
    [InlineData(@"D:\Apps\ContextSnap\Snapctx.exe")]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe")]
    [InlineData(null)]
    public void Resolve_ReportsMissingExecutable(string? processPath)
    {
        var result = LaunchTargetResolver.Resolve(processPath, Published, Files());

        Assert.Null(result.Target);
        Assert.Contains("executable was not found", result.Error);
    }

    [Fact]
    public void Resolve_RejectsEmptyBaseDirectory()
    {
        Assert.Null(LaunchTargetResolver.Resolve(null, " ", Files()).Target);
    }

    [Theory]
    [InlineData(@"D:\x\bin\Debug\net10.0-windows", true)]
    [InlineData(@"D:\x\bin\Release\net10.0-windows\", true)]
    [InlineData(@"D:\x\publish\ContextSnap", false)]
    [InlineData(@"C:\Program Files\ContextSnap", false)]
    public void IsDevelopmentBuild_DetectsBuildOutputFolders(string directory, bool expected)
    {
        Assert.Equal(expected, LaunchTargetResolver.IsDevelopmentBuild(directory));
    }
}

public class StartupRegistrationTests
{
    private const string StartupFolder = @"T:\FakeProfile\Startup";
    private const string ShortcutPath = StartupFolder + @"\Snapctx.lnk";
    private static readonly LaunchTarget Target = new(@"D:\Apps\ContextSnap\Snapctx.exe", "--startup", @"D:\Apps\ContextSnap", false);

    private sealed class FakeShortcutStore : IShortcutStore
    {
        public Dictionary<string, ShortcutDefinition> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Exception? ThrowOnWrite { get; set; }
        public Exception? ThrowOnDelete { get; set; }
        public int Writes { get; private set; }

        public bool Exists(string shortcutPath) => Files.ContainsKey(shortcutPath);

        public ShortcutDefinition? Read(string shortcutPath) => Files.GetValueOrDefault(shortcutPath);

        public void Write(string shortcutPath, ShortcutDefinition shortcut)
        {
            if (ThrowOnWrite is not null)
                throw ThrowOnWrite;
            Writes++;
            Files[shortcutPath] = shortcut;
        }

        public void Delete(string shortcutPath)
        {
            if (ThrowOnDelete is not null)
                throw ThrowOnDelete;
            Files.Remove(shortcutPath);
        }
    }

    private static StartupRegistration Registration(
        FakeShortcutStore store, LaunchTargetResult? target = null, string? folder = StartupFolder, bool? disabledByWindows = null) =>
        new(store, () => folder, () => target ?? new LaunchTargetResult(Target, null), _ => disabledByWindows);

    [Fact]
    public void Enable_CreatesShortcutPointingAtExecutable()
    {
        var store = new FakeShortcutStore();

        var state = Registration(store).Apply(enabled: true);

        Assert.True(state.Success);
        Assert.True(state.IsRegistered);
        Assert.Equal(ShortcutPath, state.ShortcutPath);
        var shortcut = store.Files[ShortcutPath];
        Assert.Equal(Target.FileName, shortcut.TargetPath);
        Assert.Equal("--startup", shortcut.Arguments);
        Assert.Equal(Target.WorkingDirectory, shortcut.WorkingDirectory);
        Assert.Equal(Target.FileName, shortcut.IconPath);
    }

    [Fact]
    public void Enable_IsIdempotentAndRepairsMovedExecutable()
    {
        var store = new FakeShortcutStore();
        var registration = Registration(store);

        registration.Apply(true);
        registration.Apply(true);
        Assert.Equal(1, store.Writes);

        var moved = new LaunchTargetResult(Target with { FileName = @"E:\New\Snapctx.exe", WorkingDirectory = @"E:\New" }, null);
        Registration(store, moved).Apply(true);

        Assert.Equal(2, store.Writes);
        Assert.Equal(@"E:\New\Snapctx.exe", store.Files[ShortcutPath].TargetPath);
    }

    [Fact]
    public void Disable_RemovesOnlyContextSnapShortcut()
    {
        var store = new FakeShortcutStore();
        store.Files[StartupFolder + @"\Other.lnk"] = new ShortcutDefinition(@"C:\Other\other.exe", "", @"C:\Other", "", "");
        Registration(store).Apply(true);

        var state = Registration(store).Apply(enabled: false);

        Assert.True(state.Success);
        Assert.False(state.IsRegistered);
        Assert.False(store.Exists(ShortcutPath));
        Assert.True(store.Exists(StartupFolder + @"\Other.lnk"));
    }

    [Fact]
    public void Disable_WhenNotRegisteredIsNoOp()
    {
        var state = Registration(new FakeShortcutStore()).Apply(enabled: false);

        Assert.True(state.Success);
        Assert.False(state.IsRegistered);
    }

    [Fact]
    public void SameNamedShortcutForAnotherProgramIsNeverChanged()
    {
        var store = new FakeShortcutStore();
        var foreign = new ShortcutDefinition(@"C:\Tools\other.exe", "", @"C:\Tools", "", "");
        store.Files[ShortcutPath] = foreign;

        var enable = Registration(store).Apply(true);
        var disable = Registration(store).Apply(false);

        Assert.False(enable.Success);
        Assert.False(disable.Success);
        Assert.True(disable.NeedsAttention);
        Assert.Same(foreign, store.Files[ShortcutPath]);
    }

    [Fact]
    public void Enable_ReportsMissingExecutableWithoutCreatingShortcut()
    {
        var store = new FakeShortcutStore();

        var state = Registration(store, new LaunchTargetResult(null, "The Snapctx executable was not found in D:\\x.")).Apply(true);

        Assert.False(state.Success);
        Assert.False(state.IsRegistered);
        Assert.Contains("executable was not found", state.Message);
        Assert.Empty(store.Files);
    }

    [Fact]
    public void Apply_ReportsMissingStartupFolder()
    {
        var state = Registration(new FakeShortcutStore(), folder: "").Apply(true);

        Assert.False(state.Success);
        Assert.Null(state.ShortcutPath);
    }

    [Theory]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(COMException))]
    public void Apply_TurnsWriteAndDeleteFailuresIntoStatus(Type exceptionType)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "denied")!;
        var store = new FakeShortcutStore { ThrowOnWrite = exception };

        var enable = Registration(store).Apply(true);

        Assert.False(enable.Success);
        Assert.False(enable.IsRegistered);
        Assert.Contains("Could not create the startup shortcut", enable.Message);

        store.ThrowOnWrite = null;
        Registration(store).Apply(true);
        store.ThrowOnDelete = exception;
        var disable = Registration(store).Apply(false);

        Assert.False(disable.Success);
        Assert.True(disable.IsRegistered);
        Assert.Contains("Could not remove the startup shortcut", disable.Message);
    }

    [Fact]
    public void Enable_WarnsWhenWindowsHasTheItemTurnedOff()
    {
        var state = Registration(new FakeShortcutStore(), disabledByWindows: true).Apply(true);

        Assert.True(state.Success);
        Assert.True(state.NeedsAttention);
        Assert.Contains("Settings → Apps → Startup", state.Message);
    }

    [Fact]
    public void Enable_MentionsDevelopmentBuild()
    {
        var dev = new LaunchTargetResult(Target with { IsDevelopmentBuild = true }, null);

        var state = Registration(new FakeShortcutStore(), dev).Apply(true);

        Assert.Contains("development build", state.Message);
    }

    [Fact]
    public void IsOwnShortcut_RecognizesLauncherAndDotnetHostUnderBothNames()
    {
        Assert.True(StartupRegistration.IsOwnShortcut(new(@"D:\a\snapctx.EXE", "--startup", "", "", "")));
        Assert.True(StartupRegistration.IsOwnShortcut(new(@"C:\dotnet\dotnet.exe", "\"D:\\a\\Snapctx.dll\" --startup", "", "", "")));
        Assert.True(StartupRegistration.IsOwnShortcut(new(@"D:\old\ContextSnap.exe", "--startup", "", "", "")));
        Assert.True(StartupRegistration.IsOwnShortcut(new(@"C:\dotnet\dotnet.exe", "\"D:\\old\\ContextSnap.dll\" --startup", "", "", "")));
        Assert.False(StartupRegistration.IsOwnShortcut(new(@"C:\dotnet\dotnet.exe", "other.dll", "", "", "")));
    }

    private const string LegacyShortcutPath = StartupFolder + @"\ContextSnap.lnk";

    [Fact]
    public void Enable_ReplacesTheShortcutFromBeforeTheRename()
    {
        var store = new FakeShortcutStore();
        store.Files[LegacyShortcutPath] = new ShortcutDefinition(@"D:\Old\ContextSnap.exe", "--startup", @"D:\Old", "", "");

        var state = Registration(store).Apply(true);

        Assert.True(state.Success);
        Assert.True(store.Exists(ShortcutPath));
        Assert.False(store.Exists(LegacyShortcutPath));
    }

    [Fact]
    public void Disable_RemovesTheShortcutFromBeforeTheRename()
    {
        var store = new FakeShortcutStore();
        store.Files[LegacyShortcutPath] = new ShortcutDefinition(@"D:\Old\ContextSnap.exe", "--startup", @"D:\Old", "", "");

        var state = Registration(store).Apply(false);

        Assert.True(state.Success);
        Assert.False(state.IsRegistered);
        Assert.False(store.Exists(LegacyShortcutPath));
    }

    [Fact]
    public void LegacyNamedShortcutForAnotherProgramIsLeftAlone()
    {
        var store = new FakeShortcutStore();
        var foreign = new ShortcutDefinition(@"C:\Tools\other.exe", "", @"C:\Tools", "", "");
        store.Files[LegacyShortcutPath] = foreign;

        Registration(store).Apply(true);
        Registration(store).Apply(false);

        Assert.Same(foreign, store.Files[LegacyShortcutPath]);
    }
}

public sealed class ShellLinkShortcutStoreTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ContextSnapLink").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>Round-trips a real .lnk file in a temporary folder (not the Startup folder).</summary>
    [Fact]
    public void WriteReadDelete_RoundTripsRealShortcut()
    {
        var store = new ShellLinkShortcutStore();
        var path = Path.Combine(_directory, "nested", "ContextSnap.lnk");
        var target = Environment.ProcessPath!;
        var shortcut = new ShortcutDefinition(target, "--startup", Path.GetDirectoryName(target)!, "Snapctx test", target);

        store.Write(path, shortcut);
        var read = store.Read(path);

        Assert.True(store.Exists(path));
        Assert.NotNull(read);
        Assert.Equal(shortcut.TargetPath, read.TargetPath, ignoreCase: true);
        Assert.Equal("--startup", read.Arguments);
        Assert.Equal(shortcut.WorkingDirectory, read.WorkingDirectory, ignoreCase: true);
        Assert.Equal("Snapctx test", read.Description);

        store.Delete(path);
        Assert.False(store.Exists(path));
        Assert.Null(store.Read(path));
    }

    [Fact]
    public void Registration_WithRealStoreInTemporaryFolder()
    {
        var appDirectory = Directory.CreateDirectory(Path.Combine(_directory, "app")).FullName;
        var executable = Path.Combine(appDirectory, "Snapctx.exe");
        File.WriteAllBytes(executable, []); // Stand-in; never executed.
        var resolved = LaunchTargetResolver.Resolve(null, appDirectory, File.Exists);
        var registration = new StartupRegistration(new ShellLinkShortcutStore(), () => _directory, () => resolved);

        var enabled = registration.Apply(true);

        Assert.True(enabled.Success, enabled.Message);
        Assert.Equal(Path.Combine(_directory, "Snapctx.lnk"), enabled.ShortcutPath);
        var written = new ShellLinkShortcutStore().Read(enabled.ShortcutPath!)!;
        Assert.Equal(executable, written.TargetPath, ignoreCase: true);
        Assert.Equal(appDirectory, written.WorkingDirectory, ignoreCase: true);
        Assert.Equal("--startup", written.Arguments);

        var disabled = registration.Apply(false);
        Assert.True(disabled.Success, disabled.Message);
        Assert.False(File.Exists(enabled.ShortcutPath));
    }
}

public class SingleInstanceTests
{
    [Fact]
    public void SecondAcquireIsNotFirstAndCanActivateFirst()
    {
        var name = "ContextSnapTest-" + Guid.NewGuid().ToString("N");
        using var activated = new ManualResetEventSlim();

        using (var first = SingleInstance.Acquire(name))
        {
            Assert.True(first.IsFirstInstance);
            first.ListenForActivation(activated.Set);

            using (var second = SingleInstance.Acquire(name))
            {
                Assert.False(second.IsFirstInstance);
                Assert.Throws<InvalidOperationException>(() => second.ListenForActivation(() => { }));
                second.ActivateFirstInstance();
            }

            Assert.True(activated.Wait(TimeSpan.FromSeconds(5)));
        }

        using var afterExit = SingleInstance.Acquire(name);
        Assert.True(afterExit.IsFirstInstance);
    }
}

public class StartupOptionTests
{
    [Fact]
    public void StartupArgumentOpensGuiMinimized()
    {
        var options = CommandLineOptions.Parse(["--startup"]);

        Assert.Equal(RunMode.Gui, options.Mode);
        Assert.True(options.StartMinimized);
        Assert.Null(options.Error);
        Assert.False(CommandLineOptions.Parse([]).StartMinimized);
    }

    [Fact]
    public void StartWithWindowsIsOnByDefaultAndPersists()
    {
        Assert.True(new AppSettings().StartWithWindows);

        var directory = Directory.CreateTempSubdirectory("ContextSnapStartupSettings").FullName;
        try
        {
            var store = new SettingsStore(Path.Combine(directory, "settings.json"));
            store.Save(new AppSettings { StartWithWindows = false });
            Assert.False(store.Load().Settings.StartWithWindows);

            File.WriteAllText(store.FilePath, "{ \"collectGit\": true }");
            Assert.True(store.Load().Settings.StartWithWindows);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
