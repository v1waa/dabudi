using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using LibreHardwareMonitor.Hardware;

namespace dbdOverlay.Services;

internal sealed class SystemPerformanceMonitor : IDisposable
{
	private struct NativeFileTime
	{
		public uint LowDateTime;

		public uint HighDateTime;

		public readonly ulong ToUInt64()
		{
			return ((ulong)HighDateTime << 32) | LowDateTime;
		}
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
	private struct MemoryStatusEx
	{
		public uint Length;

		public uint MemoryLoad;

		public ulong TotalPhysical;

		public ulong AvailablePhysical;

		public ulong TotalPageFile;

		public ulong AvailablePageFile;

		public ulong TotalVirtual;

		public ulong AvailableVirtual;

		public ulong AvailableExtendedVirtual;
	}

	private readonly DispatcherTimer _timer;

	private readonly Dispatcher _dispatcher;

	private readonly object _hardwareLock = new object();

	private Computer? _computer;

	private CancellationTokenSource? _elevatedSensorCancellation;

	private ulong _previousIdle;

	private ulong _previousKernel;

	private ulong _previousUser;

	private int _tickCount;

	private bool _hardwareReadInProgress;

	private bool _elevatedSensorAttempted;

	private bool _isRunning;

	private double? _cpuTemperatureC;

	private double? _gpuPercent;

	private double? _gpuTemperatureC;

	public event Action<PerformanceSnapshot>? SnapshotUpdated;

	public SystemPerformanceMonitor()
	{
		_dispatcher = Dispatcher.CurrentDispatcher;
		_timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(1.0)
		};
		_timer.Tick += OnTimerTick;
	}

	public void Start()
	{
		if (!_isRunning)
		{
			_isRunning = true;
			OpenDirectHardwareMonitor();
			ReadCpuPercent();
			_timer.Start();
			_ = UpdateHardwareSensorsAsync();
			PublishSnapshot();
		}
	}

	public void Stop()
	{
		if (_isRunning)
		{
			_isRunning = false;
			_timer.Stop();
			_elevatedSensorCancellation?.Cancel();
			_elevatedSensorCancellation?.Dispose();
			_elevatedSensorCancellation = null;
			CloseDirectHardwareMonitor();
		}
	}

	public void Dispose()
	{
		Stop();
	}

	private void OnTimerTick(object? sender, EventArgs e)
	{
		_tickCount++;
		if (_tickCount % 4 == 0)
		{
			_ = UpdateHardwareSensorsAsync();
		}
		PublishSnapshot();
	}

	private void PublishSnapshot()
	{
		double value = ReadCpuPercent();
		var (usedMemoryGb, totalMemoryGb) = ReadMemory();
		this.SnapshotUpdated?.Invoke(new PerformanceSnapshot(value, _cpuTemperatureC, _gpuPercent, _gpuTemperatureC, usedMemoryGb, totalMemoryGb));
	}

	private async Task UpdateHardwareSensorsAsync()
	{
		if (_hardwareReadInProgress || !_isRunning)
		{
			return;
		}
		_hardwareReadInProgress = true;
		try
		{
			Task<(double? CpuTemperature, double? GpuLoad, double? GpuTemperature)> directTask = Task.Run((Func<(double?, double?, double?)>)ReadDirectHardwareSensors);
			Task<(double? Load, double? Temperature)> nvidiaTask = ReadNvidiaSensorsAsync();
			Task<(double? CpuTemperature, double? GpuLoad, double? GpuTemperature)> hardwareTask = ReadHardwareMonitorSensorsAsync();
			await Task.WhenAll(directTask, nvidiaTask, hardwareTask);
			(double?, double?, double?) tuple = await directTask;
			double? directCpuTemperature = tuple.Item1;
			double? directGpuLoad = tuple.Item2;
			double? directGpuTemperature = tuple.Item3;
			(double?, double?) tuple2 = await nvidiaTask;
			double? nvidiaLoad = tuple2.Item1;
			double? nvidiaTemperature = tuple2.Item2;
			(double?, double?, double?) obj = await hardwareTask;
			double? item = obj.Item1;
			double? item2 = obj.Item2;
			double? item3 = obj.Item3;
			_cpuTemperatureC = ValidateTemperature(directCpuTemperature ?? item);
			_gpuPercent = ValidatePercent(directGpuLoad ?? nvidiaLoad ?? item2);
			_gpuTemperatureC = ValidateTemperature(directGpuTemperature ?? nvidiaTemperature ?? item3);
			if (!_cpuTemperatureC.HasValue)
			{
				StartElevatedSensorReader();
			}
		}
		catch
		{
		}
		finally
		{
			_hardwareReadInProgress = false;
		}
	}

