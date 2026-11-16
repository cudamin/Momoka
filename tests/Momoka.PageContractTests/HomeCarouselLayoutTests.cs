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

            // 2026-11-16 实屏：右栏按钮不显式拉伸时会缩成标题那么宽、只剩左上角一小块。
            var card = banner.Descendants().Single(element => (string?)element.Attribute(X + "Key") == "EgBannerRailCardStyle");
            foreach (var property in new[] { "HorizontalAlignment", "VerticalAlignment" })
            {
                Assert.Equal("Stretch", (string?)card.Elements().Single(element => element.Name.LocalName == "Setter"
                    && (string?)element.Attribute("Property") == property).Attribute("Value"));
            }
        });

        Test("商店版式背景：整宽版式才扩边，商店用原图、不加大面积高斯", () =>
        {
            var banner = Source("Momoka.Shell/Views/HomeBanner.xaml.cs");
            Assert.Contains("slide.SetLeftExtension(layout != CarouselLayout.Store)", banner);
            Assert.Contains("slide.SetLeftExtension(_layout != CarouselLayout.Store)", banner);

            var slide = Source("Momoka.Shell/Views/BannerSlide.cs");
            Assert.Contains("internal void SetLeftExtension(bool enabled)", slide);
            Assert.Contains("DecodePixelsAsync(bytes, PictureWidth, _extendLeft, cts.Token)", slide);

            var loader = Source("Momoka.Shell/Media/BannerPictureLoader.cs");
            Assert.Contains("if (!extendLeft) return new HeroPictureLoader.HeroPixels(pixels, width, height);", loader);

            var cache = Source("Momoka.Shell/Media/BannerBackdropCache.cs");
            Assert.Contains("int requestedWidth, bool extendLeft = true", cache);
            Assert.Contains("? CarouselBackdrop.LeftExtensionShare", cache);
        });

        Test("商店版式取色：左侧渐变与卡片光晕接同一条剧照代表色", () =>
        {
            var banner = Source("Momoka.Shell/Views/HomeBanner.xaml.cs");
            Assert.Contains("private void PaintFadeInk()", banner);
            Assert.Contains("private void PublishAmbient()", banner);
            Assert.Contains("internal event Action<Windows.UI.Color>? AmbientChanged;", banner);
            Assert.Contains("Current?.Ambient", banner);

            var page = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "HomePage.xaml"));
            Assert.True(page.Descendants().Any(element => (string?)element.Attribute(X + "Name") == "BannerGlow"),
                "商店版式的卡片光晕不在页面里");
            Assert.True(page.Descendants().Any(element => (string?)element.Attribute(X + "Name") == "BannerGlowBrush"),
                "光晕的径向画刷不在页面里");

            var pageCode = Source("Momoka.Shell/Views/HomePage.xaml.cs");
            Assert.Contains("Banner.AmbientChanged +=", pageCode);
            Assert.Contains("private void PaintBannerGlow()", pageCode);
            Assert.Contains("BannerGlowBrush.GradientStops", pageCode);
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
