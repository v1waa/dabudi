using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace dbdOverlay;

public partial class OverlayWindow : Window, IComponentConnector
{
	private readonly DispatcherTimer _timer;

	private DateTime _dsEndTime = DateTime.MinValue;

	private DateTime _endEndTime = DateTime.MinValue;

	private double _dsTotal;

	private double _endTotal;

	private Key _closeKey = Key.Escape;

	private ModifierKeys _closeModifiers;

	private const int WM_HOTKEY = 786;

	private const int HOTKEY_ID_CLOSE = 9001;

	private const int GWL_EXSTYLE = -20;

	private const int WS_EX_NOACTIVATE = 134217728;

	private HwndSource? _source;

	private bool _isCloseHotkeyRegistered;

	public OverlayWindow()
	{
		InitializeComponent();
		base.SourceInitialized += OverlayWindow_SourceInitialized;
		base.Loaded += OverlayWindow_Loaded;
		base.Closed += OverlayWindow_Closed;
		base.KeyDown += OverlayWindow_KeyDown;
		_timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(100.0)
		};
		_timer.Tick += Timer_Tick;
	}

	[DllImport("user32.dll")]
	private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

	[DllImport("user32.dll")]
	private static extern bool UnregisterHotKey(nint hWnd, int id);

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

	private void OverlayWindow_SourceInitialized(object? sender, EventArgs e)
	{
		ApplyNoActivateStyle();
	}

	private void ApplyNoActivateStyle()
	{
		try
		{
			nint handle = new WindowInteropHelper(this).Handle;
			if (handle != IntPtr.Zero)
			{
				int num = ((IntPtr)GetWindowLongPtr(handle, -20)).ToInt32();
				num |= 0x8000000;
				SetWindowLongPtr(handle, -20, new IntPtr(num));
			}
		}
		catch
		{
		}
	}

	private void OverlayWindow_Loaded(object? sender, RoutedEventArgs e)
	{
		Rect workArea = SystemParameters.WorkArea;
		base.Left = workArea.Right - base.Width - 10.0;
		base.Top = workArea.Top + 100.0;
	}

	public void Start(int dsSeconds, int endSeconds)
	{
		_dsTotal = ((dsSeconds > 0) ? dsSeconds : 60);
		_endTotal = ((endSeconds > 0) ? endSeconds : 20);
		_dsEndTime = DateTime.UtcNow.AddSeconds(_dsTotal);
		_endEndTime = DateTime.UtcNow.AddSeconds(_endTotal);
		UpdateUi();
		base.ShowActivated = false;
		if (!base.IsVisible)
		{
			Show();
		}
		EnsureHook();
		RegisterCloseHotkey();
		_timer.Start();
	}

	public bool SetCloseHotkey(Key key, ModifierKeys modifiers)
	{
		_closeKey = key;
		_closeModifiers = modifiers;
		EnsureHook();
		return RegisterCloseHotkey();
	}

	private void EnsureHook()
	{
		if (_source != null)
		{
			return;
		}
		try
		{
			nint handle = new WindowInteropHelper(this).Handle;
			if (handle != IntPtr.Zero)
			{
				_source = HwndSource.FromHwnd(handle);
				if (_source != null)
				{
					_source.AddHook(HwndHook);
				}
			}
		}
		catch
		{
		}
	}

	private nint HwndHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
	{
		if (msg != 786)
		{
			return IntPtr.Zero;
		}
		if (((IntPtr)wParam).ToInt32() == 9001)
		{
			handled = true;
			CloseOverlay();
		}
		return IntPtr.Zero;
	}

	private bool RegisterCloseHotkey()
	{
		try
		{
			nint handle = new WindowInteropHelper(this).Handle;
			if (handle == IntPtr.Zero || _closeKey == Key.None)
			{
				return false;
			}
			try
			{
				UnregisterHotKey(handle, 9001);
			}
			catch
			{
			}
			uint vk = (uint)KeyInterop.VirtualKeyFromKey(_closeKey);
			return _isCloseHotkeyRegistered = RegisterHotKey(handle, 9001, (uint)_closeModifiers, vk);
		}
		catch
		{
			_isCloseHotkeyRegistered = false;
			return false;
		}
	}

	private void UnregisterCloseHotkey()
	{
		if (!_isCloseHotkeyRegistered)
		{
			return;
		}
		try
		{
			nint handle = new WindowInteropHelper(this).Handle;
			if (handle == IntPtr.Zero)
			{
				return;
			}
			try
			{
				UnregisterHotKey(handle, 9001);
			}
			catch
			{
			}
		}
		finally
		{
			_isCloseHotkeyRegistered = false;
		}
	}

	private void OverlayWindow_Closed(object? sender, EventArgs e)
	{
		if (_timer.IsEnabled)
		{
			_timer.Stop();
		}
		UnregisterCloseHotkey();
		if (_source != null)
		{
			try
			{
				_source.RemoveHook(HwndHook);
			}
			catch
			{
			}
			_source = null;
		}
	}

	private void OverlayWindow_KeyDown(object? sender, KeyEventArgs e)
	{
		try
		{
			if (e.Key == Key.Escape)
			{
				CloseOverlay();
			}
			else if (e.Key == _closeKey && (Keyboard.Modifiers & _closeModifiers) == _closeModifiers)
			{
				CloseOverlay();
			}
		}
		catch
		{
		}
	}

	private void CloseOverlay()
	{
		if (_timer.IsEnabled)
		{
			_timer.Stop();
		}
		Close();
	}

	private void Timer_Tick(object? sender, EventArgs e)
	{
		DateTime utcNow = DateTime.UtcNow;
		TimeSpan obj = ((_dsEndTime > utcNow) ? (_dsEndTime - utcNow) : TimeSpan.Zero);
		TimeSpan timeSpan = ((_endEndTime > utcNow) ? (_endEndTime - utcNow) : TimeSpan.Zero);
		if (obj == TimeSpan.Zero && timeSpan == TimeSpan.Zero)
		{
			_timer.Stop();
			Close();
		}
		else
		{
			UpdateUi();
		}
	}

	private void UpdateUi()
	{
		DateTime utcNow = DateTime.UtcNow;
		TimeSpan timeSpan = ((_dsEndTime > utcNow) ? (_dsEndTime - utcNow) : TimeSpan.Zero);
		TimeSpan timeSpan2 = ((_endEndTime > utcNow) ? (_endEndTime - utcNow) : TimeSpan.Zero);
		double fraction = ((_dsTotal > 0.0) ? (timeSpan.TotalSeconds / _dsTotal) : 0.0);
		double fraction2 = ((_endTotal > 0.0) ? (timeSpan2.TotalSeconds / _endTotal) : 0.0);
		dsMaskPath.Data = CreateSectorGeometry(32.0, 32.0, 32.0, fraction);
		endMaskPath.Data = CreateSectorGeometry(32.0, 32.0, 32.0, fraction2);
		txtDsTimeOverlay.Text = ((timeSpan > TimeSpan.Zero) ? $"{timeSpan.TotalSeconds:F1}s" : "Inactive");
		txtEndTimeOverlay.Text = ((timeSpan2 > TimeSpan.Zero) ? $"{timeSpan2.TotalSeconds:F1}s" : "Inactive");
	}

	private Geometry CreateSectorGeometry(double cx, double cy, double radius, double fraction)
	{
		fraction = Math.Max(0.0, Math.Min(1.0, fraction));
		if (fraction <= 0.0)
		{
			return new EllipseGeometry(new Point(cx, cy), radius, radius);
		}
		if (fraction >= 1.0)
		{
			return Geometry.Empty;
		}
		double num = fraction * 360.0;
		double num2 = -90.0 * Math.PI / 180.0;
		double num3 = (-90.0 + num) * Math.PI / 180.0;
		Point point = new Point(cx + radius * Math.Cos(num2), cy + radius * Math.Sin(num2));
		Point point2 = new Point(cx + radius * Math.Cos(num3), cy + radius * Math.Sin(num3));
		bool isLargeArc = num > 180.0;
		StreamGeometry streamGeometry = new StreamGeometry();
		using (StreamGeometryContext streamGeometryContext = streamGeometry.Open())
		{
			streamGeometryContext.BeginFigure(new Point(cx, cy), isFilled: true, isClosed: true);
			streamGeometryContext.LineTo(point, isStroked: true, isSmoothJoin: true);
			streamGeometryContext.ArcTo(point2, new Size(radius, radius), 0.0, isLargeArc, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: true);
			streamGeometryContext.LineTo(new Point(cx, cy), isStroked: true, isSmoothJoin: true);
		}
		streamGeometry.Freeze();
		return streamGeometry;
	}
}
