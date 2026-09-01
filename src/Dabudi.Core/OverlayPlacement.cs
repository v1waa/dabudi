namespace Dabudi.Core;

public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

public enum OverlayAnchor { TopLeft, TopRight, RightCenter, Center }

public static class OverlayPlacement
{
    public static (int X, int Y) Calculate(PixelRect bounds, PixelRect workArea, int width, int height,
        double scale, OverlayAnchor anchor, double horizontalMargin, double verticalMargin)
    {
        var area = anchor == OverlayAnchor.Center ? bounds : workArea;
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
        return (x, y);
    }
}
