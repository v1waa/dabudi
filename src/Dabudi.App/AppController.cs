using Dabudi.Presentation;

namespace Dabudi;

public sealed class AppController : IDisposable
{
    private readonly SettingsStore _store;
    private readonly AppLog _log;
    private readonly bool _canSave;
    private readonly bool _smokeTest;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _ticker = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly ClickerEngine _clicker = new(new WindowsInputSender());
    private readonly HardwareMonitor _hardware;
    private HotkeyRegistry? _hotkeys;
    private long _suppressUntil;
    private bool _disposed;

    public AppSettings Settings { get; private set; }
    public OverlayManager Overlays { get; } = new();
    public ElapsedTimer Elapsed { get; } = new();
    public EffectTimers Effects { get; } = new();
    public PerformanceSnapshot LatestPerformance { get; private set; }
    public bool IsClickerRunning => _clicker.IsRunning;
    public bool IsExiting { get; private set; }
    public string Status { get; private set; }
    public bool StatusIsError { get; private set; }
    public event Action? StateChanged;
    public event Action? SettingsChanged;
    public event Action? StatusChanged;
    public event Action? ExitRequested;

    public AppController(SettingsStore store, SettingsLoadResult loaded, AppLog log, bool smokeTest)
    {
        _store = store;
        _log = log;
        _canSave = loaded.CanSave;
        _smokeTest = smokeTest;
        Settings = loaded.Settings;
        Status = loaded.Notice ?? "Готово";
        StatusIsError = loaded.Notice != null;
        _hardware = new(log);
        _hardware.Updated += OnPerformanceUpdated;
        _clicker.Failed += OnClickerFailed;
        _ticker.Tick += (_, _) => Tick();
        Theme.Apply(Settings);
    }

    public void Attach(nint handle)
    {
        if (_smokeTest) return;
        _hotkeys = new(handle);
        ReportHotkeyErrors(_hotkeys.Initialize(Settings.Shortcuts));
        if (Settings.RunAtStartup && _canSave)
        {
            try { StartupRegistration.SetEnabled(true); }
            catch (Exception exception) { Fail("Не удалось обновить автозапуск", exception); }
        }
    }

    public void OnHotkey(nint id, nint detail)
    {
        if (System.Diagnostics.Stopwatch.GetTimestamp() < _suppressUntil) return;
        if (_hotkeys?.Resolve(id, detail) is { } action) Run(action);
    }

    public void SuspendHotkeys()
    {
        _clicker.Stop();
        _hotkeys?.Suspend();
        NotifyState();
    }

    public void ResumeHotkeys()
    {
        _suppressUntil = System.Diagnostics.Stopwatch.GetTimestamp() + System.Diagnostics.Stopwatch.Frequency / 3;
        if (_hotkeys != null) ReportHotkeyErrors(_hotkeys.Resume());
    }

    public void Run(AppAction action)
    {
        if (_disposed || IsExiting) return;
        try
        {
            switch (action)
            {
                case AppAction.RestartEffects:
                    Effects.Start(Settings.DecisiveStrikeSeconds, Settings.EnduranceSeconds);
                    if (!Effects.IsActive)
                    {
                        CloseEffects();
                        Report("Оба таймера отключены. Укажите длительность больше нуля.");
                        break;
                    }
                    Overlays.Show(OverlayKind.Effects, Settings).Render(Effects.Snapshot());
                    Report("Таймеры DBD запущены");
                    if (_hotkeys != null) ReportHotkeyErrors(_hotkeys.SetEffectsActive(true));
                    break;
                case AppAction.CloseEffects:
                    CloseEffects();
                    Report("Таймеры DBD закрыты");
                    break;
                case AppAction.ToggleStopwatch:
                    Elapsed.Toggle();
                    Overlays.Show(OverlayKind.Stopwatch, Settings).Render(Elapsed);
                    Report(Elapsed.State == StopwatchState.Paused ? "Секундомер на паузе" : "Секундомер работает");
                    break;
                case AppAction.ResetStopwatch:
                    Elapsed.Reset();
                    Overlays.Close(OverlayKind.Stopwatch);
                    Report("Секундомер сброшен");
                    break;
                case AppAction.ToggleCrosshair:
                    if (Overlays.IsVisible(OverlayKind.Crosshair)) Overlays.Close(OverlayKind.Crosshair);
                    else Overlays.Show(OverlayKind.Crosshair, Settings);
                    Report(Overlays.IsVisible(OverlayKind.Crosshair) ? "Прицел показан" : "Прицел скрыт");
                    break;
                case AppAction.TogglePerformance:
                    var visible = !Overlays.IsVisible(OverlayKind.Performance);
                    if (visible) Overlays.Show(OverlayKind.Performance, Settings).Render(LatestPerformance);
                    else Overlays.Close(OverlayKind.Performance);
                    if (!_smokeTest) _hardware.SetEnabled(visible);
                    Report(visible ? "Мониторинг включён" : "Мониторинг выключен");
                    break;
                case AppAction.ToggleClicker:
                    if (_clicker.IsRunning) _clicker.Stop();
                    else
                    {
                        var errors = Settings.Validate();
                        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
                        _clicker.Start(Settings.ClickTarget, Settings.ClicksPerSecond);
                    }
                    Report(_clicker.IsRunning ? "Автокликер включён. Переключитесь в нужное окно." : "Автокликер выключен");
                    break;
                case AppAction.StopAll:
                    StopAll();
                    Report("Все инструменты остановлены");
                    break;
                case AppAction.Exit:
                    IsExiting = true;
                    StopAll();
                    ExitRequested?.Invoke();
                    break;
            }
            NotifyState();
        }
        catch (Exception exception) { Fail("Не удалось выполнить действие", exception); }
    }

