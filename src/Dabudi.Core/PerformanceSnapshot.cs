namespace Dabudi.Core;

public readonly record struct PerformanceSnapshot(double? CpuPercent, double? CpuTemperature,
    double? GpuPercent, double? GpuTemperature, double UsedMemoryGiB, double TotalMemoryGiB)
{
    public CpuTemperatureStatus CpuStatus { get; init; }
}

public enum CpuTemperatureStatus { Checking, Ready, DriverMissing, AccessRequired, Unavailable, Installing, RestartRequired, Failed }

public readonly record struct CpuTemperatureReading(double? Temperature, CpuTemperatureStatus Status)
{
    public static CpuTemperatureReading FromSensor(double? temperature, bool driverReady, bool canAccess)
    {
        // Missing driver access must never turn a failed MSR read into a believable temperature.
        if (!driverReady) return new(null, CpuTemperatureStatus.DriverMissing);
        if (!canAccess) return new(null, CpuTemperatureStatus.AccessRequired);
        return temperature is >= -20 and <= 150 && double.IsFinite(temperature.Value)
            ? new(temperature, CpuTemperatureStatus.Ready) : new(null, CpuTemperatureStatus.Unavailable);
    }
}
