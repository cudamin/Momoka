using System.Xml.Linq;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 主页轮播「商店版式」的结构契约：主卡、圆点、下排推荐分行，推荐区用同一组三张卡响应式改位。
/// 实际尺寸、命中区、焦点和随窗口换档由 Shell 原生探针核对；这里防止圆点重回剧照或换档重建控件。
/// </summary>
internal static class HomeCarouselLayoutTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static void Register()
    {
        Test("轮播商店版式：Stage 分主卡、圆点与下排三行，圆点和推荐在主卡裁剪外", () =>
        {
            var banner = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "HomeBanner.xaml"));
            var names = banner.Descendants()
                .Select(element => (string?)element.Attribute(X + "Name"))
                .Where(name => name is not null)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var name in new[] { "Stage", "Frame", "Rail", "DotsHost", "PageDots", "PrevButton", "NextButton" })
                Assert.True(names.Contains(name), name + " 不在轮播标记里");

            var stage = Single(banner, "Stage");
            Assert.True(stage.Elements().Any(element => (string?)element.Attribute(X + "Name") == "HeroClip"),
                "大图不在 Stage 里");
            Assert.True(stage.Elements().Any(element => (string?)element.Attribute(X + "Name") == "Rail"),
                "右栏不在 Stage 里（会整块压在大图上）");
            Assert.False(stage.Elements().Any(element => (string?)element.Attribute(X + "Name") == "Frame"),
                "大图应该由自己的圆角层包着，而不是直接站在 Stage 里");

            var rows = stage.Elements().Single(element => element.Name.LocalName == "Grid.RowDefinitions").Elements()
                .Select(element => (string?)element.Attribute(X + "Name")).ToArray();
            Assert.Equal(3, rows.Length);
            Assert.Equal("HeroRow", rows[0]);
            Assert.Equal("DotsRow", rows[1]);
            Assert.Equal("RecommendationsRow", rows[2]);

            var dots = Single(banner, "DotsHost");
            Assert.True(ReferenceEquals(stage, dots.Parent), "圆点应由 Stage 独立行承载");
            Assert.Equal("1", (string?)dots.Attribute("Grid.Row"));
            Assert.True(dots.Elements().Any(element => (string?)element.Attribute(X + "Name") == "PageDots"),
                "圆点没有站在卡片下面那一行里");
            foreach (var name in new[] { "HeroClip", "Frame" })
                Assert.False(Single(banner, name).Descendants().Any(element =>
                    (string?)element.Attribute(X + "Name") is "DotsHost" or "PageDots" or "Rail"),
                    "圆点或推荐又回到了主卡的裁剪范围里");
        });

        Test("轮播商店版式：辅助区可改行列，三张卡只声明一次并保留点击与自动化入口", () =>
        {
            var banner = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "HomeBanner.xaml"));
            var rail = Single(banner, "Rail");
            var small = Single(banner, "RailSmallCards");
            foreach (var element in new[] { rail, small })
            {
                Assert.Equal(3, element.Elements().Single(child => child.Name.LocalName == "Grid.RowDefinitions").Elements().Count());
                Assert.Equal(3, element.Elements().Single(child => child.Name.LocalName == "Grid.ColumnDefinitions").Elements().Count());
            }
            Assert.Equal("12", (string?)Single(banner, "RailMiddleRow").Attribute("Height"));
            Assert.Equal("12", (string?)Single(banner, "SmallGapColumn").Attribute("Width"));
            Assert.Equal("0", (string?)Single(banner, "RecommendationsRow").Attribute("Height"));
            Assert.Equal("Collapsed", (string?)rail.Attribute("Visibility"));

            var names = new[] { "RailBigCard", "RailSmallCardA", "RailSmallCardB" };
            Assert.Equal(3, rail.Descendants().Count(element => element.Name.LocalName == "Button"));
            for (var index = 0; index < names.Length; index++)
            {
                var card = Single(banner, names[index]);
                Assert.Equal("Button", card.Name.LocalName);
                Assert.Equal("OnRailCardClicked", (string?)card.Attribute("Click"));
                Assert.Equal("BannerRailCard" + (index + 1), (string?)card.Attribute("AutomationProperties.AutomationId"));
                Assert.True(rail.Descendants().Contains(card));
                Assert.True(ReferenceEquals(index == 0 ? rail : small, card.Parent));
            }

            var layout = Source("Momoka.Shell/Views/HomeBanner.StoreLayout.cs");
            Assert.Contains("Grid.SetRow(Rail, below ? 2 : 0)", layout);
            Assert.Contains("Grid.SetColumn(Rail, side ? 2 : 0)", layout);
            Assert.Contains("Grid.SetRow(RailSmallCards, below ? 0 : 2)", layout);
            Assert.Contains("Grid.SetColumn(RailSmallCards, below ? 2 : 0)", layout);
            Assert.Contains("Grid.SetRow(RailSmallCardB, below ? 2 : 0)", layout);
            Assert.Contains("Grid.SetColumn(RailSmallCardB, below ? 0 : 2)", layout);
            foreach (var rebuild in new[] { "Children.Clear(", "Children.Remove(", "Children.Add(", "new Button", "new Border", "new Grid()" })
                Assert.DoesNotContain(rebuild, layout);

            var code = Source("Momoka.Shell/Views/HomeBanner.xaml.cs");
            Assert.Contains("HomeCarousel.ShowsRecommendations(_layout, width, _slides.Count)", code);
            Assert.Contains("PlaceRecommendations(width, height, rail, recommendations)", code);
            Assert.Contains("if (ReferenceEquals(_railSlides[slot], slide)) continue;", code);
        });

        Test("轮播商店版式：主卡、圆点与下排分别计高，圆点与卡片控件保持原有样式", () =>
        {
            var banner = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "HomeBanner.xaml"));

            var host = Single(banner, "DotsHost");
            var pageDots = Single(banner, "PageDots");
            var top = (string?)pageDots.Attribute("Margin") ?? "";
            Assert.Contains("10", top);

            var carousel = Source("Momoka.Core/Emby/HomeCarousel.cs");
            Assert.Contains("public const double DotsHeight = 28;", carousel);
            var code = Source("Momoka.Shell/Views/HomeBanner.xaml.cs");
            Assert.Contains("DotsRow.Height = new GridLength(dots)", code);
            Assert.Contains("_bandHeight + dots + _recommendationsHeight", code);
            var layout = Source("Momoka.Shell/Views/HomeBanner.StoreLayout.cs");
            Assert.Contains("HeroRow.Height = new GridLength(heroHeight)", layout);
            Assert.Contains("RecommendationsRow.Height = new GridLength(_recommendationsHeight)", layout);

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

        Test("商店推荐图片：下排与侧栏都参与预取，未换条目不重复清空图片订阅", () =>
        {
            var code = Source("Momoka.Shell/Views/HomeBanner.xaml.cs");
            var start = code.IndexOf("private void Prefetch()", StringComparison.Ordinal);
            Assert.True(start >= 0, "找不到真实轮播图片预取入口");
            var end = code.IndexOf("private void SyncArrows()", start, StringComparison.Ordinal);
            Assert.True(end > start, "找不到真实轮播图片预取边界");
            var prefetch = code[start..end];
            Assert.Contains("if (_recommendationsShown)", prefetch);
            Assert.DoesNotContain("if (_railShown)", prefetch);
            Assert.Contains("HomeCarousel.RailIndices(_index, _slides.Count)", prefetch);
            Assert.Contains("if (wanted.Contains(i))", prefetch);
            Assert.Contains("else _slides[i].Release()", prefetch);
            Assert.Contains("if (_railSlides.All(slide => slide is null)) return;", code);
        });

        Test("商店版式大图自己剪四边圆角，沉浸档由页面拨成 0", () =>
        {
            var banner = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "HomeBanner.xaml"));
            var clip = banner.Descendants().Single(element => (string?)element.Attribute(X + "Name") == "HeroClip");
            Assert.Equal("{StaticResource EgCardCornerRadius}", (string?)clip.Attribute("CornerRadius"));
            Assert.NotNull(clip.Attribute("Background"));
            Assert.True(clip.Elements().Any(element => (string?)element.Attribute(X + "Name") == "Frame"),
                "大图没有被自己的圆角层包住");

            var page = Source("Momoka.Shell/Views/HomePage.Carousel.cs");
            Assert.Contains("Banner.SetCardCornerRadius(corners);", page);
            var slide = Source("Momoka.Shell/Views/HomeBanner.xaml.cs");
            Assert.Contains("internal void SetCardCornerRadius(CornerRadius radius)", slide);
        });

        Test("剧照自己让开一格圆角，弧线上不再留剧照的亮边", () =>
        {
            var banner = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "HomeBanner.xaml"));
            var host = banner.Descendants().Single(element => (string?)element.Attribute(X + "Name") == "PictureHost");
            var clip = host.Descendants().Single(element => (string?)element.Attribute(X + "Name") == "PictureClip");
            Assert.Equal("1", (string?)clip.Attribute("Margin"));
            Assert.Equal("15", (string?)clip.Attribute("CornerRadius"));
            Assert.NotNull(clip.Attribute("Background"));
            foreach (var layer in new[] { "LayerA", "LayerB" })
                Assert.True(clip.Descendants().Any(element => (string?)element.Attribute(X + "Name") == layer),
                    layer + " 不在剧照自己的圆角层里");

            var code = Source("Momoka.Shell/Views/HomeBanner.xaml.cs");
            Assert.Contains("PictureClip.CornerRadius = inner", code);
            Assert.Contains("PictureClip.Margin = inset", code);
            Assert.Contains("var inset = PictureClip.Margin.Left;", code);
        });

        Test("商店版式左侧渐变接剧照代表色，卡片外不再有光晕", () =>
        {
            var banner = Source("Momoka.Shell/Views/HomeBanner.xaml.cs");
            Assert.Contains("private void PaintFadeInk()", banner);
            Assert.Contains("Current?.Ambient", banner);
            Assert.Contains("52d / peak", banner);

            var page = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "HomePage.xaml"));
            Assert.False(page.Descendants().Any(element => (string?)element.Attribute(X + "Name") == "BannerGlow"),
                "光晕已按用户令撤掉，不该再出现在页面里");
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
