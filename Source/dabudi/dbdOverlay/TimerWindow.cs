using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Threading;

namespace dbdOverlay;

public partial class TimerWindow : Window, IComponentConnector
{
	private enum TimerState
	{
		Idle,
		Running,
		Paused
	}

	private const int GWL_EXSTYLE = -20;

	private const int WS_EX_TRANSPARENT = 32;

	private const int WS_EX_TOOLWINDOW = 128;

	private const int WS_EX_NOACTIVATE = 134217728;

	private readonly DispatcherTimer _uiTimer;

	private readonly Stopwatch _segmentStopwatch;

	private TimerState _state;

	private TimeSpan _accumulated = TimeSpan.Zero;

	[DllImport("user32.dll", EntryPoint = "GetWindowLong")]
	private static extern int GetWindowLong32(nint hWnd, int nIndex);

	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
	private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

	[DllImport("user32.dll", EntryPoint = "SetWindowLong")]
	private static extern int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
	private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

	private static nint GetWindowLongPtr(nint hWnd, int nIndex)
	{
		if (IntPtr.Size != 8)
		{
			return new IntPtr(GetWindowLong32(hWnd, nIndex));
		}
		return GetWindowLongPtr64(hWnd, nIndex);
	}

	private static nint SetWindowLongPtr(nint hWnd, int nIndex, nint newLong)
	{
		if (IntPtr.Size != 8)
		{
			return new IntPtr(SetWindowLong32(hWnd, nIndex, ((IntPtr)newLong).ToInt32()));
		}
		return SetWindowLongPtr64(hWnd, nIndex, newLong);
	}

	public TimerWindow()
	{
		InitializeComponent();
		base.SourceInitialized += OnSourceInitialized;
		_segmentStopwatch = new Stopwatch();
		_uiTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(33.0)
		};
		_uiTimer.Tick += OnUiTick;
	}

	private void OnSourceInitialized(object? sender, EventArgs e)
	{
		try
		{
			nint handle = new WindowInteropHelper(this).Handle;
			if (handle != IntPtr.Zero)
			{
				int num = ((IntPtr)GetWindowLongPtr(handle, -20)).ToInt32();
				num |= 0x80000A0;
				SetWindowLongPtr(handle, -20, new IntPtr(num));
			}
		}
		catch
		{
		}
	}

	public void StartFresh()
	{
		_accumulated = TimeSpan.Zero;
		_segmentStopwatch.Reset();
		_segmentStopwatch.Start();
		_state = TimerState.Running;
		SetRunningVisual();
		UpdateDisplay();
		PositionTopLeft();
		if (!base.IsVisible)
		{
			Show();
		}
		if (!_uiTimer.IsEnabled)
		{
			_uiTimer.Start();
		}
	}

	public void HandleHotkey()
	{
		switch (_state)
		{
		case TimerState.Running:
			Pause();
			break;
		case TimerState.Idle:
		case TimerState.Paused:
			ForceClose();
			break;
		}
	}

	public void Pause()
	{
		if (_state == TimerState.Running)
		{
			_segmentStopwatch.Stop();
			_accumulated += _segmentStopwatch.Elapsed;
			_segmentStopwatch.Reset();
			_state = TimerState.Paused;
			SetPausedVisual();
			UpdateDisplay();
		}
	}

	public void Resume()
	{
		if (_state == TimerState.Paused)
		{
			_segmentStopwatch.Reset();
			_segmentStopwatch.Start();
			_state = TimerState.Running;
			SetRunningVisual();
			UpdateDisplay();
		}
	}

	public void ForceClose()
	{
		_uiTimer.Stop();
		_segmentStopwatch.Stop();
		Close();
	}

	private void OnUiTick(object? sender, EventArgs e)
	{
		if (_state == TimerState.Running)
		{
			UpdateDisplay();
		}
	}

	private TimeSpan GetCurrentElapsed()
	{
		if (_state != TimerState.Running)
		{
			return _accumulated;
		}
		return _accumulated + _segmentStopwatch.Elapsed;
	}

	private void UpdateDisplay()
	{
		TimeSpan currentElapsed = GetCurrentElapsed();
		int value = (int)currentElapsed.TotalMinutes;
		int seconds = currentElapsed.Seconds;
		int value2 = currentElapsed.Milliseconds / 10;
		txtTimer.Text = $"{value}:{seconds:D2}.{value2:D2}";
	}

	private void SetRunningVisual()
	{
		txtTimer.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
	}

	private void SetPausedVisual()
	{
		txtTimer.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
	}

	private void PositionTopLeft()
	{
		base.Dispatcher.InvokeAsync(delegate
		{
			Rect workArea = SystemParameters.WorkArea;
			base.Left = workArea.Left + 16.0;
			base.Top = workArea.Top + 16.0;
		}, DispatcherPriority.Loaded);
	}
}
