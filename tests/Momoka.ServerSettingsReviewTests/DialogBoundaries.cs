using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
    public enum ElementTheme { Light, Dark }
    public sealed class XamlRoot;
    public sealed class Style { }
    public sealed class RoutedEventArgs : EventArgs { }
    public readonly record struct Thickness(double Left, double Top, double Right, double Bottom);
    public class FrameworkElement
    {
        public XamlRoot? XamlRoot { get; set; } = new();
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
    public sealed class TextChangedEventArgs : EventArgs;
    public class TextBox : FrameworkElement { public string Text { get; set; } = ""; public object? Header { get; set; } }
    public class ComboBox : FrameworkElement { public int SelectedIndex { get; set; } }
    public class Button : FrameworkElement { public Action? Invoke { get; set; } }
    public class InfoBar : FrameworkElement
    {
        public InfoBarSeverity Severity { get; set; }
        public string Message { get; set; } = "";
        public bool IsOpen { get; set; }
    }
    public enum ContentDialogResult { None, Primary, Secondary }
    public class ContentDialogClosingEventArgs : EventArgs { public bool Cancel { get; set; } }
    public class ContentDialogButtonClickEventArgs : EventArgs
    {
        public bool Cancel { get; set; }
        private TaskCompletionSource? _pending;
        public DialogDeferral GetDeferral() { _pending = new(TaskCreationOptions.RunContinuationsAsynchronously); return new(_pending); }
        internal Task Completed => _pending?.Task ?? Task.CompletedTask;
    }
    public sealed class DialogDeferral(TaskCompletionSource pending) { public void Complete() => pending.TrySetResult(); }
    public class ContentDialog : FrameworkElement
    {
        internal static bool ManualShow { get; set; }
        private TaskCompletionSource<ContentDialogResult>? _shown;
        public object? Title { get; set; }
        public Dictionary<string, object> Resources { get; } = [];
        public bool IsPrimaryButtonEnabled { get; set; } = true;
        public bool Hidden { get; private set; }
        public bool IsOpen { get; private set; }
        public ElementTheme RequestedTheme { get; set; }
        public event Action<ContentDialog, ContentDialogButtonClickEventArgs>? PrimaryButtonClick;
        public event Action<ContentDialog, ContentDialogClosingEventArgs>? Closing;
        public void RaisePrimary() => PrimaryButtonClick?.Invoke(this, new());
        public void Hide() { var args = new ContentDialogClosingEventArgs(); Closing?.Invoke(this, args); if (!args.Cancel) { Hidden = true; IsOpen = false; _shown?.TrySetResult(ContentDialogResult.None); } }
        public async Task<ContentDialogResult> ShowAsync()
        {
            IsOpen = true;
            if (ManualShow) { _shown = new(TaskCreationOptions.RunContinuationsAsynchronously); return await _shown.Task; }
            var result = await MetadataPlan.OnShow(this);
            if (result == ContentDialogResult.Primary) await SaveAsync();
            else Hide();
            return IsOpen ? ContentDialogResult.None : result;
        }
        public async Task SaveAsync()
        {
            var args = new ContentDialogButtonClickEventArgs();
            PrimaryButtonClick?.Invoke(this, args);
            await args.Completed;
            if (!args.Cancel) Hide();
        }
    }
}
namespace Momoka.Shell.Views
{
    public sealed class ThemeValue { public bool IsDark => true; }
    public static class ThemeHost { public static ThemeValue Current { get; } = new(); }
    internal interface IShellActions
    {
        void Notify(string message, InfoBarSeverity severity = InfoBarSeverity.Informational);
        Momoka.Emby.ItemMenuPermissions MenuPermissionsFor(Momoka.Emby.EmbySessionScope scope);
    }
    internal sealed class CardItem { public required Momoka.Emby.EmbyItem Item { get; init; } }
    public sealed partial class MetadataDialog
    {
        internal readonly TextBox NameBox = new(), OriginalTitleBox = new(), SortNameBox = new(), OverviewBox = new(), OfficialRatingBox = new(), CommunityRatingBox = new(), CriticRatingBox = new(), ProductionYearBox = new(), PremiereDateBox = new(), GenresBox = new(), TagsBox = new(), ParentIndexBox = new(), IndexBox = new();
        internal readonly FrameworkElement IndexRow = new(), EditorScroll = new(), SavingProgress = new();
        internal readonly InfoBar Problem = new();
        private void InitializeComponent() { }
    }
    public sealed class EmbyMetadataDialog : ContentDialog
    {
        public EmbyMetadataDialog(string name, Momoka.Emby.EmbyMetadataEdit edit, Momoka.Emby.EmbyMetadataEditorInfo info, Func<Momoka.Emby.EmbyMetadataEdit, Task<bool>> save) { _ = name; Edit = edit; _ = info; _ = save; }
        public Momoka.Emby.EmbyMetadataEdit Edit { get; }
        public bool Changed => false;
        public void SetItemType(string type) { }
    }
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
            TestButton.Invoke = () => OnTest(this, new());
        }
    }
}
