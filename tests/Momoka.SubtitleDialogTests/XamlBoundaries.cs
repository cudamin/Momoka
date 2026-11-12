// Inert value/event boundaries only. The close guard is the actual production Closing delegate.
namespace Microsoft.UI.Xaml;

public enum Visibility { Visible, Collapsed }
public enum ElementTheme { Dark, Light }
public enum FocusState { Programmatic }
public class RoutedEventArgs : EventArgs;
public sealed class XamlRootChangedEventArgs : EventArgs;
public delegate void TypedEventHandler<TSender, TArgs>(TSender sender, TArgs args);

/// <summary>Inert size/event boundary: the dialog only reads it to fit its own layout.</summary>
public sealed class XamlRoot
{
    public XamlRootSize Size { get; set; } = new(1920, 1080);
    public ContentIslandBoundary ContentIslandEnvironment { get; } = new();
    public event TypedEventHandler<XamlRoot, XamlRootChangedEventArgs>? Changed;
    internal void RaiseChanged() => Changed?.Invoke(this, new XamlRootChangedEventArgs());
}

public sealed class ContentIslandBoundary
{
    public Microsoft.UI.WindowId AppWindowId { get; } = new(1);
}

public readonly struct XamlRootSize(double width, double height)
{
    public double Width { get; } = width;
    public double Height { get; } = height;
}

public class DependencyObject;

public class FrameworkElement : DependencyObject
{
    internal FrameworkElement? Parent { get; set; }
    public bool IsLoaded { get; set; } = true;
    public object? DataContext { get; set; }
    public XamlRoot? XamlRoot { get; set; }
    public Visibility Visibility { get; set; } = Visibility.Visible;
    public bool IsEnabled { get; set; } = true;
    public double MinWidth { get; set; }
    public double Width { get; set; }
    public double MaxHeight { get; set; }
    public FixtureDispatcher DispatcherQueue { get; } = new();
    public event Action<object, RoutedEventArgs>? Loaded { add { } remove { } }
    public event Action<object, RoutedEventArgs>? Unloaded;
    internal void FixtureUnload() => Unloaded?.Invoke(this, new());
    public bool Focus(FocusState state) => true;
}

public sealed class FixtureDispatcher
{
    public bool TryEnqueue(Action action)
    {
        if (SynchronizationContext.Current is Momoka.Tests.UiQueue context) context.Enqueue(action);
        else action();
        return true;
    }
}
