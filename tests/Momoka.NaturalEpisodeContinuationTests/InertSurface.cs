using Momoka.Playback;

namespace Momoka.Tests;

// Only a composition endpoint identity; this test never attaches or renders a frame.
internal sealed class InertSurface : IVideoSurface
{
    public (int Width, int Height) Size => (640, 360);
    public event Action? GeometryChanged { add { } remove { } }
    public void AttachSwapChain(IntPtr swapChain) => throw new AssertionException("forbidden composition/native call");
}
