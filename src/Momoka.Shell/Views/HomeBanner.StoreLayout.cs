using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;

namespace Momoka.Shell.Views;

public sealed partial class HomeBanner
{
    /// <summary>
    /// 保留所有控件实例与焦点，只调整 Grid 行列。推荐区独立于主图的裁剪、动效与媒体库让位高度。
    /// Gallery 的 AdaptiveTrigger 按窗口宽度切换；这里按实际内容宽与可用条目算，避免侧栏和 1860 封顶误判。
    /// </summary>
    private void PlaceRecommendations(double width, double heroHeight, bool side, bool shown)
    {
        var below = shown && !side;
        var gap = HomeCarousel.RailGap;
        var columns = HomeCarousel.RecommendationColumns(width);
        var rows = HomeCarousel.RailRows(heroHeight);
        _recommendationsHeight = below ? HomeCarousel.RecommendationHeight(width) + gap : 0;

        HeroRow.Height = new GridLength(heroHeight);
        RecommendationsRow.Height = new GridLength(_recommendationsHeight);
        Grid.SetRow(Rail, below ? 2 : 0);
        Grid.SetColumn(Rail, side ? 2 : 0);
        Rail.Margin = below ? new Thickness(0, gap, 0, 0) : default;

        RailTopRow.Height = below ? new GridLength(1, GridUnitType.Star) : new GridLength(rows.Big);
        RailMiddleRow.Height = new GridLength(below ? 0 : gap);
        RailBottomRow.Height = new GridLength(below ? 0 : rows.Small);
        RailBigColumn.Width = below ? new GridLength(columns.Big) : new GridLength(1, GridUnitType.Star);
        RailMiddleColumn.Width = new GridLength(below ? gap : 0);
        RailSmallColumn.Width = new GridLength(below ? columns.Small : 0);
        Grid.SetRow(RailSmallCards, below ? 0 : 2);
        Grid.SetColumn(RailSmallCards, below ? 2 : 0);

        SmallGapColumn.Width = new GridLength(below ? 0 : gap);
        SmallSecondColumn.Width = below ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        SmallGapRow.Height = new GridLength(below ? gap : 0);
        SmallSecondRow.Height = below ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetRow(RailSmallCardB, below ? 2 : 0);
        Grid.SetColumn(RailSmallCardB, below ? 0 : 2);
    }
}
