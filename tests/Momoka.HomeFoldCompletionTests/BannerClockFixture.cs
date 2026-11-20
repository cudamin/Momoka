using Microsoft.UI.Xaml;
using Momoka.Emby;

namespace Momoka.Shell.Views;

// The production Apply/SyncTimer methods own list retention and clock restarts. Image rendering,
// transitions and DispatcherTimer are inert boundaries; no window or real timer thread is created.
internal sealed partial class BannerClockFixture
{
    private List<BannerSlide> _slides = [];
    private int _index;
    private bool _second;
    private bool _active = true;
    private CarouselLayout _layout = CarouselLayout.Full;
    // No host means no measured width; Apply then takes the status-only branch.
    private double ActualWidth => 0;
    private long _cycleStarted;
    private readonly BannerCycleClock _cycleClock = new();
    private readonly TimeSpan _dwell = HomeCarousel.Dwell;
    private bool _heroHover;
    private readonly DispatcherQueueTimer _timer = new();
    private readonly Layer LayerA = new();
    private readonly Layer LayerB = new();
    private readonly Layer LogoImage = new();
    private readonly Layer LogoRow = new();
    private readonly ShimmerBoundary PictureShimmer = new();
    private readonly Scale CountdownScale = new();
    private Visibility Visibility { get; set; }
    private BannerSlide? Current => _slides.ElementAtOrDefault(_index);
    private event EventHandler<BannerSlide?>? SlideChanged;
    internal bool Running => _timer.Running;
    internal int Starts => _timer.Starts;
    internal int Stops => _timer.Stops;
    internal bool ShimmerActive => PictureShimmer.IsActive;
    internal int ShimmerStops => PictureShimmer.Stops;
    internal double Progress => CountdownScale.ScaleX;
    internal double PaintedProgress { get; private set; }
    internal int Advances { get; private set; }
    internal int CurrentIndex => _index;
    internal bool Paused => _cycleClock.IsPaused;
    internal void SetNow(long milliseconds) => Environment.TickCount64 = milliseconds;
    internal void Tick() => OnTick(_timer, EventArgs.Empty);
    internal void SetLoadingPlaceholder() => PictureShimmer.IsActive = true;
    internal void SetActive(bool value) { _active = value; SyncTimer(); }
    internal void SetHeroHover(bool value) { _heroHover = value; UpdateCyclePause(); }
    internal void SetStore(bool value) { _layout = value ? CarouselLayout.Store : CarouselLayout.Full; UpdateCyclePause(); }
    private void StopMotions() { }
    private void Unwatch() { }
    private void SyncArrows() { }
    // Layout and per-slide status belong to the XAML host; the fixture only proves the list and clock rules.
    private void Resize(double width) { }
    private void SyncSlideStatus() { }
    private void PaintFadeInk() { }
    private void PaintCountdown(double progress) => PaintedProgress = progress;
    // Actual OnTick decides whether to advance; navigation/rendering is inert after that decision.
    private void Advance(int delta)
    {
        Advances++;
        _index = HomeCarousel.Step(_index, delta, _slides.Count);
        SyncTimer();
    }
    private void Show(int index, bool animate = true) { _index = index; _ = _second; }
    private void OnPictureUnavailable(object? sender, EventArgs args) { }
    private static Layer Brush(Layer layer) => layer;
    private sealed class Scale { internal double ScaleX { get; set; } }
    private sealed class Layer
    {
        internal double Opacity { get; set; }
        internal object? ImageSource { get; set; }
        internal object? Source { get; set; }
        internal Visibility Visibility { get; set; }
    }
    // The production methods are copied unchanged at build time. Only time and timer delivery use
    // controlled values in this fixture, so these tests never wait or create a timer thread.
    private sealed class DispatcherQueueTimer
    {
        internal bool Running { get; private set; }
        internal int Starts { get; private set; }
        internal int Stops { get; private set; }
        internal void Start() { Running = true; Starts++; }
        internal void Stop() { Running = false; Stops++; }
    }
    private static class Environment
    {
        internal static long TickCount64 { get; set; }
    }
}

// Only the IsActive value and stop receipt; actual Composition visuals belong to Shimmer's native probe.
internal sealed class ShimmerBoundary
{
    private bool _active;
    internal bool IsActive
    {
        get => _active;
        set
        {
            if (_active && !value) Stops++;
            _active = value;
        }
    }
    internal int Stops { get; private set; }
}

internal sealed class BannerSlide(string id)
{
    internal List<Task?> Requests { get; } = [];
    internal bool PictureRetained { get; private set; }
    internal int Releases { get; private set; }
    internal Task EnsureAsync(Task? presentation = null)
    {
        Requests.Add(presentation);
        PictureRetained = true;
        return Task.CompletedTask;
    }
    internal Microsoft.UI.Xaml.Media.Imaging.BitmapImage? Logo { get; set; }
    internal Visibility LogoVisibility => Logo is null ? Visibility.Collapsed : Visibility.Visible;
    internal EmbyItem Item { get; } = new() { Id = id, Name = id };
    internal string Signature => Item.Id;
    internal event EventHandler? PictureUnavailable;
    internal void Release()
    {
        PictureRetained = false;
        Releases++;
    }
    internal void SetGaussianBackdrop(bool value) { }
    internal void SetLeftExtension(bool value) { }
    internal void Fail() => PictureUnavailable?.Invoke(this, EventArgs.Empty);
}
