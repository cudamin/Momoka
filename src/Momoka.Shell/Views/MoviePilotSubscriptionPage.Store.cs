using Microsoft.UI.Xaml;
using Momoka.Emby;
using Momoka.Theming;

namespace Momoka.Shell.Views;

public sealed partial class MoviePilotSubscriptionPage
{
    private StoreBackdropFade? _storeBackdropFade;
    private readonly StoreBackdropStyle.Tint _storeTint = new();

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
        StoreBackdropStyle.Apply(StoreBackdropSides, StoreBackdropBottom,
            _storeTint.Resolve(MediaViewModel.BackdropPixels, MediaViewModel.Backdrop is not null, ThemeHost.Current.Colors.Window));
        StoreBackdropAcrylic.Width = StoreBackdrop.Width;
        StoreBackdropAcrylic.Height = StoreBackdrop.Height;
        StoreBackdropAcrylic.Visibility = MediaViewModel.Plain && width is > 0 and < DetailHero.StoreWideFloor
            && MediaViewModel.Backdrop is not null && !_accessibility.HighContrast ? Visibility.Visible : Visibility.Collapsed;
        StoreBackdropStyle.ApplyAcrylic(StoreBackdropAcrylicFill, StoreBackdropAcrylicBottom,
            _storeTint.Resolve(MediaViewModel.BackdropPixels, MediaViewModel.Backdrop is not null, ThemeHost.Current.Colors.Window),
            ThemeHost.ToColor(ThemeHost.Current.Colors.Window));
    }

}
