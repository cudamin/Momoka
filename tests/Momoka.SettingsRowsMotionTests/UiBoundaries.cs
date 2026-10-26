// UI/clock boundaries only. SettingsRowsMotion.cs is compiled unchanged from the production tree.
using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Windows.Foundation
{
    public delegate void TypedEventHandler<TSender, TArgs>(TSender sender, TArgs args);
    public readonly record struct Point(double X, double Y);
    public readonly record struct Size(double Width, double Height);
    public readonly record struct Rect(double X, double Y, double Width, double Height)
    {
        public double Left => X;
        public double Top => Y;
        public double Right => X + Width;
        public double Bottom => Y + Height;
    }
}

namespace Microsoft.UI.Dispatching
{
    public enum DispatcherQueuePriority { Low = -10, Normal = 0, High = 10 }

    public sealed class DispatcherQueue
    {
        private readonly List<(DispatcherQueuePriority Priority, Action Callback)> _pending = [];
        public bool HasThreadAccess { get; set; } = true;
        public bool RejectEnqueue { get; set; }
        public int PendingCount => _pending.Count;
        public bool TryEnqueue(DispatcherQueuePriority priority, Action callback)
        {
            if (RejectEnqueue) return false;
            _pending.Add((priority, callback));
            return true;
        }
        public void DrainOne()
        {
            if (_pending.Count == 0) return;
            var next = _pending.OrderByDescending(item => item.Priority).First();
            _pending.Remove(next);
            var previous = HasThreadAccess;
            HasThreadAccess = true;
            try { next.Callback(); }
            finally { HasThreadAccess = previous; }
        }
        public void Drain()
        {
            for (var count = 0; _pending.Count > 0; count++)
            {
                if (count == 1000) throw new InvalidOperationException("Unbounded dispatcher work.");
                DrainOne();
            }
        }
    }
}

namespace Microsoft.UI.Xaml
{
    public delegate void DependencyPropertyChangedCallback(DependencyObject sender, DependencyProperty property);
    public delegate void RoutedEventHandler(object sender, RoutedEventArgs args);
    public delegate void SizeChangedEventHandler(object sender, SizeChangedEventArgs args);
    public sealed class RoutedEventArgs : EventArgs;
    public enum Visibility { Visible, Collapsed }
    public enum ElementTheme { Default, Light, Dark }
    public enum HorizontalAlignment { Left, Center, Right, Stretch }
    public enum VerticalAlignment { Top, Center, Bottom, Stretch }
    public enum FlowDirection { LeftToRight, RightToLeft }
    public sealed class XamlRoot;
    public readonly record struct Thickness(double Left, double Top, double Right, double Bottom)
    {
        public Thickness(double all) : this(all, all, all, all) { }
    }
    public readonly record struct Duration(TimeSpan TimeSpan);
    public sealed class SizeChangedEventArgs(Windows.Foundation.Size previous, Windows.Foundation.Size current) : EventArgs
    {
        public Windows.Foundation.Size PreviousSize { get; } = previous;
        public Windows.Foundation.Size NewSize { get; } = current;
    }
    public sealed record DependencyProperty(string Name, object? DefaultValue)
    {
        public static object UnsetValue { get; } = new();
    }

