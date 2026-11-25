using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;

namespace Momoka.Shell.Views;

/// <summary>
/// 已在屏的图片绑定晚到时淡入。缓存位图不保证再发 ImageOpened，因此直接观察 Source；
/// 离树期间已到的图在 Loaded 时保持可见，同一 Source 不重复闪空。
/// </summary>
internal sealed class ImageArrivalFade : IDisposable
{
    internal const int DurationMs = 220;
    private static readonly UISettings SystemSettings = new();
    private readonly DependencyObject _target;
    private readonly DependencyProperty _sourceProperty;
    private readonly FrameworkElement _host;
    private readonly Image? _image;
    private readonly ImageBrush? _brush;
    private readonly double _finalOpacity;
    private readonly long _sourceToken;
    private readonly Func<object?>? _contentIdentity;
    private ImageSource? _seen;
    private object? _seenIdentity;
    private Storyboard? _board;
    private EventHandler<object>? _completed;
    private bool _watchingPreferences;
    private bool _disposed;
    private bool _suppressed;

    internal ImageArrivalFade(Image image)
        : this(image, null)
    {
    }

    internal ImageArrivalFade(Image image, Func<object?>? contentIdentity)
        : this(image, Image.SourceProperty, image, image.Opacity)
    {
        _image = image;
        _contentIdentity = contentIdentity;
        _seen = image.Source;
        _seenIdentity = contentIdentity?.Invoke();
        image.ImageFailed += OnImageFailed;
    }

    // The host contains only this picture; theme scrims must remain its siblings. Animating a
    // FrameworkElement keeps this on the established independent Opacity Storyboard path.
    internal ImageArrivalFade(ImageBrush brush, FrameworkElement lifetimeHost)
        : this(brush, ImageBrush.ImageSourceProperty, lifetimeHost, lifetimeHost.Opacity)
    {
        _brush = brush;
        _seen = brush.ImageSource;
        brush.ImageFailed += OnImageFailed;
    }

    private ImageArrivalFade(DependencyObject target, DependencyProperty sourceProperty, FrameworkElement host, double finalOpacity)
    {
        _target = target;
        _sourceProperty = sourceProperty;
        _host = host;
        _finalOpacity = finalOpacity;
        _sourceToken = target.RegisterPropertyChangedCallback(sourceProperty, OnSourceChanged);
        host.Unloaded += OnUnloaded;
    }

    internal bool IsRunning => _board is not null;
    internal Func<bool>? IsUpdatingAnimatedFrame { get; set; }

    private void OnSourceChanged(DependencyObject sender, DependencyProperty property)
    {
        if (_disposed || IsUpdatingAnimatedFrame?.Invoke() == true) return;
        var source = _image is not null ? _image.Source : _brush?.ImageSource;
        if (ReferenceEquals(source, _seen)) return;
        var identity = _contentIdentity?.Invoke();
        // 同一张卡仅换解码尺寸时，旧图已经可见。高清替换直接落定；首次到图与容器换卡仍淡入。
        var replacesVisibleContent = source is not null && _seen is not null && identity is not null
            && ReferenceEquals(identity, _seenIdentity);
        _seen = source;
        _seenIdentity = identity;
        Stop();
        if (_suppressed || replacesVisibleContent || source is null || _host.XamlRoot is null || !HomeMotion.AnimationsEnabled) return;

        // Repeater 的回收幻影可能把 IsLoaded 置为 false，但 XamlRoot 仍在、画面也仍在。
        WatchPreferences();
        SetOpacity(0);
        var fade = new DoubleAnimation
        {
            From = 0,
            To = _finalOpacity,
            Duration = new Duration(TimeSpan.FromMilliseconds(DurationMs)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(fade, _host);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var board = new Storyboard();
        board.Children.Add(fade);
        _board = board;
        _completed = (_, _) =>
        {
            if (ReferenceEquals(_board, board)) Stop();
        };
        board.Completed += _completed;
        try
        {
            board.Begin();
        }
        catch (Exception)
        {
            // 离树或应用关闭时平台可能拒绝启动；图片仍须恢复最终不透明度。
            Stop();
        }
    }

    internal void Stop()
    {
        var board = _board;
        _board = null;
        if (board is not null && _completed is not null) board.Completed -= _completed;
        _completed = null;
        StopWatchingPreferences();
        board?.Stop();
        SetOpacity(_finalOpacity);
    }

    // 关联封面本次停留始终连续呈现；高清替换不能再次从透明开始。
    internal void Suppress()
    {
        _suppressed = true;
        Stop();
    }

    private void SetOpacity(double value)
    {
        _host.Opacity = value;
    }

    private void OnImageFailed(object sender, ExceptionRoutedEventArgs args) => Stop();
    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (_host.XamlRoot is not null) return;
        Stop();
        StopWatchingPreferences();
    }

    private void WatchPreferences()
    {
        if (_watchingPreferences || _disposed) return;
        _watchingPreferences = true;
        HomeMotion.PreferenceChanged += ApplyPreference;
        SystemSettings.AnimationsEnabledChanged += OnSystemPreferenceChanged;
    }

    private void StopWatchingPreferences()
    {
        if (!_watchingPreferences) return;
        _watchingPreferences = false;
        HomeMotion.PreferenceChanged -= ApplyPreference;
        SystemSettings.AnimationsEnabledChanged -= OnSystemPreferenceChanged;
    }

    private void ApplyPreference()
    {
        if (_disposed) return;
        if (!HomeMotion.AnimationsEnabled) Stop();
    }

    private void OnSystemPreferenceChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args)
    {
        if (_host.DispatcherQueue.HasThreadAccess) ApplyPreference();
        else _host.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.High, ApplyPreference);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        StopWatchingPreferences();
        _target.UnregisterPropertyChangedCallback(_sourceProperty, _sourceToken);
        _host.Unloaded -= OnUnloaded;
        if (_image is not null) _image.ImageFailed -= OnImageFailed;
        if (_brush is not null) _brush.ImageFailed -= OnImageFailed;
    }
}
