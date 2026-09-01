namespace Dabudi.Core;

public enum ClickerMode { Repeat, OnceAfterDelay }

// False means there was no eligible foreground window; a one-shot must not retry later.
public interface IInputSender { bool Send(InputTarget target); }
public sealed record ClickerCompletion(long RunId, bool InputSent, Exception? Error);

public sealed class ClickerEngine(IInputSender sender, TimeProvider? timeProvider = null) : IDisposable
{
    private readonly object _gate = new();
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private CancellationTokenSource? _run;
    private long _runId, _startedAt;
    private TimeSpan? _delay;
    private bool _disposed;
    public bool IsRunning { get { lock (_gate) return _run != null; } }
    public long RunId { get { lock (_gate) return _runId; } }
    public TimeSpan? RemainingDelay
    {
        get
        {
            lock (_gate)
                return _run != null && _delay is { } delay
                    ? TimeSpan.FromSeconds(Math.Max(0, (delay - _clock.GetElapsedTime(_startedAt)).TotalSeconds)) : null;
        }
    }
    public event Action<ClickerCompletion>? Finished;

    public void Start(InputTarget target, int clicksPerSecond, ClickerMode mode = ClickerMode.Repeat, double delaySeconds = 5)
    {
        if (!target.IsValid) throw new ArgumentException("Некорректная клавиша автокликера.", nameof(target));
        if (clicksPerSecond is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(clicksPerSecond));
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!double.IsFinite(delaySeconds) || delaySeconds is < 0.1 or > 86400)
            throw new ArgumentOutOfRangeException(nameof(delaySeconds));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            StopUnderLock();
            var run = new CancellationTokenSource();
            _run = run;
            _startedAt = _clock.GetTimestamp();
            _delay = mode == ClickerMode.OnceAfterDelay ? TimeSpan.FromSeconds(delaySeconds) : null;
            // Register the timer before returning so the delay starts at the user's command.
            _ = RunAsync(target, clicksPerSecond, _delay, run, _runId);
        }
    }

    public void Stop() { lock (_gate) StopUnderLock(); }

    private void StopUnderLock()
    {
        _runId++;
        var run = _run;
        _run = null;
        _delay = null;
        run?.Cancel();
    }

    private async Task RunAsync(InputTarget target, int cps, TimeSpan? delay, CancellationTokenSource run, long runId)
    {
        Exception? error = null;
        var inputSent = false;
        var notify = false;
        try
        {
            if (delay is { } wait)
            {
                await Task.Delay(wait, _clock, run.Token).ConfigureAwait(false);
                // Stop and Send share a lock: no cancelled/replaced worker can emit after Stop returns.
                lock (_gate)
                {
                    if (_run != run || run.IsCancellationRequested) return;
                    inputSent = sender.Send(target);
                }
            }
            else
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1d / cps), _clock);
                while (await timer.WaitForNextTickAsync(run.Token).ConfigureAwait(false))
                {
                    lock (_gate)
                    {
                        if (_run != run || run.IsCancellationRequested) return;
                        sender.Send(target);
                    }
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
                    _delay = null;
                    notify = true;
                }
                run.Dispose();
            }
        }
        if (notify) Finished?.Invoke(new(runId, inputSent, error));
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
