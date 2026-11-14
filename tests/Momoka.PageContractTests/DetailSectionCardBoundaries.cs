namespace Microsoft.UI.Xaml
{
    public enum VerticalAlignment { Top, Center, Bottom, Stretch }
}

namespace Microsoft.UI.Xaml.Controls
{
    public sealed class StackPanel : Microsoft.UI.Xaml.FrameworkElement
    {
        public Microsoft.UI.Xaml.Style? Style { get; set; }
        public Microsoft.UI.Xaml.Thickness Padding { get; set; }
        public double Spacing { get; set; }
        public Microsoft.UI.Xaml.Visibility Visibility { get; set; }
    }

    public sealed class Border : Microsoft.UI.Xaml.FrameworkElement
    {
        public Microsoft.UI.Xaml.Style? Style { get; set; }
        public Microsoft.UI.Xaml.FrameworkElement? Child { get; set; }
    }
}

namespace Momoka.Shell.Views
{
    public sealed class ShelfHead : Microsoft.UI.Xaml.FrameworkElement
    {
        public bool StoreCard { get; set; }
        public Microsoft.UI.Xaml.VerticalAlignment VerticalAlignment { get; set; }
    }
}
