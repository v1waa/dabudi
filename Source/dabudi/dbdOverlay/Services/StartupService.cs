using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace dbdOverlay.Services;

internal static class StartupService
{
	private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	private const string ValueName = "dabudi";

	public static bool SetEnabled(bool enabled)
	{
		try
		{
			using RegistryKey? registryKey = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
			if (registryKey == null)
			{
				return false;
			}
			if (!enabled)
			{
				registryKey.DeleteValue("dabudi", throwOnMissingValue: false);
				return true;
			}
			string? text = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
			if (string.IsNullOrWhiteSpace(text))
			{
				return false;
			}
			registryKey.SetValue("dabudi", "\"" + text + "\"", RegistryValueKind.String);
			return true;
		}
		catch
		{
			return false;
		}
	}
}
