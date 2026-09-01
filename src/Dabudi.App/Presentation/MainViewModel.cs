using System.Collections.ObjectModel;

namespace Dabudi.Presentation;

public sealed record DisplayOption(string Device, string Label);
public sealed record ClickModeOption(ClickerMode Value, string Label);

public sealed class HotkeyRow(AppAction action, AppController controller) : ObservableObject
{
    public AppAction Action { get; } = action;
    public string Name => Shortcut.ActionName(Action);
    public string Binding => KeyNames.Format(controller.Settings.Shortcuts[Action]);
    public ICommand Clear { get; } = new RelayCommand(() => controller.ChangeShortcut(action, new()));
    public ICommand Reset { get; } = new RelayCommand(() => controller.ChangeShortcut(action, Shortcut.Defaults()[action]));
    public void Refresh() => Changed(nameof(Binding));
}

public sealed class MainViewModel : ObservableObject
{
    private readonly AppController _controller;
    private string _ds = "", _end = "", _cps = "", _delay = "", _size = "", _monitor = "";
    private ClickerMode _clickMode;
    private string _background = "", _panel = "", _accent = "", _text = "", _crosshair = "";
    private bool _startup, _drag;
    public string DsSeconds { get => _ds; set => Set(ref _ds, value); }
    public string EndSeconds { get => _end; set => Set(ref _end, value); }
    public string ClicksPerSecond { get => _cps; set => Set(ref _cps, value); }
    public string ClickDelaySeconds { get => _delay; set => Set(ref _delay, value); }
    public ClickerMode SelectedClickMode
    {
        get => _clickMode;
        set
        {
            Set(ref _clickMode, value);
            Changed(nameof(IsDelayedClick));
            Changed(nameof(ClickerTimingLabel));
            Changed(nameof(ClickerTimingHint));
            Changed(nameof(ClickerButton));
        }
    }
    public bool IsDelayedClick => SelectedClickMode == ClickerMode.OnceAfterDelay;
    public string ClickerTimingLabel => IsDelayedClick ? "Нажать через" : "Скорость";
    public string ClickerTimingHint => IsDelayedClick ? "Секунды (0,1–86 400)" : "Кликов в секунду (1–50)";
    public IReadOnlyList<ClickModeOption> ClickModes { get; } =
    [new(ClickerMode.Repeat, "Повторять нажатия"), new(ClickerMode.OnceAfterDelay, "Одно нажатие через…")];
    public string CrosshairSize { get => _size; set => Set(ref _size, value); }
    public string SelectedMonitor { get => _monitor; set => Set(ref _monitor, value); }
    public string BackgroundColor { get => _background; set => Set(ref _background, value); }
    public string PanelColor { get => _panel; set => Set(ref _panel, value); }
    public string AccentColor { get => _accent; set => Set(ref _accent, value); }
    public string TextColor { get => _text; set => Set(ref _text, value); }
    public string CrosshairColor { get => _crosshair; set => Set(ref _crosshair, value); }
    public bool RunAtStartup { get => _startup; set => Set(ref _startup, value); }
    public bool AllowDragging { get => _drag; set => Set(ref _drag, value); }
    public ObservableCollection<DisplayOption> Displays { get; } = new();
    public IReadOnlyList<HotkeyRow> Hotkeys { get; }
    public HotkeyRow CrosshairHotkey => KeyRow(AppAction.ToggleCrosshair);
    public HotkeyRow StopwatchHotkey => KeyRow(AppAction.ToggleStopwatch);
    public HotkeyRow ResetStopwatchHotkey => KeyRow(AppAction.ResetStopwatch);
    public HotkeyRow ClickerHotkey => KeyRow(AppAction.ToggleClicker);
    public HotkeyRow PerformanceHotkey => KeyRow(AppAction.TogglePerformance);
    public HotkeyRow StartEffectsHotkey => KeyRow(AppAction.RestartEffects);
    public HotkeyRow CloseEffectsHotkey => KeyRow(AppAction.CloseEffects);
    public HotkeyRow StopAllHotkey => KeyRow(AppAction.StopAll);
    public HotkeyRow ExitHotkey => KeyRow(AppAction.Exit);
    private HotkeyRow KeyRow(AppAction action) => Hotkeys.First(row => row.Action == action);
    public string Status => _controller.Status;
    public Brush StatusBrush => (Brush)Application.Current.Resources[_controller.StatusIsError ? "DangerBrush" : "MutedTextBrush"];
    public string ElapsedDisplay => OverlayWindow.FormatTime(_controller.Elapsed.Elapsed);
    public string StopwatchStatus => _controller.Elapsed.State switch
    {
        StopwatchState.Running => "Работает", StopwatchState.Paused => "Пауза", _ => "Остановлен"
    };
    public string StopwatchButton => _controller.Elapsed.State switch
    {
        StopwatchState.Running => "Пауза", StopwatchState.Paused => "Продолжить", _ => "Запустить"
    };
    public string ClickerStatus => _controller.ClickerRemainingDelay is { } remaining ? $"Через {remaining.TotalSeconds:0.0} с"
        : _controller.IsClickerRunning ? "Работает" : "Остановлен";
    public string ClickerButton => _controller.IsClickerRunning
        ? _controller.ClickerRemainingDelay.HasValue ? "Отменить" : "Остановить"
        : IsDelayedClick ? "Запланировать" : "Запустить";
    public string ClickTarget => KeyNames.Format(_controller.Settings.ClickTarget);
    public string CrosshairButton => _controller.Overlays.IsVisible(OverlayKind.Crosshair) ? "Скрыть" : "Показать";
    public bool IsCrosshairVisible => _controller.Overlays.IsVisible(OverlayKind.Crosshair);
    public string PerformanceButton => _controller.Overlays.IsVisible(OverlayKind.Performance) ? "Скрыть" : "Показать";
    public string EffectsStatus => _controller.Effects.IsActive ? "Таймеры работают" : "Таймеры остановлены";
    public string PerformanceStatus => _controller.Overlays.IsVisible(OverlayKind.Performance) ? "Включён" : "Выключен";
    public ICommand ToggleStopwatch { get; }
    public ICommand ResetStopwatch { get; }
    public ICommand ToggleClicker { get; }
    public ICommand ToggleCrosshair { get; }
    public ICommand TogglePerformance { get; }
    public ICommand StartEffects { get; }
    public ICommand CloseEffects { get; }
    public ICommand StopAll { get; }
    public ICommand Exit { get; }
    public ICommand Save { get; }
    public ICommand ResetTheme { get; }
    public ICommand OpenSettings { get; }
    public ICommand OpenLogs { get; }

