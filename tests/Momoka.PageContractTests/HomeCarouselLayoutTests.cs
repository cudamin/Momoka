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
            Assert.Contains("var compact = below && HomeCarousel.StacksRecommendations(width)", layout);
            Assert.Contains("var columnsBelow = below && !compact", layout);
            Assert.Contains("Grid.SetRow(RailSmallCards, columnsBelow ? 0 : 2)", layout);
            Assert.Contains("Grid.SetColumn(RailSmallCards, columnsBelow ? 2 : 0)", layout);
            Assert.Contains("Grid.SetRow(RailSmallCardB, columnsBelow ? 2 : 0)", layout);
            Assert.Contains("Grid.SetColumn(RailSmallCardB, columnsBelow ? 0 : 2)", layout);
            foreach (var rebuild in new[] { "Children.Clear(", "Children.Remove(", "Children.Add(", "new Button", "new Border", "new Grid()" })
                Assert.DoesNotContain(rebuild, layout);

            var code = Source("Momoka.Shell/Views/HomeBanner.xaml.cs");
            Assert.Contains("HomeCarousel.ShowsRecommendations(_layout, width, _slides.Count)", code);
            Assert.Contains("PlaceRecommendations(width, height, rail, recommendations)", code);
            Assert.Contains("if (ReferenceEquals(_railSlides[slot], slide)) continue;", code);
        });

        Test("轮播紧凑商店版式：大推荐满行，下方小卡并排，退出紧凑档恢复原有行列", () =>
        {
            var layout = Source("Momoka.Shell/Views/HomeBanner.StoreLayout.cs");
            Assert.Contains("HomeCarousel.RailRows(compact ? HomeCarousel.RecommendationHeight(width) : heroHeight)", layout);
            Assert.Contains("RailTopRow.Height = columnsBelow ? new GridLength(1, GridUnitType.Star) : new GridLength(rows.Big)", layout);
            Assert.Contains("RailMiddleRow.Height = new GridLength(columnsBelow ? 0 : gap)", layout);
            Assert.Contains("RailBottomRow.Height = new GridLength(columnsBelow ? 0 : rows.Small)", layout);
            Assert.Contains("RailBigColumn.Width = columnsBelow ? new GridLength(columns.Big) : new GridLength(1, GridUnitType.Star)", layout);
            Assert.Contains("RailMiddleColumn.Width = new GridLength(columnsBelow ? gap : 0)", layout);
            Assert.Contains("RailSmallColumn.Width = new GridLength(columnsBelow ? columns.Small : 0)", layout);
            Assert.Contains("SmallGapColumn.Width = new GridLength(columnsBelow ? 0 : gap)", layout);
            Assert.Contains("SmallSecondColumn.Width = columnsBelow ? new GridLength(0) : new GridLength(1, GridUnitType.Star)", layout);
            Assert.Contains("SmallGapRow.Height = new GridLength(columnsBelow ? gap : 0)", layout);
            Assert.Contains("SmallSecondRow.Height = columnsBelow ? new GridLength(1, GridUnitType.Star) : new GridLength(0)", layout);
        });

        Test("商店主卡倒计时：左下 16 DIP 原生扇形不接管命中，分页点与推荐仍在主卡外", () =>
        {
            var banner = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "HomeBanner.xaml"));
            var countdown = Single(banner, "HeroCountdown");
            Assert.Equal("Grid", countdown.Name.LocalName);
            Assert.Equal("16", (string?)countdown.Attribute("Width"));
            Assert.Equal("16", (string?)countdown.Attribute("Height"));
            Assert.Equal("12,0,0,12", (string?)countdown.Attribute("Margin"));
            Assert.Equal("Left", (string?)countdown.Attribute("HorizontalAlignment"));
            Assert.Equal("Bottom", (string?)countdown.Attribute("VerticalAlignment"));
            Assert.Equal("False", (string?)countdown.Attribute("IsHitTestVisible"));
            Assert.Equal("Collapsed", (string?)countdown.Attribute("Visibility"));
            Assert.True(Single(banner, "Frame").Descendants().Contains(countdown), "扇形必须位于主卡的裁剪范围内");
            foreach (var name in new[] { "DotsHost", "Rail" })
                Assert.False(Single(banner, name).Descendants().Contains(countdown), "推荐与圆点不能承载主卡倒计时");
            Assert.Equal("Ellipse", Single(banner, "CountdownFace").Name.LocalName);
            Assert.Equal("Path", Single(banner, "CountdownSlice").Name.LocalName);
            Assert.Equal("8,8", (string?)Single(banner, "CountdownFigure").Attribute("StartPoint"));
            Assert.Equal("True", (string?)Single(banner, "CountdownFigure").Attribute("IsClosed"));
            Assert.Equal("8,0", (string?)Single(banner, "CountdownStart").Attribute("Point"));
            var arc = Single(banner, "CountdownArc");
            Assert.Equal("8,8", (string?)arc.Attribute("Size"));
            Assert.Equal("Clockwise", (string?)arc.Attribute("SweepDirection"));
        });

        Test("商店主卡悬停：暂停只绑定主卡，扇形与翻页使用同一个可恢复时钟", () =>
        {
            var banner = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "HomeBanner.xaml"));
            var hero = Single(banner, "HeroClip");
            Assert.Equal("OnHeroPointerEntered", (string?)hero.Attribute("PointerEntered"));
            Assert.Equal("OnHeroPointerEntered", (string?)hero.Attribute("PointerMoved"));
            Assert.Equal("OnHeroPointerExited", (string?)hero.Attribute("PointerExited"));
            foreach (var name in new[] { "Root", "Rail", "DotsHost" })
                Assert.False(Single(banner, name).Attributes().Any(attribute => attribute.Value is "OnHeroPointerEntered" or "OnHeroPointerExited"),
                    "主卡之外不能因指针停留而暂停");

            var code = Source("Momoka.Shell/Views/HomeBanner.xaml.cs");
            Assert.Contains("_heroWatch = new HoverWatch(HeroClip", code);
            Assert.Contains("private readonly BannerCycleClock _cycleClock = new()", code);
            Assert.Contains("HeroCountdown.Visibility = _layout == CarouselLayout.Store && _slides.Count > 1", code);
            Assert.Contains("_cycleClock.Restart(_cycleStarted)", code);
            Assert.Contains("_cycleClock.SetPaused(_layout == CarouselLayout.Store && _heroHover, _cycleStarted)", code);
            Assert.Contains("TimeSpan.FromMilliseconds(_cycleClock.ElapsedMilliseconds(Environment.TickCount64))", code);
            Assert.Contains("PaintCountdown(elapsed / _dwell)", code);
            var unloaded = code.IndexOf("private void OnUnloaded(", StringComparison.Ordinal);
            var nextHandler = code.IndexOf("private void OnColorSystemTick(", unloaded, StringComparison.Ordinal);
            Assert.True(unloaded >= 0 && nextHandler > unloaded, "找不到主卡卸载范围");
            Assert.Contains("_heroWatch.Leave()", code[unloaded..nextHandler]);

            var countdown = Source("Momoka.Shell/Views/HomeBanner.Countdown.cs");
            Assert.Contains("var paused = _layout == CarouselLayout.Store && _heroHover", countdown);
            Assert.Contains("_cycleClock.SetPaused(paused, Environment.TickCount64)", countdown);
            Assert.Contains("_cycleClock.ElapsedMilliseconds(Environment.TickCount64)", countdown);
            Assert.Contains("if (paused) _timer?.Stop()", countdown);
            Assert.Contains("if (e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse) return", countdown);
            Assert.DoesNotContain(".Restart(", countdown);
            Assert.DoesNotContain("CreateTimer(", countdown);
            Assert.DoesNotContain("new Storyboard", countdown);
            Assert.Contains("var remaining = Math.Clamp(1 - progress, 0, 1)", countdown);
            Assert.Contains("CountdownArc.IsLargeArc = remaining > 0.5", countdown);
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
            Assert.Contains("var extendLeft = _extendLeft;", slide);
            Assert.Contains("DecodePixelsAsync(bytes, PictureWidth, extendLeft, cts.Token)", slide);

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
