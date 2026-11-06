using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Windows.UI;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    // 阅读保护固定在图片层，滚动内容和标题栏不再重复叠暗。沉浸背景也必须压住任意位置的亮部。
    private bool UsesBackdropFade => ViewModel.BackdropShown
        && ViewModel.ItemType is EmbyItemType.Movie or EmbyItemType.Series or EmbyItemType.Season;

    private Color ReadingShadeColor => Color.FromArgb(
        AppearanceOpacity.ReadingShadeAlpha(ViewModel.BackdropStrength, ViewModel.GaussianBackdropApplied),
        DetailHero.ScrimInk.R, DetailHero.ScrimInk.G, DetailHero.ScrimInk.B);

    private void PaintBackdropFade()
    {
        BackdropFade.Background = new SolidColorBrush(ReadingShadeColor);
        BackdropFade.Visibility = UsesBackdropFade ? Visibility.Visible : Visibility.Collapsed;
    }
}
