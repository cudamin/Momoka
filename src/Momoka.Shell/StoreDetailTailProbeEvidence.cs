using Momoka.Emby;

namespace Momoka.Shell;

/// <summary>商店关闭沉浸后的尾部下限与实际首卡读数；不产生布局、不修改产品计算。</summary>
/// <remarks>
/// 2026-11-18 起没有「预告控制排让位」那一档：那排键挪进了画框，尾部只剩画面与内容两项。
/// <c>WideTrailer</c> 留着只作读数（那排键这一刻在不在宽版式上），不参与尾部高度。
/// </remarks>
internal readonly record struct StoreDetailTailRead(bool ReserveBackdrop, bool WideTrailer,
    double PictureHeight, double HeroHeight, double ModelMinimum, double ActualMinimum,
    double ContentHeight, double ActualHeight, double? CoverBottom, double? DetailsBottom,
    double? PictureBottom, double? ControlsBottom, double? FirstCardTop);

internal static class StoreDetailTailProbeEvidence
{
    internal static void Require(StoreDetailTailRead read)
    {
        var sizes = new[] { read.PictureHeight, read.HeroHeight, read.ModelMinimum, read.ActualMinimum,
            read.ContentHeight, read.ActualHeight };
        var edges = new[] { read.CoverBottom, read.DetailsBottom, read.PictureBottom,
            read.ControlsBottom, read.FirstCardTop };
        if (sizes.Any(value => !double.IsFinite(value) || value < 0)
            || edges.Any(value => value is { } edge && (!double.IsFinite(edge) || edge < 0)))
            throw new InvalidOperationException("商店尾部尺寸或实际卡片边界无效");

        var expected = read.ReserveBackdrop
            ? DetailHero.StoreTailHeight(read.PictureHeight, read.HeroHeight, DetailHero.StorePreviewGap)
            : 0;
        var actualHeight = Math.Max(read.ContentHeight, expected);
        if (read.ModelMinimum != expected || Math.Abs(read.ActualMinimum - expected) > 0.1
            || Math.Abs(read.ActualHeight - actualHeight) > 1.5)
            throw new InvalidOperationException($"商店尾部未按内容与剧照补高：模型 {read.ModelMinimum:0.##}、"
                + $"控件下限 {read.ActualMinimum:0.##}（应为 {expected:0.##}）、"
                + $"实高 {read.ActualHeight:0.##}（应为 {actualHeight:0.##}）");

        if (read.FirstCardTop is not { } first) return;
        var lastEdge = new[] { read.CoverBottom, read.DetailsBottom, read.PictureBottom, read.ControlsBottom }
            .Where(value => value.HasValue).Select(value => value!.Value).DefaultIfEmpty(0).Max();
        if (first < lastEdge + DetailHero.StorePreviewGap - 1.5)
            throw new InvalidOperationException($"商店首卡仍压在实际封面、操作、剧照或预告控制上："
                + $"首卡 {first:0.##}、上方末沿 {lastEdge:0.##}、应留 {DetailHero.StorePreviewGap}");
    }
}
