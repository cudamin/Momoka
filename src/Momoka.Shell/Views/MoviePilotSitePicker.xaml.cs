using Momoka.Shell.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

public sealed partial class MoviePilotSitePicker : UserControl
{
    public MoviePilotSitePicker() => InitializeComponent();
    public MoviePilotSitePickerViewModel ViewModel { get; private set; } = new();

    internal void Attach(MoviePilotSitePickerViewModel viewModel)
    {
        ViewModel = viewModel;
        Bindings.Update();
    }

    private void OnOpening(object? sender, object args) => _ = ViewModel.EnsureLoadedAsync();

    internal void SetCompact(double maxWidth = 180)
    {
        SiteLabel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        ChooseSites.MaxWidth = maxWidth;
        // 搜索框里不给这颗下拉留外圈：只让文字和箭头占位，右边仍留 2 给清除键。
        ChooseSites.Padding = new Microsoft.UI.Xaml.Thickness(2, 0, 2, 0);
    }

    internal void ClosePopup() => SitesFlyout.Hide();
}
