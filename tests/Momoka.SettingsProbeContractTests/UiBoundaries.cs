using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Momoka.Tests;

// No HWND, WinRT, desktop, real XAML or animation clock exists in these boundaries. The actual
// page/row/HomeMotion bodies are source-linked; view-model behavior always lives in Momoka.dll.
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

namespace Windows.UI.ViewManagement
{
    public sealed class UISettingsAnimationsEnabledChangedEventArgs : EventArgs;
    public sealed class UISettings
    {
        private static bool _systemAnimations = true;
        private static Windows.Foundation.TypedEventHandler<UISettings, UISettingsAnimationsEnabledChangedEventArgs>? _changed;
        internal static bool SystemAnimations
        {
            get => _systemAnimations;
            set
            {
                if (_systemAnimations == value) return;
                _systemAnimations = value;
                _changed?.Invoke(new(), new());
            }
        }
        internal static int Subscribers => _changed?.GetInvocationList().Length ?? 0;
        public bool AnimationsEnabled => SystemAnimations;
        public event Windows.Foundation.TypedEventHandler<UISettings, UISettingsAnimationsEnabledChangedEventArgs>? AnimationsEnabledChanged
        {
            add => _changed += value;
            remove => _changed -= value;
        }
    }
}

namespace Microsoft.UI.Dispatching
{
    public enum DispatcherQueuePriority { Low = -10, Normal = 0, High = 10 }
    public sealed class DispatcherQueue
    {
        private readonly List<(DispatcherQueuePriority Priority, Action Callback)> _pending = [];
        public int Count => _pending.Count;
        public bool HasThreadAccess { get; set; } = true;
        public bool RejectEnqueue { get; set; }
        public bool TryEnqueue(Action action) => TryEnqueue(DispatcherQueuePriority.Normal, action);
        public bool TryEnqueue(DispatcherQueuePriority priority, Action action)
        {
            if (RejectEnqueue) return false;
            _pending.Add((priority, action));
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
                if (count == 1000) throw new InvalidOperationException("Unbounded boundary dispatcher work");
                DrainOne();
            }
        }
    }
}

namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
    public enum ElementTheme { Default, Light, Dark }
    public enum HorizontalAlignment { Left, Center, Right, Stretch }
    public enum VerticalAlignment { Top, Center, Bottom, Stretch }
    public enum FlowDirection { LeftToRight, RightToLeft }
    public readonly record struct Thickness(double Left, double Top, double Right, double Bottom)
    {
        public Thickness(double all) : this(all, all, all, all) { }
    }
    public sealed class RoutedEventArgs : EventArgs;
    public delegate void RoutedEventHandler(object sender, RoutedEventArgs args);
    public delegate void SizeChangedEventHandler(object sender, SizeChangedEventArgs args);
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
        private readonly Dictionary<string, (object Owner, double Value)> _animations = [];
        private readonly Dictionary<long, (DependencyProperty Property, Action<DependencyObject, DependencyProperty> Callback)> _callbacks = [];
        private long _nextCallback;
        internal List<DependencyObject> VisualChildren { get; } = [];
        internal DependencyObject? VisualParent { get; private set; }
        public Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; } = new();
        internal int PropertySubscriptions => _callbacks.Count;
        public object ReadLocalValue(DependencyProperty property) => _locals.TryGetValue(property, out var value) ? value! : DependencyProperty.UnsetValue;
        public object? GetValue(DependencyProperty property) => _animations.TryGetValue(property.Name, out var animation)
            ? animation.Value : _locals.TryGetValue(property, out var value) ? value : property.DefaultValue;
        public void SetValue(DependencyProperty property, object? value)
        {
            var previous = GetValue(property);
            _locals[property] = value;
            Notify(property, previous);
        }
        public void ClearValue(DependencyProperty property)
        {
            var previous = GetValue(property);
            _locals.Remove(property);
            Notify(property, previous);
        }
        private void Notify(DependencyProperty property, object? previous)
        {
            if (Equals(previous, GetValue(property))) return;
            foreach (var item in _callbacks.Values.ToArray())
                if (item.Property == property) item.Callback(this, property);
        }
        public long RegisterPropertyChangedCallback(DependencyProperty property, Action<DependencyObject, DependencyProperty> callback)
        {
            var token = ++_nextCallback;
            _callbacks.Add(token, (property, callback));
            return token;
        }
        public void UnregisterPropertyChangedCallback(DependencyProperty property, long token)
        {
            if (!_callbacks.TryGetValue(token, out var item) || item.Property != property)
                throw new InvalidOperationException("Wrong boundary property subscription");
            _callbacks.Remove(token);
        }
        internal void Animate(string property, object owner, double value) => _animations[property] = (owner, value);
        internal void ClearAnimation(string property, object owner)
        {
            if (_animations.TryGetValue(property, out var active) && ReferenceEquals(active.Owner, owner)) _animations.Remove(property);
        }
        internal void AddVisual(DependencyObject child)
        {
            if (child.VisualParent is not null) throw new InvalidOperationException("Already parented boundary element");
            VisualChildren.Add(child);
            child.VisualParent = this;
        }
        internal void RemoveVisual(DependencyObject child)
        {
            if (!VisualChildren.Remove(child)) throw new InvalidOperationException("Not an owned boundary element");
            child.VisualParent = null;
        }
        internal void ClearVisuals()
        {
            foreach (var child in VisualChildren) child.VisualParent = null;
            VisualChildren.Clear();
        }
    }
    public class UIElement : DependencyObject
    {
        public static DependencyProperty OpacityProperty { get; } = new("Opacity", 1d);
        public static DependencyProperty RenderTransformProperty { get; } = new("RenderTransform", null);
        public static DependencyProperty VisibilityProperty { get; } = new("Visibility", Visibility.Visible);
        public double Opacity { get => (double)GetValue(OpacityProperty)!; set => SetValue(OpacityProperty, value); }
        public Microsoft.UI.Xaml.Media.Transform? RenderTransform { get => (Microsoft.UI.Xaml.Media.Transform?)GetValue(RenderTransformProperty); set => SetValue(RenderTransformProperty, value); }
        public Visibility Visibility { get => (Visibility)GetValue(VisibilityProperty)!; set => SetValue(VisibilityProperty, value); }
        public bool IsHitTestVisible { get; set; } = true;
        public bool IsTabStop { get; set; } = true;
    }
    public class FrameworkElement : UIElement
    {
        public static DependencyProperty HeightProperty { get; } = new("Height", double.NaN);
        private double _actualHeight = 120;
        public string Name { get; set; } = "";
        public object? Tag { get; set; }
        public object? DataContext { get; set; }
        public object? XamlRoot { get; set; }
        public bool IsLoaded { get; private set; }
        public double ActualHeight { get => double.IsNaN(Height) ? _actualHeight : Height; set => _actualHeight = value; }
        public double ActualWidth { get; set; } = 400;
        public double Width { get; set; } = double.NaN;
        public double Height { get => (double)GetValue(HeightProperty)!; set => SetValue(HeightProperty, value); }
        public double Top { get; set; }
        public double Left { get; set; }
        public ElementTheme RequestedTheme { get; set; }
        public ElementTheme ActualTheme => RequestedTheme;
        public FlowDirection FlowDirection { get; set; }
        public Microsoft.UI.Xaml.Media.RectangleGeometry? Clip { get; set; }
        public Action? OnUpdateLayout { get; set; }
        public event RoutedEventHandler? Loaded;
        public event RoutedEventHandler? Unloaded;
        public event SizeChangedEventHandler? SizeChanged;
        public event EventHandler<object>? LayoutUpdated;
        public void RaiseLoaded() { IsLoaded = true; Loaded?.Invoke(this, new()); }
        public void RaiseUnloaded() { IsLoaded = false; Unloaded?.Invoke(this, new()); }
        public void RaiseSizeChanged(double width, double height)
        {
            var previous = new Windows.Foundation.Size(ActualWidth, ActualHeight);
            ActualWidth = width;
            ActualHeight = height;
            SizeChanged?.Invoke(this, new(previous, new(width, ActualHeight)));
        }
        public virtual void UpdateLayout() { OnUpdateLayout?.Invoke(); RaiseLayoutUpdated(); }
        public void RaiseLayoutUpdated() => LayoutUpdated?.Invoke(this, new());
        private Windows.Foundation.Point Origin()
        {
            var x = Left;
            var y = Top + DriftY(RenderTransform);
            for (var parent = VisualParent; parent is not null; parent = parent.VisualParent)
                if (parent is FrameworkElement element)
                {
                    x += element.Left;
                    y += element.Top + DriftY(element.RenderTransform);
                }
            return new(x, y);
        }
        private static double DriftY(Microsoft.UI.Xaml.Media.Transform? transform) => transform switch
        {
            Microsoft.UI.Xaml.Media.TranslateTransform drift => drift.Y,
            Microsoft.UI.Xaml.Media.TransformGroup group => group.Children.Sum(DriftY),
            _ => 0
        };
        public Microsoft.UI.Xaml.Media.GeneralTransform TransformToVisual(UIElement? target)
        {
            var own = Origin();
            var other = target is FrameworkElement element ? element.Origin() : new Windows.Foundation.Point();
            return new(own.X - other.X, own.Y - other.Y);
        }
    }
    public sealed class DataTemplate;
    public readonly record struct Duration(TimeSpan TimeSpan);
}

