using Momoka.Emby;

namespace Momoka.MoviePilot;

/// <summary>
/// 订阅墙上的一张卡铺多大。宽度跟着这一页的可用宽度走，和媒体库同一套算法
/// （<see cref="LibraryCardMetrics.Fit"/>）：先按最小卡片宽定列数，再把这一行剩下的宽度平摊给这几列 ——
/// 窗口变宽、封面跟着变宽，塞得下多一列时退回最小宽附近再涨。
/// <para>
/// 2026-11-16 用户令「订阅媒体库怎么不会跟媒体库一样自动调整封面大小」：此前 <c>ItemsWrapGrid</c> 的
/// <c>ItemWidth="352"</c> 与卡片自己的 <c>Width="340"</c> 是两个写死的数，窗口拖宽只会多出空档、拖窄会把
/// 卡片裁掉一截（这一页的横向滚动是关着的）。网页端 subscribe 页也不是定宽：卡片网格是
/// <c>grid-subscribe-card { grid-template-columns: repeat(auto-fill, minmax(15rem, 1fr)) }</c>，每张卡填满它
/// 那一格（<c>MoviePilot/frontend/src/styles/common.scss</c>）。
/// </para>
/// <para>
/// 高度**不**跟着宽度走：网页端 SubscribeCard 是 <c>min-height="150"</c>，宽出来的部分是那张铺满整卡的背景图，
/// 而卡里的海报（72×108）、年份／片名与底部那行状态都是按 150 的高度摆的。把高度也拉起来，里面就空一截。
/// </para>
/// </summary>
public static class MoviePilotWallMetrics
{
    /// <summary>网页端 <c>minmax(15rem, 1fr)</c> 的那个 15rem（根字号 16px）：再窄就不该硬挤一列。</summary>
    public const int MinCardWidth = 240;

    /// <summary>网页端 SubscribeCard 的 <c>min-height="150"</c>，也是卡内那几件摆得下的高度。</summary>
    public const int CardHeight = 150;

    /// <summary>
    /// 一格与下一格之间那条缝。卡片容器的右上／右下都靠 <c>Margin="0,0,12,12"</c> 让出来
    /// （<c>MoviePilotSubscriptionWallView.xaml</c> 的 ItemContainerStyle），所以版面一格 = 卡片宽 + 这条缝。
    /// </summary>
    public const double Spacing = 12;

    /// <summary>
    /// 这一页的可用宽度能排几列、版面一格多宽（含它右边那条缝，<c>ItemsWrapGrid.ItemWidth</c> 直接用这个数）。
    /// <para>
    /// 传给 <see cref="LibraryCardMetrics.Fit"/> 的可用宽度先让掉一条缝：卡片容器的右下外边距连最后一列也要
    /// 留，按「只在列间留缝」摊出来的宽度乘上列数会多出一条缝，版面就只肯排 n−1 列、右边空一格。
    /// </para>
    /// <para>
    /// 量不到宽度（0、负数或非有限值）时给最小宽一列；页面还没上屏那一帧就是这样，量到之后会再算一次。
    /// </para>
    /// </summary>
    public static (int Columns, int ItemWidth) Fit(double available)
    {
        var gap = (int)Math.Round(Spacing);
        if (!double.IsFinite(available) || available <= 0) return (1, MinCardWidth + gap);

        var (columns, cell) = LibraryCardMetrics.Fit(available - gap, MinCardWidth, gap);
        return (columns, cell + gap);
    }
}
