// Only XAML, dispatcher and neighboring services are simulated. The complete header partial is source-linked.
using System.ComponentModel;
using System.Numerics;
using System.Windows.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;

namespace Windows.Foundation
{
    public delegate void TypedEventHandler<TSender, TResult>(TSender sender, TResult args);
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
        public int EnqueueAttempts { get; private set; }
        public bool TryEnqueue(Action callback) => TryEnqueue(DispatcherQueuePriority.Normal, callback);
        public bool TryEnqueue(DispatcherQueuePriority priority, Action callback)
        {
            EnqueueAttempts++;
            if (RejectEnqueue) return false;
            _pending.Add((priority, callback));
            return true;
        }
        public Action TakeNext()
        {
            var next = _pending.OrderByDescending(item => item.Priority).First();
            _pending.Remove(next);
            return next.Callback;
        }
        public void Drain()
        {
            for (var count = 0; _pending.Count > 0; count++)
            {
                if (count >= 100) throw new InvalidOperationException("Unbounded dispatcher callbacks.");
                var previous = HasThreadAccess;
                HasThreadAccess = true;
                try { TakeNext()(); }
                finally { HasThreadAccess = previous; }
            }
        }
    }
}

namespace Microsoft.UI.Xaml
{
    public sealed class DispatcherTimer
    {
        public TimeSpan Interval { get; set; }
        public bool IsEnabled { get; private set; }
        public event EventHandler<object>? Tick;
        public void Start() => IsEnabled = true;
        public void Stop() => IsEnabled = false;
        public Action LateTick()
        {
            var tick = Tick;
            return () => tick?.Invoke(this, new());
        }
        public void Fire() { if (IsEnabled) Tick?.Invoke(this, new()); }
    }
    public delegate void RoutedEventHandler(object sender, RoutedEventArgs args);
    public sealed class RoutedEventArgs : EventArgs;
    public sealed class SizeChangedEventArgs : EventArgs;
    public enum Visibility { Visible, Collapsed }
    public enum FocusState { Unfocused, Pointer, Keyboard, Programmatic }
    public enum VerticalAlignment { Top, Center, Bottom, Stretch }
    public readonly record struct Thickness(double Left, double Top, double Right, double Bottom)
    {
        public Thickness(double uniform) : this(uniform, uniform, uniform, uniform) { }
        public Thickness(double horizontal, double vertical) : this(horizontal, vertical, horizontal, vertical) { }
    }
    public sealed class XamlRoot
    {
        public object? FocusedElement { get; set; }
    }
    public sealed record DependencyProperty(string Name);
    public class DependencyObject
    {
        public DispatcherQueue DispatcherQueue { get; set; } = new();
        public FrameworkElement? Parent { get; set; }
        public object? GetValue(DependencyProperty property) => null;
    }
    public class UIElement : DependencyObject
    {
        private Visibility _visibility = Visibility.Visible;
        public virtual Visibility Visibility
        {
            get => _visibility;
            set
            {
                if (value == _visibility) return;
                _visibility = value;
                VisibilityChanges++;
                OnVisibilityChanged(value);
            }
        }
        protected virtual void OnVisibilityChanged(Visibility value) =>
            CommunityToolkit.WinUI.Animations.Implicit.VisibilityChanged(this, value);
        public int VisibilityChanges { get; private set; }
        public bool IsHitTestVisible { get; set; } = true;
        public bool IsTabStop { get; set; } = true;
        public bool IsEnabled { get; set; } = true;
        public bool FocusAccepted { get; set; } = true;
        public int FocusCalls { get; private set; }
        public FocusState FocusState { get; private set; }
        public XamlRoot? XamlRoot { get; set; } = new();
        public Vector3 Translation { get; set; }
        public double Opacity { get; set; } = 1;
        public bool Focus(FocusState state)
        {
            FocusCalls++;
            if (!FocusAccepted || XamlRoot is null) return false;
            FocusState = state;
            XamlRoot.FocusedElement = this;
            return true;
        }
    }
    public class FrameworkElement : UIElement
    {
        private RoutedEventHandler? _loaded;
        private RoutedEventHandler? _unloaded;
        private EventHandler<object>? _layoutUpdated;
        public event RoutedEventHandler? Loaded { add => _loaded += value; remove => _loaded -= value; }
        public event RoutedEventHandler? Unloaded { add => _unloaded += value; remove => _unloaded -= value; }
        public event EventHandler<object>? LayoutUpdated { add => _layoutUpdated += value; remove => _layoutUpdated -= value; }
        public int LayoutSubscriptions => _layoutUpdated?.GetInvocationList().Length ?? 0;
        public bool IsLoaded { get; set; } = true;
        public double ActualWidth { get; set; } = 600;
        public double ActualHeight { get; set; } = 40;
        public double Width { get; set; } = double.NaN;
        public double Height { get; set; } = double.NaN;
        public double MinWidth { get; set; }
        public double MaxWidth { get; set; } = double.PositiveInfinity;
        public Thickness Margin { get; set; }
        public Thickness Padding { get; set; }
        public double LayoutX { get; set; }
        public double LayoutY { get; set; }
        public bool ThrowOnTransform { get; set; }
        public Exception? TransformError { get; set; }
        public int TransformCalls { get; private set; }
        public void RaiseLoaded() { IsLoaded = true; _loaded?.Invoke(this, new()); }
        public void RaiseUnloaded() { IsLoaded = false; _unloaded?.Invoke(this, new()); }
        public void RaiseLayout() => _layoutUpdated?.Invoke(this, new());
        public void UpdateLayout() { }
        public Microsoft.UI.Xaml.Media.GeneralTransform TransformToVisual(UIElement target)
        {
            TransformCalls++;
            if (TransformError is not null) throw TransformError;
            if (ThrowOnTransform) throw new InvalidOperationException("Detached visual.");
            double x = LayoutX, y = LayoutY;
            for (var parent = Parent; parent is not null; parent = parent.Parent)
            {
                if (parent is Microsoft.UI.Xaml.Controls.ScrollView scroll) y -= scroll.VerticalOffset;
                if (ReferenceEquals(parent, target)) return new(x, y);
                x += parent.LayoutX;
                y += parent.LayoutY;
            }
            return new(x, y);
        }
    }
}