namespace Microsoft.UI.Xaml.Navigation
{
    public enum NavigationCacheMode { Required }
}

namespace Microsoft.UI.Xaml.Controls
{
    public enum ScrollingAnimationMode { Disabled }
    public enum ScrollingSnapPointsMode { Ignore }
    public sealed class ScrollingScrollOptions(ScrollingAnimationMode animation, ScrollingSnapPointsMode snap)
    {
        public ScrollingAnimationMode AnimationMode { get; } = animation;
        public ScrollingSnapPointsMode SnapPointsMode { get; } = snap;
    }
    public sealed class ScrollView : Microsoft.UI.Xaml.FrameworkElement
    {
        public int ScrollResets { get; private set; }
        public ScrollingScrollOptions? LastOptions { get; private set; }
        public int ScrollTo(double x, double y, ScrollingScrollOptions options) { LastOptions = options; ScrollResets++; return ScrollResets; }
    }

    public class Page : Microsoft.UI.Xaml.FrameworkElement
    {
        public object? Content { get; set; }
        public Microsoft.UI.Xaml.Navigation.NavigationCacheMode NavigationCacheMode { get; set; }
    }
    public class ItemsControl : Microsoft.UI.Xaml.FrameworkElement
    {
        private readonly Dictionary<object, Microsoft.UI.Xaml.FrameworkElement> _containers = new(ReferenceEqualityComparer.Instance);
        public object? ItemsSource { get; set; }
        public Microsoft.UI.Xaml.DataTemplate? ItemTemplate { get; set; }
        public DataTemplateSelector? ItemTemplateSelector { get; set; }
        internal IEnumerable<object> BoundItems => _containers.Keys.ToArray();
        public Microsoft.UI.Xaml.FrameworkElement? ContainerFromItem(object row) => _containers.GetValueOrDefault(
            row is Momoka.Shell.ViewModels.SettingRow wrapper ? wrapper.Inner : row);
        internal void SetContainer(object row, Microsoft.UI.Xaml.FrameworkElement container) { _containers.Add(row, container); AddVisual(container); }
        internal void RemoveContainer(object row) { if (_containers.Remove(row, out var container)) RemoveVisual(container); }
        public Microsoft.UI.Xaml.Media.Animation.TransitionCollection? ItemContainerTransitions { get; set; }
    }
    public class ListViewBase : ItemsControl { }
    public sealed class ListView : ListViewBase { }
    public class DataTemplateSelector;
    public sealed class Border : Microsoft.UI.Xaml.FrameworkElement
    {
        public Microsoft.UI.Xaml.Thickness Padding { get; set; }
    }
    public sealed class ScrollViewer : Microsoft.UI.Xaml.FrameworkElement
    {
        public int ScrollResets { get; private set; }
        public bool LastDisableAnimation { get; private set; }
        public bool ChangeView(double? x, double? y, float? zoom, bool disableAnimation)
        {
            LastDisableAnimation = disableAnimation;
            ScrollResets++;
            return true;
        }
    }
    public sealed class ContentPresenter : Microsoft.UI.Xaml.FrameworkElement
    {
        public object? Content { get; set; }
        public Microsoft.UI.Xaml.DataTemplate? ContentTemplate { get; set; }
        public DataTemplateSelector? ContentTemplateSelector { get; set; }
        public Microsoft.UI.Xaml.HorizontalAlignment HorizontalContentAlignment { get; set; }
        public Microsoft.UI.Xaml.VerticalAlignment VerticalContentAlignment { get; set; }
    }
    public sealed class Canvas : Microsoft.UI.Xaml.FrameworkElement
    {
        public Canvas() => Children = new(this);
        public ElementCollection Children { get; }
        public static void SetLeft(Microsoft.UI.Xaml.FrameworkElement element, double value) => element.Left = value;
        public static void SetTop(Microsoft.UI.Xaml.FrameworkElement element, double value) => element.Top = value;
        public override void UpdateLayout()
        {
            foreach (var presenter in Children.OfType<ContentPresenter>())
                if (presenter.VisualChildren.Count == 0)
                {
                    var root = new Grid();
                    root.AddVisual(new TextBlock { Text = ((Momoka.Shell.ViewModels.SettingRow)presenter.Content!).Label });
                    presenter.AddVisual(root);
                }
            base.UpdateLayout();
        }
    }
    public sealed class ElementCollection(Microsoft.UI.Xaml.FrameworkElement owner) : Collection<Microsoft.UI.Xaml.UIElement>
    {
        protected override void InsertItem(int index, Microsoft.UI.Xaml.UIElement item) { owner.AddVisual(item); base.InsertItem(index, item); }
        protected override void RemoveItem(int index) { owner.RemoveVisual(this[index]); base.RemoveItem(index); }
        protected override void ClearItems() { foreach (var item in this) owner.RemoveVisual(item); base.ClearItems(); }
    }
    public sealed class Grid : Microsoft.UI.Xaml.FrameworkElement { }
    public sealed class TextBlock : Microsoft.UI.Xaml.FrameworkElement
    {
        public string Text { get; set; } = "";
    }
    public sealed class ToggleSwitch : Microsoft.UI.Xaml.FrameworkElement
    {
        public required Action Toggled { get; init; }
    }
    public sealed class Frame : Microsoft.UI.Xaml.FrameworkElement
    {
        public object? Content { get; set; }
    }
    public sealed class CollectionViewSource
    {
        private object? _source;
        public int Writes { get; private set; }
        public object? Source { get => _source; set { _source = value; Writes++; } }
    }
    public sealed class DragItemsStartingEventArgs : EventArgs
    {
        public bool Cancel { get; set; }
    }
    public sealed class DragItemsCompletedEventArgs : EventArgs { }
    public sealed class ItemsSourceView
    {
        public int Count { get; set; }
    }
    public sealed class ItemsRepeater : Microsoft.UI.Xaml.FrameworkElement
    {
        public ItemsSourceView? ItemsSourceView { get; set; }
        public event Action<ItemsRepeater, ItemsRepeaterElementPreparedEventArgs>? ElementPrepared;
        public event Action<ItemsRepeater, ItemsRepeaterElementClearingEventArgs>? ElementClearing;
        public Microsoft.UI.Xaml.FrameworkElement? TryGetElement(int index) => VisualChildren.ElementAtOrDefault(index) as Microsoft.UI.Xaml.FrameworkElement;
        public void Prepare(Microsoft.UI.Xaml.FrameworkElement element, int index) => ElementPrepared?.Invoke(this, new(element, index));
        public void Clear(Microsoft.UI.Xaml.FrameworkElement element) => ElementClearing?.Invoke(this, new(element));
    }
    public sealed class ItemsRepeaterElementPreparedEventArgs(Microsoft.UI.Xaml.FrameworkElement element, int index)
    {
        public Microsoft.UI.Xaml.FrameworkElement Element { get; } = element;
        public int Index { get; } = index;
    }
    public sealed class ItemsRepeaterElementClearingEventArgs(Microsoft.UI.Xaml.FrameworkElement element)
    {
        public Microsoft.UI.Xaml.FrameworkElement Element { get; } = element;
    }
}

