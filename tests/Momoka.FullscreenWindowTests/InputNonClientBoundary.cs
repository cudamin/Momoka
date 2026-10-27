using Momoka.Shell.Interop;
using Momoka.Tests;
using Windows.Graphics;

namespace Microsoft.UI.Input;

internal enum NonClientRegionKind { Passthrough, Caption, Minimize, Maximize, Close }

internal sealed class InputNonClientPointerSource(NativeState state)
{
    private readonly Dictionary<NonClientRegionKind, RectInt32[]> _regions = [];
    internal IReadOnlyDictionary<NonClientRegionKind, RectInt32[]> Regions => _regions;

    internal void SetRegionRects(NonClientRegionKind kind, RectInt32[] rects)
    {
        state.Call("SetRegionRects:" + kind, write: true);
        // Never substitute [Empty] for []: recording the caller's exact arrays is the regression seam.
        var snapshot = (RectInt32[])rects.Clone();
        _regions[kind] = snapshot;
        state.RegionWrites.Add(new RegionWrite(kind, snapshot));
    }

    internal static InputNonClientPointerSource? GetForWindowId(IntPtr fixtureId)
    {
        Assert.Equal(Native.State.Handle, fixtureId);
        Native.State.Call("GetForWindowId", read: true);
        return Native.State.ConfiguredNonClient;
    }
}