    private void CloseEffects()
    {
        Effects.Stop();
        Overlays.Close(OverlayKind.Effects);
        if (_hotkeys != null) ReportHotkeyErrors(_hotkeys.SetEffectsActive(false));
    }

    public void StopAll()
    {
        _clicker.Stop();
        _hardware.SetEnabled(false);
        Effects.Stop();
        Elapsed.Reset();
        Overlays.CloseAll();
        if (_hotkeys != null) ReportHotkeyErrors(_hotkeys.SetEffectsActive(false));
        NotifyState();
    }

    private void Tick()
    {
        if (Effects.IsActive)
        {
            var effects = Effects.Snapshot();
            if (effects.IsComplete)
            {
                CloseEffects();
                Report("Таймеры DBD завершены");
            }
            else Overlays.Get(OverlayKind.Effects)?.Render(effects);
        }
        Overlays.Get(OverlayKind.Stopwatch)?.Render(Elapsed);
        NotifyState();
    }

    private void NotifyState()
    {
        _ticker.IsEnabled = Effects.IsActive || Elapsed.State == StopwatchState.Running;
        StateChanged?.Invoke();
    }

    private void OnPerformanceUpdated(PerformanceSnapshot snapshot)
    {
        if (_dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(new Action(() =>
        {
            if (_disposed || !Overlays.IsVisible(OverlayKind.Performance)) return;
            LatestPerformance = snapshot;
            Overlays.Get(OverlayKind.Performance)?.Render(snapshot);
            StateChanged?.Invoke();
        }));
    }

    private void OnClickerFailed(Exception exception)
    {
        if (_dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(new Action(() =>
        {
            if (_disposed) return;
            Fail("Автокликер остановлен", exception);
            NotifyState();
        }));
    }

    public bool ChangeShortcut(AppAction action, Shortcut shortcut)
    {
        var shortcuts = new Dictionary<AppAction, Shortcut>(Settings.Shortcuts) { [action] = shortcut };
        return Save(Settings with { Shortcuts = shortcuts });
    }
    public bool ChangeClickTarget(InputTarget target) => Save(Settings with { ClickTarget = target });

    public bool Save(AppSettings settings)
    {
        if (!_canSave) { Report("Запись настроек отключена: исходный файл недоступен или создан новой версией dabudi.", true); return false; }
        var errors = settings.Validate();
        if (errors.Count > 0) { Report(string.Join(" ", errors), true); return false; }
        var shortcutsChanged = settings.Shortcuts.Count != Settings.Shortcuts.Count
            || settings.Shortcuts.Any(pair => !Settings.Shortcuts.TryGetValue(pair.Key, out var previous) || previous != pair.Value);
        if (shortcutsChanged && _hotkeys != null && !_hotkeys.TryApply(settings.Shortcuts, out var hotkeyError))
        { Report(hotkeyError ?? "Не удалось назначить горячие клавиши.", true); return false; }
        var startupChanged = settings.RunAtStartup != Settings.RunAtStartup;
        try
        {
            if (startupChanged && !_smokeTest) StartupRegistration.SetEnabled(settings.RunAtStartup);
            _store.Save(settings);
        }
        catch (Exception exception)
        {
            if (shortcutsChanged) _hotkeys?.TryApply(Settings.Shortcuts, out _);
            if (startupChanged && !_smokeTest)
            {
                try { StartupRegistration.SetEnabled(Settings.RunAtStartup); }
                catch (Exception rollbackError) { _log.Write("Could not restore startup setting", rollbackError); }
            }
            Fail("Не удалось сохранить настройки", exception);
            return false;
        }
        if (settings.ClickTarget != Settings.ClickTarget || settings.ClicksPerSecond != Settings.ClicksPerSecond)
            _clicker.Stop();
        Settings = settings;
        Theme.Apply(Settings);
        Overlays.Configure(Settings);
        SettingsChanged?.Invoke();
        NotifyState();
        Report("Настройки сохранены");
        return true;
    }

    public void RefreshDisplays() => Overlays.Configure(Settings);
    public void Report(string message, bool isError = false)
    {
        Status = message;
        StatusIsError = isError;
        if (isError) _log.Write(message);
        StatusChanged?.Invoke();
    }

    public void OpenSettingsDirectory() => OpenDirectory(_store.DirectoryPath);
    public void OpenLogDirectory() => OpenDirectory(_log.DirectoryPath);
    private void OpenDirectory(string path)
    {
        try { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception exception) { Fail("Не удалось открыть папку", exception); }
    }

    private void ReportHotkeyErrors(IReadOnlyList<string> errors)
    {
        if (errors.Count > 0) Report(string.Join(" ", errors), true);
    }
    private void Fail(string message, Exception exception)
    {
        _log.Write(message, exception);
        Report(message + ": " + exception.Message, true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        IsExiting = true;
        StopAll();
        _disposed = true;
        _ticker.Stop();
        _clicker.Failed -= OnClickerFailed;
        _hardware.Updated -= OnPerformanceUpdated;
        _clicker.Dispose();
        _hardware.Dispose();
        _hotkeys?.Dispose();
    }
}
