namespace LedMatrix.App.Services;

/// <summary>
/// Ensures one running copy. A second launch signals the first one to show its window, then exits.
/// The mutex name is also used by the installer (AppMutex) to ask users to close the app first.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    public const string MutexName = "DeoLedMatrixManager.SingleInstance";
    private const string ShowEventName = "DeoLedMatrixManager.Show";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly bool _owned;
    private RegisteredWaitHandle? _registration;

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out _owned);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
    }

    public bool IsFirstInstance => _owned;

    /// <summary>Signals the already-running instance to bring its window up.</summary>
    public void SignalFirstInstance() => _showEvent.Set();

    /// <summary>Calls <paramref name="onShowRequested"/> (on a thread-pool thread) whenever another launch asks us to show.</summary>
    public void ListenForShowRequests(Action onShowRequested) =>
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _showEvent, (_, _) => onShowRequested(), null, Timeout.Infinite, executeOnlyOnce: false);

    public void Dispose()
    {
        _registration?.Unregister(null);
        _showEvent.Dispose();
        if (_owned)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
