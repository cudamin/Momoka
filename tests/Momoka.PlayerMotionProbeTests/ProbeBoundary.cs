// Boundary values only. These types never create a window, load native code, move input or contact a server.
namespace Momoka.Shell.Windowing
{
    internal sealed class HostWindow
    {
        internal IntPtr Handle { get; } = new(1);
        internal bool Fullscreen { get; set; }
    }

    /// <summary>
    /// 退场观测里唯一用到覆盖层的地方是「窗口还原之后的客户区」这一读。
    /// <see cref="Bounds"/> 由夹具在停止回调里改，模拟「窗口还回浏览几何」。
    /// </summary>
    internal static class VideoFrameOverlay
    {
        internal static Momoka.Shell.Interop.NativeRect Bounds { get; set; }
        internal static Momoka.Shell.Interop.NativeRect ClientRect(IntPtr window) => Bounds;
    }
}

namespace Momoka.Shell.Interop
{
    internal struct NativePoint
    {
        internal int X;
        internal int Y;
    }

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

        internal static volatile bool OverlayVisible;
        internal static bool ForeignOverlay;
        internal static bool WrongOverlayClass;
        internal static bool OverlayReadFails;
        internal static NativeRect OverlayBounds;
        internal static TaskCompletionSource OverlayRead { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal static readonly IntPtr OverlayWindow = new(2);
        internal static IntPtr NextWindow(IntPtr parent, IntPtr after, string? className, string? title) =>
            after == IntPtr.Zero && OverlayVisible ? OverlayWindow : IntPtr.Zero;
        internal static bool IsWindowVisible(IntPtr handle) => handle == OverlayWindow && OverlayVisible;
        internal static uint GetWindowThreadProcessId(IntPtr handle, out uint process)
        {
            process = ForeignOverlay ? uint.MaxValue : (uint)Environment.ProcessId;
            return 1;
        }
        internal static int GetClassName(IntPtr handle, char[] buffer, int capacity)
        {
            var name = WrongOverlayClass ? "OtherWindow" : "MomokaVideoFrame";
            name.CopyTo(0, buffer, 0, name.Length);
            return name.Length;
        }
        internal static bool GetWindowRect(IntPtr handle, out NativeRect bounds)
        {
            if (handle == OverlayWindow)
            {
                bounds = OverlayBounds;
                OverlayRead.TrySetResult();
                return !OverlayReadFails;
            }
            if (handle != new IntPtr(1)) throw new InvalidOperationException("Unexpected fixture window");
            Reads++;
            bounds = Bounds;
            return true;
        }

        // 屏幕取样：给一个可控灰阶，ScreenLuma 读到的就是 Luma 本身。
        internal static int Luma { get; set; } = 90;
        internal static int Samples { get; private set; }
        internal static bool GetClientRect(IntPtr handle, out NativeRect client)
        {
            client = new NativeRect { Right = 800, Bottom = 450 };
            return true;
        }
        internal static bool ClientToScreen(IntPtr handle, ref NativePoint point) => true;
        internal static IntPtr GetDC(IntPtr window) => new(7);
        internal static int ReleaseDC(IntPtr window, IntPtr device) => 1;
        internal static uint GetPixel(IntPtr device, int x, int y)
        {
            Samples++;
            var value = (uint)Math.Clamp(Luma, 0, 255);
            return value | (value << 8) | (value << 16);
        }
        internal static void ResetSamples() => Samples = 0;
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

        /// <summary>退场观测要读的两样：停止前交接，以及正式路径不等它走完的那条淡出。</summary>
        internal PlayerState ViewModel { get; } = new();
        internal Task ExitFade { get; set; } = Task.CompletedTask;

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

    internal sealed class PlayerState
    {
        internal Func<Task>? PrepareStopAsync { get; set; }
    }
}

namespace Momoka.Shell
{
    /// <summary>实屏截图在夹具里只是端点：不创建窗口、不碰屏幕。</summary>
    internal static class CompositionPlaybackProbe
    {
        internal static Task SaveScreenAsync(Momoka.Shell.Windowing.HostWindow window, string file) => Task.CompletedTask;
    }

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
