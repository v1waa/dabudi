using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Dabudi.Infrastructure;

[SupportedOSPlatform("windows")]
public static class StartupRegistration
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "dabudi";

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true)
            ?? throw new IOException("Не удалось открыть настройки автозапуска Windows.");
        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }
        var executable = Environment.ProcessPath ?? throw new IOException("Не удалось определить путь к dabudi.exe.");
        key.SetValue(ValueName, $"\"{executable}\" --tray", RegistryValueKind.String);
    }
}
