using System;
using System.IO;
using System.Text.Json;
using dbdOverlay.Models;
using dbdOverlay.Services;

namespace dbdOverlay;

public class SettingsModel
{
	private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true,
		WriteIndented = true
	};

	public int DsDuration { get; set; } = 60;

	public int EndDuration { get; set; } = 15;

	public uint Modifiers { get; set; }

	public string Key { get; set; } = "F9";

	public uint CloseModifiers { get; set; }

	public string CloseKey { get; set; } = "Escape";

	public uint ExitModifiers { get; set; }

	public string ExitKey { get; set; } = string.Empty;

	public uint CrosshairModifiers { get; set; }

	public string CrosshairKey { get; set; } = string.Empty;

	public string ThemeName { get; set; } = "SageGraphite";

	public string GridColor { get; set; } = "#222222";

	public string BorderColor { get; set; } = "#222222";

	public string AccentColor { get; set; } = "#C2D8C4";

	public string TextColor { get; set; } = "#E8F2E9";

	public uint TimerModifiers { get; set; }

	public string TimerKey { get; set; } = "F7";

	public uint PerformanceModifiers { get; set; }

	public string PerformanceKey { get; set; } = "F10";

	public int ClickerCps { get; set; } = 10;

	public MouseButtonKind ClickerButton { get; set; }

	public ClickerInputKind ClickerTargetKind { get; set; }

	public int ClickerVirtualKey { get; set; }

	public uint ClickerModifiers { get; set; }

	public string ClickerKey { get; set; } = "F6";

	public bool RunAtStartup { get; set; }

	public static string GetSettingsPath()
	{
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dbdOverlay");
		Directory.CreateDirectory(text);
		return Path.Combine(text, "settings.json");
	}

	public static SettingsModel Load()
	{
		try
		{
			string settingsPath = GetSettingsPath();
			if (!File.Exists(settingsPath))
			{
				return new SettingsModel();
			}
			SettingsModel? obj = JsonSerializer.Deserialize<SettingsModel>(File.ReadAllText(settingsPath), _jsonOptions) ?? new SettingsModel();
			obj.Normalize();
			return obj;
		}
		catch
		{
			return new SettingsModel();
		}
	}

	public void Save()
	{
		try
		{
			Normalize();
			string settingsPath = GetSettingsPath();
			string contents = JsonSerializer.Serialize(this, _jsonOptions);
			File.WriteAllText(settingsPath, contents);
		}
		catch
		{
		}
	}

	private void Normalize()
	{
		bool num = !string.Equals(ThemeName, "SageGraphite", StringComparison.OrdinalIgnoreCase);
		ThemeName = "SageGraphite";
		AppThemePalette theme = AppThemeService.GetTheme("SageGraphite");
		if (num)
		{
			GridColor = theme.AppBackground;
			BorderColor = theme.PanelBackground;
			AccentColor = theme.Accent;
			TextColor = theme.TextPrimary;
		}
		GridColor = (string.IsNullOrWhiteSpace(GridColor) ? theme.AppBackground : GridColor.Trim());
		BorderColor = (string.IsNullOrWhiteSpace(BorderColor) ? theme.PanelBackground : BorderColor.Trim());
		AccentColor = (string.IsNullOrWhiteSpace(AccentColor) ? theme.Accent : AccentColor.Trim());
		TextColor = (string.IsNullOrWhiteSpace(TextColor) ? theme.TextPrimary : TextColor.Trim());
		ClickerCps = Math.Clamp(ClickerCps, 1, 50);
		if (!Enum.IsDefined(ClickerTargetKind))
		{
			ClickerTargetKind = ClickerInputKind.MouseButton;
		}
		if (ClickerTargetKind == ClickerInputKind.KeyboardKey && (ClickerVirtualKey <= 0 || ClickerVirtualKey > 65535))
		{
			ClickerTargetKind = ClickerInputKind.MouseButton;
			ClickerVirtualKey = 0;
		}
	}
}
