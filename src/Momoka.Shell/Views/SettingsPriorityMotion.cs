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
/// 关键词表的换位弹簧。只动画模板根的 TranslateTransform，不占用 WinUI 管理的容器 visual。
/// 同一次换位让挪动项与让位项一起回弹；离树、快速换位、关闭动画都会清理旧行程。
/// </summary>
internal sealed class SettingsPriorityMotion
{
    internal const int DurationMs = 380;
    private readonly ListView _list;
    private readonly SettingTitleRulesRow _row;
    private readonly Dictionary<FrameworkElement, Storyboard> _active = [];
    private Dictionary<TitleRuleChoice, double> _positions = [];
    private Dictionary<TitleRuleChoice, double>? _before;
    private bool _dragging;
    private bool _queued;
    private bool _detached;

    internal SettingsPriorityMotion(ListView list, SettingTitleRulesRow row)
    {
        _list = list;
        _row = row;
        _list.UpdateLayout();
        _positions = Measure();
        _row.Rules.CollectionChanged += OnChanged;
        _list.LayoutUpdated += OnLayoutUpdated;
    }

    internal bool IsAnimating => _queued || _active.Count > 0;
    internal bool HasSpring => _active.Count > 0;

    private Dictionary<TitleRuleChoice, double> Measure()
    {
        var positions = new Dictionary<TitleRuleChoice, double>();
        foreach (var rule in _row.Rules)
        {
            if (Root(rule) is not { ActualHeight: > 0 } root) continue;
            positions[rule] = root.TransformToVisual(_list).TransformPoint(new Point()).Y;
        }
        return positions;
    }

    private void OnLayoutUpdated(object? sender, object args)
    {
        if (!_queued && !_dragging && _active.Count == 0) _positions = Measure();
    }

    private void OnChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (_dragging || _detached) return;
        _before ??= _positions;
        QueueSettle();
    }

    internal void BeginDrag(DragItemsStartingEventArgs args)
    {
        if (args.Items.Count != 1 || args.Items[0] is not TitleRuleChoice)
        {
            args.Cancel = true;
            return;
        }
        BeginDrag();
    }

    // 原生 DragItemsStarting 与隔离探针共用同一套两阶段提交和动画快照。
    internal void BeginDrag()
    {
        Stop();
        _before = Measure();
        _dragging = true;
        _row.BeginReorder();
    }

    internal void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        _row.EndReorder();
        QueueSettle();
    }

    private void QueueSettle()
    {
        if (_queued || _detached) return;
        _queued = true;
        if (!_list.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _queued = false;
            // 离树后 TransformToVisual 会抛；先确认树还在，再量布局。Detach 会清掉快照，这里只管本次。
            if (_detached || _dragging || _list.XamlRoot is null)
            {
                _before = null;
                return;
            }
            var before = _before;
            _before = null;
            Stop();
            _list.UpdateLayout();
            var after = Measure();
            _positions = after;
            if (before is null || !HomeMotion.AnimationsEnabled) return;
            foreach (var (rule, top) in after)
                if (before.TryGetValue(rule, out var oldTop) && Root(rule) is { } root)
                    Spring(root, oldTop - top);
        }))
        {
            _queued = false;
            _before = null;
        }
    }

    private FrameworkElement? Root(TitleRuleChoice rule) =>
        _list.ContainerFromItem(rule) is DependencyObject container ? FindRoot(container) : null;

    private static FrameworkElement? FindRoot(DependencyObject parent)
    {
        if (parent is FrameworkElement { Name: "PriorityRuleRoot" } root) return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            if (FindRoot(VisualTreeHelper.GetChild(parent, index)) is { } found) return found;
        return null;
    }

    private void Spring(FrameworkElement root, double distance)
    {
        if (Math.Abs(distance) < 0.5 || root.RenderTransform is not TranslateTransform drift) return;
        // 长距离拖动也不把浮层外的一整屏拉进来；回弹幅度独立限到 7 DIP，避免邻行文字打架。
        distance = Math.Clamp(distance, -160, 160);
        var rebound = Math.Sign(distance) * Math.Min(7, Math.Abs(distance) * 0.1);
        drift.Y = distance;
        var frames = new DoubleAnimationUsingKeyFrames { Duration = new Duration(TimeSpan.FromMilliseconds(DurationMs)) };
        AddFrame(frames, 0, distance);
        AddFrame(frames, 230, -rebound);
        AddFrame(frames, 310, rebound * 0.3);
        AddFrame(frames, DurationMs, 0);
        Storyboard.SetTarget(frames, drift);
        Storyboard.SetTargetProperty(frames, "Y");
        var board = new Storyboard();
        board.Children.Add(frames);
        _active.Add(root, board);
        board.Completed += (_, _) =>
        {
            if (!_active.Remove(root)) return;
            board.Stop();
            drift.Y = 0;
        };
        board.Begin();
    }

    private static void AddFrame(DoubleAnimationUsingKeyFrames frames, int milliseconds, double value) =>
        frames.KeyFrames.Add(new SplineDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds)),
            Value = value,
            KeySpline = new KeySpline { ControlPoint1 = new Point(0.2, 0.8), ControlPoint2 = new Point(0.25, 1) }
        });

    internal void Stop()
    {
        foreach (var (root, board) in _active)
        {
            board.Stop();
            if (root.RenderTransform is TranslateTransform drift) drift.Y = 0;
        }
        _active.Clear();
    }

    internal void Detach()
    {
        _detached = true;
        _row.Rules.CollectionChanged -= OnChanged;
        _list.LayoutUpdated -= OnLayoutUpdated;
        EndDrag();
        _before = null;
        Stop();
    }
}
