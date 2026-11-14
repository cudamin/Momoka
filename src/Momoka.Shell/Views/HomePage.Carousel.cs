using Microsoft.UI.Xaml;
using Momoka.Configuration;

namespace Momoka.Shell.Views;

public sealed partial class HomePage
{
    internal bool BannerImmersive => _homeSettings?.Settings.Ui.EnableImmersive == true;
    internal bool BannerShown => Banner.Visibility == Visibility.Visible;
    private double BannerOverlayLeftInset => BannerImmersive ? 36 : 24;

    private void OnAppearanceChanged(UiSettings ui)
    {
        if (_released || !ReferenceEquals(ui, _homeSettings?.Settings.Ui)) return;
        SyncCarouselLayout();
    }

    /// <summary>恢复主树 171a1e6 的通栏轮播与 SyncBleed；商店保留后来加入的圆角留白。</summary>
    private void SyncCarouselLayout()
    {
        if (_released) return;
        var immersive = BannerImmersive;
        var zone = immersive ? new Thickness(0) : new Thickness(36, 20, 13, 0);
        if (BannerZone.Margin != zone) BannerZone.Margin = zone;
        var corners = immersive ? new CornerRadius(0) : (CornerRadius)Application.Current.Resources["EgCardCornerRadius"];
        if (BannerCard.CornerRadius != corners) BannerCard.CornerRadius = corners;
        var top = immersive && BannerShown ? -ChromeHeight : 0;
        var margin = Scroller.Margin;
        if (margin.Top != top) Scroller.Margin = new Thickness(margin.Left, top, margin.Right, margin.Bottom);
        PlaceLibraryOverlay();
        PaintInk();
        PlaceScrollBar();
        ScheduleOverlay();
    }

    private void PaintInk() => _actions?.SetTitleStrip(BannerImmersive && BannerShown
        ? TitleStrip.OnScrim : TitleStrip.Plain);
}
