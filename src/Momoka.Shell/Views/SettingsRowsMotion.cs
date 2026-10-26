using System.Collections.Specialized;
using System.ComponentModel;
using Momoka.Diagnostics;
using Momoka.Shell.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace Momoka.Shell.Views;

/// <summary>
/// 设置行的显隐、补位和卡片高度共用一趟短动画，不借用无法调速的系统 Add/Delete 过渡。
/// Rows 当场更新；只有删除行的不可交互副本留到淡出结束，正常控件不搬树、不重建。
/// </summary>
internal sealed class SettingsRowsMotion : IDisposable
{
    internal const int DurationMs = 160;
    private readonly ItemsControl _list;
    private readonly SettingSection _section;
    private readonly Border? _surface;
    private readonly UISettings _systemSettings = new();
    private readonly List<(FrameworkElement Element, long Token)> _visibilitySubscriptions = [];
    private readonly Dictionary<SettingRow, RowFrame> _known = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<FrameworkElement, RowRestore> _animated = new(ReferenceEqualityComparer.Instance);
    private RowFrame[] _fromRows = [];
    private Storyboard? _board;
    private EventHandler<object>? _completed;
    private RectangleGeometry? _clip;
    private Popup? _ghost;
    private Canvas? _ghostRows;
    private readonly List<(ContentPresenter Copy, Rect Bounds)> _ghostPositions = [];
    private bool _queued;
    private bool _arranging;
    private bool _detached;
    private int _generation;
    private double _fromHeight;

