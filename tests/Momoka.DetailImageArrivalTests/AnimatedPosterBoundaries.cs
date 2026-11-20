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

        internal static Task<AnimatedPngFrames?> DecodeAsync(byte[] bytes, int requestedPixelWidth, int byteBudget,
            CancellationToken token) => Decode(bytes, token);
    }
}
