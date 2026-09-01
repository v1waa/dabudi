using System.Diagnostics;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using Dabudi.Core;
using Microsoft.Win32;

namespace Dabudi.Infrastructure;

[SupportedOSPlatform("windows")]
public static class CpuSensorAccess
{
    public const string InstallerUrl = "https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe";
    public const string InstallerSha256 = "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";
    private static readonly Version MinimumVersion = new(2, 2, 0);

    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static CpuTemperatureStatus Probe()
    {
        // Read afresh: LibreHardwareMonitor's static PawnIo.Version is cached before installation.
        using var registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
        if (!Version.TryParse(key?.GetValue("DisplayVersion") as string, out var version) || version < MinimumVersion)
            return CpuTemperatureStatus.DriverMissing;
        using var device = NativeMethods.CreateFile(@"\\?\GLOBALROOT\Device\PawnIO", 0xC0000000, 3, 0, 3, 0x80, 0);
        if (!device.IsInvalid) return CpuTemperatureStatus.Ready;
        return IsElevated ? CpuTemperatureStatus.Unavailable : CpuTemperatureStatus.AccessRequired;
    }

    public static async Task<bool> InstallAsync(CancellationToken token)
    {
        if (!IsElevated) throw new UnauthorizedAccessException("Для установки датчиков нужны права администратора.");
        var directory = Path.Combine(Path.GetTempPath(), "dabudi-sensors-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "PawnIO_setup.exe");
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60), MaxResponseContentBufferSize = 8 * 1024 * 1024 };
            var bytes = await client.GetByteArrayAsync(InstallerUrl, token).ConfigureAwait(false);
            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(InstallerSha256, StringComparison.Ordinal))
                throw new IOException("Контрольная сумма установщика PawnIO не совпадает.");
            await File.WriteAllBytesAsync(path, bytes, token).ConfigureAwait(false);
            // Keep the verified image locked against replacement until the installer exits.
            using var verified = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!Convert.ToHexString(SHA256.HashData(verified)).Equals(InstallerSha256, StringComparison.Ordinal))
                throw new IOException("Файл установщика PawnIO был изменён.");
            using var process = Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true, Arguments = "-install -silent"
            }) ?? throw new IOException("Не удалось запустить установщик PawnIO.");
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            if (process.ExitCode == 3010) return true;
            if (process.ExitCode != 0) throw new IOException($"Установка PawnIO завершилась с кодом {process.ExitCode}.");
            return false;
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
