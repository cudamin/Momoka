namespace Windows.Foundation
{
    public delegate void TypedEventHandler<TSender, TArgs>(TSender sender, TArgs args);
    public readonly record struct Point(double X, double Y);
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
        public DispatcherQueueTimer CreateTimer() => new();
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
    public sealed class DispatcherQueueTimer
    {
        public static List<DispatcherQueueTimer> Created { get; } = [];
        public DispatcherQueueTimer() => Created.Add(this);
        public TimeSpan Interval { get; set; }
        public bool IsRepeating { get; set; }
        public bool IsRunning { get; private set; }
        public event Windows.Foundation.TypedEventHandler<DispatcherQueueTimer, object>? Tick;
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
        public void Fire() { if (IsRunning) Tick?.Invoke(this, new()); }
        public Action CapturePostedTick()
        {
            var callbacks = Tick;
            return () => callbacks?.Invoke(this, new());
        }
        public bool HasHandlers => Tick is not null;
    }
}

namespace Microsoft.UI.Xaml
{
    public delegate void RoutedEventHandler(object sender, RoutedEventArgs args);
    public sealed class RoutedEventArgs : EventArgs;
    public readonly record struct Duration(TimeSpan TimeSpan);
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
        public double Opacity { get; set; } = 1;
        public object? RenderTransform { get; set; }
        public Windows.Foundation.Point RenderTransformOrigin { get; set; }
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
        public bool HasLoadedHandlers => Loaded is not null;
        public bool HasUnloadedHandlers => Unloaded is not null;
        public bool HasLayoutHandlers => LayoutUpdated is not null;
        public Microsoft.UI.Xaml.Media.GeneralTransform TransformToVisual(UIElement root) => new(X, Y);
        public void Load() { IsLoaded = true; Loaded?.Invoke(this, new()); }
        public void Unload() { IsLoaded = false; Unloaded?.Invoke(this, new()); }
        public void Layout() => LayoutUpdated?.Invoke(this, new());
        public Action CapturePostedLoaded()
        {
            var callbacks = Loaded;
            return () => callbacks?.Invoke(this, new());
        }
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
        public Action CapturePostedTick()
        {
            var callback = Tick;
            return () => callback?.Invoke(this, new());
        }
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public class Page : Microsoft.UI.Xaml.FrameworkElement
    {
        public Frame? Frame { get; set; }
    }
    public sealed class ScrollView : Microsoft.UI.Xaml.FrameworkElement;
    public sealed class ScrollViewer : Microsoft.UI.Xaml.FrameworkElement;
    public sealed class Frame : Microsoft.UI.Xaml.FrameworkElement
    {
        public object? Content { get; set; }
        public event Microsoft.UI.Xaml.Navigation.NavigatingCancelEventHandler? Navigating;
        public event Microsoft.UI.Xaml.Navigation.NavigationEventHandler? NavigationStopped;
        public event Microsoft.UI.Xaml.Navigation.NavigationFailedEventHandler? NavigationFailed;
        public bool HasNavigationHandlers => Navigating is not null || NavigationStopped is not null || NavigationFailed is not null;
        public void Navigate(object? parameter, bool back = false) => Navigating?.Invoke(this, new()
        { Parameter = parameter, NavigationMode = back ? Microsoft.UI.Xaml.Navigation.NavigationMode.Back : Microsoft.UI.Xaml.Navigation.NavigationMode.New });
        public void StopNavigation() => NavigationStopped?.Invoke(this, new());
        public void FailNavigation() => NavigationFailed?.Invoke(this, new());
    }
    public sealed class Image : Microsoft.UI.Xaml.FrameworkElement
    {
        public Microsoft.UI.Xaml.Media.Imaging.BitmapSource? Source { get; set; }
    }
    public sealed class TextBlock : Microsoft.UI.Xaml.FrameworkElement
    {
        public double FontSize { get; set; } = 32;
    }
}

namespace Microsoft.UI.Xaml.Navigation
{
    public enum NavigationMode { New, Back }
    public delegate void NavigatingCancelEventHandler(object sender, NavigatingCancelEventArgs args);
    public delegate void NavigationEventHandler(object sender, NavigationEventArgs args);
    public delegate void NavigationFailedEventHandler(object sender, NavigationFailedEventArgs args);
    public sealed class NavigationEventArgs : EventArgs;
    public sealed class NavigationFailedEventArgs : EventArgs;
    public sealed class NavigatingCancelEventArgs : EventArgs
    {
        public object? Parameter { get; set; }
        public NavigationMode NavigationMode { get; set; }
    }
}