namespace Microsoft.UI.Xaml.Input
{
    public static class FocusManager
    {
        public static object? GetFocusedElement(XamlRoot root) => root.FocusedElement;
    }
}

namespace Microsoft.UI.Xaml.Automation
{
    public static class AutomationProperties
    {
        private static readonly Dictionary<UIElement, AccessibilityView> Views = [];
        public static AccessibilityView GetAccessibilityView(UIElement element) =>
            Views.GetValueOrDefault(element, AccessibilityView.Content);
        public static void SetAccessibilityView(UIElement element, AccessibilityView value) => Views[element] = value;
    }
}

namespace Microsoft.UI.Xaml.Automation.Peers
{
    public enum AccessibilityView { Raw, Control, Content }
}

namespace Microsoft.UI.Xaml.Media.Animation
{
    public enum EasingMode { EaseIn, EaseOut, EaseInOut, Linear }
    public sealed class DirectConnectedAnimationConfiguration;
    public sealed class ConnectedAnimation(UIElement source)
    {
        public UIElement Source { get; } = source;
        public UIElement? Target { get; private set; }
        public object? Configuration { get; set; }
        public bool Starts { get; set; } = true;
        public bool ThrowOnStart { get; set; }
        public bool ThrowOnCancel { get; set; }
        public int Cancels { get; private set; }
        public event Windows.Foundation.TypedEventHandler<ConnectedAnimation, object>? Completed;
        public bool TryStart(UIElement target)
        {
            Target = target;
            if (ThrowOnStart) throw new InvalidOperationException("Start rejected.");
            return Starts;
        }
        public void Complete() => Completed?.Invoke(this, new());
        public Action LateComplete()
        {
            var completed = Completed;
            return () => completed?.Invoke(this, new());
        }
        public void Cancel()
        {
            Cancels++;
            if (ThrowOnCancel) throw new InvalidOperationException("Cancel rejected.");
        }
    }
    public sealed class ConnectedAnimationService
    {
        public static bool Enabled { get; set; }
        public static bool Starts { get; set; } = true;
        public static bool ThrowOnStart { get; set; }
        public static List<ConnectedAnimation> Created { get; } = [];
        public static ConnectedAnimationService GetForCurrentView() => new();
        public ConnectedAnimation PrepareToAnimate(string key, UIElement source)
        {
            if (!Enabled) throw new InvalidOperationException("Source has no composition node.");
            var animation = new ConnectedAnimation(source) { Starts = Starts, ThrowOnStart = ThrowOnStart };
            Created.Add(animation);
            return animation;
        }
    }
}

