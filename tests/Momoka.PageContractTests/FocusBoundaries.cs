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
    public sealed class ItemsView : Control;
    public sealed class ScrollView : Control;
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
    public sealed class PosterCard : Microsoft.UI.Xaml.Controls.UserControl;
    public sealed class EpisodeRow : Microsoft.UI.Xaml.Controls.UserControl;
}
