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
        StoreBackdrop.Visibility = MediaViewModel.Plain && MediaViewModel.Backdrop is not null && !_accessibility.HighContrast
            ? Visibility.Visible : Visibility.Collapsed;
        var width = MediaViewModel.Plain ? Math.Min(1840, MediaViewModel.PageWidth) : MediaViewModel.PageWidth;
        StoreBackdrop.Height = DetailHero.StoreBackdropHeight(width, MediaViewModel.HeroBandHeight);
        StoreBackdrop.Width = DetailHero.StoreBackdropWidth(width);
        // 环境底跟剧照背景同进同退，并且至少铺满第一屏（2026-11-16：深/浅窗口底色那一层必须随窗口缩放）。
        StoreAmbient.Height = Math.Max(StoreBackdrop.Height, MediaViewModel.Viewport);
        StoreAmbient.Visibility = StoreBackdrop.Visibility;
        // 图盒内保持满色，越过剧照右沿与下沿才渐隐到窗口画布（同媒体详情页）。
        _storeAmbientFade?.UpdateAmbient(width, StoreAmbient.Height, width, StoreBackdrop.Height);
        StoreBackdropSides.Background = _storeAmbientBrush.Resolve(MediaViewModel.BackdropPixels,
            StoreAmbient.Visibility == Visibility.Visible, ThemeHost.Current);
        StoreBackdropAcrylic.Width = StoreBackdrop.Width;
        StoreBackdropAcrylic.Height = StoreBackdrop.Height;
        StoreBackdropAcrylic.Visibility = MediaViewModel.Plain && width is > 0 and < DetailHero.StoreWideFloor
            && MediaViewModel.Backdrop is not null && !_accessibility.HighContrast ? Visibility.Visible : Visibility.Collapsed;
        StoreBackdropStyle.ApplyAcrylic(StoreBackdropAcrylicFill, StoreBackdropAcrylicBottom,
            _storeTint.Resolve(MediaViewModel.BackdropPixels, MediaViewModel.Backdrop is not null, ThemeHost.Current.Colors.Window, ThemeHost.Current.IsDark),
            ThemeHost.ToColor(ThemeHost.Current.Colors.Window));
    }

}
