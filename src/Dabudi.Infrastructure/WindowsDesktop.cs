using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Dabudi.Core;

namespace Dabudi.Infrastructure;

public sealed record DisplayInfo(string Device, PixelRect Bounds, PixelRect WorkArea, bool IsPrimary);

[SupportedOSPlatform("windows")]
public static class WindowsDesktop
{
    public static IReadOnlyList<DisplayInfo> Displays()
    {
        var displays = new List<DisplayInfo>();
        NativeMethods.EnumDisplayMonitors(0, 0, (nint monitor, nint _, ref NativeMethods.Rect _, nint _) =>
        {
            if (ReadDisplay(monitor) is { } display) displays.Add(display);
            return true;
        }, 0);
        return displays.OrderByDescending(d => d.IsPrimary).ThenBy(d => d.Device).ToArray();
    }

    public static string ForegroundDisplay() => ReadDisplay(
        NativeMethods.MonitorFromWindow(NativeMethods.GetForegroundWindow(), 2))?.Device ?? "";

    private static DisplayInfo? ReadDisplay(nint monitor)
    {
        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>(), Device = "" };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return null;
        static PixelRect Convert(NativeMethods.Rect r) => new(r.Left, r.Top, r.Right, r.Bottom);
        return new(info.Device, Convert(info.Monitor), Convert(info.Work), (info.Flags & 1) != 0);
    }

    public static void SetOverlayStyles(nint handle, bool allowDragging)
    {
        var style = NativeMethods.GetWindowLongPtr(handle, -20).ToInt64() | 0x08000080L;
        style = allowDragging ? style & ~0x20L : style | 0x20L;
        NativeMethods.SetWindowLongPtr(handle, -20, (nint)style);
    }

    public static void SetDarkTitleBar(nint handle)
    {
        var enabled = 1;
        NativeMethods.DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
    }

    public static void Position(nint handle, string device, OverlayAnchor anchor, double horizontalMargin = 16, double verticalMargin = 16)
    {
        var displays = Displays();
        var display = displays.FirstOrDefault(d => d.Device == device) ?? displays.FirstOrDefault();
        if (display == null || !NativeMethods.GetWindowRect(handle, out var rect)) return;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        var scale = Math.Max(96, NativeMethods.GetDpiForWindow(handle)) / 96d;
        var (x, y) = OverlayPlacement.Calculate(display.Bounds, display.WorkArea, width, height,
            scale, anchor, horizontalMargin, verticalMargin);
        if (!NativeMethods.SetWindowPos(handle, -1, x, y, 0, 0, 0x0011))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось разместить оверлей.");
    }

    public static PixelRect WindowBounds(nint handle)
    {
        if (!NativeMethods.GetWindowRect(handle, out var rect))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось прочитать положение оверлея.");
        return new(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }
}
