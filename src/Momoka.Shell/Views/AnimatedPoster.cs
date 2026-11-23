using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Shell.Media;
using Windows.UI.ViewManagement;

namespace Momoka.Shell.Views;

/// <summary>Supplies APNG frames to an existing Image without changing its card binding.</summary>
internal sealed class AnimatedPoster
{
    private static readonly SemaphoreSlim ActiveSlots = new(24);
    private static readonly SemaphoreSlim PreviewSlots = new(2);
    private static readonly AnimationSurfaceBudget CardSurfaces = new(192L * 1024 * 1024);
    private static readonly AnimationSurfaceBudget PreviewSurfaces = new(128L * 1024 * 1024);
    private static readonly SemaphoreSlim DecodeSlots = new(2);
    private static readonly TimeSpan MinimumFrameDelay = TimeSpan.FromMilliseconds(33);
    private static readonly UISettings SystemSettings = new();
    private readonly Image _image;
    private readonly bool _preview;
    private IDisposable? _heldSurface;
    private BitmapImage? _source;
    private WriteableBitmap? _surface;
    private XamlRoot? _root;
    private CancellationTokenSource? _playback;
    private bool _inViewport;
    private bool _writing;
    private bool _watching;

    internal AnimatedPoster(Image image, ImageArrivalFade? arrival = null, bool preview = false)
    {
        _image = image;
        _preview = preview;
        if (arrival is not null) arrival.IsUpdatingAnimatedFrame = () => _writing;
        image.RegisterPropertyChangedCallback(Image.SourceProperty, OnSourceChanged);
        image.EffectiveViewportChanged += OnViewportChanged;
        image.Loaded += OnLoaded;
        image.Unloaded += OnUnloaded;
        UpdateSource();
    }

    internal ImageSource? OriginalSource => _source ?? _image.Source;

    private void OnSourceChanged(DependencyObject sender, DependencyProperty property)
    {
        if (!_writing) UpdateSource();
    }

    private void UpdateSource()
    {
        Stop();
        _source = _image.Source is BitmapImage source && PosterCache.TryGetAnimation(source, out _) ? source : null;
        Refresh();
    }

