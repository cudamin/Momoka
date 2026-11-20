using Momoka.Emby;
using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 「参考主页轮播大图版-misty-4.9.css 给主页轮播功能」, asserted without a window. What
/// <see cref="HomeCarousel"/> answers is 「which items get to be the big picture, and what does one of them
/// say」 — the halves that a control cannot be asked about once it is drawn: a set of slides built from
/// 设置里那个来源发回来的候选（2026-09-13「轮播图改用前十个最近添加」，同日下午来源、媒体、张数、秒数四样
/// 进了设置），which card on the right the one on
/// screen corresponds to, and a band height that has to leave the shelves below it reachable.
/// </summary>
internal static class HomeCarouselTests
{
    public static void Register()
    {
        RegisterSlides();
        RegisterStep();
        RegisterHeight();
        RegisterText();
        RegisterBackdrop();
        RegisterPreferences();
        RegisterLayout();
    }

    /// <summary>
    /// 「参考上图在设置中再添加一版轮播图」（用户 2026-11-16，附图是商店轮播在不同窗口大小下的样子）：
    /// 商店版式在宽窗口左主右辅，中档主图满行、三张推荐左大右小；紧凑档推荐上大下二小。这一组钉的是
    /// 内容宽度和条目数的换档边界、参考图的卡片比例，以及推荐区接着哪三张。
    /// </summary>
    private static void RegisterLayout()
    {
        Test("轮播版式：缺键读出来是现有整宽版式，推荐区只在商店版式里出现", () =>
        {
            Assert.Equal(0, (int)CarouselLayout.Full);
            Assert.Equal(1, (int)CarouselLayout.Store);
            Assert.False(HomeCarousel.ShowsRail(CarouselLayout.Full, 1600, 10));
            Assert.False(HomeCarousel.ShowsRecommendations(CarouselLayout.Full, 1600, 10));
            Assert.False(HomeCarousel.ShowsRecommendations(CarouselLayout.Full, 848, 10));
            Assert.True(HomeCarousel.ShowsRail(CarouselLayout.Store, 1600, 10));
        });

        Test("轮播版式：900 内容宽是侧栏边界，窄一像素推荐移到下排", () =>
        {
            Assert.Equal(900d, HomeCarousel.RailBasisWidth);
            Assert.False(HomeCarousel.ShowsRail(CarouselLayout.Store, 899, 4));
            Assert.True(HomeCarousel.ShowsRecommendations(CarouselLayout.Store, 899, 4));
            Assert.True(HomeCarousel.ShowsRail(CarouselLayout.Store, 900, 4));
            Assert.True(HomeCarousel.ShowsRail(CarouselLayout.Store, 901, 4));
        });

        Test("轮播版式：320 内容宽是推荐边界，再窄只留主图", () =>
        {
            Assert.Equal(320d, HomeCarousel.RecommendationsMinWidth);
            Assert.False(HomeCarousel.ShowsRecommendations(CarouselLayout.Store, 319, 4));
            Assert.True(HomeCarousel.ShowsRecommendations(CarouselLayout.Store, 320, 4));
            Assert.True(HomeCarousel.ShowsRecommendations(CarouselLayout.Store, 321, 4));
            Assert.False(HomeCarousel.ShowsRail(CarouselLayout.Store, 320, 4));
            Assert.Equal(0d, HomeCarousel.RecommendationHeight(319));
            Assert.Equal(279d, HomeCarousel.RecommendationHeight(320));
        });

        Test("轮播版式：680 内容宽从竖排推荐恢复左大右二小", () =>
        {
            Assert.Equal(680d, HomeCarousel.RecommendationsCompactWidth);
            Assert.False(HomeCarousel.StacksRecommendations(319));
            Assert.True(HomeCarousel.StacksRecommendations(320));
            Assert.True(HomeCarousel.StacksRecommendations(679));
            Assert.False(HomeCarousel.StacksRecommendations(680));
            Assert.False(HomeCarousel.StacksRecommendations(681));
            Assert.False(HomeCarousel.StacksRecommendations(900));
            foreach (var width in new[] { 679d, 680d })
            {
                Assert.True(HomeCarousel.ShowsRecommendations(CarouselLayout.Store, width, 4));
                Assert.False(HomeCarousel.ShowsRail(CarouselLayout.Store, width, 4));
            }
            Assert.Equal(582d, HomeCarousel.RecommendationHeight(679));
            Assert.Equal((452d, 216d), HomeCarousel.RecommendationColumns(680));
            Assert.Equal(254d, HomeCarousel.RecommendationHeight(680));
        });

        Test("轮播版式：条目不足四张时宽窄两档都隐藏推荐，避免重复主角", () =>
        {
            Assert.Equal(3, HomeCarousel.RailSlots);
            foreach (var width in new[] { 320d, 470d, 679d, 680d, 848d, 1120d })
            {
                foreach (var count in new[] { 0, 1, 2, 3 })
                {
                    Assert.False(HomeCarousel.ShowsRecommendations(CarouselLayout.Store, width, count));
                    Assert.False(HomeCarousel.ShowsRail(CarouselLayout.Store, width, count));
                }
                Assert.True(HomeCarousel.ShowsRecommendations(CarouselLayout.Store, width, 4));
            }
        });

        Test("轮播版式：未测量和无效内容宽不显示推荐", () =>
        {
            foreach (var width in new[] { 0d, -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                Assert.False(HomeCarousel.ShowsRecommendations(CarouselLayout.Store, width, 10));
                Assert.False(HomeCarousel.ShowsRail(CarouselLayout.Store, width, 10));
                Assert.False(HomeCarousel.StacksRecommendations(width));
                Assert.Equal((0d, 0d), HomeCarousel.RecommendationColumns(width));
                Assert.Equal(0d, HomeCarousel.RecommendationHeight(width));
            }
        });

        Test("轮播版式：1120 宽参考图为 671 主卡配 437 右栏，间隔统一 12", () =>
        {
            Assert.Equal(437d, HomeCarousel.RailWidth(1120));
            Assert.Equal(671d, HomeCarousel.MainWidth(1120, true));
            Assert.Equal(377d, HomeCarousel.StoreHeight(0, 1120, true));
            Assert.Equal(12d, HomeCarousel.RailGap);

            // 更宽时辅助卡同步增长，不能停在旧版的固定最大宽度。
            Assert.True(HomeCarousel.RailWidth(1860) > HomeCarousel.RailWidth(1120));
            Assert.True(HomeCarousel.RailWidth(2400) > HomeCarousel.RailWidth(1860));
            Assert.Equal(848d, HomeCarousel.MainWidth(848, false));
            Assert.Equal(0d, HomeCarousel.MainWidth(0, true));
            Assert.Equal(0d, HomeCarousel.RailWidth(0));
            Assert.Equal(0d, HomeCarousel.RailWidth(double.NaN));
            Assert.Equal(0d, HomeCarousel.RailWidth(double.PositiveInfinity));
        });

        Test("轮播版式：宽档四卡接近 16:9，整行和右栏上下都不留多余空洞", () =>
        {
            foreach (var width in new[] { 900d, 1120d, 1422d, 1860d, 2400d })
            {
                var main = HomeCarousel.MainWidth(width, true);
                var rail = HomeCarousel.RailWidth(width);
                var hero = HomeCarousel.StoreHeight(0, width, true);
                var (big, small) = HomeCarousel.RailRows(hero);
                Assert.Equal(width, main + HomeCarousel.RailGap + rail);
                Assert.Equal(hero, big + HomeCarousel.RailGap + small);
                Assert.True(Math.Abs(main / HomeCarousel.WindowAspect - hero) <= 1);
                Assert.True(Math.Abs(rail / HomeCarousel.WindowAspect - big) <= 1);
                Assert.True(Math.Abs((rail - HomeCarousel.RailGap) / 2 / HomeCarousel.WindowAspect - small) <= 1);
            }
            Assert.Equal((0d, 0d), HomeCarousel.RailRows(HomeCarousel.RailGap));
            Assert.Equal((0d, 0d), HomeCarousel.RailRows(0));
        });

        Test("轮播版式：848 宽参考图主卡满行，下排 564 大卡配 272 小卡列", () =>
        {
            Assert.Equal(477d, HomeCarousel.StoreHeight(0, 848, false));
            Assert.Equal((564d, 272d), HomeCarousel.RecommendationColumns(848));
            Assert.Equal(317d, HomeCarousel.RecommendationHeight(848));

            foreach (var width in new[] { 680d, 848d, 899d })
            {
                var (big, small) = HomeCarousel.RecommendationColumns(width);
                var height = HomeCarousel.RecommendationHeight(width);
                Assert.Equal(width, big + HomeCarousel.RailGap + small);
                Assert.True(big > small && small > 0);
                Assert.True(Math.Abs(big / HomeCarousel.WindowAspect - height) <= 1);
                Assert.True(Math.Abs(small / HomeCarousel.WindowAspect - (height - HomeCarousel.RailGap) / 2) <= 1);
            }
            Assert.Equal((0d, 0d), HomeCarousel.RecommendationColumns(HomeCarousel.RailGap));
        });

        Test("轮播版式：470 紧凑参考图推荐满行 264 高，下方两张 229 宽小卡并排", () =>
        {
            Assert.True(HomeCarousel.StacksRecommendations(470));
            Assert.Equal(264d, HomeCarousel.StoreHeight(0, 470, false));
            Assert.Equal(405d, HomeCarousel.RecommendationHeight(470));
            Assert.Equal((264d, 129d), HomeCarousel.RailRows(HomeCarousel.RecommendationHeight(470)));
            Assert.Equal(229d, (470 - HomeCarousel.RailGap) / 2);

            foreach (var width in new[] { 320d, 470d, 600d, 679d })
            {
                var height = HomeCarousel.RecommendationHeight(width);
                var (big, small) = HomeCarousel.RailRows(height);
                Assert.Equal(height, big + HomeCarousel.RailGap + small);
                Assert.True(big > small && small > 0);
                Assert.True(Math.Abs(width / HomeCarousel.WindowAspect - big) <= 1);
                Assert.True(Math.Abs((width - HomeCarousel.RailGap) / 2 / HomeCarousel.WindowAspect - small) <= 1);
            }
        });

        Test("轮播版式：商店主卡按自身 16:9 取高，仍受一屏与下限约束", () =>
        {
            Assert.Equal(337d, HomeCarousel.StoreHeight(0, 1000, true));
            Assert.Equal(HomeCarousel.StoreHeight(0, 1000, true),
                HomeCarousel.NaturalStoreHeight(1000, true));
            Assert.Equal(562d, HomeCarousel.StoreHeight(0, 1000, false));

            // 上限是一屏，商店下限单独为 240，整宽版式的 300 下限不变。
            Assert.Equal(360d, HomeCarousel.StoreHeight(360, 1120, true));
            Assert.Equal(360d, HomeCarousel.StoreHeight(360, 1000, false));
            Assert.Equal(HomeCarousel.StoreMinHeight, HomeCarousel.StoreHeight(0, 100, false));
            Assert.Equal(HomeCarousel.UnmeasuredHeight, HomeCarousel.StoreHeight(0, 0, true));

            // 分版式取用时：整宽档一个像素不变，商店档换成上面两副形状。
            Assert.Equal(HomeCarousel.Height(700, 1000), HomeCarousel.BandHeight(CarouselLayout.Full, 700, 1000, false));
            Assert.Equal(337d, HomeCarousel.BandHeight(CarouselLayout.Store, 700, 1000, true));
            Assert.Equal(HomeCarousel.StoreHeight(700, 1000, false),
                HomeCarousel.BandHeight(CarouselLayout.Store, 700, 1000, false));

            // 圆点那一行的高度是页面预留的，不是压在剧照里。
            Assert.Equal(28d, HomeCarousel.DotsHeight);
        });

        Test("轮播版式：紧凑主卡的 240 下限不改变整宽版式，窗口恢复后返回设计高度", () =>
        {
            Assert.Equal(240d, HomeCarousel.StoreMinHeight);
            Assert.Equal(300d, HomeCarousel.MinHeight);
            foreach (var width in new[] { 320d, 470d, 679d, 680d, 848d })
            {
                Assert.Equal(HomeCarousel.Height(700, width), HomeCarousel.BandHeight(CarouselLayout.Full, 700, width, false));
                Assert.Equal(HomeCarousel.Height(700, width), HomeCarousel.BandHeight(CarouselLayout.Full, 700, width, true));
            }
            Assert.Equal(300d, HomeCarousel.Height(0, 470));
            Assert.Equal(240d, HomeCarousel.StoreHeight(0, 320, false));
            Assert.Equal(240d, HomeCarousel.StoreHeight(200, 470, false));
            Assert.Equal(264d, HomeCarousel.StoreHeight(700, 470, false));
            Assert.Equal(264d, HomeCarousel.NaturalStoreHeight(470, false));
            Assert.Equal(382d, HomeCarousel.StoreHeight(700, 679, false));
        });

        Test("轮播版式：右栏接着台上那张的后面三张，到末尾绕回开头", () =>
        {
            Assert.True(HomeCarousel.RailIndices(0, 10).SequenceEqual([1, 2, 3]));
            Assert.True(HomeCarousel.RailIndices(8, 10).SequenceEqual([9, 0, 1]));
            Assert.True(HomeCarousel.RailIndices(0, 4).SequenceEqual([1, 2, 3]));
            Assert.True(HomeCarousel.RailIndices(0, 3).SequenceEqual([1, 2]));
            Assert.True(HomeCarousel.RailIndices(0, 1).Count == 0);
            Assert.True(HomeCarousel.RailIndices(0, 0).Count == 0);
        });
    }

    /// <summary>
    /// 设置里那四行（2026-09-13「新增在设置中设置轮播图要使用什么媒体，和要使用最近添加还是随机的还有数量
    /// 的选项。还有封面的轮换的秒数」）在 Core 这一头的落点：张数和秒数夹进同一档（设置页的行范围和
    /// SettingsMigration.Normalize 读的都是这对常量），媒体到请求参数的两张表，还有枚举从 0 数起 ——
    /// 旧设置文件缺键读出来的整数就是装机默认，这条断言就是「升上来的老用户一个像素都不变」的凭据。
    /// </summary>
    private static void RegisterPreferences()
    {
        Test("轮播：设置里的张数和秒数夹进同一档", () =>
        {
            Assert.Equal(HomeCarousel.MinSlots, HomeCarousel.ClampSlots(0));
            Assert.Equal(HomeCarousel.MinSlots, HomeCarousel.ClampSlots(1));
            Assert.Equal(7, HomeCarousel.ClampSlots(7));
            Assert.Equal(HomeCarousel.MaxSlots, HomeCarousel.ClampSlots(99));
            Assert.Equal(HomeCarousel.Slots, HomeCarousel.ClampSlots(HomeCarousel.Slots));

            Assert.Equal(TimeSpan.FromSeconds(HomeCarousel.MinDwellSeconds), HomeCarousel.DwellFor(0));
            Assert.Equal(TimeSpan.FromSeconds(12), HomeCarousel.DwellFor(12));
            Assert.Equal(TimeSpan.FromSeconds(HomeCarousel.MaxDwellSeconds), HomeCarousel.DwellFor(3600));
            Assert.Equal(HomeCarousel.Dwell, HomeCarousel.DwellFor(HomeCarousel.DefaultDwellSeconds));
        });

        Test("轮播：媒体到请求参数的两张表 —— 最近添加的剧集要带单集，随机不带", () =>
        {
            // 最近添加走 /Items/Latest，GroupItems 并剧之后那条可能以任意一头出现，所以 Series 和 Episode
            // 都要；随机走 /Items SortBy=Random，没有 GroupItems，单集上来就是一部剧一张 —— 只按 Series 要。
            Assert.Equal(0, HomeCarousel.LatestTypes(CarouselMediaType.All).Count);
            Assert.Equal("Movie", HomeCarousel.LatestTypes(CarouselMediaType.Movies)[0]);
            Assert.Equal("Series", HomeCarousel.LatestTypes(CarouselMediaType.Series)[0]);
            Assert.Equal("Episode", HomeCarousel.LatestTypes(CarouselMediaType.Series)[1]);

            Assert.Equal("Movie", HomeCarousel.RandomTypes(CarouselMediaType.All)[0]);
            Assert.Equal("Series", HomeCarousel.RandomTypes(CarouselMediaType.All)[1]);
            Assert.Equal("Movie", HomeCarousel.RandomTypes(CarouselMediaType.Movies)[0]);
            Assert.Equal("Series", HomeCarousel.RandomTypes(CarouselMediaType.Series)[0]);
        });

        Test("轮播：来源和媒体的枚举从 0 数起，缺键读出来就是装机默认", () =>
        {
            Assert.Equal(0, (int)CarouselSource.Recent);
            Assert.Equal(1, (int)CarouselSource.Random);
            Assert.Equal(0, (int)CarouselMediaType.All);
            Assert.Equal(1, (int)CarouselMediaType.Movies);
            Assert.Equal(2, (int)CarouselMediaType.Series);
        });
    }

    private static void RegisterBackdrop()
    {
        Test("轮播：左侧延长原图宽度的15%，整幅原图和高度保持不变", () =>
        {
            const int width = 100;
            const int height = 12;
            var original = Enumerable.Range(0, width * height * 4).Select(index => (byte)(index % 251)).ToArray();
            var (pixels, extendedWidth) = CarouselBackdrop.ExtendLeft(original, width, height);

            Assert.Equal(115, extendedWidth);
            Assert.Equal(extendedWidth * height * 4, pixels.Length);
            for (var y = 0; y < height; y++)
                Assert.True(original.AsSpan((y * width + 8) * 4, (width - 8) * 4)
                    .SequenceEqual(pixels.AsSpan((y * extendedWidth + 23) * 4, (width - 8) * 4)), "过渡之后的每个像素都必须保持原样");
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    Assert.Equal(original[(y * width + x) * 4 + 3], pixels[(y * extendedWidth + x + 15) * 4 + 3]);
            Assert.Equal(192, CarouselBackdrop.ExtensionWidth(1280));
        });

        Test("轮播：纯色背景延长及模糊不会改变颜色", () =>
        {
            const int width = 100;
            const int height = 16;
            var original = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var at = (y * width + x) * 4;
                    original[at] = 40;
                    original[at + 1] = 60;
                    original[at + 2] = 190;
                    original[at + 3] = 255;
                }

            var (pixels, extendedWidth) = CarouselBackdrop.ExtendLeft(original, width, height);
            for (var y = 0; y < height; y++)
            {
                var row = y * extendedWidth * 4;
                Assert.Equal((byte)60, pixels[row + 1]);
                Assert.Equal((byte)190, pixels[row + 2]);
                Assert.True(pixels.AsSpan(row + 14 * 4, 4).SequenceEqual(pixels.AsSpan(row + 15 * 4, 4)), "接缝不能出现色阶");
            }
        });

        Test("轮播：单像素背景也能延长，没有空行或黑色填充", () =>
        {
            byte[] original = [12, 60, 220, 255];
            var (pixels, width) = CarouselBackdrop.ExtendLeft(original, 1, 1);
            Assert.Equal(2, width);
            Assert.True(pixels.AsSpan(0, 4).SequenceEqual(original));
            Assert.True(pixels.AsSpan(4, 4).SequenceEqual(original));
        });

        Test("轮播：完整延长区高斯经原图左侧过渡恢复清晰", () =>
        {
            const int width = 200;
            const int height = 120;
            var original = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var at = (y * width + x) * 4;
                    original[at] = original[at + 1] = original[at + 2] = (byte)(y % 2 == 0 ? 0 : 255);
                    original[at + 3] = 255;
                }
            var (pixels, extendedWidth) = CarouselBackdrop.ExtendLeft(original, width, height);
            var extension = extendedWidth - width;
            const int transition = 16;
            var middle = extension / 2;
            var first = pixels[(60 * extendedWidth + middle) * 4];
            var second = pixels[(61 * extendedWidth + middle) * 4];
            Assert.True(Math.Abs(first - second) < 8, "扩边内部应消除交替行的细纹，不能只是采色拉长");
            for (var y = 0; y < height; y++)
            {
                Assert.True(original.AsSpan((y * width + transition) * 4, (width - transition) * 4)
                    .SequenceEqual(pixels.AsSpan((y * extendedWidth + extension + transition) * 4, (width - transition) * 4)), "过渡之后原始封面保持清晰");
            }
            for (var x = 0; x < extension; x++)
            {
                Assert.True(Math.Abs(pixels[(60 * extendedWidth + x) * 4] - pixels[(61 * extendedWidth + x) * 4]) < 8,
                    "延长区最后一列也必须消除交替行细纹");
            }
        });
        Test("轮播：模糊与清晰的交界不产生黑白阶跃", () =>
        {
            const int width = 1280;
            const int height = 80;
            var source = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var offset = (y * width + x) * 4;
                    source[offset] = source[offset + 1] = source[offset + 2] = (byte)(x == 0 ? 0 : 255);
                    source[offset + 3] = (byte)((x * 59 + y * 23) % 256);
                }
            var original = source.ToArray();
            var result = CarouselBackdrop.ExtendLeft(source, width, height);
            for (var y = 0; y < height; y++)
            {
                var row = y * result.Width * 4;
                Assert.True(Math.Abs(result.Pixels[row + 191 * 4] - result.Pixels[row + 192 * 4]) < 20, "扩边与原图之间不能硬切");
                Assert.True(Math.Abs(result.Pixels[row + 192 * 4] - result.Pixels[row + 193 * 4]) < 25, "硬切不能搬到原图第二列");
                Assert.True(source.AsSpan((y * width + 102) * 4, (width - 102) * 4)
                    .SequenceEqual(result.Pixels.AsSpan((y * result.Width + 294) * 4, (width - 102) * 4)));
                for (var x = 0; x < result.Width; x++)
                    Assert.Equal(source[(y * width + Math.Max(0, x - 192)) * 4 + 3], result.Pixels[(y * result.Width + x) * 4 + 3]);
            }
            Assert.True(source.SequenceEqual(original));
        });
        Test("轮播：过渡中段恢复细节，尾端自然回到原图", () =>
        {
            const int width = 1280;
            const int height = 80;
            var source = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var offset = (y * width + x) * 4;
                    source[offset] = source[offset + 1] = source[offset + 2] = (byte)(y % 2 == 0 ? 0 : 255);
                    source[offset + 3] = 255;
                }
            var result = CarouselBackdrop.ExtendLeft(source, width, height);
            for (var y = 26; y < 52; y++)
            {
                var row = y * result.Width * 4;
                var edge = source[y * width * 4];
                Assert.True(Math.Abs(result.Pixels[row + 191 * 4] - result.Pixels[row + 192 * 4]) < 8);
                var middleDifference = Math.Abs(result.Pixels[row + 243 * 4] - edge);
                Assert.True(middleDifference is > 30 and < 90, "过渡中点不能全清晰或全模糊");
                Assert.True(Math.Abs(result.Pixels[row + 284 * 4] - edge) <= 2, "过渡末尾不能留下线性衰减的收口");
            }
        });
        Test("轮播：预取消的扩边不会继续处理", () =>
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => CarouselBackdrop.ExtendLeft([1, 2, 3, 4], 1, 1, cancellation.Token));
        });
    }

    private static void RegisterSlides()
    {
        Test("轮播：按最近添加的次序，头十个上", () =>
        {
            // 「轮播图改用前十个最近添加」：来的次序就是上的次序，第十张之后的轮不上 —— 继续观看从
            // 2026-09-13 起不再参加，这个函数也就只剩最近添加一个参数。
            var latest = Enumerable.Range(0, 14).Select(index => Wide($"l{index}")).ToList();
            var slides = HomeCarousel.Slides(latest);

            Assert.Equal(HomeCarousel.Slots, slides.Count);
            Assert.Equal("l0", slides[0].Id);
            Assert.Equal("l9", slides[^1].Id);
            Assert.False(slides.Any(item => item.Id == "l10"));
        });

        Test("轮播：一个剧集只占一张幻灯片", () =>
        {
            // 服务器那头的 GroupItems 已经把同一部剧的最新单集并成了一条，这里这条是保险：一个剧集四张
            // 幻灯片站在同一张背景图上、挂着同一个名字，读起来就是一条停住了的轮播。「前十个」因此是
            // 筛完之后的前十个。
            var slides = HomeCarousel.Slides(
                [Episode("e1", "s1"), Episode("e2", "s1"), Episode("e3", "s2"), Wide("m1")]);

            Assert.Equal(3, slides.Count);
            Assert.Equal("e1", slides[0].Id);
            Assert.Equal("e3", slides[1].Id);
            Assert.Equal("m1", slides[2].Id);
        });

        Test("轮播：没有宽图的条目上不了轮播", () =>
        {
            var poster = new EmbyItem { Id = "p1", Name = "只有海报", Type = EmbyItemType.Movie };
            poster.ImageTags["Primary"] = "p";

            var slides = HomeCarousel.Slides([poster, Wide("l2")]);

            Assert.Equal(1, slides.Count);
            Assert.Equal("l2", slides[0].Id);
        });

        Test("轮播：只有艺术图的条目也不上", () =>
        {
            // 艺术图是透明片名图，铺满整条带就是黑底加一行巨型片名（2026-11-18 用户报的那一张）。它归
            // 详情页右上角；轮播只要背景图和缩略图这两种像画面的宽图。
            var art = new EmbyItem { Id = "art1", Name = "只有艺术图", Type = EmbyItemType.Movie };
            art.ImageTags[EmbyImageStore.Art] = "a";

            var slides = HomeCarousel.Slides([art, Wide("l3")]);

            Assert.Equal(1, slides.Count);
            Assert.Equal("l3", slides[0].Id);
        });

        Test("轮播：借来的剧集背景图算宽图", () =>
        {
            var episode = Episode("e5", "s3", backdrop: false);
            episode.ParentBackdropItemId = "s3";
            episode.ParentBackdropImageTags.Add("parentbd");

            Assert.Equal(1, HomeCarousel.Slides([episode]).Count);
            Assert.Equal(0, HomeCarousel.Slides([Episode("e6", "s4", backdrop: false)]).Count);
        });

        Test("轮播：最多就那几张，多的不要", () =>
        {
            var many = Enumerable.Range(0, 30).Select(index => Wide($"m{index}")).ToList();

            Assert.Equal(HomeCarousel.Slots, HomeCarousel.Slides(many).Count);
            Assert.Equal(3, HomeCarousel.Slides(many, slots: 3).Count);

            // 0 张的余量就是不要轮播，而不是「有几张算几张」。
            Assert.Equal(0, HomeCarousel.Slides(many, slots: 0).Count);
        });

        Test("轮播：最近添加空了就没有轮播", () =>
            Assert.Equal(0, HomeCarousel.Slides([]).Count));
    }

    private static void RegisterStep()
    {
        Test("轮播：翻到头再翻回第一张", () =>
        {
            Assert.Equal(1, HomeCarousel.Step(0, 1, 3));
            Assert.Equal(0, HomeCarousel.Step(2, 1, 3));
            Assert.Equal(2, HomeCarousel.Step(0, -1, 3));
            Assert.Equal(1, HomeCarousel.Step(2, -1, 3));
        });

        Test("轮播：一张和一张都没有时按哪边都不动", () =>
        {
            Assert.Equal(0, HomeCarousel.Step(0, 1, 1));
            Assert.Equal(0, HomeCarousel.Step(0, -1, 1));

            // 数目为零时返回 0 而不是负数：这个数会被当成下标用。
            Assert.Equal(0, HomeCarousel.Step(0, 1, 0));
            Assert.Equal(0, HomeCarousel.Step(3, -1, 0));
        });

        Test("轮播：摘掉一张之后台上该是谁", () =>
        {
            // 摘在台上那张前面：台上那张跟着往前挪一格，人不变。
            Assert.Equal(1, HomeCarousel.AfterDrop(0, 2, 5));
            Assert.Equal(2, HomeCarousel.AfterDrop(2, 3, 5));

            // 摘在台下那张后面：台上那张原地不动。
            Assert.Equal(2, HomeCarousel.AfterDrop(3, 2, 5));
            Assert.Equal(2, HomeCarousel.AfterDrop(4, 2, 5));

            // 摘的正是台上那张：原位置由后面那张补上；原来最后一张被摘掉就回到第一张。
            Assert.Equal(2, HomeCarousel.AfterDrop(2, 2, 5));
            Assert.Equal(0, HomeCarousel.AfterDrop(4, 4, 5));

            // 只剩一张：摘完就是「没有台上那张」，0 不会越界。
            Assert.Equal(0, HomeCarousel.AfterDrop(0, 0, 1));
            Assert.Equal(0, HomeCarousel.AfterDrop(0, 0, 0));
        });
    }

    private static void RegisterHeight()
    {
        Test("轮播：封面扩展到原快捷入口区域，仍按整幅宽度换算高度", () =>
        {
            Assert.Equal(455d, HomeCarousel.Height(2000, 1064));
            Assert.Equal(470d, HomeCarousel.Height(2000, 1100));
            Assert.Equal(821d, HomeCarousel.Height(2000, 1920));
            Assert.Equal(0.76d, HomeCarousel.BandHeightShare);
            Assert.Equal(HomeCarousel.UnmeasuredHeight, HomeCarousel.Height(800, 0));
            Assert.Equal(HomeCarousel.UnmeasuredHeight, HomeCarousel.Height(800, -100));
            Assert.Equal(608d, HomeCarousel.UnmeasuredHeight);
            Assert.Equal(HomeCarousel.MinHeight, HomeCarousel.Height(2000, 300));
            Assert.Equal(HomeCarousel.MinHeight, HomeCarousel.Height(2000, 640));
        });

        Test("轮播：一屏是上限，超出去带子就不再长高", () =>
        {
            Assert.Equal(800d, HomeCarousel.Height(800, 3111));
            Assert.Equal(470d, HomeCarousel.Height(700, 1100));
            Assert.Equal(371d, HomeCarousel.Height(371, 1100));
            Assert.Equal(360d, HomeCarousel.Height(360, 1100));
            Assert.Equal(470d, HomeCarousel.Height(0, 1100));
            Assert.Equal(HomeCarousel.MinHeight, HomeCarousel.Height(100, 1100));
        });

        Test("轮播：改变宽度时封面保持同一形状，图片不变形", () =>
        {
            var shape = HomeCarousel.WindowAspect / HomeCarousel.BandHeightShare;
            for (var width = 760d; width <= 3600; width += 20)
            {
                var band = HomeCarousel.Height(2400, width);
                Assert.Equal(Math.Round(width * HomeCarousel.BandHeightShare / HomeCarousel.WindowAspect), band);
                Assert.True(Math.Abs(width / band - shape) < 0.01,
                    $"带宽 {width} 时带子的形状是 {width / band:0.000}:1，不是 {shape:0.000}:1");
            }
        });

        Test("轮播：开窗那一档的页面正好是 16:9", () =>
        {
            Assert.Equal(16d / 9, HomeCarousel.WindowAspect);
            Assert.Equal("16:9", HomeCarousel.WindowAspectLabel);
            Assert.Equal(1422d, Math.Round(HomeCarousel.WindowAspect * 800));
            Assert.Equal(608d, HomeCarousel.Height(800, 1422));
        });
    }

    private static void RegisterText()
    {
        Test("轮播：单集报到集，电影报年份", () =>
        {
            var episode = Episode("e7", "s5");
            episode.ParentIndexNumber = 1;
            episode.IndexNumber = 2;
            episode.Name = "旅途的终点";
            episode.ProductionYear = 2016;

            Assert.Equal("S1:E2 - 旅途的终点  ·  2016", HomeCarousel.Caption(episode));

            var film = Wide("f1");
            film.ProductionYear = 1999;
            Assert.Equal("1999", HomeCarousel.Caption(film));
        });

        Test("轮播：什么都不知道的条目就不写那行", () =>
            Assert.Equal("", HomeCarousel.Caption(Wide("f2"))));

        Test("轮播：简介只去空白，截几行是带的事", () =>
        {
            var item = Wide("f3");
            item.Overview = "  一句话。  ";
            Assert.Equal("一句话。", HomeCarousel.Synopsis(item));
            Assert.Equal("", HomeCarousel.Synopsis(Wide("f4")));
        });

        Test("轮播：位置从 1 数起", () =>
        {
            Assert.Equal("第 1 张，共 8 张", HomeCarousel.Position(0, 8));
            Assert.Equal("第 3 张，共 8 张", HomeCarousel.Position(2, 8));
        });
    }

    /// <summary>A film with a backdrop of its own — the plainest thing that qualifies as a slide.</summary>
    private static EmbyItem Wide(string id)
    {
        var item = new EmbyItem { Id = id, Name = id, Type = EmbyItemType.Movie };
        item.BackdropImageTags.Add("bd");
        return item;
    }

    private static EmbyItem Episode(string id, string seriesId, bool backdrop = true)
    {
        var item = new EmbyItem
        {
            Id = id,
            Name = id,
            Type = EmbyItemType.Episode,
            SeriesId = seriesId,
            SeriesName = seriesId
        };

        if (backdrop) item.BackdropImageTags.Add("bd");
        return item;
    }
}
