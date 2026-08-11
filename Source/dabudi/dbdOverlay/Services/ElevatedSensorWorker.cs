using System;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;

namespace dbdOverlay.Services;

internal static class ElevatedSensorWorker
{
	public static async Task RunAsync(string pipeName)
	{
		using NamedPipeClientStream pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
		await pipe.ConnectAsync(15000);
		await using StreamWriter writer = new StreamWriter(pipe)
		{
			AutoFlush = true
		};
		Computer computer = new Computer
		{
			IsCpuEnabled = true,
			IsGpuEnabled = true,
			IsMotherboardEnabled = true,
			IsControllerEnabled = true
		};
		try
		{
			computer.Open();
			while (pipe.IsConnected)
			{
				(double? CpuTemperature, double? GpuLoad, double? GpuTemperature) tuple = ReadSensors(computer);
				double? item = tuple.CpuTemperature;
				double? item2 = tuple.GpuLoad;
				double? item3 = tuple.GpuTemperature;
				string value = string.Join('|', Format(item), Format(item2), Format(item3));
				try
				{
					await writer.WriteLineAsync(value);
				}
				catch (IOException)
				{
					break;
				}
				await Task.Delay(1500);
			}
		}
		finally
		{
			computer.Close();
		}
	}

	private static (double? CpuTemperature, double? GpuLoad, double? GpuTemperature) ReadSensors(Computer computer)
	{
		double? cpuTemperature = null;
		double? gpuLoad = null;
		double? gpuTemperature = null;
		foreach (IHardware item in computer.Hardware)
		{
			IHardware current;
			IHardware hardware = (current = item);
			bool isCpu = hardware.HardwareType == HardwareType.Cpu;
			HardwareType hardwareType = hardware.HardwareType;
			bool isGpu = (uint)(hardwareType - 4) <= 2u;
			ReadHardwareTree(current, isCpu, isGpu, ref cpuTemperature, ref gpuLoad, ref gpuTemperature);
		}
		return (CpuTemperature: cpuTemperature, GpuLoad: gpuLoad, GpuTemperature: gpuTemperature);
	}

	private static void ReadHardwareTree(IHardware hardware, bool isCpu, bool isGpu, ref double? cpuTemperature, ref double? gpuLoad, ref double? gpuTemperature)
	{
		hardware.Update();
		ISensor[] sensors = hardware.Sensors;
		foreach (ISensor sensor in sensors)
		{
			if (sensor.Value.HasValue)
			{
				double value = sensor.Value.Value;
				if (sensor.SensorType == SensorType.Temperature && (isCpu || IsCpuTemperatureName(sensor.Name)))
				{
					SetMaximum(ref cpuTemperature, value);
				}
				else if (isGpu && sensor.SensorType == SensorType.Temperature)
				{
					SetMaximum(ref gpuTemperature, value);
				}
				else if (isGpu && sensor.SensorType == SensorType.Load && (sensor.Name.Contains("GPU Core", StringComparison.OrdinalIgnoreCase) || sensor.Name.Contains("D3D 3D", StringComparison.OrdinalIgnoreCase) || sensor.Name.Contains("GPU Total", StringComparison.OrdinalIgnoreCase)))
				{
					SetMaximum(ref gpuLoad, value);
				}
			}
		}
		IHardware[] subHardware = hardware.SubHardware;
		foreach (IHardware hardware2 in subHardware)
		{
			IHardware hardware3 = hardware2;
			bool isCpu2 = isCpu || hardware2.HardwareType == HardwareType.Cpu;
			bool flag = isGpu;
			if (!flag)
			{
				HardwareType hardwareType = hardware2.HardwareType;
				bool flag2 = (uint)(hardwareType - 4) <= 2u;
				flag = flag2;
			}
			ReadHardwareTree(hardware3, isCpu2, flag, ref cpuTemperature, ref gpuLoad, ref gpuTemperature);
		}
	}

	private static bool IsCpuTemperatureName(string name)
	{
		if (!name.Equals("CPU", StringComparison.OrdinalIgnoreCase) && !name.Contains("CPU Package", StringComparison.OrdinalIgnoreCase) && !name.Contains("CPU Core", StringComparison.OrdinalIgnoreCase) && !name.Contains("CPU Socket", StringComparison.OrdinalIgnoreCase) && !name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) && !name.Contains("Tdie", StringComparison.OrdinalIgnoreCase) && !name.Contains("Core Average", StringComparison.OrdinalIgnoreCase) && !name.Contains("Core Max", StringComparison.OrdinalIgnoreCase))
		{
			return name.Contains("CCD", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static void SetMaximum(ref double? target, double value)
	{
		if (!double.IsNaN(value) && value >= -20.0 && value <= 150.0 && (!target.HasValue || value > target.Value))
		{
			target = value;
		}
	}

	private static string Format(double? value)
	{
		return value?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty;
	}
}
