// Layout, input/row endpoints and a controlled Storyboard clock only; no production cancellation copy.
using System.Collections.ObjectModel;

namespace Windows.Foundation
{
    public readonly record struct Point(double X, double Y);
}

namespace Microsoft.UI.Dispatching
{
    public enum DispatcherQueuePriority { Low }
    public sealed class DispatcherQueue
    {
        private readonly Queue<Action> _pending = [];
        public int PendingCount => _pending.Count;
        public bool RejectEnqueue { get; set; }
        public bool TryEnqueue(DispatcherQueuePriority priority, Action callback)
        {
            if (RejectEnqueue) return false;
            _pending.Enqueue(callback);
            return true;
        }
        public void DrainOne()
        {
            if (_pending.TryDequeue(out var callback)) callback();
        }
        public void Drain()
        {
            for (var count = 0; _pending.Count > 0; count++)
            {
                if (count == 100) throw new InvalidOperationException("Unbounded queued work.");
                DrainOne();
            }
        }
    }
}

namespace Microsoft.UI.Xaml
{
    public readonly record struct Duration(TimeSpan TimeSpan);
    public class DependencyObject
    {
        public List<DependencyObject> Children { get; } = [];
        public DependencyObject? Parent { get; private set; }
        public void AddChild(DependencyObject child)
        {
            if (child.Parent is not null) throw new InvalidOperationException("Reparenting a live control.");
            Children.Add(child);
            child.Parent = this;
        }
    }
    public class FrameworkElement : DependencyObject
    {
        public string Name { get; set; } = "";
        public object? XamlRoot { get; set; } = new();
        public double ActualHeight { get; set; } = 50;
        public double LayoutY { get; set; }
        public Microsoft.UI.Xaml.Media.TranslateTransform? RenderTransform { get; set; }
        public Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; } = new();
        public event EventHandler<object>? LayoutUpdated;
        public int LayoutSubscriberCount => LayoutUpdated?.GetInvocationList().Length ?? 0;
        public Action? LayoutReading { get; set; }
        public void UpdateLayout()
        {
            LayoutReading?.Invoke();
            LayoutUpdated?.Invoke(this, new());
        }
        private double OriginY()
        {
            var y = LayoutY + (RenderTransform?.Y ?? 0);
            for (var parent = Parent; parent is not null; parent = parent.Parent)
                if (parent is FrameworkElement element) y += element.LayoutY + (element.RenderTransform?.Y ?? 0);
            return y;
        }
        public Microsoft.UI.Xaml.Media.GeneralTransform TransformToVisual(FrameworkElement relative)
        {
            if (XamlRoot is null || relative.XamlRoot is null) throw new InvalidOperationException("Off-tree visual measurement.");
            return new(OriginY() - relative.OriginY());
        }
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public sealed class ListView : Microsoft.UI.Xaml.FrameworkElement
    {
        public Dictionary<object, Microsoft.UI.Xaml.FrameworkElement> Containers { get; } = new(ReferenceEqualityComparer.Instance);
        public Microsoft.UI.Xaml.DependencyObject? ContainerFromItem(object item) => Containers.GetValueOrDefault(item);
    }
    public sealed class DragItemsStartingEventArgs
    {
        public List<object> Items { get; } = [];
        public bool Cancel { get; set; }
    }
}

namespace Microsoft.UI.Xaml.Media
{
    public sealed class TranslateTransform : Microsoft.UI.Xaml.DependencyObject
    {
        private double _baseY;
        private (object Owner, double Value)? _animation;
        public double Y { get => _animation?.Value ?? _baseY; set => _baseY = value; }
        internal void Animate(object owner, double value) => _animation = (owner, value);
        internal void ClearAnimation(object owner)
        {
            if (ReferenceEquals(_animation?.Owner, owner)) _animation = null;
        }
    }
    public sealed class GeneralTransform(double y)
    {
        public Windows.Foundation.Point TransformPoint(Windows.Foundation.Point point) => new(point.X, point.Y + y);
    }
    public static class VisualTreeHelper
    {
        public static int GetChildrenCount(Microsoft.UI.Xaml.DependencyObject parent) => parent.Children.Count;
        public static Microsoft.UI.Xaml.DependencyObject GetChild(Microsoft.UI.Xaml.DependencyObject parent, int index) => parent.Children[index];
    }
}

