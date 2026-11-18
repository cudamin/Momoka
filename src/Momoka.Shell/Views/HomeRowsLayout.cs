using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Momoka.Shell.Views;

/// <summary>
/// 主页那一叠货架：从上到下逐排码放，行与行之间隔一个 <see cref="Spacing"/>。
/// <para>
/// **不虚拟化是有意的**（2026-11-18 用户报「聚合模式下主页的最近添加显示不全」）。原来的竖向
/// <c>StackLayout</c> 只实现视口里的行，其余行按已实现的行的尺寸估算总高；主页的行高差别很大
/// （继续观看／媒体库是宽卡、媒体库排更矮，各库的「最近添加」是 2:3 海报、音乐排是方形），
/// 聚合模式下十来排一起排时估算的总高会短，滚动条到不了底，最后几排永远不进实现范围，
/// 屏上就是「最近添加只显示了一部分，下面一片空白」。行数本来就只有每个在线账号每个库一排
/// （几个到十几个），一次性排完的代价远小于这个错；每排卡片仍由 <see cref="ShelfStrip"/> 自己的横向
/// 虚拟化管着，这里不替它建视口外的卡。
/// </para>
/// <para>
/// 只量高、不估高：<see cref="MeasureOverride"/> 量每一行，<see cref="ArrangeOverride"/> 按量到的
/// 高度往下摞，所以总高与屏上内容一致，滚到底就是真的底。
/// </para>
/// </summary>
public sealed partial class HomeRowsLayout : NonVirtualizingLayout
{
    /// <summary>排与排之间的空当，和原来 <c>StackLayout</c> 上的那个数一致（4）。</summary>
    public double Spacing { get; set; }

    protected override Size MeasureOverride(NonVirtualizingLayoutContext context, Size availableSize)
    {
        var width = 0.0;
        var height = 0.0;
        for (var index = 0; index < context.Children.Count; index++)
        {
            var child = context.Children[index];
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            if (index > 0) height += Spacing;
            height += child.DesiredSize.Height;
            width = Math.Max(width, child.DesiredSize.Width);
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize)
    {
        var y = 0.0;
        for (var index = 0; index < context.Children.Count; index++)
        {
            var child = context.Children[index];
            if (index > 0) y += Spacing;
            var height = child.DesiredSize.Height;
            child.Arrange(new Rect(0, y, finalSize.Width, height));
            y += height;
        }

        return finalSize;
    }
}
