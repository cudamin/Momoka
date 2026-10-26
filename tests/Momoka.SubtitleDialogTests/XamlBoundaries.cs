// Inert value/event boundaries only. The close guard is the actual production Closing delegate.
namespace Microsoft.UI.Xaml;

public enum Visibility { Visible, Collapsed }
public enum ElementTheme { Dark, Light }
public enum FocusState { Programmatic }
public sealed class RoutedEventArgs : EventArgs;
public sealed class XamlRootChangedEventArgs : EventArgs;
public delegate void TypedEventHandler<TSender, TArgs>(TSender sender, TArgs args);

/// <summary>Inert size/event boundary: the dialog only reads it to fit its own layout.</summary>
public sealed class XamlRoot
{
    public XamlRootSize Size { get; set; } = new(1920, 1080);
    public event TypedEventHandler<XamlRoot, XamlRootChangedEventArgs>? Changed;
    internal void RaiseChanged() => Changed?.Invoke(this, new XamlRootChangedEventArgs());
}

public readonly struct XamlRootSize(double width, double height)
{
    public double Width { get; } = width;
    public double Height { get; } = height;
}

public class FrameworkElement
{
    public object? DataContext { get; set; }
    public XamlRoot? XamlRoot { get; set; }
    public Visibility Visibility { get; set; } = Visibility.Visible;
    public bool IsEnabled { get; set; } = true;
    public double MinWidth { get; set; }
    public double Width { get; set; }
    public double MaxHeight { get; set; }
    // The dialog only attaches its own handlers; nothing in the fixture raises them.
    public event Action<object, RoutedEventArgs>? Loaded { add { } remove { } }
    public event Action<object, RoutedEventArgs>? Unloaded { add { } remove { } }
    public bool Focus(FocusState state) => true;
}
