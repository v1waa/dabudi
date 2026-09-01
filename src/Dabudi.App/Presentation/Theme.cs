namespace Dabudi.Presentation;

public static class Theme
{
    public static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    public static void Apply(AppSettings settings)
    {
        var resources = Application.Current.Resources;
        resources["AppBackgroundBrush"] = Brush(settings.BackgroundColor);
        resources["PanelBackgroundBrush"] = Brush(settings.PanelColor);
        resources["AccentBrush"] = Brush(settings.AccentColor);
        resources["TextBrush"] = Brush(settings.TextColor);
        var accent = (Color)ColorConverter.ConvertFromString(settings.AccentColor);
        resources["AccentTextBrush"] = Brush(Luminance(accent) > .45 ? "#222222" : "#FFFFFF");
        resources[SystemColors.WindowBrushKey] = resources["ControlBackgroundBrush"];
        resources[SystemColors.WindowTextBrushKey] = resources["TextBrush"];
        resources[SystemColors.HighlightBrushKey] = resources["ControlHoverBrush"];
        resources[SystemColors.HighlightTextBrushKey] = resources["TextBrush"];
    }

    private static double Luminance(Color c) => (.2126 * c.R + .7152 * c.G + .0722 * c.B) / 255;
}
