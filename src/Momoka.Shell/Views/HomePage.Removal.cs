using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;

namespace Momoka.Shell.Views;

public sealed partial class HomePage
{
    /// <summary>
    /// 删除确认之后把这一件从主页上摘掉。三个删除入口 —— 删记录和库文件、删除记录及两端、服务器删除 ——
    /// 都走这一条路，交互反馈才一致（用户令 2026-11-03）。
    /// <para>
    /// <b>不重读整页。</b>重读会换掉整页海报、把阅读位置推回顶上，一次删除看起来像翻了一页；这里只让屏上
    /// 那几张卡淡出收拢，再从集合里摘掉。之后任何一次重读（定时检查、从别的页面回来、用户手动刷新）由
    /// <see cref="HomeViewModel.Remember"/> 记住的这一件滤掉，服务器元数据晚一步消失也不会把它带回来。
    /// </para>
    /// </summary>
    internal async Task RemoveAsync(EmbyItem removed)
    {
        // 先记住再等动画：动画中途离开这一页，重读也不该把它带回来。
        ViewModel.Remember(removed);

        var posters = new List<PosterCard>();
        foreach (var card in ViewModel.CardsOf(removed))
            if (PosterOf(card) is { } poster) posters.Add(poster);
        if (posters.Count > 0) RemovalMotions++;

        // 同一件可能同时挂在两排上（继续观看和最近添加），一起走完再一起摘。
        if (posters.Count > 0)
        {
            var played = Task.WhenAll(posters.Select(poster => poster.PlayRemovalAsync()));
            await Task.WhenAny(played, Task.Delay(700));
        }

        // 动画把不透明度留在 0 上；容器会被回收给下一张卡，先还原再摘，免得下一张卡带着透明的底上来。
        foreach (var poster in posters) poster.ResetRemoval();

        var offset = Scroller.VerticalOffset;
        ViewModel.Remove(removed);

        // 内容真的短了一截时 ScrollView 会自己把偏移夹回可滚范围；除此之外一个像素都不动 ——
        // 用户令：删除之后保持当前页与滚动位置。
        if (Math.Abs(Scroller.VerticalOffset - offset) > 0.5)
            Scroller.ScrollTo(0, offset, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
    }

    /// <summary>自检用：真正起过几趟离场动画。它没涨就说明这条路上只剩集合删除，看不到任何反馈。</summary>
    internal int RemovalMotions { get; private set; }

    /// <summary>某张卡此刻在屏上的元素；被虚拟化掉的那几张没有容器，返回 null。</summary>
    private PosterCard? PosterOf(CardItem card)
    {
        for (var index = 0; index < HomeRows.Count; index++)
        {
            if (ShelfRepeater.TryGetElement(index) is not FrameworkElement row) continue;
            if (HomeFoldMotion.Find<ShelfStrip>(row)?.CardElement(card) is { } poster) return poster;
        }

        return null;
    }
}