    public MainViewModel(AppController controller)
    {
        _controller = controller;
        ICommand Command(AppAction action) => new RelayCommand(() => controller.Run(action));
        ToggleStopwatch = Command(AppAction.ToggleStopwatch);
        ResetStopwatch = Command(AppAction.ResetStopwatch);
        ToggleClicker = new RelayCommand(() =>
        {
            if (controller.IsClickerRunning || SaveSettings()) controller.Run(AppAction.ToggleClicker);
        });
        ToggleCrosshair = Command(AppAction.ToggleCrosshair);
        TogglePerformance = Command(AppAction.TogglePerformance);
        StartEffects = new RelayCommand(() => { if (SaveSettings()) controller.Run(AppAction.RestartEffects); });
        CloseEffects = Command(AppAction.CloseEffects);
        StopAll = Command(AppAction.StopAll);
        Exit = Command(AppAction.Exit);
        Save = new RelayCommand(() => SaveSettings());
        ResetTheme = new RelayCommand(() => { LoadColors(new AppSettings()); SaveSettings(); });
        OpenSettings = new RelayCommand(controller.OpenSettingsDirectory);
        OpenLogs = new RelayCommand(controller.OpenLogDirectory);
        Hotkeys = Enum.GetValues<AppAction>().Select(action => new HotkeyRow(action, controller)).ToArray();
        controller.StateChanged += RefreshState;
        controller.StatusChanged += () => { Changed(nameof(Status)); Changed(nameof(StatusBrush)); };
        controller.SettingsChanged += () =>
        {
            foreach (var row in Hotkeys) row.Refresh();
            Changed(nameof(ClickTarget));
            Changed(nameof(StatusBrush));
        };
        LoadFields();
        RefreshDisplays();
    }

