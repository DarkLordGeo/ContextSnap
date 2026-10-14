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
    private RegisteredWaitHandle? _registration;

    private SingleInstance(string name)
    {
        _mutex = new Mutex(initiallyOwned: true, $@"Local\{name}.Instance", out var createdNew);
        IsFirstInstance = createdNew;
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}.Activate");
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
        _activation.Dispose();
        if (IsFirstInstance)
            _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
