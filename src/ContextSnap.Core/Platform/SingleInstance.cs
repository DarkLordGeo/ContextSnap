namespace ContextSnap.Platform;

/// <summary>
/// Ensures one ContextSnap window per Windows session. A second launch signals the first instance
/// (which brings its window forward) and exits. Uses session-local named kernel objects only.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    public const string DefaultName = "ContextSnap";

    private const uint AllowAnyProcess = 0xFFFFFFFF;

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activation;
    private readonly EventWaitHandle _exit;
    private RegisteredWaitHandle? _registration;
    private RegisteredWaitHandle? _exitRegistration;

    private SingleInstance(string name)
    {
        _mutex = new Mutex(initiallyOwned: true, $@"Local\{name}.Instance", out var createdNew);
        IsFirstInstance = createdNew;
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}.Activate");
        _exit = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}.Exit");
    }

    public bool IsFirstInstance { get; }

    public static SingleInstance Acquire(string name = DefaultName) => new(name);

    /// <summary>First instance only: runs <paramref name="onActivate"/> (on a thread-pool thread) whenever another launch asks it to show.</summary>
    public void ListenForActivation(Action onActivate)
    {
        if (!IsFirstInstance)
            throw new InvalidOperationException("Only the first instance can listen for activation requests.");

        _registration ??= ThreadPool.RegisterWaitForSingleObject(
            _activation, (_, _) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>First instance only: runs <paramref name="onExit"/> (on a thread-pool thread) when <c>ContextSnap --exit</c> asks it to quit.</summary>
    public void ListenForExit(Action onExit)
    {
        if (!IsFirstInstance)
            throw new InvalidOperationException("Only the first instance can listen for exit requests.");

        _exitRegistration ??= ThreadPool.RegisterWaitForSingleObject(
            _exit, (_, _) => onExit(), null, Timeout.Infinite, executeOnlyOnce: true);
    }

    /// <summary>Second instance: asks the running instance to quit. Returns false when none is running.</summary>
    public bool RequestExitOfFirstInstance()
    {
        if (IsFirstInstance)
            return false;
        _exit.Set();
        return true;
    }

    /// <summary>Second instance: asks the running instance to bring its window to the front.</summary>
    public void ActivateFirstInstance()
    {
        // Lets the running instance take the foreground; this launch was started by the user, so it may grant that.
        NativeMethods.AllowSetForegroundWindow(AllowAnyProcess);
        _activation.Set();
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _exitRegistration?.Unregister(null);
        _activation.Dispose();
        _exit.Dispose();
        if (IsFirstInstance)
            _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
