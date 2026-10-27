using Microsoft.UI.Windowing;
using Momoka.Shell.Interop;

namespace Momoka.Tests;

// Observable boundary values, not a fullscreen or window-frame implementation.
internal sealed record BoundaryCall(string Name, bool Fullscreen, bool FreeSizing, long Style,
    NativeRect Bounds, TitleBarHeightOption? HeightOption);

internal sealed class NativeState
{
    internal IntPtr Handle { get; } = new(123);
    internal IntPtr MonitorHandle { get; } = new(456);
    internal NativeRect Bounds { get; set; } = new() { Left = -1100, Top = 90, Right = 180, Bottom = 810 };
    internal NativeRect RestoredBounds { get; set; } = new() { Left = -900, Top = 110, Right = 120, Bottom = 790 };
    internal NativeRect MaximizedBounds { get; set; } = new() { Left = -1920, Top = 0, Right = 0, Bottom = 1040 };
    internal NativeRect Client { get; set; } = new() { Left = 0, Top = 0, Right = 1280, Bottom = 720 };
    internal NativeRect Monitor { get; set; } = new() { Left = -1920, Top = -200, Right = 0, Bottom = 880 };
    internal NativeRect Work { get; set; } = new() { Left = -1920, Top = -200, Right = 0, Bottom = 840 };
    internal long Style { get; set; } = unchecked((long)0x912345678BCF1357UL);
    internal long RestoredStyle { get; set; } = unchecked((long)0x912345678BCF1357UL);
    internal long MaximizedStyle { get; set; } = unchecked((long)0x912345678BCF1357UL);
    internal uint Dpi { get; set; } = 96;
    internal bool MonitorAvailable { get; set; } = true;
    internal bool Maximized { get; set; }
    internal bool TrayHidden { get; set; }
    internal bool TrayWatching { get; set; }
    internal bool MarkedFullscreen { get; set; }
    internal bool BandTimerRunning { get; set; }
    internal bool FrameTransitionsEnabled { get; set; } = true;
    internal string? FailAt { get; set; }
    internal Func<(bool Fullscreen, bool FreeSizing)> Observe { get; set; } = () => (false, false);
    internal AppWindowTitleBar? ObservedTitleBar { get; set; }
    internal AppWindow? ConfiguredAppWindow { get; set; }
    internal Microsoft.UI.Input.InputNonClientPointerSource? ConfiguredNonClient { get; set; }
    internal List<BoundaryCall> Calls { get; } = [];
    internal List<PositionWrite> PositionWrites { get; } = [];
    internal List<long> StyleWrites { get; } = [];
    internal List<TitleBarHeightOption> HeightWrites { get; } = [];
    internal List<(bool Border, bool TitleBar)> PresenterWrites { get; } = [];
    internal List<RegionWrite> RegionWrites { get; } = [];
    internal List<NativeRect> PublishedBefore { get; } = [];
    internal List<NativeRect> PublishedAfter { get; } = [];
    internal int Reads { get; private set; }
    internal int Writes { get; private set; }

    internal void Call(string name, bool write = false, bool read = false)
    {
        var (fullscreen, freeSizing) = Observe();
        Calls.Add(new BoundaryCall(name, fullscreen, freeSizing, Style, Bounds,
            ObservedTitleBar?.PreferredHeightOption));
        if (FailAt == name) throw new BoundaryFailure(name);
        if (write) Writes++;
        if (read) Reads++;
    }

    internal void ClearTrace()
    {
        Calls.Clear();
        PositionWrites.Clear();
        StyleWrites.Clear();
        HeightWrites.Clear();
        PresenterWrites.Clear();
        RegionWrites.Clear();
        PublishedBefore.Clear();
        PublishedAfter.Clear();
        Reads = 0;
        Writes = 0;
    }
}

internal sealed class BoundaryFailure(string name) : Exception(name);
internal sealed record PositionWrite(IntPtr InsertAfter, int X, int Y, int Width, int Height, uint Flags);
internal sealed record RegionWrite(Microsoft.UI.Input.NonClientRegionKind Kind, Windows.Graphics.RectInt32[] Rects);
