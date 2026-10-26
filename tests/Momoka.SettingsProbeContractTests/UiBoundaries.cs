using System.Collections.Specialized;
using System.ComponentModel;
using Momoka.Tests;

// No HWND, WinRT, desktop, real XAML or animation clock exists in these boundaries. The actual
// page/row/HomeMotion bodies are source-linked; view-model behavior always lives in Momoka.dll.
namespace Windows.Foundation
{
    public readonly record struct Size(double Width, double Height);
    public readonly record struct Rect(double X, double Y, double Width, double Height)
    {
        public double Top => Y;
        public double Bottom => Y + Height;
    }
}

namespace Windows.UI.ViewManagement
{
    public sealed class UISettings
    {
        internal static bool SystemAnimations { get; set; } = true;
        public bool AnimationsEnabled => SystemAnimations;
    }
}

namespace Microsoft.UI.Dispatching
{
    public enum DispatcherQueuePriority { Low }
    public sealed class DispatcherQueue
    {
        private readonly Queue<Action> _pending = new();
        public int Count => _pending.Count;
        public bool TryEnqueue(Action action) { _pending.Enqueue(action); return true; }
        public bool TryEnqueue(DispatcherQueuePriority priority, Action action) => TryEnqueue(action);
        public void Drain() { while (_pending.TryDequeue(out var action)) action(); }
    }
}

namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
    public sealed class RoutedEventArgs : EventArgs { }
    public delegate void RoutedEventHandler(object sender, RoutedEventArgs args);
    public delegate void SizeChangedEventHandler(object sender, SizeChangedEventArgs args);
    public sealed class SizeChangedEventArgs(Windows.Foundation.Size size) : EventArgs
    {
        public Windows.Foundation.Size NewSize { get; } = size;
    }
    public sealed class DependencyProperty { }
    public class DependencyObject
    {
        internal List<DependencyObject> VisualChildren { get; } = [];
        internal DependencyObject? VisualParent { get; private set; }
        internal void AddVisual(DependencyObject child)
        {
            if (child.VisualParent is not null) throw new InvalidOperationException("Already parented boundary element");
            VisualChildren.Add(child);
            child.VisualParent = this;
        }
        internal void ClearVisuals()
        {
            foreach (var child in VisualChildren) child.VisualParent = null;
            VisualChildren.Clear();
        }
    }
    public class UIElement : DependencyObject
    {
        public static DependencyProperty OpacityProperty { get; } = new();
        private readonly Dictionary<long, Action<DependencyObject, DependencyProperty>> _callbacks = [];
        private long _nextCallback;
        private double _opacity = 1;
        public double Opacity
        {
            get => _opacity;
            set
            {
                if (_opacity == value) return;
                _opacity = value;
                foreach (var callback in _callbacks.Values.ToArray()) callback(this, OpacityProperty);
            }
        }
        public long RegisterPropertyChangedCallback(DependencyProperty property, Action<DependencyObject, DependencyProperty> callback)
        {
            var token = ++_nextCallback;
            _callbacks.Add(token, callback);
            return token;
        }
        public void UnregisterPropertyChangedCallback(DependencyProperty property, long token) => _callbacks.Remove(token);
    }
    public class FrameworkElement : UIElement
    {
        public string Name { get; set; } = "";
        public object? Tag { get; set; }
        public object? DataContext { get; set; }
        public object? XamlRoot { get; set; }
        public bool IsLoaded { get; private set; }
        public double ActualHeight { get; set; } = 120;
        public double ActualWidth { get; set; } = 400;
        public double Height { get; set; } = double.NaN;
        public double Top { get; set; }
        public Visibility Visibility { get; set; } = Visibility.Visible;
        public Microsoft.UI.Xaml.Media.Transform? RenderTransform { get; set; }
        public Microsoft.UI.Xaml.Media.RectangleGeometry? Clip { get; set; }
        public Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; } = new();
        public event RoutedEventHandler? Loaded;
        public event RoutedEventHandler? Unloaded;
        public event SizeChangedEventHandler? SizeChanged;
        public void RaiseLoaded() { IsLoaded = true; Loaded?.Invoke(this, new()); }
        public void RaiseUnloaded() { IsLoaded = false; Unloaded?.Invoke(this, new()); }
        public void RaiseSizeChanged(double width, double height) => SizeChanged?.Invoke(this, new(new(width, height)));
        public void UpdateLayout() { }
        public Microsoft.UI.Xaml.Media.GeneralTransform TransformToVisual(FrameworkElement target) => new(Top);
    }
    public readonly record struct Duration(TimeSpan TimeSpan);
    public sealed class DispatcherTimer
    {
        public TimeSpan Interval { get; set; }
        public bool IsEnabled { get; private set; }
        public event EventHandler<object>? Tick;
        public void Start() => IsEnabled = true;
        public void Stop() => IsEnabled = false;
        public void Complete() { if (IsEnabled) Tick?.Invoke(this, new()); }
    }
}

