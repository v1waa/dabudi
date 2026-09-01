using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Dabudi.Core;
using Dabudi.Infrastructure;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Stopwatch stops, retains the result and starts a fresh run at zero", Sync(() =>
    {
        var clock = new ManualClock();
        var timer = new ElapsedTimer(clock);
        timer.Toggle(); clock.Advance(3); timer.Toggle();
        clock.Advance(50);
        Check(timer.State == StopwatchState.Stopped && timer.Elapsed == TimeSpan.FromSeconds(3));
        timer.Toggle();
        Check(timer.State == StopwatchState.Running && timer.Elapsed == TimeSpan.Zero);
        clock.WallClock = DateTimeOffset.UtcNow.AddYears(-2);
        clock.Advance(2);
        Check(timer.Elapsed == TimeSpan.FromSeconds(2));
        timer.Reset(); clock.Advance(7);
        Check(timer.State == StopwatchState.Idle && timer.Elapsed == TimeSpan.Zero);
    })),
    ("CPU temperatures require valid readings and real driver access", Sync(() =>
    {
        Check(CpuTemperatureReading.FromSensor(65, true, true) == new CpuTemperatureReading(65, CpuTemperatureStatus.Ready));
        Check(CpuTemperatureReading.FromSensor(65, false, true) == new CpuTemperatureReading(null, CpuTemperatureStatus.DriverMissing));
        Check(CpuTemperatureReading.FromSensor(0, true, false) == new CpuTemperatureReading(null, CpuTemperatureStatus.AccessRequired));
        foreach (var value in new double?[] { null, double.NaN, double.PositiveInfinity, -21, 151 })
            Check(CpuTemperatureReading.FromSensor(value, true, true) == new CpuTemperatureReading(null, CpuTemperatureStatus.Unavailable));
    })),
    ("Overlay anchors use physical monitor origins and scaled margins", Sync(() =>
    {
        var bounds = new PixelRect(-2560, -300, 0, 1140);
        var work = new PixelRect(-2520, -270, 0, 1080);
        Check(OverlayPlacement.Calculate(bounds, work, 366, 189, 1.5, OverlayAnchor.TopRight, 18, 18) == (-393, -243));
        Check(OverlayPlacement.Calculate(bounds, work, 600, 100, 2, OverlayAnchor.TopLeft, 16, 16) == (-2488, -238));
        Check(OverlayPlacement.Calculate(bounds, work, 30, 30, 2, OverlayAnchor.Center, 0, 0) == (-1295, 405));
    })),
    ("Zero durations disable effects", Sync(() =>
    {
        var clock = new ManualClock();
        var timer = new EffectTimers(clock);
        timer.Start(60, 0);
        Check(timer.IsActive && !timer.Snapshot().Endurance.Enabled);
        Check(timer.Snapshot().Endurance.RemainingSeconds == 0 && timer.Snapshot().Endurance.Fraction == 0);
        timer.Start(0, 0);
        Check(!timer.IsActive && timer.Snapshot().IsComplete);
    })),
    ("Countdown uses monotonic time, expires and restarts", Sync(() =>
    {
        var clock = new ManualClock();
        var timer = new EffectTimers(clock);
        timer.Start(5, 2); clock.Advance(3);
        clock.WallClock = DateTimeOffset.UtcNow.AddYears(-2);
        Check(timer.Snapshot().DecisiveStrike.RemainingSeconds == 2 && timer.Snapshot().Endurance.RemainingSeconds == 0);
        clock.Advance(3);
        Check(timer.Snapshot().IsComplete);
        timer.Start(5, 2);
        Check(timer.Snapshot().DecisiveStrike.RemainingSeconds == 5);
    })),
    ("Duplicate hotkeys are rejected", Sync(() =>
    {
        var settings = new AppSettings();
        settings.Shortcuts[AppAction.ToggleClicker] = settings.Shortcuts[AppAction.ToggleStopwatch];
        Check(settings.Validate().Any(e => e.Contains("двум действиям")));
    })),
    ("Synthetic key cannot trigger any app shortcut", Sync(() =>
    {
        var settings = new AppSettings { ClickTarget = new(InputKind.Keyboard, MouseButton.Left, 0x41) };
        settings.Shortcuts[AppAction.StopAll] = new(0x41, ShortcutModifiers.Control);
        Check(settings.Validate().Any(e => e.Contains("Клавиша автокликера")));
    })),
    ("Malformed settings normalize to valid values", Sync(() =>
    {
        var settings = new AppSettings { Shortcuts = null!, AccentColor = null!, MonitorDevice = null!,
            EnduranceSeconds = -1, ClicksPerSecond = 500, ClickTarget = new((InputKind)999),
            ClickMode = (ClickerMode)999, ClickDelaySeconds = double.NaN };
        Check(AppSettings.Normalize(settings).Validate().Count == 0);
    })),
    ("Legacy settings preserve disabled effects and key choices", Sync(() =>
    {
        using var json = JsonDocument.Parse("""
            {"DsDuration":75,"EndDuration":0,"Key":"F4","Modifiers":2,"TimerKey":"F7",
             "CrosshairKey":"","ClickerTargetKind":1,"ClickerVirtualKey":65,"ClickerCps":12,
             "GridColor":"#121212","AccentColor":"#AABBCC","RunAtStartup":true}
            """);
        var migrated = SettingsStore.MigrateLegacy(json.RootElement, key => key switch { "F4" => 0x73, "F7" => 0x76, _ => 0 });
        Check(migrated.DecisiveStrikeSeconds == 75 && migrated.EnduranceSeconds == 0);
        Check(!migrated.Shortcuts[AppAction.ToggleCrosshair].IsEnabled && migrated.RunAtStartup);
        Check(migrated.Shortcuts[AppAction.RestartEffects] == new Shortcut(0x73, ShortcutModifiers.Control));
        Check(migrated.ClickTarget.VirtualKey == 65 && migrated.ClicksPerSecond == 12);
        Check(migrated.BackgroundColor == "#121212" && migrated.AccentColor == "#AABBCC" && migrated.Validate().Count == 0);
    })),
    ("Settings save atomically and keep previous version", Sync(() => WithStore((store, _) =>
    {
        store.Save(new AppSettings());
        store.Save(new AppSettings { EnduranceSeconds = 0, ClickMode = ClickerMode.OnceAfterDelay, ClickDelaySeconds = 1.25 });
        var loaded = store.Load().Settings;
        Check(loaded.EnduranceSeconds == 0 && loaded.ClickMode == ClickerMode.OnceAfterDelay && loaded.ClickDelaySeconds == 1.25);
        using var backup = JsonDocument.Parse(File.ReadAllText(store.FilePath + ".bak"));
        Check(backup.RootElement.GetProperty("EnduranceSeconds").GetInt32() == 15);
        Check(!Directory.EnumerateFiles(store.DirectoryPath, "*.tmp").Any());
    }))),
    ("Corrupt settings are preserved before defaults load", Sync(() => WithStore((store, _) =>
    {
        Directory.CreateDirectory(store.DirectoryPath);
        File.WriteAllText(store.FilePath, "{broken");
        var loaded = store.Load();
        Check(loaded.CanSave && loaded.Notice != null && loaded.Settings.Validate().Count == 0);
        Check(File.ReadAllText(store.FilePath) == "{broken");
        Check(Directory.EnumerateFiles(store.DirectoryPath, "*.invalid-*").Count() == 1);
    }))),
    ("Future settings schema is not overwritten", Sync(() => WithStore((store, _) =>
    {
        Directory.CreateDirectory(store.DirectoryPath);
        File.WriteAllText(store.FilePath, "{\"SchemaVersion\":999}");
        Check(!store.Load().CanSave);
        Check(File.ReadAllText(store.FilePath) == "{\"SchemaVersion\":999}");
    }))),
    ("Upgrade restores the classic defaults and preserves custom appearance", Sync(() => WithStore((store, _) =>
    {
        Directory.CreateDirectory(store.DirectoryPath);
        File.WriteAllText(store.FilePath, """
            {"SchemaVersion":3,"BackgroundColor":"#202323","PanelColor":"#2B2F2F",
             "AccentColor":"#C2D8C4","TextColor":"#E8F2E9","CrosshairSize":24,"CrosshairColor":"#C2D8C4"}
            """);
        var loaded = store.Load();
        Check(loaded.Notice == null && loaded.CanSave);
        Check(loaded.Settings.BackgroundColor == "#222222" && loaded.Settings.PanelColor == "#222222");
        Check(loaded.Settings.CrosshairSize == 15 && loaded.Settings.CrosshairColor == "#FFFFFF");
        Check(loaded.Settings.ClickMode == ClickerMode.Repeat && loaded.Settings.ClickDelaySeconds == 5);
        store.Save(loaded.Settings);
        Check(store.Load().Settings.SchemaVersion == AppSettings.CurrentSchema);
        var custom = AppSettings.Normalize(new AppSettings { SchemaVersion = 3, BackgroundColor = "#121212",
            PanelColor = "#343434", CrosshairSize = 32, CrosshairColor = "#FF8800" });
        Check(custom.BackgroundColor == "#121212" && custom.PanelColor == "#343434"
            && custom.CrosshairSize == 32 && custom.CrosshairColor == "#FF8800");
    }))),
    ("Delay settings reject non-finite and out-of-range values", Sync(() =>
    {
        foreach (var delay in new[] { double.NaN, double.PositiveInfinity, -1, 0, .01, 86401 })
            Check((new AppSettings { ClickDelaySeconds = delay }).Validate().Count > 0);
        foreach (var delay in new[] { .1, 1.5, 86400 })
            Check((new AppSettings { ClickDelaySeconds = delay }).Validate().Count == 0);
    })),
    ("Clicker Stop waits for in-flight input and blocks later emissions", async () =>
    {
        foreach (var mode in Enum.GetValues<ClickerMode>())
        {
            using var sender = new BlockingSender();
            using var clicker = new ClickerEngine(sender);
            clicker.Start(new(), 50, mode, .1);
            await sender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var stopping = Task.Run(clicker.Stop);
            await Task.Delay(40);
            Check(!stopping.IsCompleted);
            sender.Release.Set();
            await stopping.WaitAsync(TimeSpan.FromSeconds(3));
            var count = sender.Count;
            await Task.Delay(80);
            Check(!clicker.IsRunning && sender.Count == count);
        }
    }),
    ("Rapid stop/restart never leaves a clicker running", async () =>
    {
        var sender = new CountingSender();
        using var clicker = new ClickerEngine(sender);
        for (var i = 0; i < 100; i++) { clicker.Start(new(), 50); clicker.Stop(); }
        clicker.Start(new(), 50);
        await Task.Delay(150);
        clicker.Stop();
        var count = sender.Count;
        Check(count > 0);
        await Task.Delay(80);
        Check(sender.Count == count && !clicker.IsRunning);
    }),
    ("Delayed input waits, emits exactly once and returns to idle", async () =>
    {
        var sender = new CountingSender();
        using var clicker = new ClickerEngine(sender);
        var finished = CompletionSource(clicker);
        var target = new InputTarget(InputKind.Keyboard, MouseButton.Left, 65);
        var start = Stopwatch.GetTimestamp();
        clicker.Start(target, 10, ClickerMode.OnceAfterDelay, .15);
        Check(clicker.IsRunning && clicker.RemainingDelay > TimeSpan.Zero);
        var result = await finished.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(Stopwatch.GetElapsedTime(start, sender.LastTimestamp) >= TimeSpan.FromMilliseconds(140));
        Check(result.InputSent && result.Error == null && !clicker.IsRunning && clicker.RemainingDelay == null);
        Check(sender.Targets.Single() == target);
        await Task.Delay(200);
        Check(sender.Count == 1);
    }),
    ("Replacing or cancelling a pending click cannot leave a later input", async () =>
    {
        var sender = new CountingSender();
        using var clicker = new ClickerEngine(sender);
        var finished = CompletionSource(clicker);
        var notifications = 0;
        clicker.Finished += _ => Interlocked.Increment(ref notifications);
        clicker.Start(new(InputKind.Mouse, MouseButton.Right), 10, ClickerMode.OnceAfterDelay, .2);
        clicker.Start(new(InputKind.Keyboard, MouseButton.Left, 66), 10, ClickerMode.OnceAfterDelay, .1);
        var result = await finished.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(result.RunId == clicker.RunId && result.InputSent);
        await Task.Delay(150);
        Check(sender.Count == 1 && sender.Targets.Single().VirtualKey == 66);
        clicker.Start(new(), 10, ClickerMode.OnceAfterDelay, .1);
        clicker.Stop();
        await Task.Delay(180);
        Check(sender.Count == 1 && notifications == 1 && !clicker.IsRunning && clicker.RemainingDelay == null);
    }),
    ("Disposing a waiting clicker cancels its input", async () =>
    {
        var sender = new CountingSender();
        var clicker = new ClickerEngine(sender);
        clicker.Start(new(), 10, ClickerMode.OnceAfterDelay, .1);
        clicker.Dispose();
        await Task.Delay(180);
        Check(sender.Count == 0 && !clicker.IsRunning);
    }),
    ("Skipped delayed input is reported and never retried", async () =>
    {
        var sender = new SkippingSender();
        using var clicker = new ClickerEngine(sender);
        var finished = CompletionSource(clicker);
        clicker.Start(new(), 10, ClickerMode.OnceAfterDelay, .1);
        var result = await finished.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(!result.InputSent && result.Error == null && !clicker.IsRunning);
        await Task.Delay(180);
        Check(sender.Count == 1);
    }),
    ("Input failure stops both modes and reports the error", async () =>
    {
        foreach (var mode in Enum.GetValues<ClickerMode>())
        {
            using var clicker = new ClickerEngine(new FailingSender());
            var finished = CompletionSource(clicker);
            clicker.Start(new(), 50, mode, .1);
            var result = await finished.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Check(result.Error is IOException && !clicker.IsRunning);
        }
    })
};

