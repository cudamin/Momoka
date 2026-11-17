using Microsoft.UI.Xaml;
using Momoka.Emby;
using Momoka.Theming;

namespace Momoka.Shell.Views;

public sealed partial class MoviePilotSubscriptionPage
{
    /// <summary>
    /// 左上角三角与整条渐变的交接档：正常窗口只画角上那层（<c>StoreShadeCorner</c>）；页宽过
    /// <see cref="StoreShadeCornerFloor"/> 后按 smoothstep 把整条那层淡入、角上那层淡出，到
    /// <see cref="StoreShadeCornerCeiling"/> 只剩整条，等同不加遮罩的当前状态
    /// （2026-11-17 用户令「正常窗口下只留左上角一个三角形，拉长窗口时候慢慢恢复到当前状态」）。
    /// </summary>
    private const double StoreShadeCornerFloor = 1400;
    private const double StoreShadeCornerCeiling = 1840;

    private StoreBackdropFade? _storeBackdropFade;
    private StoreBackdropFade? _storeAmbientFade;
    private readonly StoreBackdropStyle.Tint _storeTint = new();
    private readonly StoreAmbientBrush _storeAmbientBrush = new();

    private void PaintStoreBackdrop()
    {
        StoreScrollContent.MaxWidth = MediaViewModel.Plain ? 1840 : double.PositiveInfinity;
        StoreScrollContent.HorizontalAlignment = MediaViewModel.Plain ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        StoreScrollContent.Width = MediaViewModel.Plain && DetailScroll.ActualWidth > 0
            ? Math.Min(1840, DetailScroll.ActualWidth) : double.NaN;
        var storeShown = MediaViewModel.Plain && MediaViewModel.Backdrop is not null && !_accessibility.HighContrast;
        // 预告片占着视口时只收起剧照，环境底继续垫在视频下面。
        StoreBackdrop.Visibility = storeShown && !_trailerOpened ? Visibility.Visible : Visibility.Collapsed;
        // 左上角那道淡墨跟着装饰背景同进同退（复刻媒体详情页商店档，2026-11-17）：背景不在时它没有东西可压。
        StoreShade.Visibility = storeShown ? Visibility.Visible : Visibility.Collapsed;
        // 2026-11-17 用户令：正常窗口下这层只留左上角三角形，拉宽再慢慢放开（见上面两档常量的说明）。
        // 三角跟着页宽收放（正常宽度约 22%），页宽过 1400 后整条那层再淡入。
        var cornerRadius = Math.Clamp(MediaViewModel.PageWidth * 0.22, 160, 360);
        StoreShadeCornerBrush.RadiusX = cornerRadius;
        StoreShadeCornerBrush.RadiusY = cornerRadius * 0.8;
        var shadeOpenness = Math.Clamp((MediaViewModel.PageWidth - StoreShadeCornerFloor)
            / (StoreShadeCornerCeiling - StoreShadeCornerFloor), 0, 1);
        var shadeSmooth = shadeOpenness * shadeOpenness * (3 - 2 * shadeOpenness);
        StoreShadeWide.Opacity = shadeSmooth;
        StoreShadeCorner.Opacity = 1 - shadeSmooth;
        var width = MediaViewModel.StorePageWidth;
        StoreBackdrop.Height = MediaViewModel.StoreBackdropHeight;
        StoreBackdrop.Width = MediaViewModel.StoreBackdropWidth;
        StoreBackdrop.Margin = MediaViewModel.StorePreviewMargin;
        // 环境底跟剧照背景同进同退；它还要垫住比剧照更高的头部内容。
        StoreAmbient.Height = MediaViewModel.StoreAmbientHeight;
        StoreAmbient.Visibility = storeShown ? Visibility.Visible : Visibility.Collapsed;
        // 2026-11-17 用户令「复刻媒体详情页商店档的背景图渐变效果」：装饰带（环境底与淡墨）跟媒体详情页一样
        // 越过 1840 的内容列、用负边距铺到窗口两边，渐变不在内容列边缘截断。内容是居中的，两边让出的等宽。
        var bleed = MediaViewModel.Plain && DetailScroll.ActualWidth > width ? (DetailScroll.ActualWidth - width) / 2 : 0;
        var bleedMargin = new Thickness(-bleed, 0, -bleed, 0);
        StoreAmbient.Margin = bleedMargin;
        StoreShade.Margin = bleedMargin;
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
