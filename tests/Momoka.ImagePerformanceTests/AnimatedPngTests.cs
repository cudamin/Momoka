using System.Buffers.Binary;
using System.IO.Compression;
using Momoka.Shell.Media;
using static Momoka.Tests.Program;

namespace Momoka.Tests;

internal static class AnimatedPngTests
{
    private const uint Red = 0xFF0000FF;
    private const uint Green = 0x00FF00FF;
    private const uint Blue = 0x0000FFFF;
    private const uint Transparent = 0;

    internal static void Register()
    {
        Test("实际 WinRT APNG：局部帧、透明度、混合、还原和清空", async () =>
        {
            var png = Png(3, 2, 3,
                [
                    new(0, 0, 3, 2, Red, 1, 10),
                    new(1, 0, 1, 1, 0x0000FF80, 11, 100, Dispose: 2, Blend: 1),
                    new(2, 1, 1, 1, Green, 3, 20, Dispose: 1),
                    new(0, 1, 1, 1, Transparent, 15, 0),
                    new(2, 1, 1, 1, 0x0000FF40, 0, 100, Blend: 1)
                ]);
            var animation = await AnimatedPngFrames.DecodeAsync(png, 12);
            Assert.NotNull(animation);
            Assert.Equal((3, 2, 3u, 5), (animation!.Width, animation.Height, animation.Plays, animation.Frames.Count));
            Assert.True(animation.Frames.Select(frame => frame.Delay.TotalMilliseconds)
                .SequenceEqual([100d, 110d, 150d, 150d, 0d]));
            Pixel(animation, 0, 0, 0, [0, 0, 255, 255]);
            Pixel(animation, 1, 1, 0, [128, 0, 127, 255]);
            Pixel(animation, 1, 1, 1, [0, 0, 255, 255]);
            Pixel(animation, 2, 1, 0, [0, 0, 255, 255]);
            Pixel(animation, 2, 2, 1, [0, 255, 0, 255]);
            Pixel(animation, 3, 2, 1, [0, 0, 0, 0]);
            Pixel(animation, 3, 0, 1, [0, 0, 0, 0]);
            Pixel(animation, 4, 2, 1, [64, 0, 0, 64]);
        });

        Test("实际 WinRT APNG：独立默认图不参与无限循环，小局部帧缩放保留时序", async () =>
        {
            var png = Png(8, 4, 0,
                [
                    new(0, 0, 4, 4, Green, 1, 5),
                    new(5, 0, 1, 1, Blue, 1, 5, Dispose: 2),
                    new(4, 0, 4, 4, Blue, 1, 5)
                ], defaultColor: Red);
            var animation = await AnimatedPngFrames.DecodeAsync(png, 2);
            Assert.NotNull(animation);
            Assert.Equal((2, 1, 0u, 3), (animation!.Width, animation.Height, animation.Plays, animation.Frames.Count));
            Pixel(animation, 0, 0, 0, [0, 255, 0, 255]);
            Pixel(animation, 0, 1, 0, [0, 0, 0, 0]);
            Pixel(animation, 1, 0, 0, [0, 255, 0, 255]);
            Pixel(animation, 2, 0, 0, [0, 255, 0, 255]);
            Pixel(animation, 2, 1, 0, [255, 0, 0, 255]);
        });

        Test("实际 WinRT APNG：静态图和损坏动图交回静态路径，取消可观察", async () =>
        {
            Assert.Null(await AnimatedPngFrames.DecodeAsync(ImageData.Png(3, 2), 3));
            var gif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");
            Assert.Null(await AnimatedPngFrames.DecodeAsync(gif, 3));

            var valid = Png(3, 2, 0, [new(0, 0, 3, 2, Red), new(1, 1, 1, 1, Blue)]);
            Assert.Null(await AnimatedPngFrames.DecodeAsync(valid[..^8], 3));
            var invalidCrc = (byte[])valid.Clone();
            invalidCrc[49] ^= 1; // First byte of the animation control chunk CRC.
            Assert.Null(await AnimatedPngFrames.DecodeAsync(invalidCrc, 3));
            var outOfBounds = Png(3, 2, 0, [new(0, 0, 3, 2, Red), new(3, 1, 1, 1, Blue)]);
            Assert.Null(await AnimatedPngFrames.DecodeAsync(outOfBounds, 3));
            var firstFrameUsingBothDataTypes = Png(3, 2, 0,
                [new(0, 0, 3, 2, Red), new(1, 1, 1, 1, Blue)], mixFirstFrameData: true);
            Assert.Null(await AnimatedPngFrames.DecodeAsync(firstFrameUsingBothDataTypes, 3));

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Cancelled(AnimatedPngFrames.DecodeAsync(valid, 3, cancelled.Token));
        });

        Test("实际 WinRT APNG：调色板与透明色块传给每一帧", async () =>
        {
            // Pillow-generated 2-bit indexed PNG with two colors and a transparent palette entry.
            var png = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAIAAAABAgMAAACJTJcZAAAADFBMVEX/AAAA/wAAAAAAAAAlu0AVAAAAA3RSTlP//wDXyg1BAAAACGFjVEwAAAACAAAAAPONk3AAAAAaZmNUTAAAAAAAAAACAAAAAQAAAAAAAAAAAGQD6AAAECjlZAAAAApJREFUeJxjYAAAAAIAAUivpHEAAAAaZmNUTAAAAAEAAAACAAAAAQAAAAAAAAAAAMgD6AAAPox9JwAAAA5mZEFUAAAAAnicY5gAAACSAJFgPbCrAAAAAElFTkSuQmCC");
            var animation = await AnimatedPngFrames.DecodeAsync(png, 2);
            Assert.NotNull(animation);
            Assert.Equal((2, 1, 2), (animation!.Width, animation.Height, animation.Frames.Count));
            Pixel(animation, 0, 0, 0, [0, 0, 255, 255]);
            Pixel(animation, 1, 0, 0, [0, 0, 0, 0]);
            Pixel(animation, 1, 1, 0, [0, 255, 0, 255]);
        });

        Test("实际 WinRT APNG：边长和留存像素预算限制高分辨率多帧", async () =>
        {
            var wide = Png(1200, 8, 0, [new(0, 0, 1200, 8, Red), new(1, 1, 1, 1, Blue)]);
            var scaled = await AnimatedPngFrames.DecodeAsync(wide, 2400);
            Assert.NotNull(scaled);
            Assert.Equal(768, scaled!.Width);
            Assert.True(scaled.Height is > 0 and < 8);

            var frames = new FixtureFrame[20];
            frames[0] = new(0, 0, 900, 900, Red);
            for (var i = 1; i < frames.Length; i++)
                frames[i] = new(i, i, 1, 1, Green);
            var png = Png(900, 900, 0, frames);
            var animation = await AnimatedPngFrames.DecodeAsync(png, 900);
            Assert.NotNull(animation);
            Assert.Equal(frames.Length, animation!.Frames.Count);
            Assert.Equal(animation.Width, animation.Height);
            Assert.True(animation.Width is > 0 and < 768, "多帧留存上限必须比单边上限更严格");
            Assert.True(animation.Frames.All(frame => frame.Pixels.Length == animation.Width * animation.Height * 4));
            Assert.True(animation.Frames.Sum(frame => (long)frame.Pixels.Length) <= 24 * 1024 * 1024);

            var visibleCard = await AnimatedPngFrames.DecodeAsync(png, 900, 4 * 1024 * 1024, default);
            Assert.NotNull(visibleCard);
            Assert.True(visibleCard!.Width < animation.Width);
            Assert.True(visibleCard.Frames.Sum(frame => (long)frame.Pixels.Length) <= 4 * 1024 * 1024);
            Assert.Null(await AnimatedPngFrames.DecodeAsync(png, 900, frames.Length * 4 - 1, default));

            var huge = Png(4097, 1, 0, [new(0, 0, 4097, 1, Red), new(1, 0, 1, 1, Blue)]);
            Assert.Null(await AnimatedPngFrames.DecodeAsync(huge, 100));
            var excessive = Png(1, 1, 0, Enumerable.Repeat(new FixtureFrame(0, 0, 1, 1, Red), 241).ToArray());
            Assert.Null(await AnimatedPngFrames.DecodeAsync(excessive, 1));
        });
    }

