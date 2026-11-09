namespace AstraFinance.App.Services;

/// <summary>
/// Ensures one running instance per Windows session. A second launch signals the first
/// instance (which brings its window to the front) and then exits.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\AstraFinance.SingleInstance.7C3E1A52";
    private const string ActivateEventName = @"Local\AstraFinance.Activate.7C3E1A52";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activateEvent;
    private readonly CancellationTokenSource _cts = new();
    private RegisteredWaitHandle? _registration;

    private SingleInstanceGuard(Mutex mutex, EventWaitHandle activateEvent)
    {
        _mutex = mutex;
        _activateEvent = activateEvent;
    }

    /// <summary>
    /// Attempts to become the primary instance. Returns null (after signalling the
    /// existing instance) when another instance is already running.
    /// </summary>
    public static SingleInstanceGuard? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        var activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        if (createdNew)
        {
            return new SingleInstanceGuard(mutex, activateEvent);
        }

        activateEvent.Set();
        activateEvent.Dispose();
        mutex.Dispose();
        return null;
    }

    /// <summary>Invokes <paramref name="onActivate"/> (on a thread-pool thread) whenever a second launch occurs.</summary>
    public void ListenForActivation(Action onActivate)
    {
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _activateEvent,
            (_, timedOut) =>
            {
                if (!timedOut && !_cts.IsCancellationRequested)
                {
                    onActivate();
                }
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _registration?.Unregister(null);
        _activateEvent.Dispose();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned by this thread; the OS releases it when the process exits.
        }

        _mutex.Dispose();
        _cts.Dispose();
    }
}