    public class DependencyObject
    {
        private readonly Dictionary<DependencyProperty, object?> _locals = [];
        private readonly Dictionary<string, (object Owner, double Value)> _animated = [];
        private readonly Dictionary<long, (DependencyProperty Property, DependencyPropertyChangedCallback Callback)> _callbacks = [];
        private long _token;
        public Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; set; } = new();
        public DependencyObject? VisualParent { get; internal set; }
        public List<DependencyObject> VisualChildren { get; } = [];
        public int PropertySubscriptionCount => _callbacks.Count;
        public object ReadLocalValue(DependencyProperty property) => _locals.TryGetValue(property, out var value)
            ? value! : DependencyProperty.UnsetValue;
        public object? GetValue(DependencyProperty property)
        {
            if (_animated.TryGetValue(property.Name, out var animation)) return animation.Value;
            return _locals.TryGetValue(property, out var value) ? value : property.DefaultValue;
        }
        public void SetValue(DependencyProperty property, object? value)
        {
            var before = GetValue(property);
            _locals[property] = value;
            Notify(property, before);
        }
        public void ClearValue(DependencyProperty property)
        {
            var before = GetValue(property);
            _locals.Remove(property);
            Notify(property, before);
        }
        private void Notify(DependencyProperty property, object? before)
        {
            if (Equals(before, GetValue(property))) return;
            foreach (var item in _callbacks.Values.ToArray())
                if (item.Property == property) item.Callback(this, property);
        }
        public long RegisterPropertyChangedCallback(DependencyProperty property, DependencyPropertyChangedCallback callback)
        {
            _callbacks.Add(++_token, (property, callback));
            return _token;
        }
        public void UnregisterPropertyChangedCallback(DependencyProperty property, long token)
        {
            if (!_callbacks.TryGetValue(token, out var item) || item.Property != property)
                throw new InvalidOperationException("Wrong property subscription ownership.");
            _callbacks.Remove(token);
        }
        internal void Animate(string property, object owner, double value) => _animated[property] = (owner, value);
        internal void ClearAnimation(string property, object owner)
        {
            if (_animated.TryGetValue(property, out var active) && ReferenceEquals(active.Owner, owner)) _animated.Remove(property);
        }
        public void AddVisualChild(DependencyObject child)
        {
            if (child.VisualParent is not null) throw new InvalidOperationException("Live controls must not be reparented.");
            VisualChildren.Add(child);
            child.VisualParent = this;
        }
        public void RemoveVisualChild(DependencyObject child)
        {
            if (!VisualChildren.Remove(child)) throw new InvalidOperationException("Not an owned visual child.");
            child.VisualParent = null;
        }
    }

    public class UIElement : DependencyObject
    {
        public static DependencyProperty OpacityProperty { get; } = new("Opacity", 1d);
        public static DependencyProperty RenderTransformProperty { get; } = new("RenderTransform", null);
        public static DependencyProperty VisibilityProperty { get; } = new("Visibility", Visibility.Visible);
        public double Opacity { get => (double)GetValue(OpacityProperty)!; set => SetValue(OpacityProperty, value); }
        public Transform? RenderTransform { get => (Transform?)GetValue(RenderTransformProperty); set => SetValue(RenderTransformProperty, value); }
        public Visibility Visibility { get => (Visibility)GetValue(VisibilityProperty)!; set => SetValue(VisibilityProperty, value); }
        public XamlRoot? XamlRoot { get; set; } = new();
        public RectangleGeometry? Clip { get; set; }
        public bool IsHitTestVisible { get; set; } = true;
        public bool IsTabStop { get; set; } = true;
    }

    public class FrameworkElement : UIElement
    {
        public static DependencyProperty HeightProperty { get; } = new("Height", double.NaN);
        private EventHandler<object>? _layoutUpdated;
        private RoutedEventHandler? _unloaded;
        private SizeChangedEventHandler? _sizeChanged;
        public event EventHandler<object>? LayoutUpdated { add => _layoutUpdated += value; remove => _layoutUpdated -= value; }
        public event RoutedEventHandler? Unloaded { add => _unloaded += value; remove => _unloaded -= value; }
        public event SizeChangedEventHandler? SizeChanged { add => _sizeChanged += value; remove => _sizeChanged -= value; }
        public int LayoutSubscriptionCount => _layoutUpdated?.GetInvocationList().Length ?? 0;
        public int UnloadedSubscriptionCount => _unloaded?.GetInvocationList().Length ?? 0;
        public int SizeSubscriptionCount => _sizeChanged?.GetInvocationList().Length ?? 0;
        public bool IsLoaded { get; set; } = true;
        public double Width { get; set; } = double.NaN;
        public double Height { get => (double)GetValue(HeightProperty)!; set => SetValue(HeightProperty, value); }
        public double NaturalHeight { get; set; } = 50;
        public double ActualHeight => double.IsNaN(Height) ? NaturalHeight : Height;
        public double ActualWidth { get; set; } = 600;
        public double LayoutX { get; set; }
        public double LayoutY { get; set; }
        public object? DataContext { get; set; }
        public ElementTheme RequestedTheme { get; set; } = ElementTheme.Dark;
        public ElementTheme ActualTheme => RequestedTheme;
        public FlowDirection FlowDirection { get; set; }
        public Action? OnUpdateLayout { get; set; }
        public virtual void UpdateLayout()
        {
            OnUpdateLayout?.Invoke();
            RaiseLayout();
        }
        public void RaiseLayout() => _layoutUpdated?.Invoke(this, new());
        public void RaiseUnload() => _unloaded?.Invoke(this, new());
        public void Resize(double width, double naturalHeight)
        {
            var previous = new Windows.Foundation.Size(ActualWidth, ActualHeight);
            ActualWidth = width;
            NaturalHeight = naturalHeight;
            _sizeChanged?.Invoke(this, new(previous, new(ActualWidth, ActualHeight)));
        }
        private Windows.Foundation.Point Origin()
        {
            var x = LayoutX;
            var y = LayoutY + DriftY(RenderTransform);
            for (var parent = VisualParent; parent is not null; parent = parent.VisualParent)
                if (parent is FrameworkElement element)
                {
                    x += element.LayoutX;
                    y += element.LayoutY + DriftY(element.RenderTransform);
                }
            return new(x, y);
        }
        internal static double DriftY(Transform? transform) => transform switch
        {
            TranslateTransform drift => drift.Y,
            TransformGroup group => group.Children.Sum(DriftY),
            _ => 0
        };
        public GeneralTransform TransformToVisual(UIElement? relative)
        {
            var own = Origin();
            var other = relative is FrameworkElement element ? element.Origin() : new Windows.Foundation.Point();
            return new(own.X - other.X, own.Y - other.Y);
        }
    }

    public sealed class DataTemplate;
}

