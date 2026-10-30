namespace Momoka.Shell.ViewModels;

/// <summary>
/// Client-side decoded-pixel budget, independent of a server's width hint or DPI. Preserve aspect
/// without upscaling; the square-root limit matters when neither edge alone bounds total pixels.
/// </summary>
internal readonly record struct CoverBackdropSize(int Width, int Height)
{
    internal const int MaxWidth = 1280;
    internal const int MaxHeight = 1280;
    internal const int MaxPixels = 1280 * 720;

    internal static CoverBackdropSize Fit(uint width, uint height)
    {
        if (width == 0 || height == 0) return default;
        var scale = Math.Min(1d, Math.Min(MaxWidth / (double)width, MaxHeight / (double)height));
        scale = Math.Min(scale, Math.Sqrt(MaxPixels / ((double)width * height)));
        return new CoverBackdropSize(Math.Max(1, (int)Math.Floor(width * scale)),
            Math.Max(1, (int)Math.Floor(height * scale)));
    }
}
