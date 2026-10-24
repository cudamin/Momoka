namespace Momoka.Shell;

/// <summary>Reads a single captured top-down BGRA frame without another screen synchronisation.</summary>
internal static class CompositionProbePixels
{
    internal static uint ColorRef(byte[] bytes, int width, int height, int column, int row)
    {
        if (width <= 0 || height <= 0 || bytes.Length != checked(width * height * 4))
            throw new ArgumentException("像素探针帧尺寸或完整数据无效");
        if ((uint)column >= (uint)width || (uint)row >= (uint)height)
            throw new ArgumentOutOfRangeException(nameof(column), "像素探针采样点在帧外");
        var index = checked((row * width + column) * 4);
        // GDI COLORREF has red in its low byte; the captured DIB stores blue first.
        return (uint)(bytes[index + 2] | bytes[index + 1] << 8 | bytes[index] << 16);
    }

    internal static string Rgb(byte[] bytes, int width, int height, int column, int row)
    {
        var color = ColorRef(bytes, width, height, column, row);
        return $"#{(color & 0xFF):X2}{((color >> 8) & 0xFF):X2}{((color >> 16) & 0xFF):X2}";
    }
}
