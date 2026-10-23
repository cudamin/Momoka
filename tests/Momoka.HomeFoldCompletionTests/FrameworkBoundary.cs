using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

// These substitutes have no HWND, WinRT, application, timer thread, network or settings IO.
// Only the framework value/event boundary is fake; all fold rules and methods are production.
namespace Windows.Foundation
{
    public readonly record struct Point(double X, double Y);
}

namespace Microsoft.UI.Dispatching
{
    public sealed class DispatcherQueue
    {
        private readonly Queue<Action> _pending = new();
        public bool AcceptEnqueue { get; set; } = true;
        public int PendingCount => _pending.Count;
        public List<DispatcherQueueTimer> Timers { get; } = [];
        public bool TryEnqueue(Action callback)
        {
            if (!AcceptEnqueue) return false;
            _pending.Enqueue(callback);
            return true;
        }
        public void Drain()
        {
            while (_pending.TryDequeue(out var callback)) callback();
        }
        public DispatcherQueueTimer CreateTimer()
        {
            var timer = new DispatcherQueueTimer();
            Timers.Add(timer);
            return timer;
        }
    }

    public sealed class DispatcherQueueTimer
    {
        public TimeSpan Interval { get; set; }
        public bool IsRepeating { get; set; }
        public bool IsRunning { get; private set; }
        public event EventHandler<object>? Tick;
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
        public void Fire()
        {
            if (IsRunning) Tick?.Invoke(this, EventArgs.Empty);
        }
    }
}

namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
    public readonly record struct Duration(TimeSpan TimeSpan);
    public class DependencyObject
    {
        public List<DependencyObject> TestChildren { get; } = [];
    }
    public class UIElement : DependencyObject
    {
        private Visibility _visibility = Visibility.Visible;
        public int CollapseCount { get; private set; }
        public Visibility Visibility
        {
            get => _visibility;
            set
            {
                if (_visibility != value && value == Visibility.Collapsed) CollapseCount++;
                _visibility = value;
            }
        }
    }
    public class FrameworkElement : UIElement
    {
        public double ActualWidth { get; set; }
        public double ActualHeight { get; set; }
        public double Opacity { get; set; } = 1;
        public Transform? RenderTransform { get; set; } = new MatrixTransform();
        public double TestTop { get; set; }
        public GeneralTransform TransformToVisual(UIElement? space) => new(TestTop, RenderTransform);
    }
}

namespace Microsoft.UI.Xaml.Media
{
    public abstract class Transform : DependencyObject;
    public sealed class MatrixTransform : Transform;
    public sealed class TranslateTransform : Transform
    {
        public double Y { get; set; }
    }
    public sealed class GeneralTransform(double top, Transform? transform)
    {
        public Point TransformPoint(Point point) => new(point.X,
            point.Y + top + (transform is TranslateTransform drift ? drift.Y : 0));
    }
    public static class VisualTreeHelper
    {
        public static int GetChildrenCount(DependencyObject node) => node.TestChildren.Count;
        public static DependencyObject GetChild(DependencyObject node, int index) => node.TestChildren[index];
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public class Page : FrameworkElement
    {
        public object? XamlRoot { get; set; } = new();
        public Microsoft.UI.Dispatching.DispatcherQueue? DispatcherQueue { get; set; } = new();
        public Action? TestLayout { get; set; }
        public void UpdateLayout() => TestLayout?.Invoke();
    }
    public sealed class ContentControl : FrameworkElement
    {
        private object? _content;
        public int ClearCount { get; private set; }
        public object? Content
        {
            get => _content;
            set
            {
                if (_content is not null && value is null) ClearCount++;
                _content = value;
            }
        }
    }
    public sealed class StackLayout
    {
        public double Spacing { get; set; }
    }
    public sealed class ItemsRepeater : FrameworkElement
    {
        public Dictionary<int, FrameworkElement> Elements { get; } = [];
        public object? Layout { get; set; } = new StackLayout();
        public object? TryGetElement(int index) => Elements.GetValueOrDefault(index);
        public object GetOrCreateElement(int index) => Elements.TryGetValue(index, out var element)
            ? element : throw new InvalidOperationException("Missing fake layout element " + index);
    }
    public sealed class ScrollViewer : FrameworkElement
    {
        public double VerticalOffset { get; set; }
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
        public TimeSpan? BeginTime { get; set; }
        public Duration Duration { get; set; }
        public CubicEase? EasingFunction { get; set; }
        public DependencyObject? TestTarget { get; set; }
        public string? TestProperty { get; set; }
    }
    public sealed class Storyboard
    {
        private EventHandler<object>? _completed;
        private readonly List<(DependencyObject Target, string Property, double Value)> _baseValues = [];
        public static List<Storyboard> Created { get; } = [];
        public static bool FailNextBegin { get; set; }
        public Storyboard() => Created.Add(this);
        public List<DoubleAnimation> Children { get; } = [];
        public bool IsRunning { get; private set; }
        public bool ThrowOnBegin { get; set; }
        public int BeginCount { get; private set; }
        public int StopCount { get; private set; }
        public int HandlerCount => _completed?.GetInvocationList().Length ?? 0;
        public Action? OnStop { get; set; }
        public event EventHandler<object> Completed
        {
            add => _completed += value;
            remove => _completed -= value;
        }
        public static void SetTarget(DoubleAnimation animation, DependencyObject target) => animation.TestTarget = target;
        public static void SetTargetProperty(DoubleAnimation animation, string property) => animation.TestProperty = property;
        public void Begin()
        {
            BeginCount++;
            foreach (var animation in Children)
            {
                var target = animation.TestTarget ?? throw new InvalidOperationException("Missing target");
                var property = animation.TestProperty ?? throw new InvalidOperationException("Missing property");
                _baseValues.Add((target, property, Read(target, property)));
            }
            if (ThrowOnBegin || FailNextBegin)
            {
                FailNextBegin = false;
                throw new InvalidOperationException("Controlled Storyboard.Begin failure");
            }
            IsRunning = true;
        }
        public Action CaptureCompletion()
        {
            var callback = _completed;
            return () => callback?.Invoke(this, EventArgs.Empty);
        }
        public void Complete()
        {
            foreach (var animation in Children)
                Write(animation.TestTarget!, animation.TestProperty!, animation.To!.Value);
            IsRunning = false;
            _completed?.Invoke(this, EventArgs.Empty);
        }
        public void Stop()
        {
            StopCount++;
            IsRunning = false;
            // Like WinUI Stop, release animation values to the local values captured by Begin.
            foreach (var value in _baseValues) Write(value.Target, value.Property, value.Value);
            _baseValues.Clear();
            var callback = OnStop;
            OnStop = null;
            callback?.Invoke();
        }
        private static double Read(DependencyObject target, string property) => (target, property) switch
        {
            (TranslateTransform drift, "Y") => drift.Y,
            (FrameworkElement element, "Opacity") => element.Opacity,
            _ => throw new InvalidOperationException("Unexpected animated boundary property")
        };
        private static void Write(DependencyObject target, string property, double value)
        {
            if (target is TranslateTransform drift && property == "Y") drift.Y = value;
            else if (target is FrameworkElement element && property == "Opacity") element.Opacity = value;
            else throw new InvalidOperationException("Unexpected animated boundary property");
        }
    }
}
