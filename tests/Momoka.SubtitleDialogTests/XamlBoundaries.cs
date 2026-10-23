// Inert value/event boundaries only. The close guard is the actual production Closing delegate.
namespace Microsoft.UI.Xaml;

public enum Visibility { Visible, Collapsed }
public enum ElementTheme { Dark, Light }
public enum FocusState { Programmatic }
public sealed class RoutedEventArgs : EventArgs;
public sealed class XamlRoot;

public class FrameworkElement
{
    public object? DataContext { get; set; }
    public XamlRoot? XamlRoot { get; set; }
    public Visibility Visibility { get; set; } = Visibility.Visible;
    public bool IsEnabled { get; set; } = true;
    public bool Focus(FocusState state) => true;
}
