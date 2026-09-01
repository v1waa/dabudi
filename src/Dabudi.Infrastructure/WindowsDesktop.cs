using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Dabudi.Infrastructure;

public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}
public sealed record DisplayInfo(string Device, PixelRect Bounds, PixelRect WorkArea, bool IsPrimary);
public enum OverlayAnchor { TopLeft, TopRight, RightCenter, Center }

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
        var area = anchor == OverlayAnchor.Center ? display.Bounds : display.WorkArea;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        var scale = Math.Max(96, NativeMethods.GetDpiForWindow(handle)) / 96d;
        var marginX = (int)Math.Round(horizontalMargin * scale);
        var marginY = (int)Math.Round(verticalMargin * scale);
        var x = anchor switch
        {
            OverlayAnchor.TopLeft => area.Left + marginX,
            OverlayAnchor.Center => area.Left + (area.Width - width) / 2,
            _ => area.Right - width - marginX
        };
        var y = anchor switch
        {
            OverlayAnchor.Center or OverlayAnchor.RightCenter => area.Top + (area.Height - height) / 2,
            _ => area.Top + marginY
        };
        if (!NativeMethods.SetWindowPos(handle, -1, x, y, 0, 0, 0x0011))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось разместить оверлей.");
    }
}