namespace Microsoft.UI.Xaml.Controls.Primitives
{
    public sealed class Popup : Microsoft.UI.Xaml.FrameworkElement
    {
        private Microsoft.UI.Xaml.UIElement? _child;
        internal static List<Popup> Created { get; } = [];
        public Popup() => Created.Add(this);
        public Microsoft.UI.Xaml.UIElement? Child
        {
            get => _child;
            set
            {
                if (_child is not null) RemoveVisual(_child);
                _child = value;
                if (value is not null) AddVisual(value);
            }
        }
        public bool IsOpen { get; set; }
        public double HorizontalOffset { get; set; }
        public double VerticalOffset { get; set; }
    }
}

namespace Microsoft.UI.Xaml.Media
{
    public abstract class Transform : Microsoft.UI.Xaml.DependencyObject;
    public sealed class TranslateTransform : Transform
    {
        public static Microsoft.UI.Xaml.DependencyProperty YProperty { get; } = new("Y", 0d);
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
        public static int GetChildrenCount(Microsoft.UI.Xaml.DependencyObject root) => root.VisualChildren.Count;
        public static Microsoft.UI.Xaml.DependencyObject GetChild(Microsoft.UI.Xaml.DependencyObject root, int index) => root.VisualChildren[index];
        public static Microsoft.UI.Xaml.DependencyObject? GetParent(Microsoft.UI.Xaml.DependencyObject root) => root.VisualParent;
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
        public Microsoft.UI.Xaml.Duration Duration { get; set; }
        public TimeSpan BeginTime { get; set; }
        public CubicEase? EasingFunction { get; set; }
        public bool EnableDependentAnimation { get; set; }
        internal Microsoft.UI.Xaml.DependencyObject? Target { get; set; }
        internal string Property { get; set; } = "";
    }
    public sealed class Storyboard
    {
        private static readonly List<Storyboard> Running = [];
        private static double _now;
        private double _started;
        private EventHandler<object>? _completed;
        public List<DoubleAnimation> Children { get; } = [];
        internal static IReadOnlyList<Storyboard> Active => Running.ToArray();
        internal int CompletionSubscriptions => _completed?.GetInvocationList().Length ?? 0;
        public event EventHandler<object>? Completed { add => _completed += value; remove => _completed -= value; }
        public static void SetTarget(DoubleAnimation animation, Microsoft.UI.Xaml.DependencyObject target) => animation.Target = target;
        public static void SetTargetProperty(DoubleAnimation animation, string property) => animation.Property = property;
        public void Begin() { _started = _now; Running.Add(this); Step(); }
        public void Stop()
        {
            foreach (var animation in Children) animation.Target?.ClearAnimation(animation.Property, this);
            Running.Remove(this);
        }
        internal Action CapturePostedCompletion()
        {
            var posted = _completed;
            return () => posted?.Invoke(this, new());
        }
        public static int Count => Running.Count;
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
                animation.Target?.Animate(animation.Property, this, from + ((animation.To ?? from) - from) * eased);
            }
            if (finished) _completed?.Invoke(this, new());
        }
        internal static void Advance(int milliseconds)
        {
            _now += milliseconds;
            foreach (var board in Running.ToArray()) board.Step();
        }
        public static void CompleteAll()
        {
            foreach (var board in Running.ToArray())
            {
                _now = Math.Max(_now, board._started + board.Children.Max(animation => animation.BeginTime.TotalMilliseconds + animation.Duration.TimeSpan.TotalMilliseconds));
                board.Step();
                board.Stop();
            }
        }
    }
    public sealed class TransitionCollection : List<object>;
}

