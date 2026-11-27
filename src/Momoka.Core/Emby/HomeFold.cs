namespace Momoka.Emby;

/// <summary>主页媒体库的位置偏好；整数值保留旧设置兼容，自动档为装机默认。</summary>
public enum HomeLibraryPlacement
{
    /// <summary>随内容可见范围变换：继续观看季集行快被裁切时上移，空间充足时回到轮播下方。</summary>
    Auto = 0,

    /// <summary>固定在轮播图中。</summary>
    OnCarousel = 1,

    /// <summary>固定在轮播图下方。</summary>
    BelowCarousel = 2
}

/// <summary>主页媒体库自动位置的裁切规则。输入为排好布局后的文字行与实际裁切边界，不设窗口宽高断点。</summary>
public static class HomeFold
{
    /// <summary>文字行距离裁切边界只剩这点余量时提前上移，避免先裁掉季集文字才翻档。</summary>
    public const double CaptionClearance = 8;

    /// <summary>商店版式与轮播关闭时固定下方，保留整宽版式的位置偏好供切回恢复。</summary>
    public static HomeLibraryPlacement EffectivePlacement(CarouselLayout layout, bool showBanner, HomeLibraryPlacement preference) =>
        layout == CarouselLayout.Store || !showBanner ? HomeLibraryPlacement.BelowCarousel : preference;

    /// <summary>固定位置直接服从偏好；自动位置只比较文字行下沿与裁切边界，轮播的设计尺寸不参与。</summary>
    /// <param name="placement">设置中的主页媒体库位置。</param>
    /// <param name="viewport">滚动视口的实际裁切下沿，坐标零点为视口上沿。</param>
    /// <param name="captionBottom">继续观看季集文字行的下沿，按未上移、滚动回顶部的布局还原。</param>
    public static bool LibraryOnBanner(HomeLibraryPlacement placement, double viewport, double captionBottom)
    {
        if (!double.IsFinite(viewport) || viewport <= 0) return false;

        return placement switch
        {
            HomeLibraryPlacement.OnCarousel => true,
            HomeLibraryPlacement.BelowCarousel => false,
            _ => double.IsFinite(captionBottom) && captionBottom > 0
                && captionBottom + CaptionClearance >= viewport
        };
    }
}
