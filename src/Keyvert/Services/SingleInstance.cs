namespace Keyvert.Services;

/// <summary>
/// Ensures one running copy per user session. A second launch signals the first one
/// to show its window and then exits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\Keyvert.Instance";
    private const string ShowEventName = @"Local\Keyvert.Show";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private RegisteredWaitHandle? _registration;

    private SingleInstance(Mutex mutex, EventWaitHandle showEvent)
    {
        _mutex = mutex;
        _showEvent = showEvent;
    }

    /// <returns>The instance guard, or null if another copy is already running (it has been told to show itself).</returns>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(true, MutexName, out bool isFirst);
        var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (isFirst)
            return new SingleInstance(mutex, showEvent);

        showEvent.Set();
        showEvent.Dispose();
        mutex.Dispose();
        return null;
    }

    /// <summary>Calls <paramref name="onShowRequested"/> on a thread-pool thread whenever another launch is attempted.</summary>
    public void ListenForShowRequests(Action onShowRequested)
    {
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _showEvent, (_, _) => onShowRequested(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _showEvent.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