namespace Microsoft.UI.Xaml.Automation
{
    public static class AutomationProperties
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Microsoft.UI.Xaml.DependencyObject, View> Views = new();
        public static void SetAccessibilityView(Microsoft.UI.Xaml.DependencyObject element, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView value) => Views.GetOrCreateValue(element).Value = value;
        internal static Microsoft.UI.Xaml.Automation.Peers.AccessibilityView GetAccessibilityView(Microsoft.UI.Xaml.DependencyObject element) => Views.TryGetValue(element, out var view) ? view.Value : Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Content;
        private sealed class View
        {
            public Microsoft.UI.Xaml.Automation.Peers.AccessibilityView Value { get; set; }
        }
    }
}

namespace Microsoft.UI.Xaml.Automation.Peers
{
    public enum AccessibilityView { Raw, Content, Control }
    public enum PatternInterface { Toggle }
    public sealed class ToggleSwitchAutomationPeer(Microsoft.UI.Xaml.Controls.ToggleSwitch control)
    {
        public object GetPattern(PatternInterface pattern) => new ToggleBoundary(control);
        private sealed class ToggleBoundary(Microsoft.UI.Xaml.Controls.ToggleSwitch control) : Microsoft.UI.Xaml.Automation.Provider.IToggleProvider
        {
            public void Toggle() => control.Toggled();
        }
    }
}

