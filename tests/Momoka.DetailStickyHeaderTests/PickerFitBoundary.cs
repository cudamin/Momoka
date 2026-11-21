using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views
{
    public sealed partial class DetailPage
    {
        internal StackPanel ColumnPickers { get; } = new();
        internal StackPanel PickerPanel { get; } = new();
        internal ComboBox ColumnSourcePicker { get; } = new();
        internal ComboBox ColumnAudioPicker { get; } = new();
        internal ComboBox ColumnSubtitlePicker { get; } = new();
        internal ComboBox SourcePicker { get; } = new();
        internal ComboBox AudioPicker { get; } = new();
        internal ComboBox SubtitlePicker { get; } = new();
        internal Grid AudioPickerRow { get; } = new();
        internal Grid SubtitlePickerRow { get; } = new();
        internal TextBlock ColumnAudioText { get; } = new();

        internal void SetUpPickerGeometry(bool store = true, bool compact = false)
        {
            ViewModel.StoreMode = store;
            ViewModel.IsCompact = compact;
            XamlRoot!.Content = this;
            foreach (var picker in new[] { ColumnSourcePicker, ColumnAudioPicker, ColumnSubtitlePicker,
                SourcePicker, AudioPicker, SubtitlePicker })
                picker.Visibility = Visibility.Collapsed;
            ColumnPickers.Parent = this;
            ColumnPickers.LayoutX = 100;
            ColumnPickers.ActualWidth = 600;
            ColumnPickers.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            PickerPanel.Parent = this;
            PickerPanel.LayoutX = 100;
            PickerPanel.ActualWidth = 600;
            PickerPanel.Padding = new(18);
            PickerPanel.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
            AudioPickerRow.Parent = ColumnPickers;
            AudioPickerRow.ActualWidth = 234;
            AudioPickerRow.ActualHeight = 40;
            ColumnAudioText.Parent = AudioPickerRow;
            ColumnAudioText.ActualWidth = 200;
            ColumnAudioText.ActualHeight = 40;
            ColumnAudioPicker.Parent = AudioPickerRow;
            SubtitlePickerRow.Parent = ColumnPickers;
            SubtitlePickerRow.LayoutX = store ? 248 : 0;
            ColumnSubtitlePicker.Parent = SubtitlePickerRow;
            ColumnSubtitlePicker.LayoutX = 50;
            ColumnSubtitlePicker.ActualWidth = 180;
            ColumnSubtitlePicker.Visibility = Visibility.Visible;
            SubtitlePicker.Parent = PickerPanel;
            SubtitlePicker.LayoutX = 18;
            SubtitlePicker.ActualWidth = 180;
            SubtitlePicker.Visibility = Visibility.Visible;
        }
    }
}

namespace Momoka.Emby
{
    internal static class EmbyItemType
    {
        internal static string ToChinese(string value) => value;
    }
}
