using Momoka.Infrastructure;

namespace Momoka.Shell.Media;

internal static class PosterDecodeSize
{
    // Zero lets WinUI retain native pixels, including every GIF frame. APNG's static fallback
    // uses the same resolution as its custom frame surface, so stopping motion stays sharp.
    internal static int Width(byte[] bytes, int logicalWidth) =>
        AnimatedPng.Parse(bytes) is not null || bytes.AsSpan().StartsWith("GIF87a"u8)
        || bytes.AsSpan().StartsWith("GIF89a"u8) ? 0 : Math.Max(1, logicalWidth);
}