namespace Microsoft.UI.Xaml.Automation.Provider
{
    public interface IToggleProvider { void Toggle(); }
}

namespace Momoka.Shell.Views
{
    internal static class ConfirmDialog { internal static object For(object page) => new(); }
    internal static class SettingsFile
    {
        internal static object SaveFor(object page) => new();
        internal static object OpenFor(object page) => new();
    }
    public sealed partial class SettingsPage
    {
        internal Microsoft.UI.Xaml.Controls.CollectionViewSource CategoryGroupsSource { get; } = new();
        internal readonly Microsoft.UI.Xaml.Controls.Grid SettingsContent = new();
        internal readonly Microsoft.UI.Xaml.Controls.ScrollViewer SettingsScroll = new();
        internal bool PendingSearchForContract { get; set; }
        private bool HasPendingSearchNavigation => PendingSearchForContract; // Real navigation is source-linked in its dedicated fixture.
        internal readonly Microsoft.UI.Xaml.Controls.Grid SettingsLayout = new();
        internal readonly Microsoft.UI.Xaml.Controls.Grid SettingsSections = new();
        internal Microsoft.UI.Xaml.Controls.Frame HostedFrame { get; } = new();
        public object? NavigationRequest => _request;
        internal string SelectedCategory { get => ViewModel.SelectedCategory; set => ViewModel.SelectedCategory = value; }
        private void InitializeComponent()
        {
            AddVisual(SettingsLayout);
            SettingsLayout.AddVisual(SettingsScroll);
            SettingsScroll.AddVisual(SettingsContent);
            SettingsScroll.ActualHeight = 900;
            SettingsContent.AddVisual(SettingsSections);
            SettingsContent.ActualHeight = 900;
        }
        private void ObserveCategoryGroups() => _observedGroups.Clear();
        private void ShowHosted() => HostedReads++;
        private void StopSourceMonitoring() { if (_session is not null) _session = null; }
        private void StopSettingsSaveState() { }
        private void StopMoviePilotMonitoring() { }
        internal int HostedReads { get; private set; }
        internal int CategorySourceWrites => CategoryGroupsSource.Writes;
        internal bool Active => _activity.IsCurrent(_activityToken);
        internal Microsoft.UI.Xaml.Controls.ItemsControl AddRows(object section, bool attach = true)
        {
            var wrapper = new Momoka.Shell.ViewModels.SettingSection(section);
            var surface = new Microsoft.UI.Xaml.Controls.Border();
            var rows = new Microsoft.UI.Xaml.Controls.ItemsControl { Tag = wrapper, ItemsSource = wrapper.Rows, ItemTemplate = new(), XamlRoot = new() };
            SettingsSections.AddVisual(surface);
            surface.AddVisual(rows);
            // Substitute only XAML's container/label binding. Keep actual row identity and live
            // containers; the real VM remains the sole owner of visibility and collection changes.
            void BindRows()
            {
                var current = wrapper.Rows.Select(row => row.Inner).ToHashSet(ReferenceEqualityComparer.Instance);
                foreach (var old in rows.BoundItems.Where(item => !current.Contains(item))) rows.RemoveContainer(old);
                foreach (var row in wrapper.Rows)
                    if (rows.ContainerFromItem(row) is null)
                    {
                        var container = new Microsoft.UI.Xaml.FrameworkElement();
                        container.AddVisual(new Microsoft.UI.Xaml.Controls.TextBlock { Text = row.Label, ActualHeight = 40 });
                        rows.SetContainer(row.Inner, container);
                    }
            }
            rows.OnUpdateLayout = () =>
            {
                for (var index = 0; index < wrapper.Rows.Count; index++) rows.ContainerFromItem(wrapper.Rows[index])!.Top = index * 40;
                surface.ActualHeight = wrapper.Rows.Count * 40;
                rows.ActualHeight = surface.ActualHeight;
            };
            NotifyCollectionChangedEventHandler bind = (_, _) => BindRows();
            wrapper.Rows.CollectionChanged += bind;
            Unloaded += (_, _) => wrapper.Rows.CollectionChanged -= bind;
            BindRows();
            rows.UpdateLayout();
            if (attach)
            {
                rows.Loaded += OnSettingsRowsLoaded;
                rows.Unloaded += OnSettingsRowsUnloaded;
            }
            rows.RaiseLoaded();
            return rows;
        }
        internal Microsoft.UI.Xaml.FrameworkElement AddEntranceSection(double top = 0)
        {
            var element = new Microsoft.UI.Xaml.FrameworkElement { Name = "SettingsSection", Top = top, XamlRoot = new() };
            SettingsSections.AddVisual(element);
            return element;
        }
        internal void AttachRowsForContract(Microsoft.UI.Xaml.Controls.ItemsControl rows) => OnSettingsRowsLoaded(rows, new());
        internal void SetRequest(SettingsRequest? request) => _request = request;
        internal void PrepareCached(SettingsRequest request) { _request = request; _activityToken = default; }
        internal Task RefreshForContract() => RefreshServerAdministrationAsync();
        internal void QueueEntranceForContract() => QueueSettingsEntrance();
        internal void DrainRows()
        {
            foreach (var row in _rowMotions.Keys) row.DispatcherQueue.Drain();
        }
        internal void CompleteRows() => Microsoft.UI.Xaml.Media.Animation.Storyboard.CompleteAll();
    }
    // Priority spring is not the subject of these contracts; reject any accidental priority-row use.
    internal sealed class SettingsPriorityMotion
    {
        internal SettingsPriorityMotion(Microsoft.UI.Xaml.Controls.ListView list, Momoka.Shell.ViewModels.SettingTitleRulesRow row) => throw new InvalidOperationException("Priority-row boundary must not execute");
        internal bool IsAnimating => false;
        internal bool HasSpring => false;
        internal void BeginDrag(Microsoft.UI.Xaml.Controls.DragItemsStartingEventArgs args) => throw new InvalidOperationException("Priority-row boundary must not execute");
        internal void EndDrag() => throw new InvalidOperationException("Priority-row boundary must not execute");
        internal void Stop() { }
        internal void Detach() { }
    }
}

