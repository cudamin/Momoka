using Momoka.MoviePilot;

// These are inert XAML/WinUI boundary objects, not a rendered UI or an alternative dialog workflow.
namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
    public enum ElementTheme { Dark, Light }
    public class RoutedEventArgs : EventArgs;
    public class FrameworkElement
    {
        public bool IsEnabled { get; set; } = true;
        public Visibility Visibility { get; set; } = Visibility.Visible;
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public enum InfoBarSeverity { Informational, Success, Warning, Error }
    public enum AutoSuggestionBoxTextChangeReason { UserInput, ProgrammaticChange }
    public class SelectionChangedEventArgs : Microsoft.UI.Xaml.RoutedEventArgs;
    public class ContentDialogButtonClickEventArgs : EventArgs { public bool Cancel { get; set; } }
    public class ContentDialogClosingEventArgs : EventArgs { public bool Cancel { get; set; } }
    public delegate void DialogEvent(ContentDialog sender, object args);
    public delegate void ClosingEvent(ContentDialog sender, ContentDialogClosingEventArgs args);
    public class ContentDialog : Microsoft.UI.Xaml.FrameworkElement
    {
        public Microsoft.UI.Xaml.ElementTheme RequestedTheme { get; set; }
        public object? Title { get; set; }
        public string PrimaryButtonText { get; set; } = "";
        public string SecondaryButtonText { get; set; } = "";
        public string CloseButtonText { get; set; } = "";
        public bool IsPrimaryButtonEnabled { get; set; }
        public bool IsSecondaryButtonEnabled { get; set; }
        public FixtureDispatcher DispatcherQueue { get; } = new();
        public event DialogEvent? Opened;
        public event ClosingEvent? Closing;
        public event DialogEvent? Closed;
        public void EmitOpened() => Opened?.Invoke(this, EventArgs.Empty);
        public void EmitClosed() => Closed?.Invoke(this, EventArgs.Empty);
        public bool EmitClosing()
        {
            var args = new ContentDialogClosingEventArgs();
            Closing?.Invoke(this, args);
            return args.Cancel;
        }
    }
    public sealed class FixtureDispatcher
    {
        public bool TryEnqueue(Action callback) { callback(); return true; }
    }
    public class TextBox : Microsoft.UI.Xaml.FrameworkElement { public string Text { get; set; } = ""; }
    public class TextBlock : TextBox;
    public class AutoSuggestBox : TextBox { public object? ItemsSource { get; set; } }
    public class AutoSuggestBoxTextChangedEventArgs : EventArgs { public AutoSuggestionBoxTextChangeReason Reason { get; set; } }
    public class AutoSuggestBoxQuerySubmittedEventArgs : EventArgs
    {
        public object? ChosenSuggestion { get; set; }
        public string QueryText { get; set; } = "";
    }
    public class AutoSuggestBoxSuggestionChosenEventArgs : EventArgs { public object? SelectedItem { get; set; } }
    public class ComboBoxItem { public object? Tag { get; set; } }
    public class ComboBox : TextBox
    {
        public static object TextProperty { get; } = new();
        public List<object> Items { get; } = [];
        public object? ItemsSource { get; set; }
        public object? SelectedItem { get; set; }
        public int SelectedIndex
        {
            get => SelectedItem is null ? -1 : Items.IndexOf(SelectedItem);
            set => SelectedItem = value >= 0 && value < Items.Count ? Items[value] : null;
        }
        public void RegisterPropertyChangedCallback(object property, Action<object, object> callback) { }
    }
    public class CheckBox : Microsoft.UI.Xaml.FrameworkElement { public bool? IsChecked { get; set; } }
    public class ProgressRing : Microsoft.UI.Xaml.FrameworkElement { public bool IsActive { get; set; } }
    public class InfoBar : Microsoft.UI.Xaml.FrameworkElement
    {
        public InfoBarSeverity Severity { get; set; }
        public string Message { get; set; } = "";
        public bool IsOpen { get; set; }
    }
    public class ScrollViewer : Microsoft.UI.Xaml.FrameworkElement
    {
        public double ScrollableHeight { get; set; }
        public void UpdateLayout() { }
        public void ChangeView(double? horizontal, double? vertical, float? zoom, bool disableAnimation) { }
    }
}