namespace Microsoft.UI.Xaml.Media
{
    public sealed class CompositeTransform : Microsoft.UI.Xaml.DependencyObject
    {
        public double ScaleX { get; set; } = 1;
        public double ScaleY { get; set; } = 1;
        public double TranslateX { get; set; }
        public double TranslateY { get; set; }
    }
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
    public class BitmapSource
    {
        public int PixelWidth { get; set; } = 200;
        public int PixelHeight { get; set; } = 300;
    }
    public sealed class BitmapImage : BitmapSource;
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
        public CubicEase? EasingFunction { get; set; }
        public Microsoft.UI.Xaml.DependencyObject? Target { get; set; }
        public string? Property { get; set; }
    }
    public sealed class Storyboard
    {
        public static List<Storyboard> Created { get; } = [];
        public Storyboard() => Created.Add(this);
        public List<DoubleAnimation> Children { get; } = [];
        public event EventHandler<object>? Completed;
        public bool HasHandlers => Completed is not null;
        public int BeginCalls { get; private set; }
        public int StopCalls { get; private set; }
        public static void SetTarget(DoubleAnimation animation, Microsoft.UI.Xaml.DependencyObject target) => animation.Target = target;
        public static void SetTargetProperty(DoubleAnimation animation, string property) => animation.Property = property;
        public void Begin() => BeginCalls++;
        public void Stop() => StopCalls++;
        public void Complete() => Completed?.Invoke(this, new());
    }
    public sealed class DirectConnectedAnimationConfiguration;
    public sealed class ConnectedAnimation
    {
        public object? Configuration { get; set; }
        public enum EventFailure { None, Before, After }
        public EventFailure AddFailure { get; set; }
        public EventFailure RemoveFailure { get; set; }
        public bool Starts { get; set; } = true;
        public bool ThrowOnStart { get; set; }
        public Action? Starting { get; set; }
        public bool ThrowOnCancel { get; set; }
        public int StartCalls { get; private set; }
        public int CancelCalls { get; private set; }
        public Windows.Foundation.TypedEventHandler<ConnectedAnimation, object>? Handlers;
        public event Windows.Foundation.TypedEventHandler<ConnectedAnimation, object>? Completed
        {
            add
            {
                if (AddFailure == EventFailure.Before) throw new InvalidOperationException("Injected Completed pre-add failure.");
                Handlers += value;
                if (AddFailure == EventFailure.After) throw new InvalidOperationException("Injected Completed post-add failure.");
            }
            remove
            {
                if (RemoveFailure == EventFailure.Before) throw new InvalidOperationException("Injected Completed pre-remove failure.");
                Handlers -= value;
                if (RemoveFailure == EventFailure.After) throw new InvalidOperationException("Injected Completed post-remove failure.");
            }
        }
        public bool TryStart(Microsoft.UI.Xaml.UIElement target)
        {
            StartCalls++;
            Starting?.Invoke();
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
        public static Action? Preparing { get; set; }
        public static List<ConnectedAnimation> Created { get; } = [];
        public static ConnectedAnimationService GetForCurrentView() => Current;
        public ConnectedAnimation PrepareToAnimate(string key, Microsoft.UI.Xaml.UIElement source)
        {
            Preparing?.Invoke();
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
        public enum EventFailure { None, Before, After }
        public static bool Enabled { get; set; } = true;
        public static EventFailure AddFailure { get; set; }
        public static EventFailure RemoveFailure { get; set; }
        public static List<UISettings> Created { get; } = [];
        public UISettings() => Created.Add(this);
        public Windows.Foundation.TypedEventHandler<UISettings, UISettingsAnimationsEnabledChangedEventArgs>? Handlers;
        public event Windows.Foundation.TypedEventHandler<UISettings, UISettingsAnimationsEnabledChangedEventArgs>? AnimationsEnabledChanged
        {
            add
            {
                if (AddFailure == EventFailure.Before) throw new InvalidOperationException("Injected pre-add failure.");
                Handlers += value;
                if (AddFailure == EventFailure.After) throw new InvalidOperationException("Injected post-add failure.");
            }
            remove
            {
                if (RemoveFailure == EventFailure.Before) throw new InvalidOperationException("Injected pre-remove failure.");
                Handlers -= value;
                if (RemoveFailure == EventFailure.After) throw new InvalidOperationException("Injected post-remove failure.");
            }
        }
        public Action CapturePostedChange()
        {
            var callbacks = Handlers;
            return () => callbacks?.Invoke(this, new());
        }
        public static void Change(bool value)
        {
            Enabled = value;
            foreach (var settings in Created.ToArray()) settings.Handlers?.Invoke(settings, new());
        }
    }
}

namespace Momoka.Emby
{
    public readonly record struct ArtworkRef(string ItemId, string ImageType, string Tag);
    public static class EmbyImageStore
    {
        public const string Primary = "Primary";
        public static string? TagFor(EmbyItem item, string type) => item.PrimaryTag;
        public static string ImageItemId(EmbyItem item, string type) => item.ImageOwner;
    }
    public static class ItemArtwork
    {
        public static ArtworkRef? Plate(EmbyItem item) => item.PlateArtwork;
    }
    public static class EmbyItemType
    {
        public const string Movie = "Movie", Series = "Series", Season = "Season", Episode = "Episode", Person = "Person";
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
        public string? PrimaryTag { get; set; } = "poster";
        public string ImageOwner { get; set; } = "item";
        public ArtworkRef? PlateArtwork { get; set; } = new("item", "Logo", "logo");
    }
}

namespace Momoka.Shell.Views
{
    public sealed class CardItem
    {
        public Momoka.Emby.EmbyItem Item { get; set; } = new();
        public Momoka.Emby.ArtworkRef? LoadedPosterArtwork { get; set; } = new("item", "Primary", "poster");
    }
    public sealed class PosterCard : Microsoft.UI.Xaml.FrameworkElement
    {
        public CardItem? Card { get; set; } = new();
        public Microsoft.UI.Xaml.Controls.Image ConnectedPoster { get; set; } = new();
        public int Retentions { get; private set; }
        internal PreviewHeadingCapture? PreviewHeading { get; set; }
        internal PreviewHeadingCapture? TakePreviewHeading()
        {
            var capture = PreviewHeading;
            PreviewHeading = null;
            return capture;
        }
        public Action RetainConnectedPoster()
        {
            Retentions++;
            return () => Retentions--;
        }

        public int ReturnSettles { get; private set; }
        public void SettleForConnectedReturn() => ReturnSettles++;
    }
    public sealed class DetailRequest
    {
        public Momoka.Emby.EmbyItem Item { get; set; } = new();
    }
    public sealed class DetailViewModel
    {
        private Momoka.Emby.EmbyItem? _currentItem;
        public Microsoft.UI.Xaml.Media.Imaging.BitmapSource? StillImage { get; set; }
        public Microsoft.UI.Xaml.Media.Imaging.BitmapImage? PlateImage { get; set; }
        public Momoka.Emby.EmbyItem? CurrentItem
        {
            get => _currentItem;
            set { _currentItem = value; PropertyChanged?.Invoke(this, new(nameof(CurrentItem))); }
        }
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public bool HasPropertyHandlers => PropertyChanged is not null;
        public bool UseConnectedPoster(Momoka.Emby.EmbyItem item, Microsoft.UI.Xaml.Media.Imaging.BitmapSource picture) => true;
        public bool UseConnectedPlate(Momoka.Emby.EmbyItem item, Microsoft.UI.Xaml.Media.Imaging.BitmapImage picture)
        {
            PlateImage = picture;
            return true;
        }
    }
    public sealed partial class DetailPage : Microsoft.UI.Xaml.Controls.Page
    {
        public Microsoft.UI.Xaml.Controls.Image ConnectedPoster { get; set; } = new();
        public DetailViewModel ViewModel { get; } = new();
        public int StoppedArrivals { get; private set; }
        public int SeededWidths { get; private set; }
        public int ForcedLayouts { get; private set; }
        internal Microsoft.UI.Xaml.Controls.TextBlock TitleText { get; } = new();
        internal Microsoft.UI.Xaml.Controls.Image TitlePlate { get; } = new();
        internal bool HeadingRunning => _previewHeadingFlight is not null;
        internal int StoppedPlateArrivals { get; private set; }
        internal Action? DuringLayout { get; set; }
        public void StopConnectedPosterArrival() => StoppedArrivals++;
        internal void StopConnectedPlateArrival() { StoppedPlateArrivals++; TitlePlate.Opacity = 1; }
        public void SeedConnectedLayoutWidth() => SeededWidths++;
        public void ForceConnectedLayout() { ForcedLayouts++; DuringLayout?.Invoke(); }
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
