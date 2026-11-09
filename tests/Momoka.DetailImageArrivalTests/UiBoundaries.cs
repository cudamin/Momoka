// Value, event and animation-clock boundaries only; production ImageArrivalFade is compile-linked.
namespace Microsoft.UI.Dispatching
{
    public enum DispatcherQueuePriority { Low, Normal, High }
    public sealed class DispatcherQueue
    {
        public bool HasThreadAccess { get; set; } = true;
        public bool Reject { get; set; }
        private readonly Queue<Action> _pending = [];
        public bool TryEnqueue(DispatcherQueuePriority priority, Action action)
        {
            if (Reject) return false;
            _pending.Enqueue(action);
            return true;
        }
        public void Drain() { while (_pending.TryDequeue(out var action)) action(); }
    }
}

namespace Microsoft.UI.Xaml
{
    public delegate void RoutedEventHandler(object sender, RoutedEventArgs args);
    public delegate void ExceptionRoutedEventHandler(object sender, ExceptionRoutedEventArgs args);
    public delegate void DependencyPropertyChangedCallback(DependencyObject sender, DependencyProperty property);
    public class RoutedEventArgs : EventArgs;
    public sealed class ExceptionRoutedEventArgs : RoutedEventArgs;
    public sealed record DependencyProperty(string Name);
    public readonly record struct Duration(TimeSpan TimeSpan);
    public sealed class XamlRoot;
    public class DependencyObject
    {
        private readonly Dictionary<long, (DependencyProperty Property, DependencyPropertyChangedCallback Callback)> _callbacks = [];
        private long _next;
        public Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; } = new();
        public int PropertySubscriptions => _callbacks.Count;
        public long RegisterPropertyChangedCallback(DependencyProperty property, DependencyPropertyChangedCallback callback)
        {
            var token = ++_next;
            _callbacks.Add(token, (property, callback));
            return token;
        }
        public void UnregisterPropertyChangedCallback(DependencyProperty property, long token) => _callbacks.Remove(token);
        protected void Changed(DependencyProperty property)
        {
            foreach (var (_, entry) in _callbacks.ToArray())
                if (entry.Property == property) entry.Callback(this, property);
        }
    }
    public class FrameworkElement : DependencyObject
    {
        public bool IsLoaded { get; private set; }
        public XamlRoot? XamlRoot { get; set; }
        public double Opacity { get; set; } = 1;
        public event RoutedEventHandler? Loaded;
        public event RoutedEventHandler? Unloaded;
        public void Load() { IsLoaded = true; XamlRoot ??= new(); Loaded?.Invoke(this, new()); }
        public void Unload(bool phantom = false) { IsLoaded = false; if (!phantom) XamlRoot = null; Unloaded?.Invoke(this, new()); }
    }
}

namespace Microsoft.UI.Xaml.Media
{
    public class ImageSource : Microsoft.UI.Xaml.DependencyObject;
    public sealed class ImageBrush : Microsoft.UI.Xaml.DependencyObject
    {
        public static Microsoft.UI.Xaml.DependencyProperty ImageSourceProperty { get; } = new("ImageSource");
        private ImageSource? _source;
        public ImageSource? ImageSource { get => _source; set { _source = value; Changed(ImageSourceProperty); } }
        public double Opacity { get; set; } = 1;
        public event Microsoft.UI.Xaml.ExceptionRoutedEventHandler? ImageFailed;
        public void Fail() => ImageFailed?.Invoke(this, new());
    }
}

namespace Microsoft.UI.Xaml.Media.Imaging
{
    public sealed class BitmapImage : Microsoft.UI.Xaml.Media.ImageSource
    {
        public int PixelWidth { get; set; } = 600;
        public int PixelHeight { get; set; } = 400;
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public sealed class Image : Microsoft.UI.Xaml.FrameworkElement
    {
        public static Microsoft.UI.Xaml.DependencyProperty SourceProperty { get; } = new("Source");
        private Microsoft.UI.Xaml.Media.ImageSource? _source;
        public Microsoft.UI.Xaml.Media.ImageSource? Source { get => _source; set { _source = value; Changed(SourceProperty); } }
        public event Microsoft.UI.Xaml.ExceptionRoutedEventHandler? ImageFailed;
        public void Fail() => ImageFailed?.Invoke(this, new());
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
        public CubicEase? EasingFunction { get; set; }
        public object? Target { get; set; }
        public string? Property { get; set; }
    }
    public sealed class Storyboard
    {
        public static List<Storyboard> Created { get; } = [];
        public static bool ThrowOnBegin { get; set; }
        public List<DoubleAnimation> Children { get; } = [];
        public event EventHandler<object>? Completed;
        public bool Running { get; private set; }
        public int CompletionSubscriptions => Completed?.GetInvocationList().Length ?? 0;
        public Storyboard() => Created.Add(this);
        public static void SetTarget(DoubleAnimation animation, Microsoft.UI.Xaml.DependencyObject target) => animation.Target = target;
        public static void SetTargetProperty(DoubleAnimation animation, string property) => animation.Property = property;
        public void Begin() { if (ThrowOnBegin) throw new InvalidOperationException("inert start failure"); Running = true; }
        public void Stop() => Running = false;
        public void Finish() => Completed?.Invoke(this, EventArgs.Empty);
    }
}

namespace Windows.UI.ViewManagement
{
    public sealed class UISettingsAnimationsEnabledChangedEventArgs : EventArgs;
    public sealed class UISettings
    {
        public static List<UISettings> Created { get; } = [];
        public static bool Enabled { get; set; } = true;
        public bool AnimationsEnabled => Enabled;
        public event Action<UISettings, UISettingsAnimationsEnabledChangedEventArgs>? AnimationsEnabledChanged;
        public int Subscriptions => AnimationsEnabledChanged?.GetInvocationList().Length ?? 0;
        public UISettings() => Created.Add(this);
        public void Change(bool enabled) { Enabled = enabled; AnimationsEnabledChanged?.Invoke(this, new()); }
    }
}

namespace Momoka.Shell.Views
{
    internal static class HomeMotion
    {
        internal static bool AnimationsEnabled => PreferenceEnabled && Windows.UI.ViewManagement.UISettings.Enabled;
        internal static bool PreferenceEnabled { get; set; } = true;
        internal static event Action? PreferenceChanged;
        internal static int Subscriptions => PreferenceChanged?.GetInvocationList().Length ?? 0;
        internal static void Change(bool enabled) { PreferenceEnabled = enabled; PreferenceChanged?.Invoke(); }
    }
    internal static class BrowsePresentation
    {
        internal static Task Gate { get; set; } = Task.CompletedTask;
        internal static Task WaitForImagesAsync() => Gate;
    }
}
