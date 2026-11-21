using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace Microsoft.UI.Xaml.Controls;

public sealed class NonVirtualizingLayoutContext(IReadOnlyList<UIElement> children)
{
    public IReadOnlyList<UIElement> Children { get; } = children;
}

public abstract class NonVirtualizingLayout
{
    protected abstract Size MeasureOverride(NonVirtualizingLayoutContext context, Size availableSize);
    protected abstract Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize);
    internal Size MeasureForFixture(NonVirtualizingLayoutContext context, Size size) => MeasureOverride(context, size);
    internal Size ArrangeForFixture(NonVirtualizingLayoutContext context, Size size) => ArrangeOverride(context, size);
}
