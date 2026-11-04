// Tree and event boundaries only. The production FocusWatch makes every ownership decision.
namespace Microsoft.UI.Xaml
{
    public class DependencyObject
    {
        internal DependencyObject? TreeParent { get; set; }
    }

    public enum FocusState { Unfocused, Pointer, Keyboard, Programmatic }
    public delegate void RoutedEventHandler(object sender, RoutedEventArgs args);
}

namespace Microsoft.UI.Xaml.Controls
{
    public class Control : FrameworkElement
    {
        public FocusState FocusState { get; set; }
        public event RoutedEventHandler? GotFocus;
        public event RoutedEventHandler? LostFocus;
        internal int GotHandlers => GotFocus?.GetInvocationList().Length ?? 0;
        internal int LostHandlers => LostFocus?.GetInvocationList().Length ?? 0;
        internal void RaiseGotFocus() => GotFocus?.Invoke(this, new());
        internal void RaiseLostFocus() => LostFocus?.Invoke(this, new());
    }

    public class UserControl : Control;
    public sealed class Button : Primitives.ButtonBase;
    public sealed class ItemContainer : Control;
    public sealed class ItemsView : Control
    {
        public int BringCalls { get; private set; }
        public bool TryGetItemIndex(double x, double y, out int index) { index = 0; return Children.Count > 0; }
        public void UpdateLayout() { }
        public void StartBringItemIntoView(int index, BringIntoViewOptions options) => BringCalls++;
    }
    public sealed class ScrollView : Control
    {
        public double VerticalOffset { get; set; }
        public void ScrollTo(double _, double y, ScrollingScrollOptions options) => VerticalOffset = y;
    }
}

namespace Microsoft.UI.Xaml.Controls.Primitives
{
    public class ButtonBase : Controls.Control;
}

namespace Microsoft.UI.Xaml.Media
{
    public static class VisualTreeHelper
    {
        public static DependencyObject? GetParent(DependencyObject node) => node.TreeParent;
    }
}

namespace Momoka.Shell.Views
{
    public sealed class PosterCard : Microsoft.UI.Xaml.Controls.UserControl
    {
        public CardItem? Card { get; set; }
    }
    public sealed class EpisodeRow : Microsoft.UI.Xaml.Controls.UserControl
    {
        public CardItem? Card { get; set; }
    }
}
