namespace Windows.Foundation
{
    public delegate void TypedEventHandler<TSender, TArgs>(TSender sender, TArgs args);
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
    public enum DispatcherQueuePriority { Low, High }
    public sealed class DispatcherQueue
    {
        private readonly Queue<Action> _pending = new();
        public bool HasThreadAccess { get; set; } = true;
        public bool RejectEnqueue { get; set; }
        public bool TryEnqueue(DispatcherQueuePriority priority, Action callback)
        {
            if (RejectEnqueue) return false;
            _pending.Enqueue(callback);
            return true;
        }
        public void Drain()
        {
            while (_pending.TryDequeue(out var callback)) callback();
        }
    }
}

namespace Microsoft.UI.Xaml
{
    public delegate void RoutedEventHandler(object sender, RoutedEventArgs args);
    public sealed class RoutedEventArgs : EventArgs;
    public enum Visibility { Visible, Collapsed }
    public sealed class XamlRoot
    {
        public UIElement? Content { get; set; }
    }
    public class DependencyObject
    {
        public DependencyObject? Parent { get; set; }
        public Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; } = new();
    }
    public class UIElement : DependencyObject
    {
        public Visibility Visibility { get; set; }
    }
    public class FrameworkElement : UIElement
    {
        public XamlRoot? XamlRoot { get; set; }
        public double ActualWidth { get; set; } = 200;
        public double ActualHeight { get; set; } = 300;
        public bool IsLoaded { get; set; } = true;
        public double X { get; set; }
        public double Y { get; set; }
        public event RoutedEventHandler? Loaded;
        public event RoutedEventHandler? Unloaded;
        public event EventHandler<object>? LayoutUpdated;
        public Microsoft.UI.Xaml.Media.GeneralTransform TransformToVisual(UIElement root) => new(X, Y);
        public void Load() { IsLoaded = true; Loaded?.Invoke(this, new()); }
        public void Unload() { IsLoaded = false; Unloaded?.Invoke(this, new()); }
        public void Layout() => LayoutUpdated?.Invoke(this, new());
    }
    public sealed class DispatcherTimer
    {
        public static List<DispatcherTimer> Created { get; } = [];
        public DispatcherTimer() => Created.Add(this);
        public TimeSpan Interval { get; set; }
        public bool IsRunning { get; private set; }
        public event EventHandler<object>? Tick;
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
        public void Fire() { if (IsRunning) Tick?.Invoke(this, new()); }
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public sealed class Frame : Microsoft.UI.Xaml.FrameworkElement
    {
        public object? Content { get; set; }
        public event Microsoft.UI.Xaml.Navigation.NavigatingCancelEventHandler? Navigating;
        public void Navigate(object? parameter, bool back = false) => Navigating?.Invoke(this, new()
        { Parameter = parameter, NavigationMode = back ? Microsoft.UI.Xaml.Navigation.NavigationMode.Back : Microsoft.UI.Xaml.Navigation.NavigationMode.New });
    }
    public sealed class Image : Microsoft.UI.Xaml.FrameworkElement
    {
        public Microsoft.UI.Xaml.Media.Imaging.BitmapSource? Source { get; set; }
    }
}

namespace Microsoft.UI.Xaml.Navigation
{
    public enum NavigationMode { New, Back }
    public delegate void NavigatingCancelEventHandler(object sender, NavigatingCancelEventArgs args);
    public sealed class NavigatingCancelEventArgs : EventArgs
    {
        public object? Parameter { get; set; }
        public NavigationMode NavigationMode { get; set; }
    }
}

namespace Microsoft.UI.Xaml.Media
{
    public sealed class GeneralTransform(double x, double y)
    {
        public Windows.Foundation.Rect TransformBounds(Windows.Foundation.Rect bounds) => bounds with { X = x, Y = y };
    }
    public static class VisualTreeHelper
    {
        public static Microsoft.UI.Xaml.DependencyObject? GetParent(Microsoft.UI.Xaml.DependencyObject owner) => owner.Parent;
    }
}

namespace Microsoft.UI.Xaml.Media.Imaging
{
    public sealed class BitmapSource
    {
        public int PixelWidth { get; set; } = 200;
        public int PixelHeight { get; set; } = 300;
    }
}

