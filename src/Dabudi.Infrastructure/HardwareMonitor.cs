using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Dabudi.Core;
using LibreHardwareMonitor.Hardware;

namespace Dabudi.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed class HardwareMonitor(AppLog log, bool cpuOnly = false) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<string> _reported = new();
    private Task? _worker;
    private volatile bool _enabled;
    private bool _disposed;
    private ulong _previousIdle, _previousTotal;
    public event Action<PerformanceSnapshot>? Updated;

    public void SetEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _enabled = enabled;
        if (enabled) _worker ??= Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        Computer? computer = null;
        var hardwareAttempted = false;
        var nvidia = cpuOnly ? null : FindNvidiaSmi();
        var access = CpuTemperatureStatus.Checking;
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                if (_enabled)
                {
                    if (!hardwareAttempted)
                    {
                        hardwareAttempted = true;
                        try { access = CpuSensorAccess.Probe(); }
                        catch (Exception exception)
                        {
                            access = CpuTemperatureStatus.Failed;
                            ReportOnce("CPU driver access probe failed", exception);
                        }
                        log.Write("CPU sensor access: " + access);
                        computer = new Computer { IsCpuEnabled = access == CpuTemperatureStatus.Ready, IsGpuEnabled = !cpuOnly };
                        try { computer.Open(); }
                        catch (Exception exception)
                        {
                            ReportOnce("Hardware initialization unavailable", exception);
                            CloseComputer(computer);
                            computer = null;
                        }
                    }
                    double? cpuTemperature = null, gpuPercent = null, gpuTemperature = null;
                    if (computer != null)
                    {
                        foreach (var hardware in computer.Hardware)
                        {
                            try { ReadHardware(hardware, ref cpuTemperature, ref gpuPercent, ref gpuTemperature); }
                            catch (Exception exception) { ReportOnce("Sensor unavailable: " + hardware.Name, exception); }
                        }
                    }
                    // This fallback must still run if LibreHardwareMonitor could not initialize.
                    if ((gpuPercent == null || gpuTemperature == null) && nvidia != null)
                    {
                        var fallback = await ReadNvidiaAsync(nvidia, _lifetime.Token).ConfigureAwait(false);
                        gpuPercent ??= fallback.Load;
                        gpuTemperature ??= fallback.Temperature;
                    }
                    var (usedMemory, totalMemory) = ReadMemory();
                    var reading = access == CpuTemperatureStatus.Ready
                        ? CpuTemperatureReading.FromSensor(cpuTemperature, driverReady: true, canAccess: true)
                        : new CpuTemperatureReading(null, access);
                    var snapshot = new PerformanceSnapshot(ReadCpuPercent(), reading.Temperature,
                        gpuPercent, gpuTemperature, usedMemory, totalMemory) { CpuStatus = reading.Status };
                    if (reading.Status == CpuTemperatureStatus.Unavailable && _reported.Add("CPU report"))
                    {
                        try { log.Write("CPU temperature unavailable. " + computer?.GetReport()); }
                        catch (Exception exception) { ReportOnce("CPU report unavailable", exception); }
                    }
                    if (_enabled && !_lifetime.IsCancellationRequested) Updated?.Invoke(snapshot);
                }
                else if (computer != null || hardwareAttempted)
                {
                    // All sensor operations, including Close, stay on this one worker.
                    CloseComputer(computer);
                    computer = null;
                    hardwareAttempted = false;
                    _previousTotal = 0;
                }
                await Task.Delay(1000, _lifetime.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception) { log.Write("Hardware worker stopped", exception); }
        finally
        {
            CloseComputer(computer);
            _lifetime.Dispose();
        }
    }

    private void CloseComputer(Computer? computer)
    {
        try { computer?.Close(); }
        catch (Exception exception) { ReportOnce("Hardware close failed", exception); }
    }

    private void ReportOnce(string message, Exception exception)
    {
        if (_reported.Add(message)) log.Write(message, exception);
    }

    private static void ReadHardware(IHardware hardware, ref double? cpuTemperature,
        ref double? gpuLoad, ref double? gpuTemperature)
    {
        hardware.Update();
        var cpu = hardware.HardwareType == HardwareType.Cpu;
        var gpu = hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;
        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.Value is not { } value || !float.IsFinite(value)) continue;
            if (sensor.SensorType == SensorType.Temperature && value is >= -20 and <= 150)
            {
                if (cpu) cpuTemperature = Max(cpuTemperature, value);
                if (gpu && !sensor.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase))
                    gpuTemperature = Max(gpuTemperature, value);
            }
            if (gpu && sensor.SensorType == SensorType.Load && value is >= 0 and <= 100
                && (sensor.Name.Contains("GPU Core", StringComparison.OrdinalIgnoreCase)
                    || sensor.Name.Contains("D3D 3D", StringComparison.OrdinalIgnoreCase)
                    || sensor.Name.Contains("GPU Total", StringComparison.OrdinalIgnoreCase)))
                gpuLoad = Max(gpuLoad, value);
        }
        foreach (var child in hardware.SubHardware) ReadHardware(child, ref cpuTemperature, ref gpuLoad, ref gpuTemperature);
    }

    private static double Max(double? previous, double value) => previous.HasValue ? Math.Max(previous.Value, value) : value;

    private double? ReadCpuPercent()
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        var total = kernel + user;
        var elapsed = total >= _previousTotal ? total - _previousTotal : 0;
        var idleElapsed = idle >= _previousIdle ? idle - _previousIdle : 0;
        double? percent = _previousTotal == 0 || elapsed == 0 ? null
            : Math.Clamp(((double)elapsed - idleElapsed) * 100 / elapsed, 0, 100);
        _previousTotal = total;
        _previousIdle = idle;
        return percent;
    }

    private static (double Used, double Total) ReadMemory()
    {
        var status = new NativeMethods.MemoryStatus { Size = (uint)Marshal.SizeOf<NativeMethods.MemoryStatus>() };
        if (!NativeMethods.GlobalMemoryStatusEx(ref status)) return (0, 0);
        return ((status.TotalPhysical - Math.Min(status.AvailablePhysical, status.TotalPhysical)) / 1073741824d,
            status.TotalPhysical / 1073741824d);
    }

    private static string? FindNvidiaSmi() => new[]
    {
        Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe")
    }.FirstOrDefault(File.Exists);

    private async Task<(double? Load, double? Temperature)> ReadNvidiaAsync(string path, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        using var process = new Process { StartInfo = new()
        {
            FileName = path, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        } };
        process.StartInfo.ArgumentList.Add("--query-gpu=utilization.gpu,temperature.gpu");
        process.StartInfo.ArgumentList.Add("--format=csv,noheader,nounits");
        var started = false;
        try
        {
            if (!(started = process.Start())) return default;
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var text = await output.ConfigureAwait(false);
            await error.ConfigureAwait(false);
            if (process.ExitCode != 0) return default;
            double? load = null, temperature = null;
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var columns = line.Split(',', StringSplitOptions.TrimEntries);
                if (columns.Length < 2) continue;
                if (double.TryParse(columns[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var l) && l is >= 0 and <= 100)
                    load = Max(load, l);
                if (double.TryParse(columns[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var t) && t is >= -20 and <= 150)
                    temperature = Max(temperature, t);
            }
            return (load, temperature);
        }
        catch (OperationCanceledException) { return default; }
        catch (Exception exception) { ReportOnce("NVIDIA fallback unavailable", exception); return default; }
        finally
        {
            if (started)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                { ReportOnce("Could not close NVIDIA probe", exception); }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _enabled = false;
        try { _lifetime.Cancel(); }
        catch (ObjectDisposedException) { }
        if (_worker == null) _lifetime.Dispose();
        // Never wait for a driver or process on the UI thread.
    }
}