    public void RefreshDisplays()
    {
        var selected = SelectedMonitor;
        Displays.Clear();
        Displays.Add(new("", "Экран активного окна"));
        var index = 0;
        foreach (var display in WindowsDesktop.Displays())
            Displays.Add(new(display.Device, $"Экран {++index} · {display.Bounds.Width} × {display.Bounds.Height}"
                + (display.IsPrimary ? " (основной)" : "")));
        if (!string.IsNullOrEmpty(selected) && !Displays.Any(d => d.Device == selected))
            Displays.Add(new(selected, "Сохранённый экран сейчас недоступен"));
        SelectedMonitor = selected;
    }

    private void LoadFields()
    {
        var settings = _controller.Settings;
        DsSeconds = settings.DecisiveStrikeSeconds.ToString(CultureInfo.InvariantCulture);
        EndSeconds = settings.EnduranceSeconds.ToString(CultureInfo.InvariantCulture);
        ClicksPerSecond = settings.ClicksPerSecond.ToString(CultureInfo.InvariantCulture);
        ClickDelaySeconds = settings.ClickDelaySeconds.ToString(CultureInfo.CurrentCulture);
        SelectedClickMode = settings.ClickMode;
        CrosshairSize = settings.CrosshairSize.ToString(CultureInfo.InvariantCulture);
        RunAtStartup = settings.RunAtStartup;
        AllowDragging = settings.AllowOverlayDragging;
        SelectedMonitor = settings.MonitorDevice;
        LoadColors(settings);
    }

    private void LoadColors(AppSettings settings)
    {
        BackgroundColor = settings.BackgroundColor;
        PanelColor = settings.PanelColor;
        AccentColor = settings.AccentColor;
        TextColor = settings.TextColor;
        CrosshairColor = settings.CrosshairColor;
    }

    public bool SaveSettings()
    {
        if (!int.TryParse(DsSeconds, out var ds) || !int.TryParse(EndSeconds, out var end)
            || !int.TryParse(ClicksPerSecond, out var cps) || !int.TryParse(CrosshairSize, out var size))
        {
            _controller.Report("Длительность, частота нажатий и размер прицела должны быть целыми числами.", true);
            return false;
        }
        if (!double.TryParse(ClickDelaySeconds.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var delay))
        {
            _controller.Report("Задержка нажатия задаётся в секундах, например 5 или 1,5.", true);
            return false;
        }
        var settings = _controller.Settings with
        {
            DecisiveStrikeSeconds = ds, EnduranceSeconds = end, ClicksPerSecond = cps, CrosshairSize = size,
            ClickMode = SelectedClickMode, ClickDelaySeconds = delay,
            RunAtStartup = RunAtStartup, AllowOverlayDragging = AllowDragging, MonitorDevice = SelectedMonitor ?? "",
            BackgroundColor = BackgroundColor.Trim(), PanelColor = PanelColor.Trim(), AccentColor = AccentColor.Trim(),
            TextColor = TextColor.Trim(), CrosshairColor = CrosshairColor.Trim()
        };
        if (!_controller.Save(settings)) return false;
        LoadFields();
        return true;
    }

    private void RefreshState()
    {
        foreach (var name in new[] { nameof(ElapsedDisplay), nameof(StopwatchStatus), nameof(StopwatchButton),
            nameof(ClickerStatus), nameof(ClickerButton), nameof(CrosshairButton), nameof(IsCrosshairVisible), nameof(PerformanceButton),
            nameof(EffectsStatus), nameof(PerformanceStatus) }) Changed(name);
    }
}
