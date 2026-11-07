using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class SettingsPage
{
    private void OnColorSwatchLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: SettingColorRow { RequiresConfirmation: true } row, Flyout: Flyout flyout } button
            || flyout.Content is not HtmlColorPicker picker) return;
        picker.ConfigureAppearance(row, flyout);
        flyout.Opened -= picker.OnAppearanceOpened;
        flyout.Opened += picker.OnAppearanceOpened;
        flyout.Closed -= picker.OnAppearanceClosed;
        flyout.Closed += picker.OnAppearanceClosed;
        button.Unloaded -= picker.OnAppearanceOwnerUnloaded;
        button.Unloaded += picker.OnAppearanceOwnerUnloaded;
    }

    private void CancelAppearanceColors()
    {
        foreach (var row in ViewModel.Sections.SelectMany(section => section.Rows).OfType<SettingColorRow>()) row.CancelEdit();
    }
}
