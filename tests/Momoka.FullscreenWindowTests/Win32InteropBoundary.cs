using Momoka.Shell.Interop;
using Momoka.Tests;

namespace Microsoft.UI;

internal static class Win32Interop
{
    // An opaque fixture token stands in for WindowId; no actual WindowId API is declared or called.
    internal static IntPtr GetWindowIdFromWindow(IntPtr handle)
    {
        Assert.Equal(Native.State.Handle, handle);
        Native.State.Call("GetWindowIdFromWindow", read: true);
        return handle;
    }
}
