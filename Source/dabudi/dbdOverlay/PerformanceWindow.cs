using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;
using dbdOverlay.Services;

namespace dbdOverlay;

public partial class PerformanceWindow : Window, IComponentConnector
{
	private readonly SystemPerformanceMonitor _monitor = new SystemPerformanceMonitor();

	public PerformanceWindow()
	{
		InitializeComponent();
		base.Loaded += OnLoaded;
		base.Closed += OnClosed;
		_monitor.SnapshotUpdated += OnSnapshotUpdated;
	}

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		Rect workArea = SystemParameters.WorkArea;
		base.Left = workArea.Right - base.Width - 18.0;
		base.Top = workArea.Top + 18.0;
		_monitor.Start();
	}

	private void OnClosed(object? sender, EventArgs e)
	{
		_monitor.SnapshotUpdated -= OnSnapshotUpdated;
		_monitor.Dispose();
	}

	private void OnSnapshotUpdated(PerformanceSnapshot snapshot)
	{
		txtCpu.Text = FormatLoadAndTemperature(snapshot.CpuPercent, snapshot.CpuTemperatureC);
		txtGpu.Text = FormatLoadAndTemperature(snapshot.GpuPercent, snapshot.GpuTemperatureC);
		txtRam.Text = ((snapshot.TotalMemoryGb <= 0.0) ? "—" : $"{snapshot.UsedMemoryGb:0.0} / {snapshot.TotalMemoryGb:0.0} GB");
	}

	private static string FormatLoadAndTemperature(double? load, double? temperature)
	{
		string obj = (load.HasValue ? $"{load.Value:0}%" : "—");
		string text = (temperature.HasValue ? $"{temperature.Value:0} °C" : "—");
		return obj + "  ·  " + text;
	}

	private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.LeftButton != MouseButtonState.Pressed)
		{
			return;
		}
		try
		{
			DragMove();
		}
		catch
		{
		}
	}
}
