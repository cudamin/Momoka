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
            long due = 0;

            for (uint loop = 0; decoded.Plays == 0 || loop < decoded.Plays; loop++)
            {
                for (int index = 0; index < decoded.FrameCount; index++)
                {
                    await DecodeSlots.WaitAsync(token).ConfigureAwait(true);
                    AnimatedPngFrame? frame;
                    try { frame = await decoded.ReadFrameAsync(index, token).ConfigureAwait(true); }
                    finally { DecodeSlots.Release(); }
                    // Decode the next frame during the preceding frame's display interval; the deadline
                    // is carried across frames so 24/25fps still average out on a 60Hz compositor clock.
                    if (due != 0) await FrameClock.WaitAsync(due, token).ConfigureAwait(true);
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
                    var paintedAt = Stopwatch.GetTimestamp();
                    var frameDelay = frame.Delay < MinimumFrameDelay ? MinimumFrameDelay : frame.Delay;
                    var delayTicks = (long)(frameDelay.TotalSeconds * Stopwatch.Frequency);
                    due = due == 0
                        ? paintedAt + delayTicks
                        : Math.Max(due + delayTicks, paintedAt + delayTicks);
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

    /// <summary>
    /// Waits for the first compositor frame at or after a deadline. A thread-pool timer rounds a 50ms
    /// frame up to the system tick (about 62ms measured in the running window), so a 20fps APNG played
    /// at 16fps with jitter. Rendering callbacks arrive once per displayed frame; appointments are carried
    /// across frames instead of restarting from the last paint, which keeps 24/25fps averaging out.
    /// The subscription exists only while at least one frame is waiting.
    /// </summary>
    private static class FrameClock
    {
        private static readonly object Gate = new();
        private static readonly List<Waiter> Waiting = [];
        // A deadline a few milliseconds before a callback still belongs to that callback; without a
        // tolerance a 50ms deadline lands microseconds before the third 60Hz tick and becomes 67ms.
        private static readonly long EarlyTolerance = Stopwatch.Frequency / 200;
        private static bool _hooked;

        internal static async Task WaitAsync(long deadline, CancellationToken token)
        {
            if (Stopwatch.GetTimestamp() + EarlyTolerance >= deadline) return;
            token.ThrowIfCancellationRequested();
            // Deliberately not RunContinuationsAsynchronously: the paint should land in this same frame.
            // Cancellation only completes the task; the awaiting UI continuation removes the appointment.
            var completion = new TaskCompletionSource();
            var waiter = new Waiter(deadline, completion);
            lock (Gate)
            {
                Waiting.Add(waiter);
                if (!_hooked)
                {
                    _hooked = true;
                    CompositionTarget.Rendering += OnRendering;
                }
            }
            using var registration = token.Register(() => completion.TrySetCanceled(token));
            try { await completion.Task.ConfigureAwait(true); }
            finally
            {
                registration.Dispose();
                Remove(waiter);
            }
        }

        private static void Remove(Waiter waiter)
        {
            lock (Gate)
            {
                if (!Waiting.Remove(waiter) || Waiting.Count > 0 || !_hooked) return;
                _hooked = false;
                CompositionTarget.Rendering -= OnRendering;
            }
        }

        private static void OnRendering(object? sender, object args)
        {
            List<Waiter>? due = null;
            lock (Gate)
            {
                if (!_hooked) return;
                var now = Stopwatch.GetTimestamp();
                for (var index = Waiting.Count - 1; index >= 0; index--)
                {
                    var waiter = Waiting[index];
                    if (now + EarlyTolerance < waiter.Deadline) continue;
                    Waiting.RemoveAt(index);
                    (due ??= []).Add(waiter);
                }
                if (Waiting.Count == 0)
                {
                    _hooked = false;
                    CompositionTarget.Rendering -= OnRendering;
                }
            }
            if (due is null) return;
            foreach (var waiter in due) waiter.Completion.TrySetResult();
        }

        private sealed record Waiter(long Deadline, TaskCompletionSource Completion);
    }
}
