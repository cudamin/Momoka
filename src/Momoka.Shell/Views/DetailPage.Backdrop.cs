using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Windows.Foundation;
using Windows.UI;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    private readonly LinearGradientBrush _backdropFadeBrush = new()
    {
        StartPoint = new Point(0.5, 0),
        EndPoint = new Point(0.5, 1),
        GradientStops =
        {
            new GradientStop { Offset = 0 },
            new GradientStop { Offset = 0.7 },
            new GradientStop { Offset = 1 }
        }
    };

    // Emby Theater 3.0.20-3.0 www/item/item.css 的蒙版在元素底部30%淡出。
    // 此处把已压暗的背景合成到主题底色；不能在其上再叠滚动区的黑罩。
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
        var color = BackdropThemeSource.Background is SolidColorBrush theme ? theme.Color : PaperColor;
        for (var index = 0; index < _backdropFadeBrush.GradientStops.Count; index++)
            _backdropFadeBrush.GradientStops[index].Color = Color.FromArgb(index == 2 ? (byte)255 : (byte)0,
                color.R, color.G, color.B);

        // 底部那道主题收边不跟强度走：它接的是正文那张纸，不是压在图上给人降亮度的那一层。0 档也留着，
        // 否则图的下沿会硬切在纸的上沿上；100 档图被遮罩吃掉，这一道看不出来。
        BackdropFade.Background = _backdropFadeBrush;
        BackdropFade.Visibility = UsesBackdropFade ? Visibility.Visible : Visibility.Collapsed;
        BackdropShade.Background = new SolidColorBrush(ReadingShadeColor);
        BackdropShade.Visibility = UsesBackdropFade && !ViewModel.GaussianBackdropApplied
            ? Visibility.Visible : Visibility.Collapsed;
    }
}
