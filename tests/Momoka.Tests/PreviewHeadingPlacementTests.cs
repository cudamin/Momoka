using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 悬停预览的徽标与片名飞进详情页时从哪儿起跑（2026-11-16 用户令「新增徽标和片名移进详情页的动画」）。
/// 纯算术：起飞缩放与平移量算错一格，屏上就是「从旁边跳进来」而不是「原来那一个移过去」。
/// </summary>
internal static class PreviewHeadingPlacementTests
{
    public static void Register()
    {
        Test("悬停详情：片名按字号比、徽标按高度比起飞缩放", () =>
        {
            Assert.Equal(0.5d, PreviewHeadingPlacement.Scale(20, 40), "面板字号是详情的一半");
            Assert.Equal(1d, PreviewHeadingPlacement.Scale(0, 40), "源高度缺失不缩放");
            Assert.Equal(1d, PreviewHeadingPlacement.Scale(20, 0), "目标高度缺失不缩放");
            Assert.Equal(3d, PreviewHeadingPlacement.Scale(999, 1), "不把目标缩到看不见");
            Assert.Equal(0.1d, PreviewHeadingPlacement.Scale(1, 999), "不把目标放到盖上整页");
        });

        Test("悬停详情：缩放后的左上角落在面板原位", () =>
        {
            var (x, y) = PreviewHeadingPlacement.Offset(100, 50, 400, 300);
            Assert.Equal(-300d, x, "横向偏移");
            Assert.Equal(-250d, y, "纵向偏移");
            Assert.Equal(100d, 400 + x, "缩放原点落在面板片名左沿");
            Assert.Equal(50d, 300 + y, "缩放原点落在面板片名上沿");
        });
    }
}
