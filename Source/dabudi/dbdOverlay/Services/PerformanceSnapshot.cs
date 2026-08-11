namespace dbdOverlay.Services;

internal readonly record struct PerformanceSnapshot(double? CpuPercent, double? CpuTemperatureC, double? GpuPercent, double? GpuTemperatureC, double UsedMemoryGb, double TotalMemoryGb);