namespace Microsoft.UI.Xaml.Media
{
    public class ImageSource;
    public sealed class GeneralTransform(double x, double y)
    {
        public Windows.Foundation.Point TransformPoint(Windows.Foundation.Point point) => new(point.X + x, point.Y + y);
        public Windows.Foundation.Rect TransformBounds(Windows.Foundation.Rect rect) => new(rect.X + x, rect.Y + y, rect.Width, rect.Height);
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public class Page : FrameworkElement;
    public class Border : FrameworkElement;
    public class Grid : FrameworkElement
    {
        public double ColumnSpacing { get; set; }
    }
    public class Button : FrameworkElement
    {
        public ICommand? Command { get; set; }
        public object? Content { get; set; }
    }
    public class TextBlock : FrameworkElement
    {
        public string? Text { get; set; }
    }
    public class Image : FrameworkElement
    {
        public Microsoft.UI.Xaml.Media.ImageSource? Source { get; set; }
    }
    public class ScrollView : FrameworkElement
    {
        public double VerticalOffset { get; set; }
        public double ScrollableHeight { get; set; } = 2000;
    }
}

namespace Momoka.Shell.ViewModels
{
    internal sealed class HeaderCommand : ICommand
    {
        private EventHandler? _changed;
        public event EventHandler? CanExecuteChanged { add => _changed += value; remove => _changed -= value; }
        public int Subscriptions => _changed?.GetInvocationList().Length ?? 0;
        public bool Allowed { get; set; } = true;
        public int Executions { get; private set; }
        public bool CanExecute(object? parameter) => Allowed;
        public void Execute(object? parameter) => Executions++;
        public void NotifyCanExecuteChanged() => _changed?.Invoke(this, EventArgs.Empty);
    }
    internal sealed class DetailViewModel : INotifyPropertyChanged
    {
        private PropertyChangedEventHandler? _changed;
        public event PropertyChangedEventHandler? PropertyChanged { add => _changed += value; remove => _changed -= value; }
        public int Subscriptions => _changed?.GetInvocationList().Length ?? 0;
        public bool IsCompact { get; set; }
        public bool Busy { get; set; }
        public bool IsReady { get; set; } = true;
        public double PageWidth { get; set; } = 600;
        public Visibility PlayVisibility { get; set; } = Visibility.Visible;
        public Microsoft.UI.Xaml.Media.ImageSource? StillImage { get; set; } = new();
        public string? Subline { get; set; } = "测试副标题";
        public HeaderCommand PlayCommand { get; } = new();
        public void Notify(string name) => _changed?.Invoke(this, new(name));
    }
}

namespace Microsoft.UI.Composition
{
    public sealed class CompositionPropertySet
    {
        public Vector3 Translation { get; private set; }
        public bool TranslationRegistered { get; private set; }
        public int TranslationWrites { get; private set; }
        public void RegisterTranslation() => TranslationRegistered = true;
        public void InsertVector3(string name, Vector3 value)
        {
            if (name != "Translation") throw new InvalidOperationException("Unexpected composition property: " + name);
            TranslationRegistered = true;
            Translation = value;
            TranslationWrites++;
        }
    }
    public sealed class Visual
    {
        public CompositionPropertySet Properties { get; } = new();
        public double Opacity { get; set; } = 1;
        public HashSet<string> Stopped { get; } = [];
        public void StopAnimation(string property)
        {
            if (property == "Translation" && !Properties.TranslationRegistered)
                throw new ArgumentException("Composition property not found: Translation", nameof(property));
            Stopped.Add(property);
        }
    }
}

namespace Microsoft.UI.Xaml.Hosting
{
    public static class ElementCompositionPreview
    {
        private static readonly Dictionary<UIElement, Microsoft.UI.Composition.Visual> Visuals = [];
        public static bool DeferTranslationRegistration { get; set; }
        public static void SetIsTranslationEnabled(UIElement element, bool enabled)
        {
            if (enabled && !DeferTranslationRegistration) GetElementVisual(element).Properties.RegisterTranslation();
        }
        public static Microsoft.UI.Composition.Visual GetElementVisual(UIElement element)
        {
            if (!Visuals.TryGetValue(element, out var visual)) Visuals.Add(element, visual = new());
            return visual;
        }
    }
}

namespace CommunityToolkit.WinUI.Animations
{
    public abstract class ImplicitTimeline
    {
        public TimeSpan Duration { get; set; }
        public Microsoft.UI.Xaml.Media.Animation.EasingMode EasingMode { get; set; }
    }
    public sealed class TranslationAnimation : ImplicitTimeline
    {
        public string? To { get; set; }
        public string? From { get; set; }
    }
    public sealed class OpacityAnimation : ImplicitTimeline
    {
        public double? To { get; set; }
        public double? From { get; set; }
    }
    public sealed class ImplicitAnimationSet : List<ImplicitTimeline>;
    public static class Implicit
    {
        private static readonly Dictionary<UIElement, ImplicitAnimationSet?> Shows = [];
        private static readonly Dictionary<UIElement, ImplicitAnimationSet?> Hides = [];
        private static readonly Dictionary<UIElement, (int Shows, int Hides)> Starts = [];
        public static int StartedShows(UIElement element) => Starts.TryGetValue(element, out var state) ? state.Shows : 0;
        public static int StartedHides(UIElement element) => Starts.TryGetValue(element, out var state) ? state.Hides : 0;
        public static ImplicitAnimationSet? GetShowAnimations(UIElement element) => Shows.GetValueOrDefault(element);
        public static ImplicitAnimationSet? GetHideAnimations(UIElement element) => Hides.GetValueOrDefault(element);
        public static void SetShowAnimations(UIElement element, ImplicitAnimationSet? value)
        {
            if (value is not null) Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(element, true);
            Shows[element] = value;
        }
        public static void SetHideAnimations(UIElement element, ImplicitAnimationSet? value)
        {
            if (value is not null) Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(element, true);
            Hides[element] = value;
        }
        public static void VisibilityChanged(UIElement element, Visibility visibility)
        {
            var state = Starts.GetValueOrDefault(element);
            if (visibility == Visibility.Visible && GetShowAnimations(element) is not null) state.Shows++;
            if (visibility == Visibility.Collapsed && GetHideAnimations(element) is not null) state.Hides++;
            Starts[element] = state;
        }
    }
}

namespace Momoka.Shell.Views
{
    internal static class HomeMotion
    {
        private static Action? _changed;
        public static bool AnimationsEnabled => PreferenceEnabled && Windows.UI.ViewManagement.UISettings.Enabled;
        public static bool PreferenceEnabled { get; private set; } = true;
        public static int Subscriptions => _changed?.GetInvocationList().Length ?? 0;
        public static event Action? PreferenceChanged { add => _changed += value; remove => _changed -= value; }
        public static void ApplyPreference(bool enabled)
        {
            PreferenceEnabled = enabled;
            _changed?.Invoke();
        }
    }
}

namespace Windows.UI.ViewManagement
{
    public delegate void AnimationsChangedHandler(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args);
    public sealed class UISettingsAnimationsEnabledChangedEventArgs : EventArgs;
    public sealed class UISettings
    {
        private AnimationsChangedHandler? _changed;
        public static List<UISettings> Created { get; } = [];
        public static bool Enabled { get; private set; } = true;
        public bool AnimationsEnabled => Enabled;
        public int Subscriptions => _changed?.GetInvocationList().Length ?? 0;
        public event AnimationsChangedHandler? AnimationsEnabledChanged { add => _changed += value; remove => _changed -= value; }
        public UISettings() => Created.Add(this);
        public static void SetAnimationsEnabled(bool enabled)
        {
            Enabled = enabled;
            foreach (var settings in Created.ToArray()) settings._changed?.Invoke(settings, new());
        }
    }
}
