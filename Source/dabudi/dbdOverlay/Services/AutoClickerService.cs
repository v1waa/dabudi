using System;
using System.Threading;
using System.Threading.Tasks;
using dbdOverlay.Models;

namespace dbdOverlay.Services;

internal sealed class AutoClickerService : IDisposable
{
	private readonly object _sync = new object();

	private CancellationTokenSource? _cancellation;

	public bool IsRunning
	{
		get
		{
			lock (_sync)
			{
				CancellationTokenSource? cancellation = _cancellation;
				return cancellation != null && !cancellation.IsCancellationRequested;
			}
		}
	}

	public void Start(ClickerBinding binding, int clicksPerSecond)
	{
		Stop();
		clicksPerSecond = Math.Clamp(clicksPerSecond, 1, 50);
		CancellationTokenSource cancellation = new CancellationTokenSource();
		lock (_sync)
		{
			_cancellation = cancellation;
		}
		Task.Run(() => RunAsync(binding, clicksPerSecond, cancellation));
	}

	public void Stop()
	{
		CancellationTokenSource? cancellation;
		lock (_sync)
		{
			cancellation = _cancellation;
			_cancellation = null;
		}
		try
		{
			cancellation?.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}
	}

	public void Dispose()
	{
		Stop();
	}

	private async Task RunAsync(ClickerBinding binding, int clicksPerSecond, CancellationTokenSource cancellation)
	{
		int intervalMs = Math.Max(1, (int)Math.Round(1000.0 / (double)clicksPerSecond));
		try
		{
			while (!cancellation.IsCancellationRequested)
			{
				if (binding.Kind == ClickerInputKind.KeyboardKey)
				{
					InputAutomationService.PressKey(binding.VirtualKey);
				}
				else
				{
					InputAutomationService.Click(binding.MouseButton);
				}
				await Task.Delay(intervalMs, cancellation.Token).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			lock (_sync)
			{
				if (_cancellation == cancellation)
				{
					_cancellation = null;
				}
			}
			cancellation.Dispose();
		}
	}
}