namespace Microsoft.UI.Xaml.Navigation
{
    public enum NavigationCacheMode { Required }
}

namespace Microsoft.UI.Xaml.Controls
{
    public class Page : Microsoft.UI.Xaml.FrameworkElement
    {
        public object? Content { get; set; }
        public Microsoft.UI.Xaml.Navigation.NavigationCacheMode NavigationCacheMode { get; set; }
    }
    public class ItemsControl : Microsoft.UI.Xaml.FrameworkElement
    {
        public object? ItemsSource { get; set; }
        public Microsoft.UI.Xaml.Media.Animation.TransitionCollection? ItemContainerTransitions { get; set; }
    }
    public class ListViewBase : ItemsControl { }
    public sealed class ListView : ListViewBase { }
    public sealed class Border : Microsoft.UI.Xaml.FrameworkElement { }
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

namespace Microsoft.UI.Xaml.Media
{
    public abstract class Transform { }
    public sealed class TranslateTransform : Transform
    {
        public double Y { get; set; }
    }
    public sealed class GeneralTransform(double top)
    {
        public Windows.Foundation.Rect TransformBounds(Windows.Foundation.Rect bounds) => bounds with { Y = bounds.Y + top };
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
        internal object? Target { get; set; }
        internal string Property { get; set; } = "";
    }
    public sealed class Storyboard
    {
        private static readonly List<Storyboard> Running = [];
        public List<DoubleAnimation> Children { get; } = [];
        public event EventHandler<object>? Completed;
        public static void SetTarget(DoubleAnimation animation, object target) => animation.Target = target;
        public static void SetTargetProperty(DoubleAnimation animation, string property) => animation.Property = property;
        public void Begin() => Running.Add(this);
        public void Stop() => Running.Remove(this);
        public static int Count => Running.Count;
        public static void CompleteAll()
        {
            foreach (var board in Running.ToArray())
            {
                foreach (var animation in board.Children)
                {
                    if (animation.Target is not null && animation.To is { } value)
                        animation.Target.GetType().GetProperty(animation.Property)!.SetValue(animation.Target, value);
                }
                board.Completed?.Invoke(board, new());
                Running.Remove(board);
            }
        }
    }
    public sealed class AddDeleteThemeTransition { }
    public sealed class RepositionThemeTransition
    {
        public bool IsStaggeringEnabled { get; set; }
    }
    public sealed class TransitionCollection : List<object> { }
}

namespace Microsoft.UI.Xaml.Automation.Peers
{
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
        internal readonly Microsoft.UI.Xaml.Controls.Grid SettingsLayout = new();
        internal readonly Microsoft.UI.Xaml.Controls.Grid SettingsSections = new();
        internal Microsoft.UI.Xaml.Controls.Frame HostedFrame { get; } = new();
        public object? NavigationRequest => _request;
        internal string SelectedCategory { get => ViewModel.SelectedCategory; set => ViewModel.SelectedCategory = value; }
        private void InitializeComponent()
        {
            AddVisual(SettingsLayout);
            SettingsLayout.AddVisual(SettingsContent);
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
            var rows = new Microsoft.UI.Xaml.Controls.ItemsControl { Tag = wrapper, ItemsSource = wrapper.Rows, XamlRoot = new() };
            SettingsSections.AddVisual(surface);
            surface.AddVisual(rows);
            // This substitutes XAML's rendered label binding, not the VM's row list or decisions.
            void BindRows()
            {
                rows.ClearVisuals();
                foreach (var row in ((System.Collections.IEnumerable)wrapper.Rows).Cast<object>())
                    rows.AddVisual(new Microsoft.UI.Xaml.Controls.TextBlock { Text = ShellRuntime.Current.Get<string>(row, "Label") });
                surface.ActualHeight = ((System.Collections.IEnumerable)wrapper.Rows).Cast<object>().Count() * 40;
            }
            NotifyCollectionChangedEventHandler bind = (_, _) => BindRows();
            wrapper.Rows.CollectionChanged += bind;
            Unloaded += (_, _) => wrapper.Rows.CollectionChanged -= bind;
            BindRows();
            rows.RaiseLoaded();
            if (attach) OnSettingsRowsLoaded(rows, new());
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
        internal void CompleteRows()
        {
            Microsoft.UI.Xaml.Media.Animation.Storyboard.CompleteAll();
            foreach (var motion in _rowMotions.Values)
            {
                var timer = (Microsoft.UI.Xaml.DispatcherTimer)typeof(SettingsRowsMotion)
                    .GetField("_settling", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(motion)!;
                timer.Complete();
            }
        }
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
    public sealed class SettingSection(object inner)
    {
        public INotifyCollectionChanged Rows => ShellRuntime.Current.Get<INotifyCollectionChanged>(inner, "Rows");
        public bool IsVisible => ShellRuntime.Current.Get<bool>(inner, "IsVisible");
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