namespace Microsoft.UI.Xaml.Media.Animation
{
    public readonly record struct KeyTime(TimeSpan TimeSpan)
    {
        public static KeyTime FromTimeSpan(TimeSpan time) => new(time);
    }
    public sealed class KeySpline
    {
        public Windows.Foundation.Point ControlPoint1 { get; set; }
        public Windows.Foundation.Point ControlPoint2 { get; set; }
    }
    public sealed class SplineDoubleKeyFrame
    {
        public KeyTime KeyTime { get; set; }
        public double Value { get; set; }
        public KeySpline? KeySpline { get; set; }
    }
    public sealed class DoubleAnimationUsingKeyFrames
    {
        public Microsoft.UI.Xaml.Duration Duration { get; set; }
        public List<SplineDoubleKeyFrame> KeyFrames { get; } = [];
        public Microsoft.UI.Xaml.DependencyObject? Target { get; internal set; }
        public string Property { get; internal set; } = "";
    }
    public sealed class Storyboard
    {
        private static readonly List<Storyboard> Running = [];
        private static double _now;
        private double _started;
        public List<DoubleAnimationUsingKeyFrames> Children { get; } = [];
        public event EventHandler<object>? Completed;
        public static IReadOnlyList<Storyboard> Active => Running;
        public static List<Storyboard> Begun { get; } = [];
        public static void SetTarget(DoubleAnimationUsingKeyFrames animation, Microsoft.UI.Xaml.DependencyObject target) => animation.Target = target;
        public static void SetTargetProperty(DoubleAnimationUsingKeyFrames animation, string property) => animation.Property = property;
        public void Begin()
        {
            _started = _now;
            Running.Add(this);
            Begun.Add(this);
            Step();
        }
        public void Stop()
        {
            Running.Remove(this);
            foreach (var animation in Children)
                if (animation.Target is Microsoft.UI.Xaml.Media.TranslateTransform drift) drift.ClearAnimation(this);
        }
        public Action CapturePostedCompletion()
        {
            var posted = Completed;
            return () => posted?.Invoke(this, new());
        }
        private void Step()
        {
            var elapsed = _now - _started;
            var finished = true;
            foreach (var animation in Children)
            {
                if (elapsed < animation.Duration.TimeSpan.TotalMilliseconds) finished = false;
                var frames = animation.KeyFrames;
                var before = frames.LastOrDefault(frame => frame.KeyTime.TimeSpan.TotalMilliseconds <= elapsed) ?? frames.First();
                var after = frames.FirstOrDefault(frame => frame.KeyTime.TimeSpan.TotalMilliseconds >= elapsed) ?? frames.Last();
                var duration = after.KeyTime.TimeSpan.TotalMilliseconds - before.KeyTime.TimeSpan.TotalMilliseconds;
                var progress = duration <= 0 ? 1 : Math.Clamp((elapsed - before.KeyTime.TimeSpan.TotalMilliseconds) / duration, 0, 1);
                if (animation.Target is Microsoft.UI.Xaml.Media.TranslateTransform drift)
                    drift.Animate(this, before.Value + (after.Value - before.Value) * progress);
            }
            if (finished) Completed?.Invoke(this, new());
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
        }
    }
}

namespace Momoka.Shell.ViewModels
{
    public sealed class TitleRuleChoice(string term)
    {
        public string Term { get; } = term;
    }
    public sealed class SettingTitleRulesRow
    {
        public ObservableCollection<TitleRuleChoice> Rules { get; } = [];
        public int BeginCalls { get; private set; }
        public int EndCalls { get; private set; }
        public void BeginReorder() => BeginCalls++;
        public void EndReorder() => EndCalls++;
    }
}

namespace Momoka.Shell.Views
{
    internal static class HomeMotion
    {
        private static Action? _changed;
        internal static bool AnimationsEnabled { get; private set; } = true;
        internal static int Subscribers => _changed?.GetInvocationList().Length ?? 0;
        internal static event Action? PreferenceChanged { add => _changed += value; remove => _changed -= value; }
        internal static void ApplyPreference(bool enabled)
        {
            if (AnimationsEnabled == enabled) return;
            AnimationsEnabled = enabled;
            _changed?.Invoke();
        }
        internal static void Reset()
        {
            if (Subscribers != 0) throw new InvalidOperationException("Preference subscription leak.");
            AnimationsEnabled = true;
        }
    }
}
