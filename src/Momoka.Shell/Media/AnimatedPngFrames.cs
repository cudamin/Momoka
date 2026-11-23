using Momoka.Infrastructure;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Momoka.Shell.Media;

/// <summary>Decodes one native-size frame at a time; frame count never multiplies retained canvases.</summary>
internal sealed class AnimatedPngFrames
{
    private readonly AnimatedPng _animation;
    private byte[]? _canvas;
    private byte[]? _restore;
    private int _index = -1;

    private AnimatedPngFrames(AnimatedPng animation)
    {
        _animation = animation;
    }

    internal int Width => _animation.Width;
    internal int Height => _animation.Height;
    internal uint Plays => _animation.Plays;
    internal int FrameCount => _animation.Frames.Count;

    internal static Task<AnimatedPngFrames?> OpenAsync(byte[] bytes, CancellationToken token = default) =>
        Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var animation = AnimatedPng.Parse(bytes);
            token.ThrowIfCancellationRequested();
            return animation is { Frames.Count: > 1 } ? new AnimatedPngFrames(animation) : null;
        }, token);

    // The returned pixels belong to this decoder and remain valid until the next read. The caller
    // paints them before reading again. No complete-animation cache or downscaled fallback is needed.
    internal Task<AnimatedPngFrame?> ReadFrameAsync(int index, CancellationToken token = default) =>
        Task.Run(() => ReadFrameCoreAsync(index, token), token);

    private async Task<AnimatedPngFrame?> ReadFrameCoreAsync(int index, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (index < 0 || index >= FrameCount || (index != 0 && index != _index + 1))
                throw new ArgumentOutOfRangeException(nameof(index));
            _canvas ??= new byte[Width * Height * 4];
            if (index == 0) Array.Clear(_canvas); // Each loop starts with a transparent canvas.
            else if (_index >= 0)
            {
                var previous = _animation.Frames[_index];
                if (previous.Dispose == 2) _restore!.CopyTo(_canvas, 0);
                else if (previous.Dispose == 1)
                {
                    for (int row = 0; row < previous.Height; row++)
                        _canvas.AsSpan(((previous.Y + row) * Width + previous.X) * 4, previous.Width * 4).Clear();
                }
            }

            var frame = _animation.Frames[index];
            if (frame.Dispose == 2)
            {
                _restore ??= new byte[_canvas.Length];
                _canvas.CopyTo(_restore, 0);
            }
            var pixels = await DecodePartAsync(_animation.GetFramePng(frame), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (pixels.Length != frame.Width * frame.Height * 4) return null;
            Draw(_canvas, Width, pixels, frame.X, frame.Y, frame.Width, frame.Height, frame.Blend != 0);
            _index = index;
            return new(_canvas, frame.Delay);
        }
        catch (Exception error) when (error is not (OperationCanceledException or OutOfMemoryException))
        {
            // WIC may reject a frame's compressed data even when the APNG container is valid.
            // The caller has already displayed the ordinary PNG fallback.
            token.ThrowIfCancellationRequested();
            return null;
        }
    }

    private static async Task<byte[]> DecodePartAsync(byte[] png, CancellationToken token)
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
        var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.ColorManageToSRgb)
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
