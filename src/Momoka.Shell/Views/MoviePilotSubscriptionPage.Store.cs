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
        StoreBackdrop.Visibility = MediaViewModel.Plain && MediaViewModel.Backdrop is not null && !_accessibility.HighContrast
            ? Visibility.Visible : Visibility.Collapsed;
        StoreBackdrop.Height = DetailHero.StorePictureHeight(MediaViewModel.PageWidth, MediaViewModel.BackdropRatio);
        StoreBackdrop.Width = DetailHero.StorePictureWidth(MediaViewModel.PageWidth, MediaViewModel.BackdropRatio);
        StoreBackdropStyle.Apply(StoreBackdropSides, StoreBackdropBottom,
            _storeTint.Resolve(MediaViewModel.BackdropPixels, MediaViewModel.Backdrop is not null, ThemeHost.Current.Colors.Window));
    }

}
