namespace Dabudi.Core;

public readonly record struct PerformanceSnapshot(double? CpuPercent, double? CpuTemperature,
    double? GpuPercent, double? GpuTemperature, double UsedMemoryGiB, double TotalMemoryGiB);