namespace Momoka.Shell.ViewModels
{
    public sealed class SettingTitleRulesRow { }
    // Closing the generic wrapper over the actual production type preserves GetType identity
    // without reproducing any SettingRow data, setters or visibility algorithms.
    public abstract class SettingRow(object inner)
    {
        internal object Inner { get; } = inner;
        public string Label => ShellRuntime.Current.Get<string>(Inner, "Label");
        internal static SettingRow Wrap(object value) => (SettingRow)Activator.CreateInstance(typeof(TypedRow<>).MakeGenericType(value.GetType()), value)!;
        private sealed class TypedRow<T>(object value) : SettingRow(value);
    }
    public sealed class SettingRows(IList inner) : IReadOnlyList<SettingRow>, INotifyCollectionChanged
    {
        private readonly Dictionary<object, SettingRow> _wrappers = new(ReferenceEqualityComparer.Instance);
        internal IList Inner { get; } = inner;
        public int Count => Inner.Count;
        public SettingRow this[int index] => Wrap(Inner[index]!);
        private SettingRow Wrap(object value)
        {
            if (!_wrappers.TryGetValue(value, out var row)) _wrappers.Add(value, row = SettingRow.Wrap(value));
            return row;
        }
        public IEnumerator<SettingRow> GetEnumerator() => Inner.Cast<object>().Select(Wrap).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public event NotifyCollectionChangedEventHandler? CollectionChanged
        {
            add => ((INotifyCollectionChanged)Inner).CollectionChanged += value;
            remove => ((INotifyCollectionChanged)Inner).CollectionChanged -= value;
        }
    }
    public sealed class SettingSection(object inner) : INotifyPropertyChanged
    {
        internal object Inner { get; } = inner;
        public SettingRows Rows { get; } = new(ShellRuntime.Current.Get<IList>(inner, "Rows"));
        public bool IsVisible => ShellRuntime.Current.Get<bool>(Inner, "IsVisible");
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add => ((INotifyPropertyChanged)Inner).PropertyChanged += value;
            remove => ((INotifyPropertyChanged)Inner).PropertyChanged -= value;
        }
    }
    public sealed class SettingsCategoryGroup { }
    public sealed class SettingsViewModel
    {
        internal object Inner { get; } = ShellRuntime.Current.TakePageViewModel();
        private ShellRuntime Runtime => ShellRuntime.Current;
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add => ((INotifyPropertyChanged)Inner).PropertyChanged += value;
            remove => ((INotifyPropertyChanged)Inner).PropertyChanged -= value;
        }
        public object CategoryGroups => Runtime.Get<object>(Inner, "CategoryGroups");
        public bool IsReady => Runtime.Get<bool>(Inner, "IsReady");
        public string SelectedCategory { get => Runtime.Get<string>(Inner, "SelectedCategory"); set => Runtime.Set(Inner, "SelectedCategory", value); }
        public bool ShowsHosted => Runtime.Get<bool>(Inner, "ShowsHosted");
        public string? SelectedSourceKey => Runtime.Get<string?>(Inner, "SelectedSourceKey");
        public void OnDanmakuPreferencesChanged() => Runtime.Invoke(Inner, "OnDanmakuPreferencesChanged");
        public void Activate() => Runtime.Invoke(Inner, "Activate");
        public void Cancel() => Runtime.Invoke(Inner, "Cancel");
        public Task RefreshServerAdministrationAsync() => (Task)Runtime.Invoke(Inner, "RefreshServerAdministrationAsync")!;
        public void UseConfirm(object ignored) { }
        public void UseFilePickers(object save, object open) { }
    }
}

