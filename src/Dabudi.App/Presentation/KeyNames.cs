namespace Dabudi.Presentation;

public static class KeyNames
{
    public static int ParseLegacy(string name) => Enum.TryParse<Key>(name, true, out var key) && key != Key.None
        ? KeyInterop.VirtualKeyFromKey(key) : 0;

    public static string Format(Shortcut shortcut)
    {
        if (!shortcut.IsEnabled) return "Не назначено";
        var parts = new List<string>();
        if (shortcut.Modifiers.HasFlag(ShortcutModifiers.Control)) parts.Add("Ctrl");
        if (shortcut.Modifiers.HasFlag(ShortcutModifiers.Alt)) parts.Add("Alt");
        if (shortcut.Modifiers.HasFlag(ShortcutModifiers.Shift)) parts.Add("Shift");
        if (shortcut.Modifiers.HasFlag(ShortcutModifiers.Windows)) parts.Add("Win");
        parts.Add(KeyLabel(shortcut.VirtualKey));
        return string.Join(" + ", parts);
    }

    public static string KeyLabel(int virtualKey) => KeyInterop.KeyFromVirtualKey(virtualKey) switch
    {
        Key.Escape => "Esc", Key.Space => "Пробел", Key.Return => "Enter",
        Key.Back => "Backspace", Key.Left => "Влево", Key.Right => "Вправо",
        Key.Up => "Вверх", Key.Down => "Вниз", Key.None => $"VK {virtualKey}",
        var key => key.ToString()
    };

    public static string Format(InputTarget target) => target.Kind == InputKind.Keyboard ? KeyLabel(target.VirtualKey)
        : target.MouseButton switch
        {
            Core.MouseButton.Left => "Левая кнопка мыши",
            Core.MouseButton.Right => "Правая кнопка мыши",
            Core.MouseButton.Middle => "Средняя кнопка мыши",
            Core.MouseButton.X1 => "Боковая кнопка 1",
            Core.MouseButton.X2 => "Боковая кнопка 2",
            _ => "Не назначено"
        };
}
