namespace Momoka.Infrastructure;

public static class LibraryPanelSize
{
    public static (double Width, double Height) For(double width, double height)
    {
        var roomWidth = Math.Max(0, double.IsFinite(width) ? width - 24 : 0);
        var roomHeight = Math.Max(0, double.IsFinite(height) ? height - 24 : 0);
        return (Math.Min(roomWidth, Math.Clamp(roomWidth * 0.72, 600, 1000)), roomHeight);
    }
}
