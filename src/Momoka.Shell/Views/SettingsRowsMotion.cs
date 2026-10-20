using System.Collections.Specialized;
using Momoka.Shell.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace Momoka.Shell.Views;

/// <summary>
/// 设置从属行的淡入淡出与补位使用系统主题过渡，卡片高度同时缓动，避免下面的分组突然跳动。
/// 数据显隐仍由视图模型同步决定；只对可见页面播放，关闭动画或离树立即恢复 Auto 高度。
/// </summary>
internal sealed class SettingsRowsMotion
{
    internal const int DurationMs = 240;
    // 行的系统过渡比卡片缩放稍长；晚一拍才报落定，不能把仍然透明的恢复行当作完成。
    private const int TransitionSettleMs = 400;
    private readonly ItemsControl _list;
    private readonly SettingSection _section;
    private readonly Border? _surface;
    private Storyboard? _height;
    private readonly DispatcherTimer _settling = new() { Interval = TimeSpan.FromMilliseconds(TransitionSettleMs) };
    private RectangleGeometry? _clip;
    private bool _queued;
    private bool _detached;
    private int _generation;
    private double _fromHeight;

    internal SettingsRowsMotion(ItemsControl list, SettingSection section)
    {
        _list = list;
        _section = section;
        _surface = VisualTreeHelper.GetParent(list) as Border;
        _section.Rows.CollectionChanged += OnChanged;
        _settling.Tick += OnSettled;
        if (_surface is not null) _surface.SizeChanged += OnSurfaceSizeChanged;
        ApplyPreference();
    }

    internal bool IsAnimating => _queued || _height is not null || _settling.IsEnabled;
    internal bool HasTransitions => _list.ItemContainerTransitions is { } transitions
        && transitions.OfType<AddDeleteThemeTransition>().Any()
        && transitions.OfType<RepositionThemeTransition>().Any();

    internal void ApplyPreference()
    {
        if (HomeMotion.AnimationsEnabled)
        {
            _list.ItemContainerTransitions = new TransitionCollection
            {
                new AddDeleteThemeTransition(),
                new RepositionThemeTransition { IsStaggeringEnabled = false }
            };
        }
        else
        {
            _list.ItemContainerTransitions = new TransitionCollection();
            Stop();
        }
    }

    private void OnChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (_queued || _detached || _surface is null || !HomeMotion.AnimationsEnabled || !_section.IsVisible) return;
        _fromHeight = _surface.ActualHeight;
        Stop();
        _queued = true;
        var generation = _generation;
        if (!_list.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (generation == _generation) AnimateHeight();
        })) _queued = false;
    }

    private void AnimateHeight()
    {
        _queued = false;
        if (_detached || _surface is null || _list.XamlRoot is null || !_section.IsVisible || !HomeMotion.AnimationsEnabled) return;
        _list.UpdateLayout();
        _settling.Start();
        var to = _surface.ActualHeight;
        if (_fromHeight <= 0 || Math.Abs(to - _fromHeight) < 0.5) return;

        _clip = new RectangleGeometry { Rect = new Rect(0, 0, _surface.ActualWidth, _fromHeight) };
        _surface.Clip = _clip;
        _surface.Height = _fromHeight;
        var resize = new DoubleAnimation
        {
            From = _fromHeight,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(DurationMs)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(resize, _surface);
        Storyboard.SetTargetProperty(resize, "Height");
        var board = new Storyboard();
        board.Children.Add(resize);
        _height = board;
        board.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_height, board)) return;
            ResetHeight();
        };
        board.Begin();
    }

    private void OnSurfaceSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (_clip is not null) _clip.Rect = new Rect(0, 0, args.NewSize.Width, args.NewSize.Height);
    }

    private void OnSettled(object? sender, object args) => _settling.Stop();

    private void ResetHeight()
    {
        var height = _height;
        _height = null;
        height?.Stop();
        if (_surface is not null)
        {
            _surface.Height = double.NaN;
            _surface.Clip = null;
        }
        _clip = null;
    }

    internal void Stop()
    {
        _generation++;
        _queued = false;
        _settling.Stop();
        ResetHeight();
    }

    internal void Detach()
    {
        _detached = true;
        _section.Rows.CollectionChanged -= OnChanged;
        _settling.Tick -= OnSettled;
        if (_surface is not null) _surface.SizeChanged -= OnSurfaceSizeChanged;
        Stop();
        _list.ItemContainerTransitions = new TransitionCollection();
    }
}
