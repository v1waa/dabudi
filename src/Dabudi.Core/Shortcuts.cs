using System.Text.Json.Serialization;

namespace Dabudi.Core;

public enum AppAction
{
    RestartEffects, CloseEffects, ToggleStopwatch, ResetStopwatch,
    ToggleCrosshair, TogglePerformance, ToggleClicker, StopAll, Exit
}

[Flags]
public enum ShortcutModifiers { None = 0, Alt = 1, Control = 2, Shift = 4, Windows = 8 }

public readonly record struct Shortcut(int VirtualKey = 0, ShortcutModifiers Modifiers = ShortcutModifiers.None)
{
    [JsonIgnore] public bool IsEnabled => VirtualKey != 0;
    [JsonIgnore] public bool IsValid => VirtualKey == 0 ? Modifiers == 0 :
        VirtualKey is >= 8 and <= 254 && !IsModifier(VirtualKey) &&
        VirtualKey != 0x7B && ((int)Modifiers & ~15) == 0;

    public static bool IsModifier(int key) => key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C
        or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    public static Dictionary<AppAction, Shortcut> Defaults() => new()
    {
        [AppAction.RestartEffects] = new(0x78),
        [AppAction.CloseEffects] = new(0x1B),
        [AppAction.ToggleStopwatch] = new(0x76),
        [AppAction.ResetStopwatch] = new(0x76, ShortcutModifiers.Control),
        [AppAction.ToggleCrosshair] = new(0x77),
        [AppAction.TogglePerformance] = new(0x79),
        [AppAction.ToggleClicker] = new(0x75),
        [AppAction.StopAll] = new(0x7A, ShortcutModifiers.Control | ShortcutModifiers.Shift),
        [AppAction.Exit] = new()
    };

    public static string ActionName(AppAction action) => action switch
    {
        AppAction.RestartEffects => "Запустить / перезапустить таймеры DBD",
        AppAction.CloseEffects => "Закрыть таймеры DBD",
        AppAction.ToggleStopwatch => "Секундомер: пуск / пауза / продолжить",
        AppAction.ResetStopwatch => "Сбросить и закрыть секундомер",
        AppAction.ToggleCrosshair => "Показать / скрыть прицел",
        AppAction.TogglePerformance => "Показать / скрыть мониторинг",
        AppAction.ToggleClicker => "Включить / выключить автокликер",
        AppAction.StopAll => "Остановить все инструменты",
        AppAction.Exit => "Выйти из dabudi",
        _ => action.ToString()
    };
}

public enum InputKind { Mouse, Keyboard }
public enum MouseButton { Left, Right, Middle, X1, X2 }

public readonly record struct InputTarget(InputKind Kind = InputKind.Mouse,
    MouseButton MouseButton = MouseButton.Left, int VirtualKey = 0)
{
    [JsonIgnore] public bool IsValid => Enum.IsDefined(Kind) && (Kind == InputKind.Mouse
        ? Enum.IsDefined(MouseButton)
        : VirtualKey is >= 8 and <= 254 && !Shortcut.IsModifier(VirtualKey));
}
