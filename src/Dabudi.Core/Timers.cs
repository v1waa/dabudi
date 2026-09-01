namespace Dabudi.Core;

public enum StopwatchState { Idle, Running, Stopped }

public sealed class ElapsedTimer(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private long _started;
    private TimeSpan _accumulated;
    public StopwatchState State { get; private set; }
    public TimeSpan Elapsed => _accumulated + (State == StopwatchState.Running
        ? _time.GetElapsedTime(_started) : TimeSpan.Zero);

    public void Toggle()
    {
        if (State == StopwatchState.Running)
        {
            _accumulated = Elapsed;
            State = StopwatchState.Stopped;
        }
        else
        {
            _accumulated = TimeSpan.Zero;
            _started = _time.GetTimestamp();
            State = StopwatchState.Running;
        }
    }

    public void Reset()
    {
        State = StopwatchState.Idle;
        _accumulated = TimeSpan.Zero;
    }
}

public readonly record struct Countdown(double RemainingSeconds, double Fraction, bool Enabled);
public readonly record struct EffectsSnapshot(Countdown DecisiveStrike, Countdown Endurance)
{
    public bool IsComplete => DecisiveStrike.RemainingSeconds <= 0 && Endurance.RemainingSeconds <= 0;
}

public sealed class EffectTimers(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private long _started;
    private int _dsSeconds;
    private int _endSeconds;
    public bool IsActive { get; private set; }

    public void Start(int decisiveStrikeSeconds, int enduranceSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(decisiveStrikeSeconds);
        ArgumentOutOfRangeException.ThrowIfNegative(enduranceSeconds);
        _dsSeconds = decisiveStrikeSeconds;
        _endSeconds = enduranceSeconds;
        _started = _time.GetTimestamp();
        IsActive = decisiveStrikeSeconds > 0 || enduranceSeconds > 0;
    }

    public EffectsSnapshot Snapshot()
    {
        if (!IsActive) return default;
        var elapsed = _time.GetElapsedTime(_started).TotalSeconds;
        return new(Read(_dsSeconds, elapsed), Read(_endSeconds, elapsed));
    }

    public void Stop() => IsActive = false;

    private static Countdown Read(int seconds, double elapsed)
    {
        var remaining = Math.Max(0, seconds - elapsed);
        return new(remaining, seconds == 0 ? 0 : Math.Clamp(remaining / seconds, 0, 1), seconds > 0);
    }
}