var failures = 0;
foreach (var (name, run) in tests)
{
    try { await run(); Console.WriteLine("PASS " + name); }
    catch (Exception exception) { failures++; Console.Error.WriteLine("FAIL " + name + "\n" + exception); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} regression checks passed.");
return failures == 0 ? 0 : 1;

static Func<Task> Sync(Action action) => () => { action(); return Task.CompletedTask; };
static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Assertion failed."); }
static TaskCompletionSource<ClickerCompletion> CompletionSource(ClickerEngine clicker)
{
    var source = new TaskCompletionSource<ClickerCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
    clicker.Finished += result => source.TrySetResult(result);
    return source;
}
static void WithStore(Action<SettingsStore, string> test)
{
    var root = Path.Combine(Path.GetTempPath(), "dabudi-test-" + Guid.NewGuid().ToString("N"));
    try { test(new SettingsStore(Path.Combine(root, "config"), new AppLog(Path.Combine(root, "logs"))), root); }
    finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}

sealed class ManualClock : TimeProvider
{
    private long _timestamp;
    public DateTimeOffset WallClock { get; set; } = DateTimeOffset.UtcNow;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _timestamp;
    public override DateTimeOffset GetUtcNow() => WallClock;
    public void Advance(int seconds) => _timestamp += seconds * TimeSpan.TicksPerSecond;
}
sealed class CountingSender : IInputSender
{
    private int _count;
    private long _lastTimestamp;
    public int Count => Volatile.Read(ref _count);
    public long LastTimestamp => Volatile.Read(ref _lastTimestamp);
    public ConcurrentQueue<InputTarget> Targets { get; } = new();
    public bool Send(InputTarget target)
    {
        Interlocked.Exchange(ref _lastTimestamp, Stopwatch.GetTimestamp());
        Targets.Enqueue(target);
        Interlocked.Increment(ref _count);
        return true;
    }
}
sealed class BlockingSender : IInputSender, IDisposable
{
    private int _count;
    public int Count => Volatile.Read(ref _count);
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ManualResetEventSlim Release { get; } = new();
    public bool Send(InputTarget target)
    {
        Interlocked.Increment(ref _count);
        Entered.TrySetResult();
        if (!Release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Blocking test sender was not released.");
        return true;
    }
    public void Dispose() { Release.Set(); Release.Dispose(); }
}
sealed class FailingSender : IInputSender
{
    public bool Send(InputTarget target) => throw new IOException("Simulated input failure.");
}
sealed class SkippingSender : IInputSender
{
    public int Count { get; private set; }
    public bool Send(InputTarget target) { Count++; return false; }
}
