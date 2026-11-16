using System.Xml.Linq;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 主页轮播「商店版式」重做（2026-11-16）的源码契约：两列卡片、右栏上大下二小、分页圆点在卡片
/// <em>下面</em>那一行，以及圆点墨不再从应用字典里找一支只写在控件里的画刷。
/// 实际尺寸、命中区与随窗口换档由 Shell 原生探针核对；这里钉的是结构，免得下一次改版把圆点又塞回剧照里。
/// </summary>
internal static class HomeCarouselLayoutTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static void Register()
    {
        Test("轮播商店版式：大图与右栏在 Stage 两列里，圆点在 Stage 外面的下一行", () =>
        {
            var banner = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "HomeBanner.xaml"));
            var names = banner.Descendants()
                .Select(element => (string?)element.Attribute(X + "Name"))
                .Where(name => name is not null)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var name in new[] { "Stage", "Frame", "Rail", "DotsHost", "PageDots", "PrevButton", "NextButton" })
                Assert.True(names.Contains(name), name + " 不在轮播标记里");

            var stage = Single(banner, "Stage");
            Assert.True(stage.Elements().Any(element => (string?)element.Attribute(X + "Name") == "Frame"),
                "大图不在 Stage 里");
            Assert.True(stage.Elements().Any(element => (string?)element.Attribute(X + "Name") == "Rail"),
                "右栏不在 Stage 里（会整块压在大图上）");
            Assert.False(stage.Descendants().Any(element => (string?)element.Attribute(X + "Name") is "DotsHost" or "PageDots"),
                "圆点又回到了卡片里面");

            var dots = Single(banner, "DotsHost");
            Assert.Equal("1", (string?)dots.Attribute("Grid.Row"));
            Assert.True(dots.Elements().Any(element => (string?)element.Attribute(X + "Name") == "PageDots"),
                "圆点没有站在卡片下面那一行里");
        });

        Test("轮播商店版式：右栏上大下二小是 2:1，圆点行高与 Core 的 DotsHeight 同一档", () =>
        {
            var banner = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "HomeBanner.xaml"));
            var rail = Single(banner, "Rail");
            var rows = rail.Elements().Single(element => element.Name.LocalName == "Grid.RowDefinitions")
                .Elements()
                .Select(element => (string?)element.Attribute("Height"))
                .ToArray();
            Assert.Equal(new[] { "2*", "8", "*" }.Length, rows.Length);
            Assert.Equal("2*", rows[0]);
            Assert.Equal("8", rows[1]);
            Assert.Equal("*", rows[2]);

            var host = Single(banner, "DotsHost");
            var pageDots = Single(banner, "PageDots");
            var top = (string?)pageDots.Attribute("Margin") ?? "";
            Assert.Contains("10", top);

            var carousel = Source("Momoka.Core/Emby/HomeCarousel.cs");
            Assert.Contains("public const double DotsHeight = 28;", carousel);

            var round = banner.Descendants().Single(element => (string?)element.Attribute(X + "Key") == "EgBannerPageRoundStyle");
            Assert.Equal("30", (string?)round.Elements().Single(element => element.Name.LocalName == "Setter"
                && (string?)element.Attribute("Property") == "Width").Attribute("Value"));
            Assert.Equal("15", (string?)round.Elements().Single(element => element.Name.LocalName == "Setter"
                && (string?)element.Attribute("Property") == "CornerRadius").Attribute("Value"));
            Assert.NotNull(host);
        });

        Test("轮播圆点墨：只从应用主题色取，不再引用控件私有的 EgBannerDotBrush", () =>
        {
            var code = Source("Momoka.Shell/Views/HomeBanner.xaml.cs");
            Assert.DoesNotContain("Resources[\"EgBannerDotBrush\"]", code);
            Assert.Contains("\"EgTextBrush\" : \"EgTextFaintBrush\"", code);
        });
    }

    private static XElement Single(XDocument document, string name) =>
        document.Descendants().Single(element => (string?)element.Attribute(X + "Name") == name);

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new AssertionException("找不到本次编译所属的工作树");
    }

    private static string Source(string path) =>
        File.ReadAllText(Path.Combine(FindRoot(), "src", path.Replace('/', Path.DirectorySeparatorChar)));
}