namespace Microsoft.UI.Xaml.Controls
{
    public class ItemsControl : FrameworkElement
    {
        private readonly Dictionary<object, FrameworkElement> _containers = new(ReferenceEqualityComparer.Instance);
        public object? ItemsSource { get; set; }
        public DataTemplate? ItemTemplate { get; set; }
        public DataTemplateSelector? ItemTemplateSelector { get; set; }
        public TransitionCollection? ItemContainerTransitions { get; set; }
        public FrameworkElement? ContainerFromItem(object row) => _containers.GetValueOrDefault(row);
        public FrameworkElement? ContainerFromIndex(int index) => ItemsSource is IEnumerable<Momoka.Shell.ViewModels.SettingRow> rows
            ? ContainerFromItem(rows.ElementAt(index)) : null;
        public void SetContainer(object row, FrameworkElement container)
        {
            _containers.Add(row, container);
            AddVisualChild(container);
        }
        public void RemoveContainer(object row)
        {
            if (_containers.Remove(row, out var container)) RemoveVisualChild(container);
        }
    }
    public class DataTemplateSelector;
    public sealed class Border : FrameworkElement
    {
        public Thickness Padding { get; set; } = new(6);
    }
    public sealed class Grid : FrameworkElement;
    public sealed class ScrollView : FrameworkElement;
    public sealed class ScrollViewer : FrameworkElement;

    public sealed class ContentPresenter : FrameworkElement
    {
        public object? Content { get; set; }
        public DataTemplate? ContentTemplate { get; set; }
        public DataTemplateSelector? ContentTemplateSelector { get; set; }
        public HorizontalAlignment HorizontalContentAlignment { get; set; }
        public VerticalAlignment VerticalContentAlignment { get; set; }
    }

    public sealed class Canvas : FrameworkElement
    {
        public Canvas() => Children = new(this);
        public ElementCollection Children { get; }
        public static void SetLeft(FrameworkElement element, double value) => element.LayoutX = value;
        public static void SetTop(FrameworkElement element, double value) => element.LayoutY = value;
        public override void UpdateLayout()
        {
            foreach (var child in Children.OfType<ContentPresenter>())
                if (child.VisualChildren.Count == 0)
                {
                    // One inert nested interactive endpoint checks recursive hit/tab/accessibility cleanup.
                    var root = new Grid();
                    root.AddVisualChild(new Grid());
                    child.AddVisualChild(root);
                }
            base.UpdateLayout();
        }
    }
    public sealed class ElementCollection(FrameworkElement owner) : Collection<UIElement>
    {
        protected override void InsertItem(int index, UIElement item)
        {
            owner.AddVisualChild(item);
            base.InsertItem(index, item);
        }
        protected override void RemoveItem(int index)
        {
            owner.RemoveVisualChild(this[index]);
            base.RemoveItem(index);
        }
        protected override void ClearItems()
        {
            foreach (var item in this) owner.RemoveVisualChild(item);
            base.ClearItems();
        }
    }
}

