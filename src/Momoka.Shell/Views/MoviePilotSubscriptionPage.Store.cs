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
        StoreScrollContent.MaxWidth = MediaViewModel.Plain ? 1840 : double.PositiveInfinity;
        StoreScrollContent.HorizontalAlignment = MediaViewModel.Plain ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        StoreScrollContent.Width = MediaViewModel.Plain && DetailScroll.ActualWidth > 0
            ? Math.Min(1840, DetailScroll.ActualWidth) : double.NaN;
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
