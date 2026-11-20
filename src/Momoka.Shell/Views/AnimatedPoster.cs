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
    private const int FrameBudget = 4 * 1024 * 1024;
    private static readonly SemaphoreSlim ActiveSlots = new(24);
    private static readonly SemaphoreSlim DecodeSlots = new(2);
    private static readonly TimeSpan MinimumFrameDelay = TimeSpan.FromMilliseconds(33);
    private static readonly UISettings SystemSettings = new();
    private readonly Image _image;
    private BitmapImage? _source;
    private WriteableBitmap? _surface;
    private XamlRoot? _root;
    private CancellationTokenSource? _playback;
    private bool _inViewport;
    private bool _writing;
    private bool _watching;

    internal AnimatedPoster(Image image, ImageArrivalFade arrival)
    {
        _image = image;
        arrival.IsUpdatingAnimatedFrame = () => _writing;
        image.RegisterPropertyChangedCallback(Image.SourceProperty, OnSourceChanged);
        image.EffectiveViewportChanged += OnViewportChanged;
        image.Loaded += OnLoaded;
        image.Unloaded += OnUnloaded;
        UpdateSource();
    }

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
        bool reserved = false;
        try
        {
            await ActiveSlots.WaitAsync(token).ConfigureAwait(true);
            reserved = true;
            await DecodeSlots.WaitAsync(token).ConfigureAwait(true);
            AnimatedPngFrames? decoded;
            try
            {
                if (!IsCurrent(playback, original) || !PosterCache.TryGetAnimation(original, out var bytes)
                    || _root is not { } root) return;
                var width = (int)Math.Ceiling(original.DecodePixelWidth * root.RasterizationScale);
                decoded = await Task.Run(() => AnimatedPngFrames.DecodeAsync(bytes, width, FrameBudget, token), token)
                    .ConfigureAwait(true);
            }
            finally { DecodeSlots.Release(); }

            if (decoded is null || !IsCurrent(playback, original)) return;
            var bitmap = new WriteableBitmap(decoded.Width, decoded.Height);
            _surface = bitmap;
            Paint(bitmap, decoded.Frames[0]);
            SetSource(bitmap);

            for (uint loop = 0; decoded.Plays == 0 || loop < decoded.Plays; loop++)
            {
                for (int index = 0; index < decoded.Frames.Count; index++)
                {
                    var frame = decoded.Frames[index];
                    if (loop != 0 || index != 0) Paint(bitmap, frame);
                    if (decoded.Plays != 0 && loop == decoded.Plays - 1 && index == decoded.Frames.Count - 1) return;
                    await Task.Delay(frame.Delay < MinimumFrameDelay ? MinimumFrameDelay : frame.Delay, token)
                        .ConfigureAwait(true);
                    if (!IsCurrent(playback, bitmap))
                    {
                        if (ReferenceEquals(_playback, playback)) Stop();
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
            if (reserved) ActiveSlots.Release();
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
    }

    private void SetSource(ImageSource? source)
    {
        _writing = true;
        try { _image.Source = source; }
        finally { _writing = false; }
    }
}