    internal SettingsRowsMotion(ItemsControl list, SettingSection section)
    {
        _list = list;
        _section = section;
        _surface = VisualTreeHelper.GetParent(list) as Border;
        _section.Rows.CollectionChanged += OnChanged;
        _section.PropertyChanged += OnSectionChanged;
        _list.LayoutUpdated += OnLayoutUpdated;
        _list.Unloaded += OnUnloaded;
        HomeMotion.PreferenceChanged += ApplyPreference;
        _systemSettings.AnimationsEnabledChanged += OnSystemPreferenceChanged;
        if (_surface is not null) _surface.SizeChanged += OnSurfaceSizeChanged;
        foreach (var element in Ancestors())
            _visibilitySubscriptions.Add((element, element.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, OnVisibilityChanged)));
        ApplyPreference();
        RememberRows();
    }

    internal bool IsAnimating => _queued || _board is not null;

    // Kept for the page/probe's existing preference contract. This now means controllable row motion
    // is enabled, NOT that ItemContainerTransitions contains fixed-duration Windows transitions.
    internal bool HasTransitions => !_detached && HomeMotion.AnimationsEnabled;

    internal void ApplyPreference()
    {
        if (_detached) return;
        _list.ItemContainerTransitions = new TransitionCollection();
        if (!HomeMotion.AnimationsEnabled) Stop();
    }

    private bool CanAnimate => !_detached && _surface is not null && _list.IsLoaded && _list.XamlRoot is not null
        && _section.IsVisible && _section.Rows.Count > 0 && HomeMotion.AnimationsEnabled
        && Ancestors().All(element => element.Visibility == Visibility.Visible);

    private IEnumerable<FrameworkElement> Ancestors()
    {
        for (DependencyObject? owner = _list; owner is not null; owner = VisualTreeHelper.GetParent(owner))
            if (owner is FrameworkElement element) yield return element;
    }

    private void OnChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (!CanAnimate)
        {
            Stop();
            return;
        }
        if (_queued) return; // One UI turn, one final desired layout; Rows itself never waits for us.

        var from = _surface!.ActualHeight;
        var frames = _known.Values.Select(frame => frame with
        {
            Bounds = _animated.TryGetValue(frame.Target, out var restore) && restore.Drift is { } drift
                ? new Rect(frame.Bounds.X, frame.Bounds.Y + drift.Y, frame.Bounds.Width, frame.Bounds.Height)
                : frame.Bounds,
            Opacity = frame.Target.Opacity
        }).ToArray();
        Stop();
        _fromHeight = from;
        _fromRows = frames;
        _queued = true;
        if (from > 0)
        {
            _surface.Height = from;
            _clip = new RectangleGeometry { Rect = new Rect(0, 0, _surface.ActualWidth, from) };
            _surface.Clip = _clip;
        }
        var generation = _generation;
        if (!_list.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (generation == _generation) Animate(generation);
        })) Stop();
    }

    private void Animate(int generation)
    {
        if (!CanAnimate)
        {
            Stop();
            return;
        }
        try
        {
            _arranging = true;
            _surface!.Height = double.NaN;
            _list.UpdateLayout();
            if (generation != _generation) return;
            if (!CanAnimate)
            {
                Stop();
                return;
            }
            var to = _surface.ActualHeight;
            var current = CurrentRows().ToArray();
            _known.Clear();
            foreach (var frame in current) _known.Add(frame.Row, frame);
            var board = new Storyboard();

            foreach (var frame in current)
            {
                var previous = _fromRows.FirstOrDefault(old => SameRow(old.Row, frame.Row));
                var opacity = previous?.Opacity ?? 0;
                var distance = previous is null ? 0 : previous.Bounds.Y - frame.Bounds.Y;
                if (Math.Abs(distance) < 0.5 && Math.Abs(opacity - frame.Opacity) < 0.01) continue;
                var restore = new RowRestore(frame.Target.ReadLocalValue(UIElement.OpacityProperty),
                    frame.Target.ReadLocalValue(UIElement.RenderTransformProperty));
                _animated.Add(frame.Target, restore);
                if (Math.Abs(opacity - frame.Opacity) >= 0.01)
                {
                    frame.Target.Opacity = opacity;
                    AddAnimation(board, frame.Target, "Opacity", opacity, frame.Opacity);
                }
                if (Math.Abs(distance) >= 0.5)
                {
                    var drift = new TranslateTransform { Y = distance };
                    restore.Drift = drift;
                    var transforms = new TransformGroup();
                    if (frame.Target.RenderTransform is { } original) transforms.Children.Add(original);
                    transforms.Children.Add(drift);
                    frame.Target.RenderTransform = transforms;
                    AddAnimation(board, drift, "Y", distance, 0);
                }
            }

            var removed = _fromRows.Where(old => !current.Any(frame => SameRow(old.Row, frame.Row))).ToArray();
            AddRemovedRows(board, removed);
            if (_fromHeight > 0 && Math.Abs(to - _fromHeight) >= 0.5)
            {
                _surface.Height = _fromHeight;
                _clip ??= new RectangleGeometry();
                _clip.Rect = new Rect(0, 0, _surface.ActualWidth, _fromHeight);
                _surface.Clip = _clip;
                AddAnimation(board, _surface, "Height", _fromHeight, to, dependent: true);
            }
            if (board.Children.Count == 0)
            {
                Stop();
                return;
            }
            _board = board;
            _queued = false;
            _completed = (_, _) =>
            {
                // A posted Completed from a canceled run must not clear a replacement run.
                if (generation != _generation || !ReferenceEquals(_board, board)) return;
                Stop();
            };
            board.Completed += _completed;
            board.Begin();
        }
        catch (Exception error)
        {
            Stop();
            Log.Warn("设置", "设置行动画失败，直接显示内容", error);
        }
        finally { _arranging = false; }
    }

    private static bool SameRow(SettingRow first, SettingRow second) => ReferenceEquals(first, second)
        || first.Label == second.Label && first.GetType() == second.GetType();

    private IEnumerable<RowFrame> CurrentRows()
    {
        foreach (var row in _section.Rows)
        {
            // Only the template root is ours to move, never the framework's layout container.
            if (_list.ContainerFromItem(row) is not FrameworkElement container || VisualTreeHelper.GetChildrenCount(container) == 0
                || VisualTreeHelper.GetChild(container, 0) is not FrameworkElement target) continue;
            var bounds = target.TransformToVisual(_list).TransformBounds(new Rect(0, 0, target.ActualWidth, target.ActualHeight));
            if (bounds.Width > 0 && bounds.Height > 0) yield return new RowFrame(row, target, bounds, target.Opacity);
        }
    }

    private void RememberRows()
    {
        if (_detached || IsAnimating || _arranging || _list.XamlRoot is null) return;
        _known.Clear();
        foreach (var frame in CurrentRows()) _known.Add(frame.Row, frame);
    }

    private void AddRemovedRows(Storyboard board, IReadOnlyList<RowFrame> removed)
    {
        if (removed.Count == 0 || _list.ItemTemplate is null && _list.ItemTemplateSelector is null) return;
        // A Popup avoids reparenting the live ItemsControl (and its focus/Loaded lifetime). These
        // presenters are disposable visual copies, not an ItemsSource or delayed availability state.
        _ghostRows = new Canvas { Width = _list.ActualWidth, Height = _fromHeight, IsHitTestVisible = false, IsTabStop = false };
        _ghost = new Popup
        {
            XamlRoot = _list.XamlRoot,
            Child = _ghostRows,
            IsHitTestVisible = false,
            IsTabStop = false,
            RequestedTheme = _list.ActualTheme
        };
        foreach (var old in removed)
        {
            var copy = new ContentPresenter
            {
                Content = old.Row,
                ContentTemplate = _list.ItemTemplate,
                ContentTemplateSelector = _list.ItemTemplateSelector,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Top,
                Width = old.Bounds.Width,
                Height = old.Bounds.Height,
                Opacity = old.Opacity,
                IsHitTestVisible = false,
                IsTabStop = false,
                FlowDirection = _list.FlowDirection
            };
            Canvas.SetLeft(copy, old.Bounds.X);
            Canvas.SetTop(copy, old.Bounds.Y);
            _ghostRows.Children.Add(copy);
            _ghostPositions.Add((copy, old.Bounds));
            AddAnimation(board, copy, "Opacity", old.Opacity, 0);
        }
        PositionRemovedRows();
        _ghost.IsOpen = true;
        _ghostRows.UpdateLayout();
        foreach (var element in VisualElements(_ghostRows))
        {
            element.IsHitTestVisible = false;
            element.IsTabStop = false;
            AutomationProperties.SetAccessibilityView(element, AccessibilityView.Raw);
        }
    }

    private static IEnumerable<UIElement> VisualElements(DependencyObject root)
    {
        if (root is UIElement element) yield return element;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in VisualElements(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private void PositionRemovedRows()
    {
        if (_ghost is null || _ghostRows is null || _surface is null) return;
        var origin = _list.TransformToVisual(null).TransformPoint(new Point());

        var bounds = new Rect(0, 0, _list.ActualWidth,
            Math.Max(0, _surface.ActualHeight - _surface.Padding.Top - _surface.Padding.Bottom));
        // Popups don't inherit the page ScrollView's clip. Keep the copies inside the same viewport
        // while scrolling or resizing; they must never paint over the navigation/next card.
        foreach (var ancestor in Ancestors().Where(element => element is ScrollView or ScrollViewer))
        {
            var viewport = ancestor.TransformToVisual(_list).TransformBounds(new Rect(0, 0, ancestor.ActualWidth, ancestor.ActualHeight));
            var left = Math.Max(bounds.Left, viewport.Left);
            var top = Math.Max(bounds.Top, viewport.Top);
            bounds = new Rect(left, top, Math.Max(0, Math.Min(bounds.Right, viewport.Right) - left),
                Math.Max(0, Math.Min(bounds.Bottom, viewport.Bottom) - top));
        }
        // Give the popup only the visible slice, not the whole old card. Otherwise its default
        // root-bounds constraint can relocate a partially offscreen card and misalign the copies.
        _ghost.HorizontalOffset = origin.X + bounds.Left;
        _ghost.VerticalOffset = origin.Y + bounds.Top;
        _ghostRows.Width = bounds.Width;
        _ghostRows.Height = bounds.Height;
        _ghostRows.Clip = new RectangleGeometry { Rect = new Rect(0, 0, bounds.Width, bounds.Height) };
        foreach (var (copy, position) in _ghostPositions)
        {
            Canvas.SetLeft(copy, position.X - bounds.Left);
            Canvas.SetTop(copy, position.Y - bounds.Top);
        }
    }

    private static void AddAnimation(Storyboard board, DependencyObject target, string property, double from, double to, bool dependent = false)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(DurationMs)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = dependent
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        board.Children.Add(animation);
    }

    private void OnLayoutUpdated(object? sender, object args)
    {
        if (_arranging || _detached) return;
        if (!CanAnimate) Stop();
        else if (IsAnimating) PositionRemovedRows();
        else RememberRows();
    }

    private void OnSurfaceSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (_clip is not null) _clip.Rect = new Rect(0, 0, args.NewSize.Width, args.NewSize.Height);
        if (!_arranging && IsAnimating && Math.Abs(args.NewSize.Width - args.PreviousSize.Width) >= 0.5) Stop();
    }

    private void OnSectionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SettingSection.IsVisible) && !_section.IsVisible) Stop();
    }

    private void OnVisibilityChanged(DependencyObject sender, DependencyProperty property)
    {
        if (!CanAnimate) Stop();
    }

    private void OnSystemPreferenceChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args)
    {
        if (_list.DispatcherQueue.HasThreadAccess) ApplyPreference();
        else _list.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.High, ApplyPreference);
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (!_list.IsLoaded) Detach();
    }

    internal void Stop()
    {
        if (_detached) return;
        _generation++;
        _queued = false;
        var board = _board;
        _board = null;
        if (board is not null && _completed is not null) board.Completed -= _completed;
        _completed = null;
        board?.Stop();
        foreach (var (target, restore) in _animated)
        {
            RestoreLocal(target, UIElement.OpacityProperty, restore.Opacity);
            RestoreLocal(target, UIElement.RenderTransformProperty, restore.Transform);
        }
        _animated.Clear();
        if (_ghost is not null)
        {
            _ghost.IsOpen = false;
            _ghost.Child = null;
        }
        _ghostRows?.Children.Clear();
        _ghostPositions.Clear();
        _ghost = null;
        _ghostRows = null;
        if (_surface is not null)
        {
            _surface.Height = double.NaN;
            _surface.Clip = null;
        }
        _clip = null;
        _fromRows = [];
    }

    private static void RestoreLocal(DependencyObject target, DependencyProperty property, object value)
    {
        if (ReferenceEquals(value, DependencyProperty.UnsetValue)) target.ClearValue(property);
        else target.SetValue(property, value);
    }

    internal void Detach()
    {
        if (_detached) return;
        Stop();
        _detached = true;
        _section.Rows.CollectionChanged -= OnChanged;
        _section.PropertyChanged -= OnSectionChanged;
        _list.LayoutUpdated -= OnLayoutUpdated;
        _list.Unloaded -= OnUnloaded;
        HomeMotion.PreferenceChanged -= ApplyPreference;
        _systemSettings.AnimationsEnabledChanged -= OnSystemPreferenceChanged;
        if (_surface is not null) _surface.SizeChanged -= OnSurfaceSizeChanged;
        foreach (var (element, token) in _visibilitySubscriptions)
            element.UnregisterPropertyChangedCallback(UIElement.VisibilityProperty, token);
        _visibilitySubscriptions.Clear();
        _known.Clear();
        _list.ItemContainerTransitions = new TransitionCollection();
    }

    public void Dispose() => Detach();

    private sealed record RowFrame(SettingRow Row, FrameworkElement Target, Rect Bounds, double Opacity);
    private sealed class RowRestore(object opacity, object transform)
    {
        internal object Opacity { get; } = opacity;
        internal object Transform { get; } = transform;
        internal TranslateTransform? Drift { get; set; }
    }
}