    private static void Pixel(AnimatedPngFrames animation, int frame, int x, int y, ReadOnlySpan<byte> expected)
    {
        var pixels = animation.Frames[frame].Pixels;
        var actual = pixels.AsSpan((y * animation.Width + x) * 4, 4);
        Assert.True(actual.SequenceEqual(expected),
            $"第 {frame} 帧 ({x},{y}) BGRA 应为 {Convert.ToHexString(expected)}，实测 {Convert.ToHexString(actual)}");
    }

    private static byte[] Png(int width, int height, int plays, FixtureFrame[] frames,
        uint? defaultColor = null, bool mixFirstFrameData = false)
    {
        using var file = new MemoryStream();
        file.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        WriteInt(header, 0, width);
        WriteInt(header, 4, height);
        header[8] = 8;
        header[9] = 6; // RGBA8.
        ImageData.Chunk(file, "IHDR", header);
        var control = new byte[8];
        WriteInt(control, 0, frames.Length);
        WriteInt(control, 4, plays);
        ImageData.Chunk(file, "acTL", control);
        if (defaultColor is { } color)
            ImageData.Chunk(file, "IDAT", Compress(width, height, color));

        var sequence = 0;
        for (var i = 0; i < frames.Length; i++)
        {
            var frame = frames[i];
            var metadata = new byte[26];
            WriteInt(metadata, 0, sequence++);
            WriteInt(metadata, 4, frame.Width);
            WriteInt(metadata, 8, frame.Height);
            WriteInt(metadata, 12, frame.X);
            WriteInt(metadata, 16, frame.Y);
            BinaryPrimitives.WriteUInt16BigEndian(metadata.AsSpan(20), frame.DelayNumerator);
            BinaryPrimitives.WriteUInt16BigEndian(metadata.AsSpan(22), frame.DelayDenominator);
            metadata[24] = frame.Dispose;
            metadata[25] = frame.Blend;
            ImageData.Chunk(file, "fcTL", metadata);
            var compressed = Compress(frame.Width, frame.Height, frame.Color);
            if (i == 0 && defaultColor is null)
            {
                if (mixFirstFrameData)
                {
                    ImageData.Chunk(file, "IDAT", compressed[..^1]);
                    WriteFrameData(compressed.AsSpan(compressed.Length - 1));
                }
                else
                {
                    ImageData.Chunk(file, "IDAT", compressed);
                }
                continue;
            }

            // Use two fdAT chunks per frame to exercise reassembly of a single compressed stream.
            var middle = compressed.Length / 2;
            WriteFrameData(compressed.AsSpan(0, middle));
            WriteFrameData(compressed.AsSpan(middle));
        }
        ImageData.Chunk(file, "IEND", []);
        return file.ToArray();

        void WriteFrameData(ReadOnlySpan<byte> bytes)
        {
            var data = new byte[4 + bytes.Length];
            WriteInt(data, 0, sequence++);
            bytes.CopyTo(data.AsSpan(4));
            ImageData.Chunk(file, "fdAT", data);
        }
    }

    private static byte[] Compress(int width, int height, uint rgba)
    {
        var row = new byte[width * 4 + 1];
        for (var x = 1; x < row.Length; x += 4)
            BinaryPrimitives.WriteUInt32BigEndian(row.AsSpan(x), rgba);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            for (var y = 0; y < height; y++) zlib.Write(row);
        }
        return compressed.ToArray();
    }

    private static void WriteInt(byte[] data, int offset, int value) =>
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(offset), value);

    private readonly record struct FixtureFrame(int X, int Y, int Width, int Height, uint Color,
        ushort DelayNumerator = 1, ushort DelayDenominator = 10, byte Dispose = 0, byte Blend = 0);
}
