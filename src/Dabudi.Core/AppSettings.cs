namespace Dabudi.Core;

public sealed record AppSettings
{
    public const int CurrentSchema = 3;
    public int SchemaVersion { get; init; } = CurrentSchema;
    public int DecisiveStrikeSeconds { get; init; } = 60;
    public int EnduranceSeconds { get; init; } = 15;
    public int ClicksPerSecond { get; init; } = 10;
    public InputTarget ClickTarget { get; init; } = new();
    public bool RunAtStartup { get; init; }
    public string MonitorDevice { get; init; } = "";
    public bool AllowOverlayDragging { get; init; }
    public int CrosshairSize { get; init; } = 24;
    public string CrosshairColor { get; init; } = "#C2D8C4";
    public string BackgroundColor { get; init; } = "#202323";
    public string PanelColor { get; init; } = "#2B2F2F";
    public string AccentColor { get; init; } = "#C2D8C4";
    public string TextColor { get; init; } = "#E8F2E9";
    public Dictionary<AppAction, Shortcut> Shortcuts { get; init; } = Shortcut.Defaults();

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (SchemaVersion != CurrentSchema) errors.Add("Неподдерживаемая версия настроек.");
        if (DecisiveStrikeSeconds is < 0 or > 3600 || EnduranceSeconds is < 0 or > 3600)
            errors.Add("Длительность каждого таймера: от 0 до 3600 секунд.");
        if (ClicksPerSecond is < 1 or > 50) errors.Add("Частота автокликера: от 1 до 50 нажатий в секунду.");
        if (!ClickTarget.IsValid) errors.Add("Некорректная клавиша автокликера.");
        if (CrosshairSize is < 8 or > 80) errors.Add("Размер прицела: от 8 до 80.");
        if (!new[] { BackgroundColor, PanelColor, AccentColor, TextColor, CrosshairColor }.All(IsColor))
            errors.Add("Цвета указываются в формате #RRGGBB.");
        if (Shortcuts == null || Enum.GetValues<AppAction>().Any(action => !Shortcuts.ContainsKey(action)))
        {
            errors.Add("Список горячих клавиш неполон.");
            return errors;
        }
        var assigned = new Dictionary<Shortcut, AppAction>();
        foreach (var (action, shortcut) in Shortcuts)
        {
            if (!Enum.IsDefined(action) || !shortcut.IsValid)
                errors.Add($"Недопустимая горячая клавиша: {Shortcut.ActionName(action)}. F12 зарезервирована Windows.");
            if (!shortcut.IsEnabled) continue;
            if (assigned.TryGetValue(shortcut, out var other))
                errors.Add($"Одна комбинация назначена двум действиям: «{Shortcut.ActionName(other)}» и «{Shortcut.ActionName(action)}».");
            else assigned.Add(shortcut, action);
            // Modifiers held by the user must not turn synthetic input into an app command.
            if (ClickTarget.Kind == InputKind.Keyboard && ClickTarget.VirtualKey == shortcut.VirtualKey)
                errors.Add($"Клавиша автокликера используется в действии «{Shortcut.ActionName(action)}». Выберите другую.");
        }
        return errors;
    }

    public static bool IsColor(string? text) => text is { Length: 7 } && text[0] == '#'
        && text.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;

    public static AppSettings Normalize(AppSettings value)
    {
        var defaults = new AppSettings();
        var shortcuts = Shortcut.Defaults();
        if (value.Shortcuts != null)
            foreach (var (action, shortcut) in value.Shortcuts)
                if (Enum.IsDefined(action)) shortcuts[action] = shortcut.IsValid ? shortcut : new();
        var used = new HashSet<Shortcut>();
        foreach (var action in Enum.GetValues<AppAction>())
            if (shortcuts[action].IsEnabled && !used.Add(shortcuts[action])) shortcuts[action] = new();
        var target = value.ClickTarget.IsValid ? value.ClickTarget : new();
        if (target.Kind == InputKind.Keyboard && shortcuts.Values.Any(s => s.VirtualKey == target.VirtualKey))
            target = new();
        return value with
        {
            SchemaVersion = CurrentSchema,
            DecisiveStrikeSeconds = Math.Clamp(value.DecisiveStrikeSeconds, 0, 3600),
            EnduranceSeconds = Math.Clamp(value.EnduranceSeconds, 0, 3600),
            ClicksPerSecond = Math.Clamp(value.ClicksPerSecond, 1, 50),
            CrosshairSize = Math.Clamp(value.CrosshairSize, 8, 80),
            MonitorDevice = value.MonitorDevice ?? "",
            BackgroundColor = IsColor(value.BackgroundColor) ? value.BackgroundColor : defaults.BackgroundColor,
            PanelColor = IsColor(value.PanelColor) ? value.PanelColor : defaults.PanelColor,
            AccentColor = IsColor(value.AccentColor) ? value.AccentColor : defaults.AccentColor,
            TextColor = IsColor(value.TextColor) ? value.TextColor : defaults.TextColor,
            CrosshairColor = IsColor(value.CrosshairColor) ? value.CrosshairColor : defaults.CrosshairColor,
            Shortcuts = shortcuts,
            ClickTarget = target
        };
    }
}
