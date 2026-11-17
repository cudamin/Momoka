using Microsoft.UI.Xaml;
using Momoka.Configuration;

namespace Momoka.Shell.Views;

public sealed partial class HomePage
{
    internal bool BannerImmersive => _homeSettings?.Settings.Ui.EnableImmersive == true;
    internal bool BannerShown => Banner.Visibility == Visibility.Visible;
    private double BannerOverlayLeftInset => BannerImmersive ? 36 : 24;

    /// <summary>
    /// 商店模式主页内容的最大宽度（DIP）。2026-11-18 用户令「主页的轮播图的宽度还有下方媒体库的最大宽度
    /// 锁定为1860」：窗口比它宽时，轮播与下方货架一起停在 1860 并居中；窗口窄下来按原来的左脊线 36、
    /// 右侧滚动条车道 13 铺满。沉浸模式不受这一档限制，仍走通栏。
    /// </summary>
    internal const double StoreContentMaxWidth = 1860;

    private void OnAppearanceChanged(UiSettings ui)
    {
        if (_released || !ReferenceEquals(ui, _homeSettings?.Settings.Ui)) return;
        if (_headerScroll.Enabled == BannerImmersive)
        {
            _headerScroll.Update(BannerImmersive, Scroller.VerticalOffset);
            SyncCarouselLayout();
            return;
        }

        // ScrollPresenter 693f472aeaf89c6dff66256ee6fcd453e5d553b8,
        // controls/dev/ScrollPresenter/ScrollPresenter.cpp ArrangeOverride updates anchored offsets
        // and raises ViewChanged during layout. A mode reflow is not a user's downward gesture.
        // Complete that one layout under the reset guard, then resume direction from its final offset.
        var reflowing = _reflowingHeader;
        _reflowingHeader = true;
        try
        {
            _headerScroll.Reset(BannerImmersive, Scroller.VerticalOffset);
            SyncCarouselLayout();
            UpdateLayout();
        }
        finally
        {
            _reflowingHeader = reflowing;
            if (!_released) ResetHeaderScroll();
        }
    }

    /// <summary>恢复主树 171a1e6 的通栏轮播与 SyncBleed；商店保留后来加入的圆角留白。</summary>
    private void SyncCarouselLayout()
    {
        if (_released) return;
        var immersive = BannerImmersive;
        var (left, right) = immersive ? (0d, 0d) : StoreContentInsets(ActualWidth);
        var zone = new Thickness(left, immersive ? 0 : 20, right, 0);
        if (BannerZone.Margin != zone) BannerZone.Margin = zone;
        // 下方货架与轮播走同一条左右边界：商店宽窗的上限落在两侧留白里，窄窗仍是脊线 36／车道 13。
        var (shelfLeft, shelfRight) = immersive ? (36d, 13d) : (left, right);
        var padding = Overlay.Padding;
        if (padding.Left != shelfLeft || padding.Right != shelfRight)
            Overlay.Padding = new Thickness(shelfLeft, padding.Top, shelfRight, padding.Bottom);
        var corners = immersive ? new CornerRadius(0) : (CornerRadius)Application.Current.Resources["EgCardCornerRadius"];
        if (BannerCard.CornerRadius != corners) BannerCard.CornerRadius = corners;
        // 大图自己也是一张卡片：圆点在卡片下面之后，外圈的 BannerCard 只剪最外沿，大图的另外两角在内部。
        Banner.SetCardCornerRadius(corners);
        var top = immersive && BannerShown ? -ChromeHeight : 0;
        var margin = Scroller.Margin;
        if (margin.Top != top) Scroller.Margin = new Thickness(margin.Left, top, margin.Right, margin.Bottom);
        PlaceLibraryOverlay();
        PaintInk();
        PlaceScrollBar();
        ScheduleOverlay();
    }

    /// <summary>
    /// 商店模式的左右留白。宽到放得下 <see cref="StoreContentMaxWidth"/> 时两边平分多出来的宽度 ——
    /// 轮播与下方货架因此一起停在 1860、在页面里居中；再窄一档照旧让出页脊线 36 与滚动条车道 13。
    /// </summary>
    private static (double Left, double Right) StoreContentInsets(double pageWidth)
    {
        var spare = pageWidth - StoreContentMaxWidth;
        return spare >= 36 + 13 ? (spare / 2, spare / 2) : (36, 13);
    }

    private void PaintInk()
    {
        if (_released) return;
        _actions?.SetTitleStrip(BannerImmersive && BannerShown && HeaderMode == HomeHeaderMode.Top
            ? TitleStrip.OnScrim : TitleStrip.Plain);
        _actions?.RefreshHomeHeader();
    }
}
