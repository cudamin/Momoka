// Boundary values only. These types never create a window, load native code, move input or contact a server.
namespace Momoka.Shell.Windowing
{
    internal sealed class HostWindow
    {
        internal IntPtr Handle { get; } = new(1);
        internal bool Fullscreen { get; set; }
    }
}

namespace Momoka.Shell.Interop
{
    internal struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
        internal readonly int Width => Right - Left;
        internal readonly int Height => Bottom - Top;
    }

    internal static class Native
    {
        internal static NativeRect Bounds { get; set; }
        internal static int Reads { get; set; }

        internal static void GetWindowRect(IntPtr handle, out NativeRect bounds)
        {
            if (handle != new IntPtr(1)) throw new InvalidOperationException("Unexpected fixture window");
            Reads++;
            bounds = Bounds;
        }
    }
}

namespace Momoka.Shell.Views
{
    // The test controls delivery of the real probe's existing observable completion signals.
    // This is deliberately not a replacement implementation of EnterPlayer/ChangeWindowAsync.
    internal sealed class PlayerPage(Momoka.Shell.Windowing.HostWindow window)
    {
        internal Action? OnBegin { get; set; }
        internal Action? OnEnd { get; set; }
        internal int Begins { get; private set; }
        internal int Ends { get; private set; }
        internal bool FullscreenRequested { get; private set; }
        internal bool PlayerVisible { get; set; }
        internal bool Active { get; set; }
        internal bool Animating { get; set; }
        internal bool Identity { get; set; } = true;
        internal bool StartupHandoverPending { get; set; }
        internal Task WindowChange { get; set; } = Task.CompletedTask;
        internal (bool Active, bool Animating, bool Identity, bool CoverHidden, bool SurfaceReady) MotionProbeState =>
            (Active, Animating, Identity, false, false);

        internal void BeginMotionProbe(bool fullscreen)
        {
            Begins++;
            FullscreenRequested = fullscreen;
            if (OnBegin is { } begin) begin();
            else
            {
                Active = PlayerVisible = true;
                window.Fullscreen = fullscreen;
            }
        }

        internal void EndMotionProbe()
        {
            Ends++;
            if (OnEnd is { } end) end();
            else CompleteExit();
        }

        internal void CompleteExit()
        {
            Active = PlayerVisible = Animating = StartupHandoverPending = false;
            window.Fullscreen = false;
        }
    }
}

namespace Momoka.Shell
{
    internal sealed class ProbeTimer
    {
        internal bool Started { get; private set; }
        internal bool Stopped { get; private set; }
        internal void Start() => Started = true;
        internal void Stop() => Stopped = true;
    }
}

namespace Momoka.Diagnostics
{
    internal static class Log
    {
        internal static void Info(string category, string message) { }
    }
}
