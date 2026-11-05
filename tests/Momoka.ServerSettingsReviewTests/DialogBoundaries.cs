using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
    public sealed class Style { }
    public sealed class RoutedEventArgs : EventArgs { }
    public readonly record struct Thickness(double Left, double Top, double Right, double Bottom);
    public class FrameworkElement
    {
        public object? Tag { get; set; }
        public object? Content { get; set; }
        public Thickness Margin { get; set; }
        public double MinWidth { get; set; }
        public Visibility Visibility { get; set; }
        public Style? Style { get; set; }
        public bool IsEnabled { get; set; } = true;
    }
}
namespace Microsoft.UI.Xaml.Controls
{
    public enum Orientation { Horizontal, Vertical }
    public enum InfoBarSeverity { Informational, Success, Warning, Error }
    public class CheckBox : FrameworkElement
    {
        public bool? IsChecked { get; set; }
        public bool IsThreeState { get; set; }
        public event EventHandler<RoutedEventArgs>? Click;
        public void RaiseClick() => Click?.Invoke(this, new());
    }
    public class StackPanel : FrameworkElement
    {
        public double Spacing { get; set; }
        public Orientation Orientation { get; set; }
        public List<FrameworkElement> Children { get; } = [];
    }
    public class TextBlock : FrameworkElement { public string Text { get; set; } = ""; }
    public class TextBox : FrameworkElement { public string Text { get; set; } = ""; }
    public class ComboBox : FrameworkElement { public int SelectedIndex { get; set; } }
    public class Button : FrameworkElement { }
    public class InfoBar : FrameworkElement
    {
        public InfoBarSeverity Severity { get; set; }
        public string Message { get; set; } = "";
        public bool IsOpen { get; set; }
    }
    public class ContentDialogButtonClickEventArgs : EventArgs { public bool Cancel { get; set; } }
    public class ContentDialog : FrameworkElement
    {
        public object? Title { get; set; }
        public Dictionary<string, object> Resources { get; } = [];
        public bool IsPrimaryButtonEnabled { get; set; } = true;
        public bool Hidden { get; private set; }
        public event Action<ContentDialog, ContentDialogButtonClickEventArgs>? PrimaryButtonClick;
        public void RaisePrimary() => PrimaryButtonClick?.Invoke(this, new());
        public void Hide() => Hidden = true;
    }
}
namespace Momoka.Shell.Views
{
    public sealed partial class NotificationEditDialog
    {
        internal TextBox NameBox { get; } = new();
        internal TextBox UrlBox { get; } = new();
        internal ComboBox RequestType { get; } = new();
        internal StackPanel WebhookFields { get; } = new();
        internal TextBlock OtherServiceNote { get; } = new();
        internal StackPanel EventList { get; } = new();
        internal StackPanel FilterList { get; } = new();
        internal Button TestButton { get; } = new();
        internal InfoBar ResultBar { get; } = new();
        private void InitializeComponent()
        {
            Resources["DlgNote"] = new Style();
            Resources["DlgFaint"] = new Style();
        }
    }
}
