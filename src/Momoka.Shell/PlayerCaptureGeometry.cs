using Momoka.Playback;

namespace Momoka.Shell;

/// <summary>Crop coordinates refer to the saved bitmap, independent of preview scaling and letterboxing.</summary>
internal static class PlayerCaptureGeometry
{
    internal static PlayerImageCrop Default(int width, int height, bool subtitles) =>
        new(0, subtitles ? (int)((long)height * 7 / 10) : 0, width,
            subtitles ? height - (int)((long)height * 7 / 10) : height);

    internal static PlayerImageCrop? Clamp(int width, int height, double x, double y, double cropWidth, double cropHeight)
    {
        if (width <= 0 || height <= 0 || !double.IsFinite(x) || !double.IsFinite(y)
            || !double.IsFinite(cropWidth) || !double.IsFinite(cropHeight)) return null;
        var left = (int)Math.Clamp(Math.Round(x), 0, width - 1);
        var top = (int)Math.Clamp(Math.Round(y), 0, height - 1);
        return new(left, top, (int)Math.Clamp(Math.Round(cropWidth), 1, width - left),
            (int)Math.Clamp(Math.Round(cropHeight), 1, height - top));
    }

    internal static PlayerImageCrop? FromViewport(int width, int height, double viewportWidth, double viewportHeight,
        double startX, double startY, double endX, double endY)
    {
        var scale = Scale(width, height, viewportWidth, viewportHeight);
        if (!double.IsFinite(scale) || scale <= 0) return null;
        var offsetX = (viewportWidth - width * scale) / 2;
        var offsetY = (viewportHeight - height * scale) / 2;
        var left = Math.Clamp((Math.Min(startX, endX) - offsetX) / scale, 0, width);
        var right = Math.Clamp((Math.Max(startX, endX) - offsetX) / scale, 0, width);
        var top = Math.Clamp((Math.Min(startY, endY) - offsetY) / scale, 0, height);
        var bottom = Math.Clamp((Math.Max(startY, endY) - offsetY) / scale, 0, height);
        return Clamp(width, height, left, top, right - left, bottom - top);
    }

    internal static double Scale(int width, int height, double viewportWidth, double viewportHeight) =>
        width > 0 && height > 0 ? Math.Min(viewportWidth / width, viewportHeight / height) : 0;
}