namespace Microsoft.UI.Xaml.Media.Animation
{
    public sealed class ConnectedAnimation
    {
        public bool Starts { get; set; } = true;
        public bool ThrowOnStart { get; set; }
        public bool ThrowOnCancel { get; set; }
        public int StartCalls { get; private set; }
        public int CancelCalls { get; private set; }
        public Windows.Foundation.TypedEventHandler<ConnectedAnimation, object>? Handlers;
        public event Windows.Foundation.TypedEventHandler<ConnectedAnimation, object>? Completed { add => Handlers += value; remove => Handlers -= value; }
        public bool TryStart(Microsoft.UI.Xaml.UIElement target)
        {
            StartCalls++;
            if (ThrowOnStart) throw new InvalidOperationException("Injected start failure.");
            return Starts;
        }
        public void Cancel()
        {
            CancelCalls++;
            if (ThrowOnCancel) throw new InvalidOperationException("Injected cancel failure.");
        }
        public void Complete() => Handlers?.Invoke(this, new());
        public Action LateComplete()
        {
            var callbacks = Handlers;
            return () => callbacks?.Invoke(this, new());
        }
    }
    public sealed class ConnectedAnimationService
    {
        public static ConnectedAnimationService Current { get; } = new();
        public static bool ThrowOnPrepare { get; set; }
        public static List<ConnectedAnimation> Created { get; } = [];
        public static ConnectedAnimationService GetForCurrentView() => Current;
        public ConnectedAnimation PrepareToAnimate(string key, Microsoft.UI.Xaml.UIElement source)
        {
            if (ThrowOnPrepare) throw new InvalidOperationException("Injected prepare failure.");
            var animation = new ConnectedAnimation();
            Created.Add(animation);
            return animation;
        }
    }
}

namespace Windows.UI.ViewManagement
{
    public sealed class UISettingsAnimationsEnabledChangedEventArgs : EventArgs;
    public sealed class UISettings
    {
        public static bool Enabled { get; set; } = true;
        public static List<UISettings> Created { get; } = [];
        public UISettings() => Created.Add(this);
        public Windows.Foundation.TypedEventHandler<UISettings, UISettingsAnimationsEnabledChangedEventArgs>? Handlers;
        public event Windows.Foundation.TypedEventHandler<UISettings, UISettingsAnimationsEnabledChangedEventArgs>? AnimationsEnabledChanged { add => Handlers += value; remove => Handlers -= value; }
        public static void Change(bool value)
        {
            Enabled = value;
            foreach (var settings in Created.ToArray()) settings.Handlers?.Invoke(settings, new());
        }
    }
}

namespace Momoka.Emby
{
    public static class EmbyItemType
    {
        public const string Movie = "Movie", Series = "Series", Season = "Season", Episode = "Episode";
    }
    public sealed class SourceScope
    {
        public bool IsCurrent { get; set; } = true;
        public object UserStateOwner { get; } = new();
    }
    public sealed class EmbyItem
    {
        public string IdentityKey { get; set; } = "source:item";
        public string Type { get; set; } = "Movie";
        public SourceScope? Source { get; set; } = new();
    }
}

namespace Momoka.Shell.Views
{
    public sealed class CardItem
    {
        public Momoka.Emby.EmbyItem Item { get; set; } = new();
    }
    public sealed class PosterCard : Microsoft.UI.Xaml.FrameworkElement
    {
        public CardItem? Card { get; set; } = new();
        public Microsoft.UI.Xaml.Controls.Image ConnectedPoster { get; set; } = new();
    }
    public sealed class DetailRequest
    {
        public Momoka.Emby.EmbyItem Item { get; set; } = new();
    }
    public sealed class DetailViewModel
    {
        public Momoka.Emby.EmbyItem? CurrentItem { get; set; }
    }
    public sealed class DetailPage : Microsoft.UI.Xaml.FrameworkElement
    {
        public Microsoft.UI.Xaml.Controls.Frame? Frame { get; set; }
        public Microsoft.UI.Xaml.Controls.Image ConnectedPoster { get; set; } = new();
        public DetailViewModel ViewModel { get; } = new();
        public int StoppedArrivals { get; private set; }
        public void StopConnectedPosterArrival() => StoppedArrivals++;
    }
    public static class HomeMotion
    {
        public static bool Preference { get; set; } = true;
        public static bool AnimationsEnabled => Preference && Windows.UI.ViewManagement.UISettings.Enabled;
        public static Action? Handlers;
        public static event Action? PreferenceChanged { add => Handlers += value; remove => Handlers -= value; }
        public static void Change(bool value) { Preference = value; Handlers?.Invoke(); }
    }
    public static class BrowsePresentation
    {
        public static List<(Microsoft.UI.Xaml.Controls.Frame Frame, bool Started)> Results { get; } = [];
        public static void ResolveConnectedMotion(Microsoft.UI.Xaml.Controls.Frame frame, bool started) => Results.Add((frame, started));
    }
}
