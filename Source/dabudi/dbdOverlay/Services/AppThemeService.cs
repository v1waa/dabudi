using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace dbdOverlay.Services;

public static class AppThemeService
{
	public const string DefaultThemeId = "SageGraphite";

	private static readonly AppThemePalette[] _themes = new AppThemePalette[1]
	{
		new AppThemePalette("SageGraphite", "Шалфейный графит", "#222222", "#222222", "#272927", "#2B2E2B", "#191B19", "#303530", "#3B433C", "#E8F2E9", "#91A493", "#C2D8C4", "#D5E7D7", "#A7C0AA", "#222222", "#56635A", "#3B443D", "#292B29", "#343934", "#C2D8C4", "#CAD6B7", "#D2B7BE", "#382B2E", "#7A000000")
	};

	public static IReadOnlyList<AppThemePalette> Themes => _themes;

	public static AppThemePalette GetTheme(string? id)
	{
		return _themes.FirstOrDefault((AppThemePalette theme) => string.Equals(theme.Id, id, StringComparison.OrdinalIgnoreCase)) ?? _themes[0];
	}

	public static void Apply(string? id)
	{
		Application current = Application.Current;
		if (current != null)
		{
			AppThemePalette theme = GetTheme(id);
			ResourceDictionary resources = current.Resources;
			SetBrush(resources, "AppBackgroundBrush", theme.AppBackground);
			SetBrush(resources, "PanelBackgroundBrush", theme.PanelBackground);
			SetBrush(resources, "SurfaceBrush", theme.Surface);
			SetBrush(resources, "SurfaceAltBrush", theme.SurfaceAlt);
			SetBrush(resources, "ControlBackgroundBrush", theme.ControlBackground);
			SetBrush(resources, "ControlHoverBrush", theme.ControlHover);
			SetBrush(resources, "ControlPressedBrush", theme.ControlPressed);
			SetBrush(resources, "TextBrush", theme.TextPrimary);
			SetBrush(resources, "MutedTextBrush", theme.TextSecondary);
			SetBrush(resources, "AccentBrush", theme.Accent);
			SetBrush(resources, "AccentHoverBrush", theme.AccentHover);
			SetBrush(resources, "AccentPressedBrush", theme.AccentPressed);
			SetBrush(resources, "AccentTextBrush", theme.AccentText);
			SetBrush(resources, "BorderBrush", theme.Border);
			SetBrush(resources, "DividerBrush", theme.Divider);
			SetBrush(resources, "TabInactiveBrush", theme.TabInactive);
			SetBrush(resources, "TabHoverBrush", theme.TabHover);
			SetBrush(resources, "SuccessBrush", theme.Success);
			SetBrush(resources, "WarningBrush", theme.Warning);
			SetBrush(resources, "DangerBrush", theme.Danger);
			SetBrush(resources, "DangerSurfaceBrush", theme.DangerSurface);
			SetBrush(resources, "ShadowBrush", theme.Shadow);
		}
	}

	private static void SetBrush(ResourceDictionary resources, string key, string colorValue)
	{
		SolidColorBrush solidColorBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorValue));
		if (solidColorBrush.CanFreeze)
		{
			solidColorBrush.Freeze();
		}
		resources[key] = solidColorBrush;
	}
}
