using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Momoka.Diagnostics;
using Windows.UI.ViewManagement;

namespace Momoka.Shell.Views;

/// <summary>
/// 新页当拍淡入，首帧之后才放行图片处理。Frame 仍使用 Suppress，不等待旧页退出。
/// 只动透明度，背景上沿、主页翻档和滚动锚点读取的布局坐标不随动画改变。
/// </summary>
internal sealed class BrowsePresentation(Frame frame)
{
    private static readonly AsyncLocal<Task?> ImageGate = new();
    private readonly UISettings _systemSettings = new();
    private TaskCompletionSource? _images;
    private NavigationTimingRun? _timing;
    private FrameworkElement? _page;
    private RoutedEventHandler? _loaded;
    private RoutedEventHandler? _unloaded;
    private EventHandler<object>? _rendering;
    private Storyboard? _motion;
    private EventHandler<object>? _completed;
    private bool _watchingPreference;
    private int _generation;

    /// <summary>
    /// 图片调用保留自己导航的等待；未由浏览导航启动的调用立即继续。
    /// AsyncLocal 随异步请求流动，另一个窗口或后续导航不会替换旧请求的门控。
    /// </summary>
    internal static Task WaitForImagesAsync()
    {
        var gate = ImageGate.Value ?? Task.CompletedTask;
        if (!gate.IsCompleted) NavigationTiming.Mark("browse-images-wait");
        return gate;
    }

    internal void Begin()
    {
        Stop();
        // 离线视图模型夹具和未上树的启动路径没有可提交的帧，不给它们制造永远等不到的门。
        if (frame.XamlRoot is null) return;
        _images = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _timing = NavigationTiming.Capture();
        ImageGate.Value = _images.Task;
    }

    internal void Attach()
    {
        if (_images is null) return;
        if (frame.Content is not FrameworkElement page)
        {
            Stop();
            return;
        }

        _page = page;
        var generation = _generation;
        _unloaded = (_, _) =>
        {
            if (generation == _generation && ReferenceEquals(_page, page)) Stop();
        };
        page.Unloaded += _unloaded;

        if (page.IsLoaded) Start(page, generation);
        else
        {
            _loaded = (_, _) =>
            {
                if (generation != _generation || !ReferenceEquals(_page, page)) return;
                page.Loaded -= _loaded;
                _loaded = null;
                Start(page, generation);
            };
            page.Loaded += _loaded;
        }
    }

    private void Start(FrameworkElement page, int generation)
    {
        if (generation != _generation || !ReferenceEquals(frame.Content, page)) return;

        _rendering = (_, _) =>
        {
            if (generation != _generation || !ReferenceEquals(_page, page)) return;
            DetachRendering();
            _timing?.Mark("browse-first-frame");
            // Rendering 是提交当前帧之前的事件。低优先级回调与异步续接保证图片不会在这个栈里抢首帧。
            if (!frame.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (generation != _generation || !ReferenceEquals(_page, page)) return;
                ReleaseImages();
            })) ReleaseImages();
        };
        CompositionTarget.Rendering += _rendering;

        if (!HomeMotion.AnimationsEnabled || frame.XamlRoot is null) return;
        WatchPreference();

        var motion = new Storyboard();
        var fade = new DoubleAnimation
        {
            From = 0.8,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(180)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fade, frame);
        Storyboard.SetTargetProperty(fade, "Opacity");
        motion.Children.Add(fade);
        _motion = motion;
        _completed = (_, _) =>
        {
            if (generation == _generation && ReferenceEquals(_motion, motion)) StopMotion();
        };
        motion.Completed += _completed;
        frame.Opacity = 0.8;
        try
        {
            _timing?.Mark("browse-motion-start");
            motion.Begin();
        }
        catch (Exception error)
        {
            StopMotion();
            Log.Warn("浏览导航", "页面淡入失败，直接显示内容", error);
        }
    }

    private void WatchPreference()
    {
        if (_watchingPreference) return;
        _watchingPreference = true;
        HomeMotion.PreferenceChanged += OnPreferenceChanged;
        _systemSettings.AnimationsEnabledChanged += OnSystemPreferenceChanged;
    }

    private void OnPreferenceChanged()
    {
        if (!HomeMotion.AnimationsEnabled) StopMotion();
    }

    private void OnSystemPreferenceChanged(UISettings sender, object args)
    {
        var generation = _generation;
        frame.DispatcherQueue.TryEnqueue(() =>
        {
            if (generation == _generation) OnPreferenceChanged();
        });
    }

    private void StopMotion()
    {
        var motion = _motion;
        _motion = null;
        if (motion is not null)
        {
            motion.Completed -= _completed;
            _completed = null;
            motion.Stop();
        }
        frame.Opacity = 1;
        if (!_watchingPreference) return;
        _watchingPreference = false;
        HomeMotion.PreferenceChanged -= OnPreferenceChanged;
        _systemSettings.AnimationsEnabledChanged -= OnSystemPreferenceChanged;
    }

    private void DetachRendering()
    {
        if (_rendering is null) return;
        CompositionTarget.Rendering -= _rendering;
        _rendering = null;
    }

    private void ReleaseImages()
    {
        _timing?.Mark("browse-images-released");
        _images?.TrySetResult();
    }

    /// <summary>换页、导航取消和身份释放都放行旧请求，由调用方自己的取消与访问归属拒绝迟到结果。</summary>
    internal void Stop()
    {
        _generation++;
        if (_page is { } page)
        {
            page.Loaded -= _loaded;
            page.Unloaded -= _unloaded;
        }
        _page = null;
        _loaded = null;
        _unloaded = null;
        DetachRendering();
        StopMotion();
        var images = _images;
        _images = null;
        _timing = null;
        if (images is null) return;
        images.TrySetResult();
        if (ReferenceEquals(ImageGate.Value, images.Task)) ImageGate.Value = null;
    }
}