namespace Momoka.Shell
{
    // Event transport only: the subscriber forwards to the current real Shell VM.
    internal static class ShellPrefs
    {
        internal static event Action? DanmakuChanged;
        internal static int DanmakuSubscribers => DanmakuChanged?.GetInvocationList().Length ?? 0;
        internal static void RaiseDanmakuChanged() => DanmakuChanged?.Invoke();
    }

    internal sealed partial class ShellNavigationProbe
    {
        internal static void BeginVisitForContract(Momoka.Shell.Views.SettingsPage page) => BeginSettingsFixtureVisit(page);
        internal static Momoka.Services.IServerCapabilities NewFakeCapabilitiesForContract() => new FakeCapabilities();
        internal static Task WaitForSettingsSettledForContract(Momoka.Shell.Views.SettingsPage page) => WaitForSettingsSettledAsync(page);
        internal static Task WaitForSettingsRowsForContract(Momoka.Shell.Views.SettingsPage page, Microsoft.UI.Xaml.Controls.ItemsControl rows) => WaitForSettingsRowsLoadedAsync(page, rows);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static Task UntilAsync(Func<bool> ready)
        {
            var timer = new System.Diagnostics.Stopwatch();
            timer.Start();
            while (!ready())
            {
                ContractPage?.DispatcherQueue.Drain();
                if (timer.Elapsed > TimeSpan.FromSeconds(2)) throw new TimeoutException("Contract condition not reached");
            }
            if (CompleteBeforeWaitReturns) Microsoft.UI.Xaml.Media.Animation.Storyboard.CompleteAll();
            return Task.CompletedTask;
        }
        private static Task LayoutAsync(Microsoft.UI.Xaml.FrameworkElement element) { element.UpdateLayout(); return Task.CompletedTask; }
        internal static Momoka.Shell.Views.SettingsPage? ContractPage { get; set; }
        internal static bool CompleteBeforeWaitReturns { get; set; }
        private static IEnumerable<Microsoft.UI.Xaml.DependencyObject> Descendants(Microsoft.UI.Xaml.DependencyObject root)
        {
            yield return root;
            for (var index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); index++)
                foreach (var child in Descendants(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, index))) yield return child;
        }
    }
}
