namespace Microsoft.UI.Xaml;

// Value-only substitutes: no WinRT, HWND, layout, input, or native calls.
public enum Visibility { Visible, Collapsed }
public enum ElementTheme { Dark, Light }
public sealed class XamlRoot;
public class RoutedEventArgs : EventArgs;
public class FrameworkElement
{
    public object? Tag { get; set; }
    public XamlRoot? XamlRoot { get; set; }
    public Visibility Visibility { get; set; } = Visibility.Visible;
}
