using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;

namespace dbdOverlay;

public partial class CrosshairWindow : Window, IComponentConnector
{
	private const int GWL_EXSTYLE = -20;

	private const int WS_EX_TRANSPARENT = 32;

	private const int WS_EX_TOOLWINDOW = 128;

	private const int WS_EX_NOACTIVATE = 134217728;

	public CrosshairWindow()
	{
		InitializeComponent();
		base.SourceInitialized += CrosshairWindow_SourceInitialized;
	}

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

	private void CrosshairWindow_SourceInitialized(object? sender, EventArgs e)
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
}