namespace Microsoft.UI.Xaml.Controls.Primitives
{
    public sealed class Popup : FrameworkElement
    {
        private UIElement? _child;
        public static List<Popup> Created { get; } = [];
        public Popup() => Created.Add(this);
        public UIElement? Child
        {
            get => _child;
            set
            {
                if (_child is not null) RemoveVisualChild(_child);
                _child = value;
                if (value is not null) AddVisualChild(value);
            }
        }
        public bool IsOpen { get; set; }
        public double HorizontalOffset { get; set; }
        public double VerticalOffset { get; set; }
    }
}

namespace Microsoft.UI.Xaml.Media
{
    public abstract class Transform : DependencyObject;
    public sealed class TranslateTransform : Transform
    {
        public static DependencyProperty YProperty { get; } = new("Y", 0d);
        public double Y { get => (double)GetValue(YProperty)!; set => SetValue(YProperty, value); }
    }
    public sealed class TransformGroup : Transform
    {
        public List<Transform> Children { get; } = [];
    }
    public sealed class GeneralTransform(double x, double y)
    {
        public Windows.Foundation.Rect TransformBounds(Windows.Foundation.Rect bounds) => bounds with { X = bounds.X + x, Y = bounds.Y + y };
        public Windows.Foundation.Point TransformPoint(Windows.Foundation.Point point) => new(point.X + x, point.Y + y);
    }
    public sealed class RectangleGeometry
    {
        public Windows.Foundation.Rect Rect { get; set; }
    }
    public static class VisualTreeHelper
    {
        public static int GetChildrenCount(DependencyObject root) => root.VisualChildren.Count;
        public static DependencyObject GetChild(DependencyObject root, int index) => root.VisualChildren[index];
        public static DependencyObject? GetParent(DependencyObject root) => root.VisualParent;
    }
}

namespace Microsoft.UI.Xaml.Media.Animation
{
    public enum EasingMode { EaseOut }
    public sealed class CubicEase
    {
        public EasingMode EasingMode { get; set; }
    }
    public sealed class DoubleAnimation
    {
        public double? From { get; set; }
        public double? To { get; set; }
        public Duration Duration { get; set; }
        public TimeSpan BeginTime { get; set; }
        public CubicEase? EasingFunction { get; set; }
        public bool EnableDependentAnimation { get; set; }
        public DependencyObject? Target { get; internal set; }
        public string Property { get; internal set; } = "";
    }
    public sealed class Storyboard
    {
        private static readonly List<Storyboard> Running = [];
        private static double _now;
        private double _started;
        private EventHandler<object>? _completed;
        public static bool ThrowOnBegin { get; set; }
        public static int Count => Running.Count;
        public static IReadOnlyList<Storyboard> Active => Running;
        public static List<Storyboard> Begun { get; } = [];
        public List<DoubleAnimation> Children { get; } = [];
        public event EventHandler<object>? Completed { add => _completed += value; remove => _completed -= value; }
        public int CompletionSubscriptionCount => _completed?.GetInvocationList().Length ?? 0;
        public static void SetTarget(DoubleAnimation animation, DependencyObject target) => animation.Target = target;
        public static void SetTargetProperty(DoubleAnimation animation, string property) => animation.Property = property;
        public void Begin()
        {
            if (ThrowOnBegin) throw new InvalidOperationException("Injected Storyboard.Begin failure.");
            _started = _now;
            Running.Add(this);
            Begun.Add(this);
            Step();
        }
        public void Stop()
        {
            foreach (var animation in Children) animation.Target?.ClearAnimation(animation.Property, this);
            Running.Remove(this);
        }
        public Action CapturePostedCompletion()
        {
            var posted = _completed;
            return () => posted?.Invoke(this, new());
        }
        private void Step()
        {
            var finished = true;
            foreach (var animation in Children)
            {
                var elapsed = _now - _started - animation.BeginTime.TotalMilliseconds;
                var progress = Math.Clamp(elapsed / animation.Duration.TimeSpan.TotalMilliseconds, 0, 1);
                if (progress < 1) finished = false;
                var eased = animation.EasingFunction is null ? progress : 1 - Math.Pow(1 - progress, 3);
                var from = animation.From ?? 0;
                var to = animation.To ?? from;
                animation.Target?.Animate(animation.Property, this, from + (to - from) * eased);
            }
            if (finished) _completed?.Invoke(this, new());
        }
        public static void Advance(int milliseconds)
        {
            _now += milliseconds;
            foreach (var board in Running.ToArray()) board.Step();
        }
        public static void Reset()
        {
            foreach (var board in Running.ToArray()) board.Stop();
            Begun.Clear();
            _now = 0;
            ThrowOnBegin = false;
            Microsoft.UI.Xaml.Controls.Primitives.Popup.Created.Clear();
        }
    }
    public sealed class AddDeleteThemeTransition;
    public sealed class RepositionThemeTransition;
    public sealed class TransitionCollection : List<object>;
}

