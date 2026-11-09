// Only the native frame, dispatcher, animation delivery and system preference are inert.
// Every navigation generation and image-gate decision comes from linked production source.
namespace Microsoft.UI.Dispatching
{
    public enum DispatcherQueuePriority { Low, Normal }
    public sealed class DispatcherQueue
    {
        private readonly Queue<Action> _pending = [];
        public bool Reject { get; set; }
        public DispatcherQueuePriority? LastPriority { get; private set; }
        public bool TryEnqueue(Action action) => TryEnqueue(DispatcherQueuePriority.Normal, action);
        public bool TryEnqueue(DispatcherQueuePriority priority, Action action)
        {
            LastPriority = priority;
            if (Reject) return false;
            _pending.Enqueue(action);
            return true;
        }
        public void Drain()
        {
            while (_pending.TryDequeue(out var action)) action();
        }
    }
}

namespace Microsoft.UI.Xaml
{
    public sealed class RoutedEventArgs : EventArgs;
    public delegate void RoutedEventHandler(object sender, RoutedEventArgs args);
    public class DependencyObject;
    public readonly record struct Duration(TimeSpan TimeSpan);
    public class FrameworkElement : DependencyObject
    {
        private RoutedEventHandler? _loaded;
        private RoutedEventHandler? _unloaded;
        public bool IsLoaded { get; private set; }
        public object? XamlRoot { get; private set; }
        public double Opacity { get; set; } = 1;
        public int LoadedHandlers => _loaded?.GetInvocationList().Length ?? 0;
        public int UnloadedHandlers => _unloaded?.GetInvocationList().Length ?? 0;
        public Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; } = new();
        public event RoutedEventHandler Loaded { add => _loaded += value; remove => _loaded -= value; }
        public event RoutedEventHandler Unloaded { add => _unloaded += value; remove => _unloaded -= value; }
        public void Load()
        {
            IsLoaded = true;
            XamlRoot = new object();
            _loaded?.Invoke(this, new RoutedEventArgs());
        }
        public void Unload()
        {
            IsLoaded = false;
            XamlRoot = null;
            _unloaded?.Invoke(this, new RoutedEventArgs());
        }
        public Action LateLoad()
        {
            var callbacks = _loaded;
            return () => callbacks?.Invoke(this, new RoutedEventArgs());
        }
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public sealed class Frame : Microsoft.UI.Xaml.FrameworkElement
    {
        public object? Content { get; set; }
    }
}

namespace Microsoft.UI.Xaml.Media
{
    public static class CompositionTarget
    {
        private static EventHandler<object>? _rendering;
        public static int HandlerCount => _rendering?.GetInvocationList().Length ?? 0;
        public static event EventHandler<object> Rendering { add => _rendering += value; remove => _rendering -= value; }
        public static void Render() => _rendering?.Invoke(null, EventArgs.Empty);
        public static Action LateRender()
        {
            var callbacks = _rendering;
            return () => callbacks?.Invoke(null, EventArgs.Empty);
        }
    }
}

namespace Microsoft.UI.Xaml.Media.Animation
{
    public enum EasingMode { EaseOut }
    public sealed class CubicEase { public EasingMode EasingMode { get; set; } }
    public sealed class DoubleAnimation
    {
        public double? From { get; set; }
        public double? To { get; set; }
        public Microsoft.UI.Xaml.Duration Duration { get; set; }
        public TimeSpan? BeginTime { get; set; }
        public CubicEase? EasingFunction { get; set; }
        public Microsoft.UI.Xaml.DependencyObject? Target { get; set; }
        public string? Property { get; set; }
    }
    public sealed class Storyboard
    {
        private EventHandler<object>? _completed;
        public static List<Storyboard> Created { get; } = [];
        public static bool FailNextBegin { get; set; }
        public Storyboard() => Created.Add(this);
        public List<DoubleAnimation> Children { get; } = [];
        public bool IsRunning { get; private set; }
        public int Handlers => _completed?.GetInvocationList().Length ?? 0;
        public event EventHandler<object> Completed { add => _completed += value; remove => _completed -= value; }
        public static void SetTarget(DoubleAnimation animation, Microsoft.UI.Xaml.DependencyObject target) => animation.Target = target;
        public static void SetTargetProperty(DoubleAnimation animation, string property) => animation.Property = property;
        public void Begin()
        {
            if (FailNextBegin)
            {
                FailNextBegin = false;
                throw new InvalidOperationException("Controlled native animation failure");
            }
            IsRunning = true;
        }
        public void Stop() => IsRunning = false;
        public void Complete() => _completed?.Invoke(this, EventArgs.Empty);
        public Action LateCompletion()
        {
            var callback = _completed;
            return () => callback?.Invoke(this, EventArgs.Empty);
        }
    }
}

namespace Windows.UI.ViewManagement
{
    public delegate void SettingsChanged(UISettings sender, object args);
    public sealed class UISettings
    {
        private SettingsChanged? _changed;
        public static List<UISettings> Created { get; } = [];
        public static bool Enabled { get; set; } = true;
        public UISettings() => Created.Add(this);
        public bool AnimationsEnabled => Enabled;
        public event SettingsChanged AnimationsEnabledChanged { add => _changed += value; remove => _changed -= value; }
        public int Handlers => _changed?.GetInvocationList().Length ?? 0;
        public void Change(bool enabled)
        {
            Enabled = enabled;
            _changed?.Invoke(this, EventArgs.Empty);
        }
    }
}

namespace Momoka.Shell.Views
{
    internal static class HomeMotion
    {
        public static bool PreferenceEnabled { get; private set; } = true;
        public static bool AnimationsEnabled => PreferenceEnabled && Windows.UI.ViewManagement.UISettings.Enabled;
        public static event Action? PreferenceChanged;
        public static void ApplyPreference(bool enabled)
        {
            PreferenceEnabled = enabled;
            PreferenceChanged?.Invoke();
        }
    }
}