	private void StartElevatedSensorReader()
	{
		if (_elevatedSensorAttempted || !_isRunning)
		{
			return;
		}
		_elevatedSensorAttempted = true;
		string? executablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
		if (!string.IsNullOrWhiteSpace(executablePath))
		{
			string pipeName = $"dabudi-sensors-{Environment.ProcessId}-{Guid.NewGuid():N}";
			CancellationTokenSource cancellation = new CancellationTokenSource();
			_elevatedSensorCancellation = cancellation;
			_ = Task.Run(() => ReceiveElevatedSensorsAsync(executablePath, pipeName, cancellation.Token));
		}
	}

	private async Task ReceiveElevatedSensorsAsync(string executablePath, string pipeName, CancellationToken cancellationToken)
	{
		try
		{
			await using NamedPipeServerStream server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
			using Process? worker = Process.Start(new ProcessStartInfo
			{
				FileName = executablePath,
				Arguments = "--sensor-worker " + pipeName,
				UseShellExecute = true,
				Verb = "runas",
				WindowStyle = ProcessWindowStyle.Hidden
			});
			if (worker == null)
			{
				return;
			}

			await server.WaitForConnectionAsync(cancellationToken);
			using StreamReader reader = new StreamReader(server);
			while (!cancellationToken.IsCancellationRequested && server.IsConnected)
			{
				string? text = await reader.ReadLineAsync(cancellationToken);
				if (text == null)
				{
					break;
				}

				var (cpuTemperature, gpuLoad, gpuTemperature) = ParseSensorLine(text);
				await _dispatcher.InvokeAsync(delegate
				{
					if (_isRunning)
					{
						_cpuTemperatureC = ValidateTemperature(cpuTemperature) ?? _cpuTemperatureC;
						_gpuPercent = ValidatePercent(gpuLoad) ?? _gpuPercent;
						_gpuTemperatureC = ValidateTemperature(gpuTemperature) ?? _gpuTemperatureC;
						PublishSnapshot();
					}
				});
			}
		}
		catch
		{
		}
	}

	private static (double? CpuTemperature, double? GpuLoad, double? GpuTemperature) ParseSensorLine(string line)
	{
		string[] array = line.Split('|', StringSplitOptions.TrimEntries);
		return (CpuTemperature: (array.Length != 0) ? ParseInvariant(array[0]) : ((double?)null), GpuLoad: (array.Length > 1) ? ParseInvariant(array[1]) : ((double?)null), GpuTemperature: (array.Length > 2) ? ParseInvariant(array[2]) : ((double?)null));
	}

	private void OpenDirectHardwareMonitor()
	{
		lock (_hardwareLock)
		{
			if (_computer != null)
			{
				return;
			}
			try
			{
				Computer computer = new Computer
				{
					IsCpuEnabled = true,
					IsGpuEnabled = true,
					IsMotherboardEnabled = true,
					IsControllerEnabled = true
				};
				computer.Open();
				_computer = computer;
			}
			catch
			{
				_computer = null;
			}
		}
	}

	private void CloseDirectHardwareMonitor()
	{
		lock (_hardwareLock)
		{
			if (_computer != null)
			{
				try
				{
					_computer.Close();
				}
				catch
				{
				}
				_computer = null;
			}
		}
	}

	private (double? CpuTemperature, double? GpuLoad, double? GpuTemperature) ReadDirectHardwareSensors()
	{
		lock (_hardwareLock)
		{
			if (_computer == null)
			{
				return (CpuTemperature: null, GpuLoad: null, GpuTemperature: null);
			}
			double? cpuTemperature = null;
			double? gpuLoad = null;
			double? gpuTemperature = null;
			try
			{
				foreach (IHardware item in _computer.Hardware)
				{
					bool isCpu = item.HardwareType == HardwareType.Cpu;
					HardwareType hardwareType = item.HardwareType;
					bool flag = (uint)(hardwareType - 4) <= 2u;
					bool isGpu = flag;
					ReadHardwareTree(item, isCpu, isGpu, ref cpuTemperature, ref gpuLoad, ref gpuTemperature);
				}
			}
			catch
			{
			}
			return (CpuTemperature: cpuTemperature, GpuLoad: gpuLoad, GpuTemperature: gpuTemperature);
		}
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
		if (!double.IsNaN(value) && (!target.HasValue || value > target.Value))
		{
			target = value;
		}
	}