    private void OnViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs args)
    {
        var viewport = args.EffectiveViewport;
        _inViewport = viewport.Width > 0 && viewport.Height > 0
            && _image.ActualWidth > 0 && _image.ActualHeight > 0
            && viewport.X < _image.ActualWidth && viewport.Y < _image.ActualHeight
            && viewport.X + viewport.Width > 0 && viewport.Y + viewport.Height > 0;
        Refresh();
    }

    private void OnLoaded(object sender, RoutedEventArgs args) => Refresh();

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (_image.XamlRoot is not null) return; // The repeater can report an unload while still on screen.
        _inViewport = false;
        Refresh();
    }

    private void Refresh()
    {
        // A minimized host can become visible again without changing the effective viewport.
        var root = _source is not null && _inViewport ? _image.XamlRoot : null;
        if (!ReferenceEquals(_root, root))
        {
            if (_root is not null) _root.Changed -= OnRootChanged;
            _root = root;
            if (_root is not null) _root.Changed += OnRootChanged;
        }

        bool visible = root is not null && IsVisible();
        if (_watching != visible)
        {
            _watching = visible;
            if (visible)
            {
                HomeMotion.PreferenceChanged += Refresh;
                SystemSettings.AnimationsEnabledChanged += OnSystemPreferenceChanged;
            }
            else
            {
                HomeMotion.PreferenceChanged -= Refresh;
                SystemSettings.AnimationsEnabledChanged -= OnSystemPreferenceChanged;
            }
        }

        if (!visible || !HomeMotion.AnimationsEnabled)
        {
            Stop();
            return;
        }

        if (_playback is not null || _surface is not null || _source is not { } original) return;
        _playback = new();
        _ = PlayAsync(original, _playback);
    }

    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Refresh();

    private bool IsVisible()
    {
        if (_image.XamlRoot is not { IsHostVisible: true }) return false;
        for (DependencyObject? element = _image; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is UIElement { Visibility: Visibility.Collapsed }) return false;
        }
        return true;
    }

    private void OnSystemPreferenceChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args)
    {
        if (_image.DispatcherQueue.HasThreadAccess) Refresh();
        else _image.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.High, Refresh);
    }

    private async Task PlayAsync(BitmapImage original, CancellationTokenSource playback)
    {
        var token = playback.Token;
        var slots = _preview ? PreviewSlots : ActiveSlots;
        IDisposable? surfaceReservation = null;
        bool reserved = false;
        try
        {
            await slots.WaitAsync(token).ConfigureAwait(true);
            reserved = true;
            await DecodeSlots.WaitAsync(token).ConfigureAwait(true);
            AnimatedPngFrames? decoded;
            try
            {
                if (!IsCurrent(playback, original) || !PosterCache.TryGetAnimation(original, out var bytes)) return;
                decoded = await AnimatedPngFrames.OpenAsync(bytes, token).ConfigureAwait(true);
            }
            finally { DecodeSlots.Release(); }

            if (decoded is null || !IsCurrent(playback, original)) return;
            // Canvas, optional restore canvas, current WriteableBitmap and temporary WIC frame.
            // Preview has its own reserve, so a full grid cannot starve the hover image or logo.
            surfaceReservation = await (_preview ? PreviewSurfaces : CardSurfaces)
                .ReserveAsync(16L * decoded.Width * decoded.Height, token).ConfigureAwait(true);
            if (!IsCurrent(playback, original)) return;
            var bitmap = new WriteableBitmap(decoded.Width, decoded.Height);
            long paintedAt = 0;
            var delay = TimeSpan.Zero;

            for (uint loop = 0; decoded.Plays == 0 || loop < decoded.Plays; loop++)
            {
                for (int index = 0; index < decoded.FrameCount; index++)
                {
                    await DecodeSlots.WaitAsync(token).ConfigureAwait(true);
                    AnimatedPngFrame? frame;
                    try { frame = await decoded.ReadFrameAsync(index, token).ConfigureAwait(true); }
                    finally { DecodeSlots.Release(); }
                    // Decode the next frame during the preceding frame's display interval.
                    var remaining = delay - (paintedAt == 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(paintedAt));
                    if (remaining > TimeSpan.Zero) await Task.Delay(remaining, token).ConfigureAwait(true);
                    if (frame is null || !IsCurrent(playback, _surface is null ? original : bitmap))
                    {
                        if (ReferenceEquals(_playback, playback)) Stop();
                        return;
                    }
                    Paint(bitmap, frame);
                    if (_surface is null)
                    {
                        _surface = bitmap;
                        SetSource(bitmap);
                    }
                    paintedAt = Stopwatch.GetTimestamp();
                    delay = frame.Delay < MinimumFrameDelay ? MinimumFrameDelay : frame.Delay;
                    if (decoded.Plays != 0 && loop == decoded.Plays - 1 && index == decoded.FrameCount - 1)
                    {
                        _heldSurface = surfaceReservation;
                        surfaceReservation = null; // The final visible frame owns its reservation until Stop.
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (ReferenceEquals(_playback, playback)) Stop();
        }
        finally
        {
            surfaceReservation?.Dispose();
            if (reserved) slots.Release();
            if (ReferenceEquals(_playback, playback)) _playback = null;
            playback.Dispose();
        }
    }

    private bool IsCurrent(CancellationTokenSource playback, ImageSource expected) =>
        ReferenceEquals(_playback, playback) && ReferenceEquals(_image.Source, expected)
        && _inViewport && HomeMotion.AnimationsEnabled && IsVisible();

    private static void Paint(WriteableBitmap bitmap, AnimatedPngFrame frame)
    {
        using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(frame.Pixels);
        bitmap.Invalidate();
    }

    private void Stop()
    {
        var playback = _playback;
        _playback = null;
        playback?.Cancel();
        var surface = _surface;
        _surface = null;
        if (surface is not null && ReferenceEquals(_image.Source, surface)) SetSource(_source);
        _heldSurface?.Dispose();
        _heldSurface = null;
    }

    private void SetSource(ImageSource? source)
    {
        _writing = true;
        try { _image.Source = source; }
        finally { _writing = false; }
    }
}
