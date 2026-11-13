using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 主页首屏的矮窗档 —— 窗口拉矮到媒体库那一排被第一屏裁掉之后，媒体库那一排压上轮播左下角、下一排补位；
/// 轮播被一屏压矮、上下裁切超出默认形状时才恢复默认（<see cref="HomeFold.LibraryOnBanner"/>，2026-09-13「轮播图
/// 上下裁切过多时隐藏，默认大小时不隐藏」—— 媒体库整个出屏但轮播还完好的那一档压着不放）。
/// 触发线改过三站：2026-09-13 落在「继续观看」牌子底下那条线；2026-09-22 又往下挪了一排（「再下面那一排」的
/// 封面顶）；**2026-11-13 收回媒体库那一排自己的下沿**（用户截图「这都重叠了媒体库还不下移」——窗口 1064×761、
/// 视口 744 时媒体库整排放得下却还压在圆角轮播上）。线由页面量（媒体库整排的顶 ＋ 实高），这里的数字只钉边界
/// 形状与几档真实窗口。
/// <para>
/// 钉的是两条边界的形状，数字取 1422 宽的默认窗那一档：带子的设计形状 608（<see cref="HomeCarousel.NaturalHeight"/>），
/// 媒体库那一排的下沿在 874。形状与从前一字未动 —— 挪的是线上站着的那一排（见下面那组算术的测试）。
/// </para>
/// <para>
/// 上面这一整套只属于 <see cref="HomeLibraryPlacement.Auto"/>（「随窗口高度变换」）那一档。2026-10-12 起设置里
/// 多了「主页媒体库位置」一行（2026-10-16 从界面卡挪进主页卡、同时改名），另两档由用户钉死，几何一个都不看 ——
/// 见文件末尾那三条。
/// </para>
/// </summary>
internal static class HomeFoldTests
{
    public static void Register()
    {
        Test("矮窗档：那条线被裁掉 —— 压上", () =>
        {
            // 视口 900：线在 922、整个在第一屏外 —— 媒体库整排一个像素都不露，这正是要压上轮播的那一档。
            Assert.True(HomeFold.LibraryOnBanner(HomeLibraryPlacement.Auto, viewport: 900, nextRowLine: 922, bandHeight: 608));
            Assert.True(HomeFold.LibraryOnBanner(HomeLibraryPlacement.Auto, 700, 922, 608), "更矮一点还是同一档");
        });

        Test("矮窗档：高窗装得下那条线 —— 默认", () =>
        {
            // 线 922 < 视口：线以下还露得出来（哪怕只是那一排的卡露了头），媒体库留在横排里。
            Assert.False(HomeFold.LibraryOnBanner(HomeLibraryPlacement.Auto, 1000, 922, 608));
            Assert.False(HomeFold.LibraryOnBanner(HomeLibraryPlacement.Auto, 923, 922, 608), "线哪怕只露一像素，也算露");
        });

        Test("矮窗档：媒体库整个出了屏、轮播还是设计形状 —— 压着不放", () =>
        {
            // 视口 620：媒体库的顶 632 在第一屏外（按「只能看到轮播图」的旧规则这一档已经回默认），但视口还
            // 撑得住带子的设计形状 608 —— 剧照上下裁切和默认大小一个样，压上档继续，媒体库跟着那一排一起
            // 住进轮播左下角（2026-09-13「轮播图上下裁切过多时隐藏，默认大小时不隐藏」）。
            Assert.True(HomeFold.LibraryOnBanner(HomeLibraryPlacement.Auto, 620, 922, 608));
            Assert.True(HomeFold.LibraryOnBanner(HomeLibraryPlacement.Auto, 632, 922, 608), "媒体库的顶正好压线，一个像素都没露");
        });

        Test("矮窗档：轮播被一屏压矮、上下裁切超出默认档 —— 恢复默认", () =>
        {
            // 视口 607 差一像素够不到带子的设计形状 608：带子被压矮，剧照上下各多裁出一截 —— 该回默认了。
            Assert.False(HomeFold.LibraryOnBanner(HomeLibraryPlacement.Auto, 607, 922, 608));
            Assert.False(HomeFold.LibraryOnBanner(HomeLibraryPlacement.Auto, 600, 922, 608), "更矮只会裁得更多");
        });

        Test("矮窗档：两条边界的等号", () =>
        {
            Assert.True(HomeFold.LibraryOnBanner(HomeLibraryPlacement.Auto, 922, 922, 608), "那条线 ＝ 视口底：线以下一个像素都没露，算裁掉");
            Assert.True(HomeFold.LibraryOnBanner(HomeLibraryPlacement.Auto, 608, 922, 608), "视口 ＝ 设计形状：正好撑住，还没多裁");
        });

        Test("矮窗档：线是媒体库整排的下沿（牌子和卡底那两行字都在里面）", () =>
        {
            // 1422／1527 宽那一档（用户 2026-09-22 的截图）：轮播带宽 1422 时带高 608，媒体库那一排从 624 起，
            // 牌子 46、牌子底下让 8、排卡 176（208 宽的 16:9 卡 117 ＋ 卡底 59）—— 下沿 ＝ 20 ＋ 608 ＋ 16 ＋ 230 ＝ 874。
            var rowTop = 20.0 + 608 + 16;
            var rowBottom = rowTop + 46 + 8 + 176;

            Assert.Equal(874, rowBottom);
            Assert.True(HomeFold.LibraryOnBanner(HomeLibraryPlacement.Auto, 760, rowBottom, 608), "默认窗放不下整排 —— 压上");
            Assert.False(HomeFold.LibraryOnBanner(HomeLibraryPlacement.Auto, 900, rowBottom, 608), "窗口再高一点、整排露出来了 —— 留在下方");
        });

        Test("矮窗档：媒体库放得下的窗口不再压上轮播（2026-11-13 实屏回归）", () =>
        {
            // 用户报的那一档：窗口 1064×761、视口 744、轮播带宽 758（带高 324），媒体库整排在默认摆法里
            // 从 360 到 590 —— 整个放得进第一屏。旧线（看下面第二排封面顶）在 909，压上；新线是 590，留在下方。
            Assert.False(HomeFold.LibraryOnBanner(HomeLibraryPlacement.Auto, viewport: 744, nextRowLine: 590, bandHeight: 324));

            // 窗口再矮到整排被裁（视口 590 ＝ 下沿）：按等号算压上；轮播还撑得住设计形状 324。
            Assert.True(HomeFold.LibraryOnBanner(HomeLibraryPlacement.Auto, 590, 590, 324));
            Assert.True(HomeFold.LibraryOnBanner(HomeLibraryPlacement.Auto, 589, 590, 324), "再矮一像素只会更该压");
        });

        Test("矮窗档：量不到视口不做决定", () =>
        {
            Assert.False(HomeFold.LibraryOnBanner(HomeLibraryPlacement.Auto, 0, 922, 608));
            Assert.False(HomeFold.LibraryOnBanner(HomeLibraryPlacement.Auto, -1, 922, 608));
        });

        // ── 设置 → 主页 → 「主页媒体库位置」的后两档（2026-10-12 进设置、2026-10-16 挪进主页卡）─────────────────────────────────────
        //
        // 那两档由用户钉死，几何一个都不看。取的就是上面那两组里两个已知的答案当反例：
        // 1000／922／608 在默认档是「不压」，607／922／608 在默认档也是「不压」，900／922／608 在默认档是「压」。
        // 递进去的线和带高在两档里都不该改变结论 —— 那正是「固定」这两个字的意思。

        Test("主页媒体库位置：固定在轮播图中 —— 不看窗口高矮", () =>
        {
            // 默认档判「不压」的两档窗口，钉住之后照样压着。
            Assert.True(HomeFold.LibraryOnBanner(HomeLibraryPlacement.OnCarousel, 1000, 922, 608), "高窗也压着");
            Assert.True(HomeFold.LibraryOnBanner(HomeLibraryPlacement.OnCarousel, 607, 922, 608), "轮播被一屏压矮也压着");

            // 线递什么都一样：调用方在钉住的档里压根不去量它（主页那一头就是拿 0 递进来的）。
            Assert.True(HomeFold.LibraryOnBanner(HomeLibraryPlacement.OnCarousel, 1000, 0, 608));
            Assert.True(HomeFold.LibraryOnBanner(HomeLibraryPlacement.OnCarousel, 300, 0, 0));
        });

        Test("主页媒体库位置：固定在轮播图下方 —— 一次都不压", () =>
        {
            // 默认档判「压」的两档窗口，钉住之后一个都不压。
            Assert.False(HomeFold.LibraryOnBanner(HomeLibraryPlacement.BelowCarousel, 900, 922, 608), "矮窗也不压");
            Assert.False(HomeFold.LibraryOnBanner(HomeLibraryPlacement.BelowCarousel, 620, 922, 608), "媒体库整个出屏也不压");
            Assert.False(HomeFold.LibraryOnBanner(HomeLibraryPlacement.BelowCarousel, 922, 922, 608), "线正压视口底也不压");
        });

        Test("主页媒体库位置：量不到视口时三档都不压", () =>
        {
            // 这一条在钉住的两档上不是「用户说了算」：量不到视口就没有可压的那块地方（主页那一头在调到这里之前
            // 已经把「轮播收着」也挡掉了）。钉住 ≠ 在没有轮播的页面上凭空压出一排来。
            foreach (var placement in Enum.GetValues<HomeLibraryPlacement>())
            {
                Assert.False(HomeFold.LibraryOnBanner(placement, 0, 922, 608), $"{placement}：视口 0");
                Assert.False(HomeFold.LibraryOnBanner(placement, -1, 922, 608), $"{placement}：视口负");
            }
        });
    }
}
