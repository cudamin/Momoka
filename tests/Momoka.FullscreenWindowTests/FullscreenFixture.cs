using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Momoka.Shell.Interop;
using Momoka.Shell.Windowing;
using Windows.Graphics;

namespace Momoka.Tests;

internal enum TitleBarBoundary { Custom, NoAppWindow, NoTitleBar, NotExtended, NoNonClient }

internal sealed class FullscreenFixture : IDisposable
{
    internal FullscreenFixture(TitleBarBoundary boundary = TitleBarBoundary.Custom, bool created = true)
    {
        State = new NativeState();
        Bar = boundary is TitleBarBoundary.NoAppWindow or TitleBarBoundary.NoTitleBar ? null : new AppWindowTitleBar(State);
        // Only the custom-title-bar boundary has usable presenter chrome. The others stand for windows where
        // the fullscreen path still has to fall back to stripping style bits.
        var presenter = boundary == TitleBarBoundary.Custom ? new OverlappedPresenter(State) : null;
        AppWindow = boundary == TitleBarBoundary.NoAppWindow ? null : new AppWindow(Bar, presenter);
        Regions = new InputNonClientPointerSource(State);
        if (boundary == TitleBarBoundary.NotExtended) Bar!.ExtendsContentIntoTitleBar = false;
        Window = new HostWindow(State, AppWindow, boundary == TitleBarBoundary.NoNonClient ? null : Regions, created);
        State.ConfiguredAppWindow = AppWindow;
        State.ConfiguredNonClient = boundary == TitleBarBoundary.NoNonClient ? null : Regions;
        State.ClearTrace();
    }

    internal NativeState State { get; }
    internal AppWindowTitleBar? Bar { get; }
    internal AppWindow? AppWindow { get; }
    internal InputNonClientPointerSource Regions { get; }
    internal HostWindow Window { get; }

    internal void SetStyle(long value)
    {
        State.Style = value;
        State.RestoredStyle = value;
        State.MaximizedStyle = value;
    }

    internal BoundaryCall Call(string name) => State.Calls.Single(call => call.Name == name);
    internal int Index(string name) => State.Calls.FindIndex(call => call.Name == name);
    internal int LastIndex(string name) => State.Calls.FindLastIndex(call => call.Name == name);
    internal void Before(string first, string second)
    {
        var firstIndex = Index(first);
        var secondIndex = Index(second);
        Assert.True(firstIndex >= 0 && secondIndex > firstIndex, $"{first} 必须在 {second} 之前");
    }

    internal void ExpectRegion(NonClientRegionKind kind, params RectInt32[] expected)
    {
        Assert.True(Regions.Regions.TryGetValue(kind, out var actual), "必须直接声明 " + kind);
        Assert.Equal(expected.Length, actual!.Length, kind + "不能被边界悄悄补空或删空");
        for (var index = 0; index < expected.Length; index++)
            Assert.Equal(expected[index], actual[index], $"{kind}[{index}]");
    }

    internal void ExpectContentRegions(RectInt32 strip)
    {
        var empty = new RectInt32(0, 0, 0, 0);
        ExpectRegion(NonClientRegionKind.Passthrough, strip);
        ExpectRegion(NonClientRegionKind.Caption, empty);
        ExpectRegion(NonClientRegionKind.Minimize, empty);
        ExpectRegion(NonClientRegionKind.Maximize, empty);
        ExpectRegion(NonClientRegionKind.Close, empty);
        Assert.Equal(5, State.RegionWrites.Count);
        Assert.Equal(5, State.RegionWrites.Select(write => write.Kind).Distinct().Count());
        Assert.True(State.RegionWrites.All(write => write.Rects.Length == 1),
            "隐藏区域仍直接写 [Empty]，不可退回 [] 或 Clear");
    }

    internal static void EqualRect(NativeRect expected, NativeRect actual, string message = "")
    {
        Assert.Equal((expected.Left, expected.Top, expected.Right, expected.Bottom),
            (actual.Left, actual.Top, actual.Right, actual.Bottom), message);
    }

    public void Dispose() => Window.UnsubscribeTheme();
}
