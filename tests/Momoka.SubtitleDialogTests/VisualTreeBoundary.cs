namespace Microsoft.UI.Xaml.Media;

/// <summary>Only parent ownership is substituted; the caller walks the production ancestor chain.</summary>
public static class VisualTreeHelper
{
    public static Microsoft.UI.Xaml.DependencyObject? GetParent(Microsoft.UI.Xaml.DependencyObject value) =>
        (value as Microsoft.UI.Xaml.FrameworkElement)?.Parent;
}
