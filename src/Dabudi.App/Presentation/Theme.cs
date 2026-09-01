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
        resources["BackgroundBrush"] = Brush(settings.BackgroundColor);
        resources["PanelBrush"] = Brush(settings.PanelColor);
        resources["AccentBrush"] = Brush(settings.AccentColor);
        resources["TextBrush"] = Brush(settings.TextColor);
        var panel = (Color)ColorConverter.ConvertFromString(settings.PanelColor);
        var text = (Color)ColorConverter.ConvertFromString(settings.TextColor);
        var accent = (Color)ColorConverter.ConvertFromString(settings.AccentColor);
        resources["MutedBrush"] = new SolidColorBrush(Blend(panel, text, .68));
        resources["BorderBrush"] = new SolidColorBrush(Blend(panel, text, .18));
        resources["HoverBrush"] = new SolidColorBrush(Blend(panel, accent, .16));
        resources["InputBrush"] = new SolidColorBrush(Blend(panel, Colors.Black, .17));
        resources["AccentTextBrush"] = new SolidColorBrush(Luminance(accent) > .45 ? Colors.Black : Colors.White);
        resources[SystemColors.WindowBrushKey] = resources["PanelBrush"];
        resources[SystemColors.WindowTextBrushKey] = resources["TextBrush"];
        resources[SystemColors.HighlightBrushKey] = resources["HoverBrush"];
        resources[SystemColors.HighlightTextBrushKey] = resources["TextBrush"];
    }

    private static Color Blend(Color a, Color b, double ratio) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * ratio), (byte)(a.G + (b.G - a.G) * ratio), (byte)(a.B + (b.B - a.B) * ratio));
    private static double Luminance(Color c) => (.2126 * c.R + .7152 * c.G + .0722 * c.B) / 255;
}
