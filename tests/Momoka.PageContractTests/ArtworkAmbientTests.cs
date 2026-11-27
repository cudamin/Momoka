using Momoka.Theming;
using StoreBackdropBlend = Momoka.Shell.Views.StoreBackdropBlend;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class ArtworkAmbientTests
{
    internal static void Register()
    {
        Test("视频底层：视口内实色匹配，外侧连续透回Mica，深浅色均预乘", () =>
        {
            foreach (var window in new[] { UiThemes.System(true).Colors.Window, UiThemes.System(false).Colors.Window })
            {
                const double page = 1240, width = 744, height = 418.5, gap = 16;
                var left = page - gap - width;
                foreach (var x in new[] { left, left + 100, page - gap })
                    foreach (var y in new[] { 0d, height / 2, height })
                        Assert.Equal(1d, Momoka.Shell.Views.StoreBackdropBlend.FoundationAlpha(x, y, page, width, height, gap));
                Assert.Equal(0d, Momoka.Shell.Views.StoreBackdropBlend.FoundationAlpha(0, 0, page, width, height, gap));
                Assert.Equal(0d, Momoka.Shell.Views.StoreBackdropBlend.FoundationAlpha(page, 0, page, width, height, gap));
                Assert.Equal(0d, Momoka.Shell.Views.StoreBackdropBlend.FoundationAlpha(left, height + 120, page, width, height, gap));
                var mid = Momoka.Shell.Views.StoreBackdropBlend.FoundationAlpha(left / 2, 0, page, width, height, gap);
                Assert.True(mid is > 0.3 and < 0.8, "左外侧没有平滑退到窗口基底");
                Assert.True(Momoka.Shell.Views.StoreBackdropBlend.FoundationAlpha(left, height + 60, page, width, height, gap) is > 0.3 and < 0.8);
                var pixels = Momoka.Shell.Views.StoreBackdropBlend.Foundation(window, page, width, height, gap);
                Assert.Equal(512 * 256 * 4, pixels.Length);
                for (var at = 0; at < pixels.Length; at += 4)
                    Assert.True(pixels[at] <= pixels[at + 3] && pixels[at + 1] <= pixels[at + 3] && pixels[at + 2] <= pixels[at + 3]);
                var row = 30 * 512 * 4;
                Assert.Equal((byte)255, pixels[row + 350 * 4 + 3]);
                Assert.Equal(window.R, pixels[row + 350 * 4 + 2]);
                for (var x = 1; x < 180; x++)
                    Assert.True(Math.Abs(pixels[row + x * 4 + 3] - pixels[row + (x - 1) * 4 + 3]) <= 4, "左侧出现硬色阶");
            }
            Assert.True(Momoka.Shell.Views.StoreBackdropBlend.Foundation(UiThemes.Default.Colors.Window,
                double.NaN, 744, 418.5, 16).All(channel => channel == 0));
        });
        Test("环境探针：连续红光团保留相对色差，拒绝单色和调反的颜色", () =>
        {
            var pixels = new byte[40 * 24 * 4];
            for (var y = 0; y < 24; y++)
                for (var x = 0; x < 40; x++)
                {
                    var dx = (x / 40d - 0.72) / 0.25;
                    var dy = (y / 24d - 0.36) / 0.5;
                    var glow = Math.Exp(-0.5 * (dx * dx + dy * dy));
                    var at = (y * 40 + x) * 4;
                    pixels[at] = (byte)Math.Round(242 - 136 * glow);
                    pixels[at + 1] = (byte)Math.Round(191 - 107 * glow);
                    pixels[at + 2] = (byte)Math.Round(66 + 181 * glow);
                    pixels[at + 3] = 255;
                }
            var output = ArtworkAmbient.Render(pixels, 40, 24, UiThemes.Default.Colors.Window, true);
            Momoka.Shell.StoreDetailProbeEvidence.RequireAmbient(output, ArtworkAmbient.Width, ArtworkAmbient.Height);
            var flat = output.ToArray();
            for (var at = 0; at < flat.Length; at += 4) { flat[at] = 70; flat[at + 2] = 20; }
            Assert.Throws<InvalidOperationException>(() => Momoka.Shell.StoreDetailProbeEvidence.RequireAmbient(flat, ArtworkAmbient.Width, ArtworkAmbient.Height));
            var flipped = output.ToArray();
            for (var at = 0; at < flipped.Length; at += 4) (flipped[at], flipped[at + 2]) = (flipped[at + 2], flipped[at]);
            Assert.Throws<InvalidOperationException>(() => Momoka.Shell.StoreDetailProbeEvidence.RequireAmbient(flipped, ArtworkAmbient.Width, ArtworkAmbient.Height));
            Assert.True(Momoka.Shell.StoreDetailProbeEvidence.PageSizeMatches(1200, 1200, 1200, 1200, 1200));
            Assert.False(Momoka.Shell.StoreDetailProbeEvidence.PageSizeMatches(1200, 1240, 1200, 1200, 1200));
            Assert.False(Momoka.Shell.StoreDetailProbeEvidence.PageSizeMatches(1200, 1200, 1200, 1240, 1200));
        });
        Test("视频渐变：端点衔接环境底，中部清晰，RGB预乘透明度且主题映射生效", () =>
        {
            var ambient = ArtworkAmbient.Render(Split(), 40, 24, UiThemes.Default.Colors.Window, true);
            var window = UiThemes.Default.Colors.Window;
            // 淡墨那一格取 520 高（与 StoreAmbientHeight／Viewport 里大的那个同义）。
            var pixels = Momoka.Shell.Views.StoreBackdropBlend.Video(ambient, window, 1240, 520, 744, 418.5, 16, 1240, 520);
            var side = Momoka.Shell.Views.StoreBackdropBlend.Side;
            int At(int x, int y) => (y * side + x) * 4;
            Assert.Equal((byte)255, pixels[At(0, 30) + 3]);
            Assert.Equal((byte)255, pixels[At(side - 1, 30) + 3]);
            Assert.Equal((byte)255, pixels[At(60, side - 1) + 3]);
            Assert.True(pixels[At(75, 20) + 3] < 5, "清晰区仍被整块染暗");
            Assert.True(pixels[At(0, 30) + 2] > window.R + 10, "左边端点误用了窗口色，丢掉环境红光");
            for (var at = 0; at < pixels.Length; at += 4)
                Assert.True(pixels[at] <= pixels[at + 3] && pixels[at + 1] <= pixels[at + 3] && pixels[at + 2] <= pixels[at + 3]);
            var lightWindow = UiThemes.System(false).Colors.Window;
            var light = Momoka.Shell.Views.StoreBackdropBlend.Video(null, lightWindow, 1240, 520, 744, 418.5, 16, 1240, 520);
            // 没有环境底时端点就是窗口色，再叠上淡墨（淡墨压在视频下面，叠图自己得补上）。
            var lightInk = lightWindow.Mix(Momoka.Shell.Views.StoreBackdropBlend.ShadeInk,
                Momoka.Shell.Views.StoreBackdropBlend.Shade(1240 - 16 - 744, 30 / 127d * 418.5, 1240, 520));
            Assert.Equal(lightInk.B, light[At(0, 30)]);
            Assert.Equal(lightInk.R, light[At(0, 30) + 2]);
            var mask = Momoka.Shell.Views.StoreBackdropBlend.Mask(ambient: false);
            for (var at = 3; at < mask.Length; at += 4) Assert.Equal(255, mask[at] + pixels[at]);
            var taller = Momoka.Shell.Views.StoreBackdropBlend.Video(ambient, window, 1240, 800, 744, 418.5, 16, 1240, 520);
            Assert.False(taller.AsSpan().SequenceEqual(pixels), "环境底晚到高度未改变对应颜色");
        });
        Test("视频渐变：边沿补上压在下面的淡墨，端点色接得住环境底", () =>
        {
            var ambient = ArtworkAmbient.Render(Split(), 40, 24, UiThemes.Default.Colors.Window, true);
            var window = UiThemes.Default.Colors.Window;
            const double page = 1240, ambientHeight = 520, videoWidth = 744, videoHeight = 418.5, gap = 16, shadeHeight = 640;
            const double left = page - gap - videoWidth;
            var shaded = Momoka.Shell.Views.StoreBackdropBlend.Video(ambient, window, page, ambientHeight,
                videoWidth, videoHeight, gap, page, shadeHeight);
            var plain = Momoka.Shell.Views.StoreBackdropBlend.Video(ambient, window, page, ambientHeight,
                videoWidth, videoHeight, gap, 0, 0);
            var side = Momoka.Shell.Views.StoreBackdropBlend.Side;
            int At(int x, int y) => (y * side + x) * 4;
            // 左沿与底边是不透明端点：淡墨不在视频上，这两处必须比不补淡墨的那版暗，中部（透明区）一个色阶都不动。
            foreach (var (x, y) in new[] { (0, 20), (0, 60), (0, side - 1) })
                Assert.True(shaded[At(x, y) + 2] < plain[At(x, y) + 2],
                    $"视频边沿没有补上淡墨（{x},{y}）：{shaded[At(x, y) + 2]} 对 {plain[At(x, y) + 2]}");
            Assert.Equal(plain[At(75, 20) + 2], shaded[At(75, 20) + 2]);
            // 端点色 == 窗口底 ← 环境底 ← 淡墨 叠出来的那个值：差一个色阶，屏上就是一道竖线。
            ThemeColor Background(double ax, double ay)
            {
                var at = ((int)Math.Round(ay * (ArtworkAmbient.Height - 1)) * ArtworkAmbient.Width
                    + (int)Math.Round(ax * (ArtworkAmbient.Width - 1))) * 4;
                var baseline = window.Mix(ThemeColor.Rgb(ambient[at + 2], ambient[at + 1], ambient[at]),
                    Momoka.Shell.Views.StoreBackdropBlend.Alpha(ax, ay, ambient: true));
                return baseline.Mix(Momoka.Shell.Views.StoreBackdropBlend.ShadeInk,
                    Momoka.Shell.Views.StoreBackdropBlend.Shade(ax * page, ay * ambientHeight, page, shadeHeight));
            }
            foreach (var (x, y) in new[] { (0, 20), (0, 60), (side - 1, 60), (60, side - 1) })
            {
                var ax = (left + x / (double)(side - 1) * videoWidth) / page;
                var ay = y / (double)(side - 1) * videoHeight / ambientHeight;
                var expect = Background(ax, ay);
                Assert.True(Math.Abs(shaded[At(x, y)] - expect.B) <= 1 && Math.Abs(shaded[At(x, y) + 1] - expect.G) <= 1
                    && Math.Abs(shaded[At(x, y) + 2] - expect.R) <= 1,
                    $"视频端点与环境底对不上（{x},{y}）：{shaded[At(x, y) + 2]},{shaded[At(x, y) + 1]},{shaded[At(x, y)]}"
                    + $" 对 {expect.R},{expect.G},{expect.B}");
            }
            // 淡墨自己：左上角最浓，往下散尽，两道叠起来不会超过 1，尺寸没量到时按「没有淡墨」处理。
            var blend = Momoka.Shell.Views.StoreBackdropBlend.Shade;
            Assert.Equal(0d, blend(0, shadeHeight, page, shadeHeight));
            // 两道同色叠起来：1-(1-横)(1-竖)，顶边那一点不能只算其中一道。
            Assert.True(Math.Abs(blend(0.3 * page, 0, page, shadeHeight)
                - (1 - (1 - (0x24 / 255d)) * (1 - (0x30 / 255d)))) < 1e-9, "顶边没有把两道淡墨叠起来");
            Assert.True(Math.Abs(blend(0.3 * page, shadeHeight, page, shadeHeight) - (0x24 / 255d)) < 1e-9,
                "底边没有退到只剩横向那道");
            Assert.True(blend(0.3 * page, 0, page, shadeHeight) > blend(0.3 * page, 0.35 * shadeHeight, page, shadeHeight)
                && blend(0.3 * page, 0.35 * shadeHeight, page, shadeHeight) > blend(0.3 * page, shadeHeight, page, shadeHeight),
                "淡墨没有从上往下散尽");
            Assert.True(blend(0.3 * page, 0, page, shadeHeight) < 1, "淡墨叠成了实色");
            Assert.Equal(0d, blend(0.3 * page, 0, 0, shadeHeight));
            Assert.Equal(0d, blend(double.NaN, 0, page, shadeHeight));
        });
        Test("视频渐变：深色主题按浓墨那份补端点色，右侧与底边仍与浅色同值", () =>
        {
            // 2026-11-27 用户令「把详情页背景图左侧改成深色的，浅色主题时候保持浅色」：深色下压在视频下面的
            // 那道浓得多，叠图的左沿端点得按它补；右侧与底边在浓墨散尽之后，两档一个色阶都不差。
            var ambient = ArtworkAmbient.Render(Split(), 40, 24, UiThemes.Default.Colors.Window, true);
            var window = UiThemes.Default.Colors.Window;
            const double page = 1240, ambientHeight = 520, videoWidth = 744, videoHeight = 418.5, gap = 16, shadeHeight = 640;
            const double left = page - gap - videoWidth;
            var side = StoreBackdropBlend.Side;
            int At(int x, int y) => (y * side + x) * 4;
            var deep = StoreBackdropBlend.Video(ambient, window, page, ambientHeight, videoWidth, videoHeight, gap, page, shadeHeight, dark: true);
            var pale = StoreBackdropBlend.Video(ambient, window, page, ambientHeight, videoWidth, videoHeight, gap, page, shadeHeight);
            Assert.True(deep[At(0, 60)] < pale[At(0, 60)], "深色下视频左沿仍按浅色淡墨补色");
            Assert.Equal(pale[At(side - 1, 60)], deep[At(side - 1, 60)]);
            Assert.Equal(pale[At(side - 1, 60) + 2], deep[At(side - 1, 60) + 2]);
            Assert.Equal(pale[At(60, side - 1)], deep[At(60, side - 1)]);
            Assert.Equal(pale[At(75, 20) + 3], deep[At(75, 20) + 3]);
            // 端点色 == 窗口底 ← 环境底 ← 浓墨（左列末色 0xA6 与竖向那道叠起来）：差一个色阶，屏上就是一道竖线。
            ThemeColor Background(double leftColumn, double ax, double ay, double shadeX, double shadeY)
            {
                var at = ((int)Math.Round(ay * (ArtworkAmbient.Height - 1)) * ArtworkAmbient.Width
                    + (int)Math.Round(ax * (ArtworkAmbient.Width - 1))) * 4;
                return window.Mix(ThemeColor.Rgb(ambient[at + 2], ambient[at + 1], ambient[at]),
                    StoreBackdropBlend.Alpha(ax, ay, ambient: true)).Mix(StoreBackdropBlend.ShadeInk,
                    StoreBackdropBlend.ShadeDark(shadeX, shadeY, leftColumn, StoreBackdropBlend.PictureFade * videoWidth, shadeHeight));
            }
            foreach (var (x, y) in new[] { (0, 20), (0, 60), (0, side - 1) })
            {
                var ax = (left + x / (double)(side - 1) * videoWidth) / page;
                var ay = y / (double)(side - 1) * videoHeight / ambientHeight;
                var expect = Background(left, ax, ay, left + x / (double)(side - 1) * videoWidth, y / (double)(side - 1) * videoHeight);
                Assert.True(Math.Abs(deep[At(x, y)] - expect.B) <= 1 && Math.Abs(deep[At(x, y) + 1] - expect.G) <= 1
                    && Math.Abs(deep[At(x, y) + 2] - expect.R) <= 1,
                    $"深色视频端点与环境底对不上（{x},{y}）：{deep[At(x, y) + 2]},{deep[At(x, y) + 1]},{deep[At(x, y)]}"
                    + $" 对 {expect.R},{expect.G},{expect.B}");
            }
            for (var at = 0; at < deep.Length; at += 4)
                Assert.True(deep[at] <= deep[at + 3] && deep[at + 1] <= deep[at + 3] && deep[at + 2] <= deep[at + 3], "深色叠图没有预乘");
            // 窄窗：视频铺满整幅、没有左列，浓墨从渐隐带起点直接起坡，左沿端点同样接得住，且仍比浅色那份暗。
            const double narrowPage = 760;
            var narrowDeep = StoreBackdropBlend.Video(ambient, window, narrowPage, ambientHeight, videoWidth, videoHeight, gap, narrowPage, shadeHeight, dark: true);
            var narrowPale = StoreBackdropBlend.Video(ambient, window, narrowPage, ambientHeight, videoWidth, videoHeight, gap, narrowPage, shadeHeight);
            var narrowExpect = Background(0, 0, 60 / 127d * videoHeight / ambientHeight, 0, 60 / 127d * videoHeight);
            Assert.True(narrowDeep[At(0, 60)] < narrowPale[At(0, 60)], "窄窗深色左沿没有比浅色暗");
            Assert.True(Math.Abs(narrowDeep[At(0, 60)] - narrowExpect.B) <= 1 && Math.Abs(narrowDeep[At(0, 60) + 2] - narrowExpect.R) <= 1,
                "窄窗深色视频端点与环境底对不上");
        });
        Test("环境渐变：保留左右红蓝色区，过渡连续且原像素不变", () =>
        {
            var source = Split();
            var before = source.ToArray();
            var output = ArtworkAmbient.Render(source, 40, 24, UiThemes.Default.Colors.Window, true);
            var left = Read(output, 8, 30);
            var right = Read(output, 87, 30);
            Assert.True(left.B > left.R + 25 && right.R > right.B + 25, "红蓝两区又被平均成单色");
            Assert.True(before.AsSpan().SequenceEqual(source), "生成环境底改了原剧照");
            for (var y = 0; y < ArtworkAmbient.Height; y++)
                for (var x = 1; x < ArtworkAmbient.Width; x++)
                {
                    var a = Read(output, x - 1, y);
                    var b = Read(output, x, y);
                    Assert.True(Math.Abs(a.R - b.R) <= 4 && Math.Abs(a.B - b.B) <= 4, "颜色过渡出现硬边");
                    Assert.Equal((byte)255, b.A);
                }
        });
        Test("环境渐变：深浅各主题的正文与副行在所有颜色上可读", () =>
        {
            foreach (var theme in UiThemes.All.Where(theme => theme.Id != UiThemes.SystemId)
                .Concat([UiThemes.System(true), UiThemes.System(false)]))
            {
                var output = ArtworkAmbient.Render(Split(), 40, 24, theme.Colors.Window, theme.IsDark);
                var ink = theme.IsDark ? ThemeColor.Parse("#C9D0D9") : theme.Colors.Text.Mix(theme.Colors.Window, 0.14);
                for (var y = 0; y < ArtworkAmbient.Height; y++)
                    for (var x = 0; x < ArtworkAmbient.Width; x++)
                        Assert.True(ThemeColor.Contrast(ink, Read(output, x, y)) >= 4.5, "文字对比不足：" + theme.Name);
            }
        });
        Test("环境渐变：坏图、黑图和透明图使用本次主题，单像素不引入空色块", () =>
        {
            foreach (var theme in new[] { UiThemes.System(true), UiThemes.System(false) })
                foreach (var (pixels, width, height) in new (byte[], int, int)[]
                { ([], 2, 2), ([], -1, 2), ([], int.MaxValue, int.MaxValue), (new byte[4], 1, 1), ([255, 100, 20, 0], 1, 1) })
                {
                    var output = ArtworkAmbient.Render(pixels, width, height, theme.Colors.Window, theme.IsDark);
                    Assert.Equal(ArtworkAmbient.Width * ArtworkAmbient.Height * 4, output.Length);
                    foreach (var x in new[] { 0, 48, 95 }) Assert.Equal(theme.Colors.Window, Read(output, x, 30));
                }
            var solid = ArtworkAmbient.Render([30, 90, 200, 255], 1, 1, UiThemes.Default.Colors.Window, true);
            Assert.Equal(Read(solid, 0, 0), Read(solid, 95, 63));
        });
        Test("环境渐变缓存：同图复用，主题与图片变化失效，清理后不能复用旧帧", () =>
        {
            var cache = new Momoka.Shell.Media.StoreAmbientPixels();
            var pixels = new Momoka.Shell.Media.HeroPictureLoader.HeroPixels(Split(), 40, 24);
            var dark = cache.Resolve(pixels, UiThemes.System(true));
            Assert.True(ReferenceEquals(dark, cache.Resolve(pixels, UiThemes.System(true))), "同图同主题重新生成");
            var light = cache.Resolve(pixels, UiThemes.System(false));
            Assert.False(ReferenceEquals(dark, light), "浅色复用了深色帧");
            Assert.True(Read(light, 30, 30).Luminance > Read(dark, 30, 30).Luminance, "同图换主题未调亮");
            var changed = cache.Resolve(pixels with { Pixels = Split() }, UiThemes.System(false));
            Assert.False(ReferenceEquals(light, changed), "新图复用了旧图帧");
            cache.Clear();
            Assert.False(ReferenceEquals(changed, cache.Resolve(pixels, UiThemes.System(false))), "清理后保留了旧帧");
            foreach (var field in typeof(Momoka.Shell.Media.StoreAmbientPixels).GetFields(
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
            {
                cache.Clear();
                Assert.Null(field.GetValue(cache));
            }
        });
    }

    private static byte[] Split()
    {
        var pixels = new byte[40 * 24 * 4];
        for (var y = 0; y < 24; y++)
            for (var x = 0; x < 40; x++)
            {
                var at = (y * 40 + x) * 4;
                pixels[at] = x < 20 ? (byte)230 : (byte)30;
                pixels[at + 1] = 70;
                pixels[at + 2] = x < 20 ? (byte)30 : (byte)230;
                pixels[at + 3] = 255;
            }
        return pixels;
    }

    private static ThemeColor Read(byte[] pixels, int x, int y)
    {
        var at = (y * ArtworkAmbient.Width + x) * 4;
        return new(pixels[at + 3], pixels[at + 2], pixels[at + 1], pixels[at]);
    }
}
