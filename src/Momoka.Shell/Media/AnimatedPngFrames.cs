using Momoka.Infrastructure;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Momoka.Shell.Media;

internal sealed record AnimatedPngFrames(int Width, int Height, uint Plays, IReadOnlyList<AnimatedPngFrame> Frames)
{
    private const int MaxSide = 768;
    internal const int MaxBytes = 24 * 1024 * 1024;

    internal static Task<AnimatedPngFrames?> DecodeAsync(byte[] bytes, int requestedPixelWidth,
        CancellationToken token = default) =>
        Task.Run(() => DecodeAsync(bytes, requestedPixelWidth, MaxBytes, token), token);

    internal static async Task<AnimatedPngFrames?> DecodeAsync(byte[] bytes, int requestedPixelWidth, int byteBudget,
        CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (AnimatedPng.Parse(bytes) is not { Frames.Count: > 1 } animation) return null;

            var scale = Math.Min(1d, (double)Math.Clamp(requestedPixelWidth, 1, MaxSide) / animation.Width);
            scale = Math.Min(scale, (double)MaxSide / animation.Height);
            scale = Math.Min(scale, Math.Sqrt((double)byteBudget /
                (4L * animation.Frames.Count * animation.Width * animation.Height)));
            var width = Math.Max(1, (int)(animation.Width * scale));
            var height = Math.Max(1, (int)(animation.Height * scale));
            if (4L * width * height * animation.Frames.Count > byteBudget) return null;

            var frames = new List<AnimatedPngFrame>(animation.Frames.Count);
            var canvas = new byte[width * height * 4];
            foreach (var frame in animation.Frames)
            {
                token.ThrowIfCancellationRequested();
                // Map both edges to the same output grid. Adjacent rectangles stay adjacent and
                // a subpixel rectangle can disappear without shifting the rest of the animation.
                int x = (int)((long)frame.X * width / animation.Width);
                int y = (int)((long)frame.Y * height / animation.Height);
                int partWidth = (int)((long)(frame.X + frame.Width) * width / animation.Width) - x;
                int partHeight = (int)((long)(frame.Y + frame.Height) * height / animation.Height) - y;
                var previous = frame.Dispose == 2 ? (byte[])canvas.Clone() : null;
                if (partWidth > 0 && partHeight > 0)
                {
                    var pixels = await DecodePartAsync(animation.GetFramePng(frame), partWidth, partHeight, token)
                        .ConfigureAwait(false);
                    if (pixels.Length != partWidth * partHeight * 4) return null;
                    Draw(canvas, width, pixels, x, y, partWidth, partHeight, frame.Blend != 0);
                }

                frames.Add(new((byte[])canvas.Clone(), frame.Delay));
                if (previous is not null) canvas = previous;
                else if (frame.Dispose == 1)
                {
                    for (int row = 0; row < partHeight; row++)
                        canvas.AsSpan(((y + row) * width + x) * 4, partWidth * 4).Clear();
                }
            }

            token.ThrowIfCancellationRequested();
            return new(width, height, animation.Plays, frames);
        }
        catch (Exception error) when (error is not (OperationCanceledException or OutOfMemoryException))
        {
            // WIC may reject a frame's compressed data even when the APNG container is valid.
            // The caller has already displayed the ordinary PNG fallback.
            token.ThrowIfCancellationRequested();
            return null;
        }
    }

    private static async Task<byte[]> DecodePartAsync(byte[] png, int width, int height, CancellationToken token)
    {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(png);
            await writer.StoreAsync().AsTask(token).ConfigureAwait(false);
            writer.DetachStream();
        }

        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(BitmapDecoder.PngDecoderId, stream)
            .AsTask(token).ConfigureAwait(false);
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)width,
            ScaledHeight = (uint)height,
            InterpolationMode = BitmapInterpolationMode.Fant
        };
        var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.ColorManageToSRgb)
            .AsTask(token).ConfigureAwait(false);
        return pixels.DetachPixelData();
    }

    private static void Draw(byte[] canvas, int stride, byte[] pixels, int x, int y, int width, int height, bool over)
    {
        for (int row = 0; row < height; row++)
        {
            var dest = canvas.AsSpan(((y + row) * stride + x) * 4, width * 4);
            var src = pixels.AsSpan(row * width * 4, width * 4);
            if (!over)
            {
                src.CopyTo(dest);
                continue;
            }

            for (int pixel = 0; pixel < src.Length; pixel += 4)
            {
                int inverseAlpha = 255 - src[pixel + 3];
                for (int channel = pixel; channel < pixel + 4; channel++)
                    dest[channel] = (byte)(src[channel] + (dest[channel] * inverseAlpha + 127) / 255);
            }
        }
    }
}

internal sealed record AnimatedPngFrame(byte[] Pixels, TimeSpan Delay);