namespace Momoka.Shell.Views
{
    public static class ThemeHost
    {
        public sealed record FixtureTheme(bool IsDark);
        public static FixtureTheme Current { get; } = new(true);
    }
    public sealed partial class MoviePilotReorganizeDialog
    {
        internal Microsoft.UI.Xaml.Controls.TextBox SourcePathBox { get; } = new();
        internal Microsoft.UI.Xaml.Controls.AutoSuggestBox HistoryQuery { get; } = new();
        internal Microsoft.UI.Xaml.Controls.ComboBox TypeBox { get; } = new();
        internal Microsoft.UI.Xaml.Controls.ComboBox TransferTypeBox { get; } = new();
        internal Microsoft.UI.Xaml.Controls.TextBox SeasonBox { get; } = new();
        internal Microsoft.UI.Xaml.Controls.TextBox EpisodeBox { get; } = new();
        internal Microsoft.UI.Xaml.Controls.AutoSuggestBox MediaIdBox { get; } = new();
        internal Microsoft.UI.Xaml.Controls.ComboBox TargetPathBox { get; } = new();
        internal Microsoft.UI.Xaml.Controls.ComboBox TargetStorageBox { get; } = new();
        internal Microsoft.UI.Xaml.Controls.ComboBox SourceBox { get; } = new();
        internal Microsoft.UI.Xaml.Controls.ComboBox HistoryBox { get; } = new();
        internal Microsoft.UI.Xaml.Controls.TextBox OriginalSourceBox { get; } = new();
        internal Microsoft.UI.Xaml.Controls.TextBlock HistorySummary { get; } = new();
        internal Microsoft.UI.Xaml.Controls.TextBox PartBox { get; } = new();
        internal Microsoft.UI.Xaml.Controls.TextBox MinSizeBox { get; } = new() { Text = "0" };
        internal Microsoft.UI.Xaml.Controls.CheckBox TypeFolderToggle { get; } = new();
        internal Microsoft.UI.Xaml.Controls.CheckBox CategoryFolderToggle { get; } = new();
        internal Microsoft.UI.Xaml.Controls.CheckBox ScrapeToggle { get; } = new();
        internal Microsoft.UI.Xaml.Controls.CheckBox FromHistoryToggle { get; } = new();
        internal Microsoft.UI.Xaml.Controls.CheckBox ConfirmChanges { get; } = new();
        internal Microsoft.UI.Xaml.Controls.TextBlock PreviewSummary { get; } = new();
        internal Microsoft.UI.Xaml.Controls.TextBlock ConfirmationText { get; } = new();
        internal Microsoft.UI.Xaml.Controls.InfoBar Problem { get; } = new();
        internal Microsoft.UI.Xaml.Controls.ProgressRing PreviewRing { get; } = new();
        internal Microsoft.UI.Xaml.FrameworkElement FormPanel { get; } = new();
        internal Microsoft.UI.Xaml.FrameworkElement EpisodeRow { get; } = new();
        internal Microsoft.UI.Xaml.FrameworkElement PreviewList { get; } = new();
        internal Microsoft.UI.Xaml.FrameworkElement ConfirmationPanel { get; } = new();
        internal Microsoft.UI.Xaml.FrameworkElement PreviewButton { get; } = new();
        internal Microsoft.UI.Xaml.Controls.ScrollViewer FormScroll { get; } = new();

        private void InitializeComponent()
        {
            TypeBox.Items.Add(new Microsoft.UI.Xaml.Controls.ComboBoxItem { Tag = MoviePilotTransferRequest.TypeMovie });
            TypeBox.Items.Add(new Microsoft.UI.Xaml.Controls.ComboBoxItem { Tag = MoviePilotTransferRequest.TypeSeries });
            foreach (var tag in new[] { "", "copy", "move", "link", "softlink" })
                TransferTypeBox.Items.Add(new Microsoft.UI.Xaml.Controls.ComboBoxItem { Tag = tag });
        }
        internal void FixturePrepare(MoviePilotTransferPreview preview)
        {
            _histories = preview.Request.Histories;
            TargetStorageBox.SelectedItem = new MoviePilotStorage("fixture", preview.Request.TargetStorage);
            TargetPathBox.Text = preview.Request.TargetPath;
            TransferTypeBox.SelectedIndex = 4;
            SourceBox.SelectedItem = new MoviePilotMediaSource("fixture", preview.Request.MediaSource, ["电影"]);
            _preview = preview;
            ConfirmChanges.IsChecked = true;
            UpdateSubmit();
        }
        internal Task FixtureSubmitAsync(bool background = false) => SubmitAsync(background);
        internal Task FixturePreviewAsync()
        {
            OnPreview(this, new Microsoft.UI.Xaml.RoutedEventArgs());
            if (_busy) throw new Momoka.Tests.AssertionException("内存夹具预览需要同步完成；不接受盲等待");
            return Task.CompletedTask;
        }
        internal bool FixtureSubmitted => _submitted;
        internal MoviePilotTransferPreview? FixturePreview => _preview;
    }
}
