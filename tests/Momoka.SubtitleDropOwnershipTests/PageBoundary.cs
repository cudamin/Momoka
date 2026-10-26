using System.ComponentModel;
using Momoka.Playback;
using Momoka.Shell.Interop;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Windowing;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.Views;

// Attach/Detach/Shutdown and the drop handlers come verbatim from production.
// All neighboring rendering/event endpoints are inert; none calls App, native, IO or playback.
public sealed partial class PlayerPage
{
    private readonly InertLifetime _ticker = new();
    private readonly InertLifetime _videoTarget = new();
    internal InertLifetime Bindings { get; } = new();
    internal bool StageActive => _onStage;
    internal string CursorWakeReason => _woke;
    internal void Drop(DragEventArgs args) => OnPictureDrop(this, args);
    internal void DragOver(DragEventArgs args) => OnPictureDragOver(this, args);
    private static ShaderSurface SurfaceFor(HostWindow window) => default;
    private Task ReturnToBrowseBeforeStopAsync() => Task.CompletedTask;
    private void EnterPlayer() => _onStage = true;
    private void LeavePlayer() => _onStage = false;
    private void StopPlayerMotion() { }
    private void CompletePlayerExit() { }
    private void StopSkipCountdown() { }
    private void CloseDanmakuPanel() { }
    private void StopWatchdog() { }
    private void ArrangeTransport() { }
    private void ShowCoverPlate() { }
    private void OnPlaybackStarted() { }
    private void OnChaptersChanged() { }
    private void OnStatusApplied(PlayerStatus status) { }
    private void OnPictureAspectChanged(double aspect) { }
    private void OnSourceAspectChanged(double aspect) { }
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args) { }
    private void OnSystemCursorChanged() { }
    private void OnGeometryChanged() { }
    private void OnClientRectTransition(NativeRect from, NativeRect to) { }
    private void OnClientSizeChanged(NativeRect client) { }
    private void OnInteractiveResizeChanged(bool active) { }
    private void CancelWindowChange() { }
    private void EndWindowDrag() { }
    private void DropTapHold() { }
    private void SetCursorHidden(bool hidden) => _cursorHidden = hidden;
}

public sealed class ShellPage;

internal sealed class InertLifetime : IDisposable
{
    public void Stop() { }
    public void Update() { }
    public void Dispose() { }
}
