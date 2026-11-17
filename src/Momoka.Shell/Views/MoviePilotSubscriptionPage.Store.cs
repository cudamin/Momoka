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

    private void PaintStoreBackdrop()
    {
        // 2026-11-17 卡片与窗口间距那条规则：内容列不再收进 1840 居中，改为铺满窗口，由视图模型的头部／
        // 正文留白把整列内容按「1500 之后 170、2250 以上随窗口等比」收窄。
        StoreScrollContent.MaxWidth = double.PositiveInfinity;
        StoreScrollContent.HorizontalAlignment = HorizontalAlignment.Stretch;
        StoreScrollContent.Width = double.NaN;
        var storeShown = MediaViewModel.Plain && MediaViewModel.Backdrop is not null && !_accessibility.HighContrast;
        // 预告片占着视口时只收起剧照，环境底继续垫在视频下面。
        StoreBackdrop.Visibility = storeShown && !_trailerOpened ? Visibility.Visible : Visibility.Collapsed;
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
