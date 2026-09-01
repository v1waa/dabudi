using System.Runtime.Versioning;

namespace Dabudi.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed class SingleInstance : IDisposable
{
    private readonly EventWaitHandle _activate = new(false, EventResetMode.AutoReset, @"Local\dabudi.Activate");
    private readonly Mutex _mutex;
    private RegisteredWaitHandle? _wait;
    public bool IsPrimary { get; }

    public SingleInstance()
    {
        _mutex = new Mutex(false, @"Local\dabudi.Application");
        try { IsPrimary = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsPrimary = true; }
        if (!IsPrimary) _activate.Set();
    }

    public void Listen(Action activate)
    {
        if (!IsPrimary) return;
        _wait = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => activate(), null,
            Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _wait?.Unregister(null);
        _activate.Dispose();
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
