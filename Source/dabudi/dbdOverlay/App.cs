using System;
using System.Threading.Tasks;
using System.Windows;
using dbdOverlay.Services;

namespace dbdOverlay;

public partial class App : Application
{
	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		if (e.Args.Length == 2 && string.Equals(e.Args[0], "--sensor-worker", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(e.Args[1]))
		{
			base.ShutdownMode = ShutdownMode.OnExplicitShutdown;
			_ = RunSensorWorkerAndExitAsync(e.Args[1]);
		}
		else
		{
			base.MainWindow = new MainWindow();
			base.MainWindow.Show();
		}
	}

	private async Task RunSensorWorkerAndExitAsync(string pipeName)
	{
		try
		{
			await ElevatedSensorWorker.RunAsync(pipeName);
		}
		catch
		{
		}
		finally
		{
			Shutdown();
		}
	}
}
