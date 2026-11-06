using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Windows.UI;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    // 保留既有容器及阅读遮罩归属；去掉下沿渐变后，不能把遮罩搬回滚动区再次叠暗。
    private bool UsesBackdropFade => ViewModel.BackdropShown && !ViewModel.Showcase
        && ViewModel.ItemType is EmbyItemType.Movie or EmbyItemType.Series or EmbyItemType.Season;

    // 整张图上的阅读遮罩。老版固定 0xB8；2026-11-06 起浓度读设置里那一档（0 不遮、100 完全不透明），
    // 最浓档就是 ScrimCeiling 那一格 —— 见 AppearanceOpacity.ScrimAlpha。高斯模糊那一档不画它（模糊自己压细节）。
    private Color ReadingShadeColor => ViewModel.GaussianBackdropApplied
        ? Color.FromArgb(0, 0, 0, 0)
        : Color.FromArgb(
            AppearanceOpacity.ScrimAlpha(DetailHero.ScrimCeiling, ViewModel.BackdropStrength),
            DetailHero.ScrimInk.R, DetailHero.ScrimInk.G, DetailHero.ScrimInk.B);

    private void PaintBackdropFade()
    {
        BackdropFade.Background = new SolidColorBrush(ReadingShadeColor);
        BackdropFade.Visibility = UsesBackdropFade ? Visibility.Visible : Visibility.Collapsed;
    }
}
