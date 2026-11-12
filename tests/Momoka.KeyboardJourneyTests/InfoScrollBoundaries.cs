namespace Windows.Foundation
{
    public readonly record struct Point(double X, double Y);
}

namespace Microsoft.UI.Xaml
{
    public class UIElement;
    public sealed class InfoPanelBoundary
    {
        public double Top { get; set; }
        public InfoTransform TransformToVisual(UIElement _) => new(Top);
    }
    public sealed class InfoTransform(double top)
    {
        public Windows.Foundation.Point TransformPoint(Windows.Foundation.Point _) => new(0, top);
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public enum ScrollingAnimationMode { Disabled }
    public sealed class ScrollingScrollOptions(ScrollingAnimationMode mode)
    {
        public ScrollingAnimationMode Mode { get; } = mode;
    }
    public sealed class ScrollView
    {
        public object? Content { get; set; } = new Microsoft.UI.Xaml.UIElement();
        public double ScrollableHeight { get; set; }
        public double VerticalOffset { get; set; }
        public List<double> Requests { get; } = [];
        public int LayoutReads { get; private set; }
        public void UpdateLayout() => LayoutReads++;
        public void ScrollTo(double x, double y, ScrollingScrollOptions _) { Requests.Add(y); VerticalOffset = y; }
    }
}

namespace Momoka.Shell.Views
{
    internal sealed partial class DetailPage
    {
        internal Microsoft.UI.Xaml.Controls.ScrollView Body { get; } = new();
        internal Microsoft.UI.Xaml.InfoPanelBoundary InfoPanel { get; } = new();
    }
}
