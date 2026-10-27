using Momoka.Tests;

namespace Momoka.Shell.Interop;

// Signatures/constants are the production call sites' boundary. No user32/DWM/COM call is made.
// Style writes store exactly the caller's IntPtr: this seam cannot repair a lossy 32-bit mask.
internal static partial class Native
{
    internal static NativeState State { get; set; } = new();

    private static void RequireWindow(IntPtr handle)
    {
        if (handle != State.Handle) throw new InvalidOperationException("Unexpected fixture HWND");
    }

    internal static IntPtr MonitorFromWindow(IntPtr handle, uint flags)
    {
        RequireWindow(handle);
        Assert.Equal(MonitorDefaultToNearest, flags);
        State.Call("MonitorFromWindow", read: true);
        return State.MonitorHandle;
    }

    internal static bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info)
    {
        Assert.Equal(State.MonitorHandle, monitor);
        Assert.Equal((uint)System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>(), info.Size);
        State.Call("GetMonitorInfo", read: true);
        if (!State.MonitorAvailable) return false;
        info.Monitor = State.Monitor;
        info.Work = State.Work;
        return true;
    }

    internal static bool IsZoomed(IntPtr handle)
    {
        RequireWindow(handle);
        State.Call("IsZoomed", read: true);
        return State.Maximized;
    }

    internal static bool ShowWindow(IntPtr handle, int command)
    {
        RequireWindow(handle);
        State.Call(command == SwRestore ? "ShowRestore" : "ShowMaximize", write: true);
        if (command == SwRestore)
        {
            State.Maximized = false;
            State.Bounds = State.RestoredBounds;
            State.Style = State.RestoredStyle;
        }
        else if (command == SwMaximize)
        {
            State.Maximized = true;
            State.Bounds = State.MaximizedBounds;
            State.Style = State.MaximizedStyle;
        }
        else throw new InvalidOperationException("Unexpected fixture ShowWindow command");
        return true;
    }

    internal static IntPtr GetWindowLongPtr(IntPtr handle, int index)
    {
        RequireWindow(handle);
        Assert.Equal(GwlStyle, index);
        State.Call("GetWindowLongPtr", read: true);
        return new IntPtr(State.Style);
    }

    internal static IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr style)
    {
        RequireWindow(handle);
        Assert.Equal(GwlStyle, index);
        State.Call("SetWindowLongPtr", write: true);
        var previous = State.Style;
        State.Style = style.ToInt64();
        State.StyleWrites.Add(State.Style);
        return new IntPtr(previous);
    }

    internal static bool GetWindowRect(IntPtr handle, out NativeRect bounds)
    {
        RequireWindow(handle);
        State.Call("GetWindowRect", read: true);
        bounds = State.Bounds;
        return true;
    }

    internal static bool GetClientRect(IntPtr handle, out NativeRect bounds)
    {
        RequireWindow(handle);
        State.Call("GetClientRect", read: true);
        bounds = State.Client;
        return true;
    }

    internal static uint GetDpiForWindow(IntPtr handle)
    {
        RequireWindow(handle);
        State.Call("GetDpiForWindow", read: true);
        return State.Dpi;
    }

    internal static bool SetWindowPos(IntPtr handle, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags)
    {
        RequireWindow(handle);
        State.Call("SetWindowPos", write: true);
        State.PositionWrites.Add(new PositionWrite(insertAfter, x, y, width, height, flags));
        // Treat the supplied geometry as the external window system's observable value.
        // Client frame calculation is deliberately NOT synthesized from styles or title-bar options.
        State.Bounds = new NativeRect { Left = x, Top = y, Right = x + width, Bottom = y + height };
        return true;
    }

    internal static void MarkFullscreen(IntPtr handle, bool value)
    {
        RequireWindow(handle);
        State.Call(value ? "MarkFullscreen" : "UnmarkFullscreen", write: true);
        State.MarkedFullscreen = value;
    }

    internal static nuint SetTimer(IntPtr handle, nuint id, uint interval, IntPtr callback)
    {
        RequireWindow(handle);
        Assert.Equal((nuint)1, id);
        Assert.Equal(250U, interval);
        Assert.Equal(IntPtr.Zero, callback);
        State.Call("SetTimer", write: true);
        State.BandTimerRunning = true;
        return id;
    }

    internal static bool KillTimer(IntPtr handle, nuint id)
    {
        RequireWindow(handle);
        Assert.Equal((nuint)1, id);
        State.Call("KillTimer", write: true);
        State.BandTimerRunning = false;
        return true;
    }
}
