namespace Dabudi.Core;

public interface IInputSender { void Send(InputTarget target); }

public sealed class ClickerEngine(IInputSender sender) : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _run;
    private bool _disposed;
    public bool IsRunning { get { lock (_gate) return _run != null; } }
    public event Action<Exception>? Failed;

    public void Start(InputTarget target, int clicksPerSecond)
    {
        if (!target.IsValid) throw new ArgumentException("Некорректная клавиша автокликера.", nameof(target));
        if (clicksPerSecond is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(clicksPerSecond));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            StopUnderLock();
            var run = new CancellationTokenSource();
            _run = run;
            _ = Task.Run(() => RunAsync(target, clicksPerSecond, run));
        }
    }

    public void Stop() { lock (_gate) StopUnderLock(); }

    private void StopUnderLock()
    {
        _run?.Cancel();
        _run = null;
    }

    private async Task RunAsync(InputTarget target, int cps, CancellationTokenSource run)
    {
        Exception? error = null;
        var reportError = false;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1d / cps));
            while (await timer.WaitForNextTickAsync(run.Token).ConfigureAwait(false))
            {
                // Stop and Send share a lock: no old worker can emit after Stop returns.
                lock (_gate)
                {
                    if (_run != run || run.IsCancellationRequested) return;
                    sender.Send(target);
                }
            }
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested) { }
        catch (Exception exception) { error = exception; }
        finally
        {
            lock (_gate)
            {
                if (_run == run)
                {
                    _run = null;
                    reportError = error != null;
                }
                run.Dispose();
            }
        }
        if (reportError && error != null) Failed?.Invoke(error);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            StopUnderLock();
        }
    }
}
