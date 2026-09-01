using System.Collections.Concurrent;
using System.Text.Json;
using Dabudi.Core;
using Dabudi.Infrastructure;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Stopwatch pauses, resumes and resets", Sync(() =>
    {
        var clock = new ManualClock();
        var timer = new ElapsedTimer(clock);
        timer.Toggle(); clock.Advance(3); timer.Toggle();
        clock.Advance(50);
        Check(timer.State == StopwatchState.Paused && timer.Elapsed == TimeSpan.FromSeconds(3));
        timer.Toggle(); clock.Advance(2);
        Check(timer.Elapsed == TimeSpan.FromSeconds(5));
        timer.Reset(); clock.Advance(7);
        Check(timer.State == StopwatchState.Idle && timer.Elapsed == TimeSpan.Zero);
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
            EnduranceSeconds = -1, ClicksPerSecond = 500, ClickTarget = new((InputKind)999) };
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
        store.Save(new AppSettings { EnduranceSeconds = 0 });
        Check(store.Load().Settings.EnduranceSeconds == 0);
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
    ("Clicker Stop waits for in-flight input and blocks later emissions", async () =>
    {
        using var sender = new BlockingSender();
        using var clicker = new ClickerEngine(sender);
        clicker.Start(new(), 50);
        await sender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var stopping = Task.Run(clicker.Stop);
        await Task.Delay(40);
        Check(!stopping.IsCompleted);
        sender.Release.Set();
        await stopping.WaitAsync(TimeSpan.FromSeconds(3));
        var count = sender.Count;
        await Task.Delay(80);
        Check(!clicker.IsRunning && sender.Count == count);
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
    ("Input failure stops the clicker and reports the error", async () =>
    {
        using var clicker = new ClickerEngine(new FailingSender());
        var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        clicker.Failed += exception => failed.TrySetResult(exception);
        clicker.Start(new(), 50);
        var exception = await failed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(exception is IOException && !clicker.IsRunning);
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
    public int Count => Volatile.Read(ref _count);
    public void Send(InputTarget target) => Interlocked.Increment(ref _count);
}
sealed class BlockingSender : IInputSender, IDisposable
{
    private int _count;
    public int Count => Volatile.Read(ref _count);
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ManualResetEventSlim Release { get; } = new();
    public void Send(InputTarget target)
    {
        Interlocked.Increment(ref _count);
        Entered.TrySetResult();
        if (!Release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Blocking test sender was not released.");
    }
    public void Dispose() { Release.Set(); Release.Dispose(); }
}
sealed class FailingSender : IInputSender
{
    public void Send(InputTarget target) => throw new IOException("Simulated input failure.");
}
