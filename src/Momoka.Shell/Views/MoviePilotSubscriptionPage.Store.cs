using Microsoft.UI.Xaml;
using Momoka.Emby;
using Momoka.Theming;

namespace Momoka.Shell.Views;

public sealed partial class MoviePilotSubscriptionPage
{
    private StoreBackdropFade? _storeBackdropFade;
    private StoreBackdropFade? _storeAmbientFade;
    private readonly StoreBackdropStyle.Tint _storeTint = new();
    private readonly StoreAmbientBrush _storeAmbientBrush = new();
    private readonly StoreBackdropStyle.ShadeDark _storeShadeDark = new();

    private void PaintStoreBackdrop()
    {
        // 2026-11-17 卡片与窗口间距那条规则：内容列不再收进 1840 居中，改为铺满窗口，由视图模型的头部／
        // 正文留白把整列内容按「1500 之后 170、2250 以上随窗口等比」收窄；2026-11-24 起两处再叠 1860 的卡片上限，
        // 超过的宽度平分到两侧，画面与装饰带仍贴窗口右沿。
        StoreScrollContent.MaxWidth = double.PositiveInfinity;
        StoreScrollContent.HorizontalAlignment = HorizontalAlignment.Stretch;
        StoreScrollContent.Width = double.NaN;
        var storeShown = MediaViewModel.Plain && MediaViewModel.Backdrop is not null && !_accessibility.HighContrast;
        // 预告片占着视口时只收起剧照，环境底继续垫在视频下面。
        StoreBackdrop.Visibility = storeShown && !_trailerOpened ? Visibility.Visible : Visibility.Collapsed;
        // 左上角那道淡墨跟着装饰背景同进同退（复刻媒体详情页商店档，2026-11-17）：背景不在时它没有东西可压。
        StoreShade.Visibility = storeShown ? Visibility.Visible : Visibility.Collapsed;
        // 2026-11-27 同媒体详情页（用户令「把详情页背景图左侧改成深色的，浅色主题时候保持浅色」）：横向那道
        // 按主题取，深色压深左侧（左列一整片深色，剧照从暗里浮出来）、浅色仍是原来那道淡墨；竖向那道两档同一支。
        // 换主题走 OnShowcaseThemeChanged → ApplyMaterialAppearance → 这一条，所以每次重画都重取。
        // 深色的几何与 LayoutStoreTrailer 同一套坐标：页宽取窗口宽，剧照左沿 = 页宽 − 右沿空隙 − 视口宽。
        StoreShadeAcross.Background = StoreBackdropStyle.Shade.Across;
        if (ThemeHost.Current.IsDark)
        {
            var shadePage = MediaViewModel.PageWidth > 0 ? MediaViewModel.PageWidth : MediaViewModel.StorePageWidth;
            StoreShadeAcross.Background = _storeShadeDark.Resolve(shadePage,
                Math.Max(0, shadePage - MediaViewModel.StorePreviewMargin.Right - MediaViewModel.StoreBackdropWidth),
                StoreBackdropBlend.PictureFade * MediaViewModel.StoreBackdropWidth);
        }
        StoreShadeDown.Background = StoreBackdropStyle.Shade.Down;
        var width = MediaViewModel.StorePageWidth;
        StoreBackdrop.Height = MediaViewModel.StoreBackdropHeight;
        StoreBackdrop.Width = MediaViewModel.StoreBackdropWidth;
        StoreBackdrop.Margin = MediaViewModel.StorePreviewMargin;
        // 环境底跟剧照背景同进同退；它还要垫住比剧照更高的头部内容。
        StoreAmbient.Height = MediaViewModel.StoreAmbientHeight;
        StoreAmbient.Visibility = storeShown ? Visibility.Visible : Visibility.Collapsed;
        // 2026-11-17 用户令「订阅详情页和媒体详情页一起改」：视口跟着窗口宽继续变宽、右沿收回窗口边；
        // 装饰带本来就铺满窗口，不再需要负边距越过 1840 内容列，bleed 因此是 0。
        var bleed = 0d;
        var bleedMargin = new Thickness(-bleed, 0, -bleed, 0);
        StoreAmbient.Margin = bleedMargin;
        // 左上角那道淡墨（2026-11-17「复刻媒体详情页商店档的背景图渐变效果」）与环境底同一份边距。
        StoreShade.Margin = bleedMargin;
        StoreTrailerFoundation.Margin = bleedMargin;
        StoreBackdropSides.Background = _storeAmbientBrush.Resolve(MediaViewModel.BackdropPixels,
            StoreAmbient.Visibility == Visibility.Visible, ThemeHost.Current);
        StoreBackdropAcrylic.Width = StoreBackdrop.Width;
        StoreBackdropAcrylic.Height = StoreBackdrop.Height;
        StoreBackdropAcrylic.Visibility = MediaViewModel.Plain && width is > 0 and < DetailHero.StoreWideFloor
            && MediaViewModel.Backdrop is not null && !_accessibility.HighContrast ? Visibility.Visible : Visibility.Collapsed;
        // 右侧剧照／预告在宽版式里带圆角，窄窗铺满整幅时保持直角（同媒体详情页）。
        var previewCorner = MediaViewModel.StoreWide
            ? (CornerRadius)Application.Current.Resources["EgCardCornerRadius"] : default(CornerRadius);
        StoreBackdropContent.CornerRadius = previewCorner;
        StoreTrailerContent.CornerRadius = previewCorner;
        StoreBackdropStyle.ApplyAcrylic(StoreBackdropAcrylicFill, StoreBackdropAcrylicBottom,
            _storeTint.Resolve(MediaViewModel.BackdropPixels, MediaViewModel.Backdrop is not null, ThemeHost.Current.Colors.Window, ThemeHost.Current.IsDark),
            ThemeHost.ToColor(ThemeHost.Current.Colors.Window));
        UpdateStoreTrailer();
    }

}
