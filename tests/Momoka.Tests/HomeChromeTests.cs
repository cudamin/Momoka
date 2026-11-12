using System.Xml.Linq;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 主页版式的源码契约（2026-11-12 晚按用户给的商店截图改）：轮播是一块四边留白的圆角卡片，货架牌子底下
/// 不再画那条通栏线。
/// <para>
/// 这几件都是「改回旧样子也不报错」的事 —— 边距少写一格、牌子上的 ShowRule 掉了、间距又回 12，屏上只是
/// 「和昨天不太一样」。真正的观感仍由实屏截图核对，这一组只钉住那几个数。
/// </para>
/// </summary>
internal static class HomeChromeTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static void Register()
    {
        Test("主页轮播：四边留白的圆角卡片，外沿还有一圈光晕", () =>
        {
            var page = HomePage();
            var zone = Named(page, "BannerZone");
            Assert.Equal("36,20,13,0", zone.Attribute("Margin")?.Value,
                "轮播卡片的四边留白变了（左 36 脊线、上 20、右 13 滚动条车道）");

            // 卡片把带子裁进圆角里：Border 的 CornerRadius 会裁孩子，所以 HomeBanner 必须在它里面。
            var card = Named(page, "BannerCard");
            Assert.Equal("Border", card.Name.LocalName);
            Assert.Equal("{StaticResource EgCardCornerRadius}", card.Attribute("CornerRadius")?.Value);
            Assert.True(card.Descendants().Any(element => element.Name.LocalName == "HomeBanner"),
                "轮播带子没有落在圆角卡片里，圆角裁不到它");

            // 光晕比卡片大 20、在卡片后面，四个停点由代码按剧照的颜色写。
            var glow = Named(page, "BannerGlow");
            Assert.Equal("-20", glow.Attribute("Margin")?.Value, "光晕没有从卡片外沿露出来");
            var gradient = glow.Descendants().Single(element => element.Name.LocalName == "RadialGradientBrush");
            Assert.Equal(4, gradient.Elements().Count(element => element.Name.LocalName == "GradientStop"),
                "光晕不是四个停点");
            Assert.True(!gradient.Elements().Any(element => element.Attribute("Color") is not null),
                "光晕停点写了死颜色：它该由当前剧照的颜色决定");
        });

        Test("主页货架牌子：不画标题底下那条通栏线，标题到卡片收到 8", () =>
        {
            var page = HomePage();
            var head = page.Descendants().Single(element => element.Name.LocalName == "ShelfHead");
            Assert.Equal("False", head.Attribute("ShowRule")?.Value,
                "主页货架又画了标题底下那条线");

            var stack = head.Parent!;
            Assert.Equal("StackPanel", stack.Name.LocalName);
            Assert.Equal("{StaticResource EgSpaceS}", stack.Attribute("Spacing")?.Value,
                "标题到卡片的间距不是 EgSpaceS(8)");

            // 详情页与设置页用的是同一块牌子，那条线在那边照留 —— 所以它必须是属性而不是删掉。
            var shelfHead = File.ReadAllText(Path.Combine(Root(),
                "src", "Momoka.Shell", "Views", "ShelfHead.xaml.cs"));
            Assert.Contains("ShowRuleProperty", shelfHead, "ShelfHead 没有 ShowRule 属性");
            Assert.Contains("new PropertyMetadata(true, OnShowRuleChanged)", shelfHead,
                "ShowRule 的默认值不是 true：详情页与设置页会跟着丢线");
        });
    }

    private static XDocument HomePage() =>
        XDocument.Load(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "HomePage.xaml"));

    private static XElement Named(XDocument document, string name) =>
        document.Descendants().Single(element => element.Attribute(Xaml + "Name")?.Value == name);

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new InvalidOperationException("本树源码缺失");
    }
}