	private static async Task<(double? Load, double? Temperature)> ReadNvidiaSensorsAsync()
	{
		string? text = await RunProcessAsync("nvidia-smi.exe", new string[2] { "--query-gpu=utilization.gpu,temperature.gpu", "--format=csv,noheader,nounits" });
		if (string.IsNullOrWhiteSpace(text))
		{
			return (Load: null, Temperature: null);
		}
		string[] array = text.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0].Split(',', StringSplitOptions.TrimEntries);
		return (Load: (array.Length != 0) ? ParseInvariant(array[0]) : ((double?)null), Temperature: (array.Length > 1) ? ParseInvariant(array[1]) : ((double?)null));
	}

	private static async Task<(double? CpuTemperature, double? GpuLoad, double? GpuTemperature)> ReadHardwareMonitorSensorsAsync()
	{
		string? text = await RunProcessAsync("powershell.exe", new string[6] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", "$ErrorActionPreference='SilentlyContinue';$cpu=$null;$gl=$null;$gt=$null;foreach($ns in @('root/LibreHardwareMonitor','root/OpenHardwareMonitor')){try{$s=Get-CimInstance -Namespace $ns -ClassName Sensor -ErrorAction Stop;$cpu=($s|Where-Object{$_.SensorType -eq 'Temperature' -and $_.Name -match 'CPU Package|CPU Core|Core Average'}|Measure-Object Value -Maximum).Maximum;$gl=($s|Where-Object{$_.SensorType -eq 'Load' -and $_.Name -match 'GPU Core|D3D 3D'}|Measure-Object Value -Maximum).Maximum;$gt=($s|Where-Object{$_.SensorType -eq 'Temperature' -and $_.Name -match 'GPU Core|GPU Hot Spot'}|Measure-Object Value -Maximum).Maximum;if($cpu -or $gl -or $gt){break}}catch{}};if(-not $cpu){try{$v=(Get-CimInstance -Namespace root/wmi -ClassName MSAcpi_ThermalZoneTemperature|Select-Object -First 1 -ExpandProperty CurrentTemperature);if($v){$cpu=($v/10)-273.15}}catch{}};[Console]::Write(('{0}|{1}|{2}' -f $cpu,$gl,$gt))" });
		if (string.IsNullOrWhiteSpace(text))
		{
			return (CpuTemperature: null, GpuLoad: null, GpuTemperature: null);
		}
		string[] array = text.Trim().Split('|', StringSplitOptions.TrimEntries);
		return (CpuTemperature: (array.Length != 0) ? ParseInvariant(array[0]) : ((double?)null), GpuLoad: (array.Length > 1) ? ParseInvariant(array[1]) : ((double?)null), GpuTemperature: (array.Length > 2) ? ParseInvariant(array[2]) : ((double?)null));
	}

	private static async Task<string?> RunProcessAsync(string fileName, string[] arguments)
	{
		try
		{
			ProcessStartInfo processStartInfo = new ProcessStartInfo
			{
				FileName = fileName,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			foreach (string item in arguments)
			{
				processStartInfo.ArgumentList.Add(item);
			}
			using Process process = new Process
			{
				StartInfo = processStartInfo
			};
			if (!process.Start())
			{
				return null;
			}
			Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
			await Task.WhenAny(process.WaitForExitAsync(), Task.Delay(3000));
			if (!process.HasExited)
			{
				try
				{
					process.Kill(entireProcessTree: true);
				}
				catch
				{
				}
				return null;
			}
			return (await outputTask).Trim();
		}
		catch
		{
			return null;
		}
	}

	private static double? ParseInvariant(string? value)
	{
		if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
		{
			return null;
		}
		return result;
	}

	private static double? ValidatePercent(double? value)
	{
		if (value.HasValue)
		{
			double valueOrDefault = value.GetValueOrDefault();
			if (valueOrDefault >= 0.0 && valueOrDefault <= 100.0)
			{
				return value;
			}
		}
		return null;
	}

	private static double? ValidateTemperature(double? value)
	{
		if (value.HasValue)
		{
			double valueOrDefault = value.GetValueOrDefault();
			if (valueOrDefault >= -20.0 && valueOrDefault <= 150.0)
			{
				return value;
			}
		}
		return null;
	}

	private double ReadCpuPercent()
	{
		if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
		{
			return 0.0;
		}
		ulong num = idleTime.ToUInt64();
		ulong num2 = kernelTime.ToUInt64();
		ulong num3 = userTime.ToUInt64();
		if (_previousKernel == 0L && _previousUser == 0L)
		{
			_previousIdle = num;
			_previousKernel = num2;
			_previousUser = num3;
			return 0.0;
		}
		ulong num4 = num - _previousIdle;
		ulong num5 = num2 - _previousKernel;
		ulong num6 = num3 - _previousUser;
		ulong num7 = num5 + num6;
		_previousIdle = num;
		_previousKernel = num2;
		_previousUser = num3;
		if (num7 == 0L)
		{
			return 0.0;
		}
		return Math.Clamp(Math.Max(0.0, num7 - num4) * 100.0 / (double)num7, 0.0, 100.0);
	}

	private static (double UsedGb, double TotalGb) ReadMemory()
	{
		MemoryStatusEx buffer = new MemoryStatusEx
		{
			Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
		};
		if (!GlobalMemoryStatusEx(ref buffer) || buffer.TotalPhysical == 0L)
		{
			return (UsedGb: 0.0, TotalGb: 0.0);
		}
		return (UsedGb: (double)(buffer.TotalPhysical - buffer.AvailablePhysical) / 1073741824.0, TotalGb: (double)buffer.TotalPhysical / 1073741824.0);
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetSystemTimes(out NativeFileTime idleTime, out NativeFileTime kernelTime, out NativeFileTime userTime);

	[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
