namespace Momoka.Shell;

/// <summary>Reads a single captured top-down BGRA frame without another screen synchronisation.</summary>
internal static class CompositionProbePixels
{
    /// <summary>Six-bar resize fixture: include both near edges and the bottom, outside the moving top strip.</summary>
    internal static int ResizeErrors(byte[] bytes, int width, int height)
    {
        _ = ColorRef(bytes, width, height, 0, 0);
        var errors = 0;
        var nearX = Math.Min(3d / Math.Max(1, width - 1), 0.02);
        var bottom = 1 - Math.Min(3d / Math.Max(1, height - 1), 0.03);
        double[] columns = [nearX, 1d / 12, 0.25, 5d / 12, 7d / 12, 0.75, 11d / 12, 1 - nearX];
        uint[] expected = [0x0000FF, 0x0000FF, 0x00FF00, 0x00FFFF, 0xFF0000, 0xFF00FF, 0xFFFF00, 0xFFFF00];
        foreach (var row in new[] { 0.22, 0.5, 0.78, bottom })
            for (var index = 0; index < columns.Length; index++)
            {
                var actual = ColorRef(bytes, width, height,
                    (int)Math.Round((width - 1) * columns[index]), (int)Math.Round((height - 1) * row));
                for (var shift = 0; shift <= 16; shift += 8)
                    if (Math.Abs((int)((actual >> shift) & 255) - (int)((expected[index] >> shift) & 255)) > 90)
                    {
                        errors++;
                        break;
                    }
            }
        return errors;
    }

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