namespace Microsoft.UI.Xaml.Automation.Peers
{
    public enum AccessibilityView { Raw, Control, Content }
}

namespace Microsoft.UI.Xaml.Automation
{
    public static class AutomationProperties
    {
        private static readonly Dictionary<DependencyObject, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView> Values = new(ReferenceEqualityComparer.Instance);
        public static void SetAccessibilityView(DependencyObject element, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView value) => Values[element] = value;
        public static Microsoft.UI.Xaml.Automation.Peers.AccessibilityView GetAccessibilityView(DependencyObject element) => Values.GetValueOrDefault(element, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Control);
    }
}

namespace Windows.UI.ViewManagement
{
    public sealed class UISettingsAnimationsEnabledChangedEventArgs : EventArgs;
    public sealed class UISettings
    {
        private static bool _animationsEnabled = true;
        private Windows.Foundation.TypedEventHandler<UISettings, UISettingsAnimationsEnabledChangedEventArgs>? _changed;
        public static List<UISettings> Created { get; } = [];
        public UISettings() => Created.Add(this);
        public bool AnimationsEnabled => _animationsEnabled;
        public int SubscriptionCount => _changed?.GetInvocationList().Length ?? 0;
        public event Windows.Foundation.TypedEventHandler<UISettings, UISettingsAnimationsEnabledChangedEventArgs>? AnimationsEnabledChanged
        {
            add => _changed += value;
            remove => _changed -= value;
        }
        public static void SetAnimationsEnabled(bool enabled)
        {
            if (_animationsEnabled == enabled) return;
            _animationsEnabled = enabled;
            foreach (var settings in Created.ToArray()) settings._changed?.Invoke(settings, new());
        }
        public static void Reset()
        {
            if (Created.Any(settings => settings.SubscriptionCount != 0)) throw new InvalidOperationException("Leaked system animation preference subscription.");
            Created.Clear();
            _animationsEnabled = true;
        }
    }
}

namespace Momoka.Shell.ViewModels
{
    // Availability/data boundary: real observable delivery, no duplicated animation or generation logic.
    public abstract class SettingRow(string label)
    {
        public string Label { get; } = label;
    }
    public sealed class TestRow(string label) : SettingRow(label);
    public sealed class OtherTestRow(string label) : SettingRow(label);
    public sealed class SettingSection : INotifyPropertyChanged
    {
        private bool _isVisible = true;
        public ObservableCollection<SettingRow> Rows { get; } = [];
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (_isVisible == value) return;
                _isVisible = value;
                PropertyChanged?.Invoke(this, new(nameof(IsVisible)));
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        public int PropertySubscriptionCount => PropertyChanged?.GetInvocationList().Length ?? 0;
    }
}

namespace Momoka.Shell.Views
{
    internal static class HomeMotion
    {
        private static bool _preference = true;
        private static Action? _changed;
        internal static bool AnimationsEnabled => _preference && Windows.UI.ViewManagement.UISettings.Created.LastOrDefault()?.AnimationsEnabled != false;
        internal static int PreferenceSubscriptionCount => _changed?.GetInvocationList().Length ?? 0;
        internal static event Action? PreferenceChanged { add => _changed += value; remove => _changed -= value; }
        internal static void ApplyPreference(bool enabled)
        {
            if (_preference == enabled) return;
            _preference = enabled;
            _changed?.Invoke();
        }
        internal static void Reset()
        {
            if (PreferenceSubscriptionCount != 0) throw new InvalidOperationException("Leaked application animation preference subscription.");
            _preference = true;
        }
    }
}
