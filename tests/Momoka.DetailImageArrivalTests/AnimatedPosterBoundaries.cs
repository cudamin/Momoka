namespace Windows.Foundation
{
    public readonly record struct Rect(double X, double Y, double Width, double Height);
}

namespace Microsoft.UI.Xaml
{
    public sealed class FixturePixelBuffer(int length)
    {
        public byte[] Bytes { get; } = new byte[length];
    }
}

namespace System.Runtime.InteropServices.WindowsRuntime
{
    public static class PixelBufferFixtureExtensions
    {
        public static Stream AsStream(this Microsoft.UI.Xaml.FixturePixelBuffer buffer) => new MemoryStream(buffer.Bytes);
    }
}

namespace Momoka.Shell.Media
{
    internal sealed record AnimatedPngFrame(byte[] Pixels, TimeSpan Delay);

    internal sealed record AnimatedPngFrames(int Width, int Height, uint Plays, IReadOnlyList<AnimatedPngFrame> Frames)
    {
        internal static Func<byte[], CancellationToken, Task<AnimatedPngFrames?>> Decode { get; set; } =
            (_, _) => Task.FromResult<AnimatedPngFrames?>(null);

        internal int FrameCount => Frames.Count;

        internal static Task<AnimatedPngFrames?> OpenAsync(byte[] bytes, CancellationToken token) => Decode(bytes, token);

        internal Func<int, CancellationToken, Task<AnimatedPngFrame?>>? Read { get; init; }

        internal Task<AnimatedPngFrame?> ReadFrameAsync(int index, CancellationToken token) =>
            Read?.Invoke(index, token) ?? Task.FromResult<AnimatedPngFrame?>(Frames[index]);
    }
}
