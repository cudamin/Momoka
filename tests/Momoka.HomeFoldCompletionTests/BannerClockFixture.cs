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
    private long _cycleStarted;
    private readonly Clock _timer = new();
    private readonly Layer LayerA = new();
    private readonly Layer LayerB = new();
    private readonly Layer LogoImage = new();
    private readonly Layer LogoRow = new();
    private readonly Scale CountdownScale = new();
    private Visibility Visibility { get; set; }
    private BannerSlide? Current => _slides.ElementAtOrDefault(_index);
    private event EventHandler<BannerSlide?>? SlideChanged;
    internal bool Running => _timer.Running;
    internal int Starts => _timer.Starts;
    internal int Stops => _timer.Stops;
    internal void SetActive(bool value) { _active = value; SyncTimer(); }
    private void StopMotions() { }
    private void Unwatch() { }
    private void SyncArrows() { }
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
    private sealed class Clock
    {
        internal bool Running { get; private set; }
        internal int Starts { get; private set; }
        internal int Stops { get; private set; }
        internal void Start() { Running = true; Starts++; }
        internal void Stop() { Running = false; Stops++; }
    }
}

internal sealed class BannerSlide(string id)
{
    internal Microsoft.UI.Xaml.Media.Imaging.BitmapImage? Logo { get; set; }
    internal Visibility LogoVisibility => Logo is null ? Visibility.Collapsed : Visibility.Visible;
    internal EmbyItem Item { get; } = new() { Id = id, Name = id };
    internal string Signature => Item.Id;
    internal event EventHandler? PictureUnavailable;
    internal void Release() { }
    internal void SetGaussianBackdrop(bool value) { }
    internal void Fail() => PictureUnavailable?.Invoke(this, EventArgs.Empty);
}
