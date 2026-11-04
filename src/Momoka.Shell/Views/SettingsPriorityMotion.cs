using System.Collections.Specialized;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Momoka.Shell.ViewModels;
using Windows.Foundation;

namespace Momoka.Shell.Views;

/// <summary>
/// 列表换位弹簧（设置页关键词表与服务器页共用）。只动画模板根的 TranslateTransform，不占用 WinUI
/// 管理的容器 visual。同一次换位让挪动项与让位项一起回弹；离树、快速换位、关闭动画都会清理旧行程。
/// </summary>
internal sealed class SettingsPriorityMotion
{
    internal const int DurationMs = 380;
    private readonly ListView _list;
    private readonly INotifyCollectionChanged _collection;
    private readonly Func<IEnumerable<object>> _items;
    private readonly Action _beginReorder;
    private readonly Action _endReorder;
    private readonly Func<FrameworkElement, bool> _isRoot;
    private readonly Func<object, bool> _isItem;
    private readonly Dictionary<FrameworkElement, Storyboard> _active = [];
    private Dictionary<object, double> _positions = [];
    private Dictionary<object, double>? _before;
    private bool _dragging;
    private bool _queued;
    private bool _detached;
    private int _generation;

    internal SettingsPriorityMotion(ListView list, SettingTitleRulesRow row)
        : this(list, row.Rules, () => row.Rules.Cast<object>(), row.BeginReorder, row.EndReorder,
            element => element.Name == "PriorityRuleRoot", item => item is TitleRuleChoice)
    {
    }

    /// <summary>服务器页两层列表用的入口：集合、模板根判据与提交动作都由调用方给。</summary>
    internal SettingsPriorityMotion(ListView list, INotifyCollectionChanged collection,
        Func<IEnumerable<object>> items, Action beginReorder, Action endReorder,
        Func<FrameworkElement, bool> isRoot, Func<object, bool>? isItem = null)
    {
        _list = list;
        _collection = collection;
        _items = items;
        _beginReorder = beginReorder;
        _endReorder = endReorder;
        _isRoot = isRoot;
        _isItem = isItem ?? (_ => true);
        _list.UpdateLayout();
        _positions = HomeMotion.AnimationsEnabled ? Measure() : [];
        _collection.CollectionChanged += OnChanged;
        _list.LayoutUpdated += OnLayoutUpdated;
        HomeMotion.PreferenceChanged += OnPreferenceChanged;
    }

    internal bool IsAnimating => _queued || _active.Count > 0;
    internal bool HasSpring => _active.Count > 0;

    private Dictionary<object, double> Measure()
    {
        var positions = new Dictionary<object, double>();
        foreach (var item in _items())
        {
            if (Root(item) is not { ActualHeight: > 0 } root) continue;
            positions[item] = root.TransformToVisual(_list).TransformPoint(new Point()).Y;
        }
        return positions;
    }

    private void OnLayoutUpdated(object? sender, object args)
    {
        if (_detached || _list.XamlRoot is null) return;
        if (!HomeMotion.AnimationsEnabled)
        {
            if (IsAnimating || _before is not null) Stop();
            return;
        }
        if (!_queued && !_dragging && _active.Count == 0) _positions = Measure();
    }

    private void OnChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (_dragging || _detached) return;
        if (!HomeMotion.AnimationsEnabled || _list.XamlRoot is null)
        {
            Stop();
            return;
        }
        _before ??= _positions;
        QueueSettle();
    }

    internal void BeginDrag(DragItemsStartingEventArgs args)
    {
        if (args.Items.Count != 1 || !_isItem(args.Items[0]))
        {
            args.Cancel = true;
            return;
        }
        BeginDrag();
    }

    // 原生 DragItemsStarting 与隔离探针共用同一套两阶段提交和动画快照。
    internal void BeginDrag()
    {
        if (_detached) return;
        Stop();
        // 拖拽提交与动画快照是两件事：禁动画仍 Begin/EndReorder，但不测量无用的 before。
        _before = HomeMotion.AnimationsEnabled ? Measure() : null;
        _dragging = true;
        _beginReorder();
    }

    internal void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        _endReorder();
        QueueSettle();
    }

    private void QueueSettle()
    {
        if (_detached) return;
        if (!HomeMotion.AnimationsEnabled || _list.XamlRoot is null)
        {
            Stop();
            return;
        }
        if (_queued) return;
        _queued = true;
        var generation = _generation;
        if (!_list.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            // Stop, a replacement drag or Detach may leave the delegate in the dispatcher queue.
            // An old delegate must not consume the new run's _queued flag or _before snapshot.
            if (generation != _generation) return;
            _queued = false;
            if (_detached || _dragging || _list.XamlRoot is null || !HomeMotion.AnimationsEnabled)
            {
                Stop();
                return;
            }
            var before = _before;
            Stop();
            var run = _generation;
            _list.UpdateLayout();
            if (run != _generation || _detached || _dragging || _list.XamlRoot is null || !HomeMotion.AnimationsEnabled) return;
            var after = Measure();
            _positions = after;
            if (before is null) return;
            foreach (var (rule, top) in after)
                if (before.TryGetValue(rule, out var oldTop) && Root(rule) is { } root)
                    Spring(root, oldTop - top);
        })) Stop();
    }

    private FrameworkElement? Root(object item) =>
        _list.ContainerFromItem(item) is DependencyObject container ? FindRoot(container) : null;

    private FrameworkElement? FindRoot(DependencyObject parent)
    {
        if (parent is FrameworkElement root && _isRoot(root)) return root;
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
        var generation = _generation;
        board.Completed += (_, _) =>
        {
            if (generation != _generation || !_active.TryGetValue(root, out var current) || !ReferenceEquals(current, board)) return;
            _active.Remove(root);
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

    private void OnPreferenceChanged()
    {
        if (!HomeMotion.AnimationsEnabled) Stop();
        else if (!_detached && !_dragging && _list.XamlRoot is not null) _list.UpdateLayout();
    }

    internal void Stop()
    {
        _generation++;
        _queued = false;
        _before = null;
        foreach (var (root, board) in _active)
        {
            board.Stop();
            if (root.RenderTransform is TranslateTransform drift) drift.Y = 0;
        }
        _active.Clear();
    }

    internal void Detach()
    {
        if (_detached) return;
        _detached = true;
        _collection.CollectionChanged -= OnChanged;
        _list.LayoutUpdated -= OnLayoutUpdated;
        HomeMotion.PreferenceChanged -= OnPreferenceChanged;
        EndDrag();
        Stop();
    }
}
