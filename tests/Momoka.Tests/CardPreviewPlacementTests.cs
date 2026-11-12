using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 悬停预览摆在哪儿（2026-11-12 用户令「从中心向两边扩散」）。
/// <para>
/// 这里全是纯算术，也正是真机上最难逐个摆出来看的那几种情形：卡片贴着货架左沿、贴着右沿、面板比窗口
/// 还宽、卡片在屏幕下半截。算错一格就是「面板半个身子在窗外」，而截图只拍得到当时那一张卡。
/// </para>
/// </summary>
internal static class CardPreviewPlacementTests
{
    public static void Register()
    {
        Test("悬停预览：面板以卡片中心为原点向两边展开", () =>
        {
            // 170 宽的卡片、400 宽的面板：左右各多出 115。
            Assert.Equal(-115d, CardPreviewPlacement.Horizontal(500, 170, 400, 1600, 12), "居中的偏移");
        });

        Test("悬停预览：贴着左沿的卡片把面板夹回窗口", () =>
        {
            var offset = CardPreviewPlacement.Horizontal(36, 170, 400, 1600, 12);
            Assert.Equal(-24d, offset, "左沿贴边");
            Assert.Equal(12d, 36 + offset, "面板左沿落在 12");
        });

        Test("悬停预览：贴着右沿的卡片把面板夹回窗口", () =>
        {
            var offset = CardPreviewPlacement.Horizontal(1500, 170, 400, 1600, 12);
            Assert.Equal(-312d, offset, "右沿贴边");
            Assert.Equal(1588d, 1500 + offset + 400, "面板右沿落在窗口宽减 12");
        });

        Test("悬停预览：窗口比面板还窄时贴左，不把面板推到负坐标", () =>
        {
            var offset = CardPreviewPlacement.Horizontal(100, 170, 400, 380, 12);
            Assert.Equal(-88d, offset, "窄窗");
            Assert.Equal(12d, 100 + offset, "左沿仍留 12");
        });

        Test("悬停预览：纵向与卡片齐平，底下放不下就整体上移", () =>
        {
            Assert.Equal(0d, CardPreviewPlacement.Vertical(300, 311, 1000, 12), "放得下");
            Assert.Equal(-123d, CardPreviewPlacement.Vertical(800, 311, 1000, 12), "底下放不下");
            Assert.Equal(12d, 600 + CardPreviewPlacement.Vertical(600, 2000, 1000, 12), "比窗口还高时顶上留 12");
        });

        Test("悬停预览：缩略图统一 2:1", () =>
        {
            Assert.Equal(186d, CardPreviewPlacement.MediaHeight(372), "372 宽");
            Assert.Equal(0d, CardPreviewPlacement.MediaHeight(0), "零宽");
        });
    }
}
