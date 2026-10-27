using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Momoka.Infrastructure;
using Momoka.Playback;
using Momoka.Shell.Interop;
using Momoka.Tests;
using Windows.Graphics;

namespace Momoka.Shell.Windowing;

// Only unselected owners/endpoints are seams. Fullscreen, intent, geometry selection and region
// algorithms come exclusively from FullscreenProduction.g.cs, compiled unchanged from production.
internal sealed partial class HostWindow
{
    private const string Category = "窗口";
    private bool _playbackTitleBar;
    private bool _trayAheadOfFullscreen;
    private bool _restoringBrowse;
    private bool _band;
    private bool _topMost;
    private bool _aside;
    private IntPtr _judged;
    private (WindowBounds Bounds, bool Maximized) _browse;
    private Action<object>? _repaint;
    private readonly NativeState _state;

    internal HostWindow(NativeState state, AppWindow? appWindow, InputNonClientPointerSource? nonClient,
        bool created = true)
    {
        _state = state;
        Native.State = state;
        _appWindow = appWindow;
        _nonClient = nonClient;
        Handle = created ? state.Handle : IntPtr.Zero;
        state.Observe = () => (Fullscreen, FreeSizing);
        state.ObservedTitleBar = appWindow?.TitleBar;
    }

    public IntPtr Handle { get; private set; }
    internal bool TrayAhead { get => _trayAheadOfFullscreen; set => _trayAheadOfFullscreen = value; }
    internal bool Band => _band;
    internal IntPtr Judged { get => _judged; set => _judged = value; }
    internal int TransitionDepth => _clientTransitionDepth;
    internal (NativeRect Bounds, IntPtr Style, bool Maximized)? Saved => _restore;
    internal int HoleCount => _holes.Count;
    internal (double X, double Y, double Width, double Height)[] StoredHoles => [.. _holes];
    internal void SetCreated(bool created) => Handle = created ? _state.Handle : IntPtr.Zero;
    internal void SetAppWindow(AppWindow? value)
    {
        _appWindow = value;
        _state.ObservedTitleBar = value?.TitleBar;
    }
    internal void SetNonClient(InputNonClientPointerSource? value) => _nonClient = value;
    internal void RefreshRegions() => UpdateTitleBarRegions();
    internal void Configure() => ConfigureTitleBar();
    internal void EnterDirect() => EnterFullscreen();
    internal void ApplyMode() => ApplyTitleBarMode();
    internal void SetBrowseRestore(WindowBounds bounds, bool restoring)
    {
        _browse = (bounds, false);
        _restoringBrowse = restoring;
    }
    internal void SetZOrderIntent(bool topMost, bool aside)
    {
        _topMost = topMost;
        _aside = aside;
    }
    internal static (RectInt32 Minimise, RectInt32 Maximise, RectInt32 Close) Buttons(
        int left, int reserved, int height) => SystemButtonRects(left, reserved, height);
    internal RectInt32[] PixelHoles(int dpi, int right, int height) => HolePixels(dpi, right, height);
    internal static RectInt32[] Caption(RectInt32[] holes, int right, int height) => CaptionAround(holes, right, height);

    // Trace the production ChangeClientRect bracket, but do not fake WinUI layout/ClientToScreen.
    private NativeRect ClientRectOnScreen()
    {
        _state.Call("ClientRectOnScreen", read: true);
        return _state.Bounds;
    }
    private void SetFrameTransitions(bool enabled)
    {
        _state.Call(enabled ? "EnableFrameTransitions" : "DisableFrameTransitions");
        _state.FrameTransitionsEnabled = enabled;
    }
    private void PublishClientRect(NativeRect before)
    {
        _state.Call("PublishClientRect");
        _state.PublishedBefore.Add(before);
        _state.PublishedAfter.Add(_state.Bounds);
    }
    private void HideTrayForFullscreen()
    {
        _state.Call("HideTrayForFullscreen", write: true);
        _state.TrayHidden = true;
    }
    private void RestoreTray()
    {
        _state.Call("RestoreTray", write: true);
        _state.TrayHidden = false;
    }
    private void WatchTrayOrder()
    {
        _state.Call("WatchTrayOrder", write: true);
        _state.TrayWatching = true;
    }
    private void StopWatchingTrayOrder()
    {
        _state.Call("StopWatchingTrayOrder", write: true);
        _state.TrayWatching = false;
    }
    private void ApplyWindowsChrome() => _state.Call("ApplyWindowsChrome", write: true);
    private void FitToPicture() => _state.Call("FitToPicture", write: true);
    private void PaintCaption(AppWindowTitleBar bar) => _state.Call("PaintCaption", write: true);
    internal void UnsubscribeTheme()
    {
        if (_repaint is not null) ThemeHost.Changed -= _repaint;
        _repaint = null;
    }
}
