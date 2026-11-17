using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class DetailLayoutTests
{
    public static void Register()
    {
        StoreDetailTailProbeEvidenceTests.Register();
        Test("商店内容留白：宽窄断点和小窗回退", () =>
        {
            foreach (var width in new[] { double.NaN, 0, 360, 599 }) Assert.Equal(32d, DetailHero.StorePageInset(width));
            foreach (var width in new[] { 600d, 760, 1023 }) Assert.Equal(56d, DetailHero.StorePageInset(width));
            foreach (var width in new[] { DetailHero.StoreWideFloor, 1024d, 1072, 1240, 1840, 2560 }) Assert.Equal(96d, DetailHero.StorePageInset(width));
            Assert.Equal(1024d, DetailHero.StoreWideFloor);
        });
        Test("商店头部上留白：窄档（亚克力）与左右留白相等，宽版式仍是 StoreHeroTop", () =>
        {
            // 2026-11-17 用户令「窄窗口出现亚克力背景时候组件整体向上移一点，左边跟上面的间距要一致」。
            foreach (var width in new[] { 360d, 599, 600, 760, 1023 })
            {
                Assert.Equal(DetailHero.StorePageInset(width), DetailHero.StoreHeroInsetTop(width));
                Assert.True(DetailHero.StoreHeroInsetTop(width) < DetailHero.StoreHeroTop,
                    "窄档上留白没有收到左右留白那一档");
            }
            foreach (var width in new[] { double.NaN, 0, DetailHero.StoreWideFloor, 1200d, 2400 })
                Assert.Equal(DetailHero.StoreHeroTop, DetailHero.StoreHeroInsetTop(width));
        });
        Test("商店片名栏：让位放不下时撑到保底宽度并压在剧照上，窗口再窄只收到页边", () =>
        {
            Assert.Equal(300d, DetailHero.StoreTextFloor);
            Assert.Equal(180d, DetailHero.StoreCoverColumn);
            static double Content(double width) => Math.Max(0, width - (2 * DetailHero.StorePageInset(width))
                - (width >= DetailHero.StoreWideFloor ? Math.Min(640, width * 0.46) + 28 : 0));

            // 1240：让位之后片名栏只剩两百四十来像素，撑到 300；头图那一行跟着长到 502，多出来的压在右侧剧照上。
            Assert.Equal(300d, DetailHero.StoreTextColumn(1240, Content(1240), 180, 22));
            Assert.Equal(502d, DetailHero.StoreHeroColumn(1240, Content(1240), 180, 22));
            // 2400：右侧剧照封顶 640 之后放得下，片名栏不再撑到保底，头图那一行仍是让位后的内容宽。
            Assert.Equal(1338d, DetailHero.StoreTextColumn(2400, Content(2400), 180, 22));
            Assert.Equal(1540d, DetailHero.StoreHeroColumn(2400, Content(2400), 180, 22));
            // 1024：断点刚过线时内容宽只剩三百出头，保底宽度顶出去 —— 但不超过整幅内容宽。
            Assert.Equal(300d, DetailHero.StoreTextColumn(1024, Content(1024), 180, 22));
            Assert.Equal(502d, DetailHero.StoreHeroColumn(1024, Content(1024), 180, 22));
            // 窄档本来就把整幅宽度给内容：一个数都不动。
            Assert.Equal(446d, DetailHero.StoreTextColumn(760, Content(760), 180, 22));
            Assert.Equal(648d, DetailHero.StoreHeroColumn(760, Content(760), 180, 22));
            // 更窄时连保底都放不下：收到页边为止，不顶出页面。
            Assert.Equal(94d, DetailHero.StoreTextColumn(360, Content(360), 180, 22));
            Assert.Equal(296d, DetailHero.StoreHeroColumn(360, Content(360), 180, 22));
        });
        Test("商店预告背景：只占顶部右侧，保持16:9且异常尺寸回退", () =>
        {
            Assert.Equal(768d, TrailerPreview.BackgroundWidth(1280));
            Assert.Equal(432d, TrailerPreview.BackgroundHeight(1280));
            Assert.Equal(920d, TrailerPreview.BackgroundWidth(2400));
            Assert.Equal(517.5d, TrailerPreview.BackgroundHeight(2400));
            foreach (var width in new[] { 360d, 760d, 1280d, 2400d })
            {
                Assert.True(TrailerPreview.BackgroundWidth(width) < width);
                Assert.Equal(9d / 16, TrailerPreview.BackgroundHeight(width) / TrailerPreview.BackgroundWidth(width));
            }
            foreach (var width in new[] { 0d, -1, double.NaN, double.PositiveInfinity })
                Assert.Equal(432d, TrailerPreview.BackgroundHeight(width));
        });
        Test("商店预告网页：拒绝HTML注入，关闭嵌入控件和键盘，单视频循环并保留宿主消息", () =>
        {
            var html = Momoka.Shell.Views.StoreTrailerDocument.Build("Abc_123-xyz");
            foreach (var setting in new[] { "controls=0", "disablekb=1", "fs=0", "loop=1", "playlist=Abc_123-xyz",
                "pointer-events:none", "tabindex=\"-1\"", "p.pauseVideo()", "p.playVideo()", "p.mute()", "p.unMute()",
                "p.getOption('captions','tracklist')", "p.setOption('captions','track'", "onApiChange", "postMessage('paused')" })
                Assert.Contains(setting, html);
            Assert.DoesNotContain("showinfo", html);
            foreach (var id in new[] { "short", "Abc_123-xy<", "Abc_123-xy\"" })
            {
                var rejected = false;
                try { Momoka.Shell.Views.StoreTrailerDocument.Build(id); }
                catch (ArgumentException) { rejected = true; }
                Assert.True(rejected, "预告网页接受了未验证ID");
            }
        });
        Test("商店背景：剧照与预告共用右侧视口，不随原图比例或窗口高度撑开", () =>
        {
            foreach (var width in new[] { 360d, 760d, 1200d, 2400d })
            {
                var pictureWidth = DetailHero.StorePictureWidth(width, 9d / 16);
                var height = DetailHero.StorePictureHeight(width, 9d / 16);
                Assert.True(pictureWidth < width && height <= 520);
                Assert.Equal(9d / 16, height / pictureWidth);
                Assert.Equal(TrailerPreview.BackgroundWidth(width), pictureWidth);
                Assert.Equal(TrailerPreview.BackgroundHeight(width), height);
                Assert.Equal(height, DetailHero.StorePictureHeight(width, 1.5));
            }
            Assert.Equal(432d, DetailHero.StorePictureHeight(0, 9d / 16));
            Assert.Equal(432d, DetailHero.StorePictureHeight(double.NaN, 1));
            Assert.Equal(337.5d, DetailHero.StorePictureHeight(1000, 0));
        });

        Test("商店窄窗背景：1024断点切换，亚克力区域覆盖晚到内容且不依赖原图比例", () =>
        {
            foreach (var width in new[] { 360d, 760d, 1023d })
            {
                Assert.Equal(width, DetailHero.StoreBackdropWidth(width));
                Assert.Equal(420d, DetailHero.StoreBackdropHeight(width, 270));
                Assert.Equal(700d, DetailHero.StoreBackdropHeight(width, 700));
                Assert.Equal(420d, DetailHero.StoreBackdropHeight(width, double.NaN));
            }
            foreach (var width in new[] { 1024d, 1025d, 1072d, 1100d, 1840d })
            {
                Assert.Equal(TrailerPreview.BackgroundWidth(width), DetailHero.StoreBackdropWidth(width));
                Assert.Equal(TrailerPreview.BackgroundHeight(width), DetailHero.StoreBackdropHeight(width, 700));
            }
        });

        Test("商店视口右沿：与窗口右边缘的空隙随窗口变宽而加大，画面本身不放大", () =>
        {
            // 2026-11-17 用户令「加大背景图和右边窗口边缘之间的间距，跟随窗口不断变宽」：
            // 16 是起点，从 1024 断点起每宽 1 DIP 加 0.03，封顶 96；画面尺寸仍按内容列那两支走。
            Assert.Equal(16d, DetailHero.StorePreviewEdgeGap(0));
            Assert.Equal(16d, DetailHero.StorePreviewEdgeGap(double.NaN));
            Assert.Equal(16d, DetailHero.StorePreviewEdgeGap(DetailHero.StoreWideFloor));
            Assert.True(Math.Abs(DetailHero.StorePreviewEdgeGap(1240) - 22.48) < 1e-6);
            Assert.True(Math.Abs(DetailHero.StorePreviewEdgeGap(1840) - 40.48) < 1e-6);
            Assert.True(Math.Abs(DetailHero.StorePreviewEdgeGap(2560) - 62.08) < 1e-6);
            Assert.Equal(96d, DetailHero.StorePreviewEdgeGap(6000));
            // 画面还是按内容列的 60%、封顶 920 —— 不跟着窗口放大。
            Assert.Equal(920d, DetailHero.StoreBackdropWidth(2560));
            Assert.Equal(TrailerPreview.BackgroundWidth(1240), DetailHero.StoreBackdropWidth(1240));
            // 相对内容列右沿的 margin：不超宽时就是那道缝；超宽时减掉右半边 bleed。
            Assert.True(Math.Abs(DetailHero.StorePreviewRightMargin(1240, 1240) - 22.48) < 1e-6);
            Assert.True(Math.Abs(DetailHero.StorePreviewRightMargin(2560, 1840) - (62.08 - 360)) < 1e-6);
            Assert.Equal(16d, DetailHero.StorePreviewRightMargin(0, 0));
        });

        Test("商店尾部下限：下方卡片排在剧照、预告控制与右沿空隙之后", () =>
        {
            // 内容高过画面：一个像素都不多占。
            Assert.Equal(0d, DetailHero.StoreTailHeight(418, 520, 0, 16));
            // 内容比画面矮：差额补在尾部，卡片从画面和 16 的空隙下面开始。
            Assert.Equal(34d, DetailHero.StoreTailHeight(418, 400, 0, 16));
            // 有预告片时把暂停/静音那排 72 也让出来。
            Assert.Equal(106d, DetailHero.StoreTailHeight(418, 400, 72, 16));
            // 没有画面、尺寸还没量到或坏数：不撑。
            Assert.Equal(0d, DetailHero.StoreTailHeight(0, 400, 0, 16));
            Assert.Equal(0d, DetailHero.StoreTailHeight(double.NaN, 400, 0, 16));
            // 头图比画面还高时也不会是负数。
            Assert.Equal(0d, DetailHero.StoreTailHeight(300, 900, 0, 16));
        });

        Test("商店环境底：随红蓝剧照取色、亮图收到阅读上限，坏图与纯黑回退，原像素不变", () =>
        {
            var fallback = Momoka.Theming.ThemeColor.Rgb(10, 15, 20);
            var paper = Momoka.Theming.ThemeColor.Rgb(0xF2, 0xF6, 0xFA);
            var red = new byte[] { 10, 20, 200, 255, 10, 20, 200, 255 };
            var saved = red.ToArray();
            var color = Momoka.Theming.ArtworkShade.Sample(red, 2, 1, fallback);
            Assert.True(color.R > color.B && color.R < 100);
            // 2026-11-14 用户令「渐变不能根据背景图颜色自动切换吗」：补过饱和度的底要能看出色相，
            // 同时最亮通道收到一个暗底的上限 —— 白字压在任何一张剧照取出来的底上都还得读得出来。
            Assert.True(color.R - color.B > 60);
            Assert.True(Momoka.Theming.ThemeColor.Contrast(paper, color) >= 4.5);
            Assert.True(red.SequenceEqual(saved));
            var blue = Momoka.Theming.ArtworkShade.Sample(new byte[] { 200, 20, 10, 255 }, 1, 1, fallback);
            Assert.True(blue.B > blue.R);
            var white = Momoka.Theming.ArtworkShade.Sample(Enumerable.Repeat<byte>(255, 16).ToArray(), 2, 2, fallback);
            Assert.True(Math.Max(white.R, Math.Max(white.G, white.B)) <= 92);
            Assert.True(Momoka.Theming.ThemeColor.Contrast(paper, white) >= 4.5);
            Assert.Equal(fallback, Momoka.Theming.ArtworkShade.Sample([], 2, 1, fallback));
            Assert.Equal(fallback, Momoka.Theming.ArtworkShade.Sample(new byte[4], 1, 1, fallback));
        });

        Test("商店远程预告：YouTube白名单只保留11位视频ID，拒绝伪造来源与无效ID", () =>
        {
            Assert.Equal("Abc_123-xyz", TrailerPreview.YouTubeId([new() { Url = "https://www.youtube.com/watch?v=Abc_123-xyz&feature=share" }]));
            Assert.Equal("Abc_123-xyz", TrailerPreview.YouTubeId([new() { Url = "https://youtu.be/Abc_123-xyz" }]));
            Assert.Null(TrailerPreview.YouTubeId([new() { Url = "https://youtube.com.evil.invalid/watch?v=Abc_123-xyz" }]));
            Assert.Null(TrailerPreview.YouTubeId([new() { Url = "http://youtube.com/watch?v=Abc_123-xyz" }]));
            Assert.Null(TrailerPreview.YouTubeId([new() { Url = "https://youtube.com/watch?v=short" }]));
            Assert.Null(TrailerPreview.YouTubeId([new() { Url = "https://youtube.com/watch?v=Abc_123-xyz&v=Abc_123-xyz" }]));
        });

        Test("显示模式：缺键默认商店，显式沉浸可保存导入并恢复默认", () =>
        {
            var property = typeof(UiSettings).GetProperty("EnableImmersive");
            Assert.NotNull(property, "缺少沉浸模式总开关");
            bool Enabled(UiSettings ui) => (bool)property!.GetValue(ui)!;
            var defaults = SettingsMigration.FromJson("{\"SchemaVersion\":27,\"Ui\":{}}",
                PassthroughSecretProtector.Instance);
            Assert.False(Enabled(defaults.Ui));
            var disabled = SettingsMigration.FromJson(
                "{\"SchemaVersion\":28,\"Ui\":{\"EnableImmersive\":false,\"AlwaysImmersive\":true,\"BackdropTintOpacity\":37}}",
                PassthroughSecretProtector.Instance);
            Assert.False(Enabled(disabled.Ui));
            var restored = SettingsMigration.FromJson(JsonSerializer.Serialize(disabled), PassthroughSecretProtector.Instance);
            Assert.False(Enabled(restored.Ui));
            SettingsPreferences.Apply(defaults, restored);
            Assert.False(Enabled(defaults.Ui));
            Assert.False(JsonSerializer.Serialize(defaults).Contains("AlwaysImmersive", StringComparison.Ordinal));
            Assert.Equal(37d, defaults.Ui.BackdropTintOpacity);
            SettingsPreferences.Apply(defaults, new AppSettings());
            Assert.False(Enabled(defaults.Ui));
            var immersive = SettingsMigration.FromJson(
                "{\"SchemaVersion\":28,\"Ui\":{\"EnableImmersive\":true}}", PassthroughSecretProtector.Instance);
            Assert.True(Enabled(immersive.Ui), "改默认不能覆盖已有沉浸偏好");
            Assert.True(Enabled(SettingsMigration.FromJson(JsonSerializer.Serialize(immersive), PassthroughSecretProtector.Instance).Ui));
        });

        Test("详情控件：新建与缺键默认按当前外观，显式偏好仍保留", () =>
        {
            Assert.Equal(39.25, new UiSettings().ControlBackgroundOpacity);
            Assert.Equal(39.25, UiSettings.DefaultControlBackgroundOpacity);
            var ui = SettingsMigration.FromJson("{\"SchemaVersion\":27,\"Ui\":{\"ControlBackgroundOpacity\":0}}",
                PassthroughSecretProtector.Instance).Ui;
            Assert.Equal(0d, ui.ControlBackgroundOpacity);
            foreach (var value in new[] { 25d, 37.5, 100d })
            {
                var stored = JsonSerializer.Serialize(new AppSettings { Ui = new UiSettings { ControlBackgroundOpacity = value } });
                Assert.Equal(value, SettingsMigration.FromJson(stored, PassthroughSecretProtector.Instance).Ui.ControlBackgroundOpacity);
            }
        });


        Test("详情卡片：旧配置缺键保留三个页面的默认顺序与可见性", () =>
        {
            var ui = SettingsMigration.FromJson("{\"SchemaVersion\":27,\"Ui\":{}}",
                PassthroughSecretProtector.Instance).Ui;
            Assert.Equal("episodes,info,seasons,cast,similar", Keys(DetailLayout.For(ui, EmbyItemType.Series)));
            Assert.Equal("episodes,info,cast", Keys(DetailLayout.For(ui, EmbyItemType.Episode)));
            Assert.Equal("info,cast,similar", Keys(DetailLayout.For(ui, EmbyItemType.Movie)));
            Assert.True(DetailLayout.For(ui, EmbyItemType.Series).All(row => row.Visible));
            Assert.Equal(UiSettings.DefaultControlBackgroundOpacity, ui.ControlBackgroundOpacity);
            Assert.Equal(UiSettings.DefaultBackdropTintOpacity, ui.BackdropTintOpacity);
            Assert.Equal(UiSettings.DefaultBackdropTintLuminosityOpacity, ui.BackdropTintLuminosityOpacity);
            Assert.False(ui.EnableImmersive);
        });

        Test("详情外观：v28 退役背景浓度与两支效果键，旧文件不报错也不留下幽灵值", () =>
        {
            foreach (var version in new[] { 26, 27, 28 })
            {
                var ui = SettingsMigration.FromJson(
                    "{\"SchemaVersion\":" + version + ",\"Ui\":{\"BackdropOpacity\":37,\"UseGaussianBackdrop\":true,\"UseAcrylicBackdrop\":true}}",
                    PassthroughSecretProtector.Instance).Ui;
                Assert.Equal(UiSettings.DefaultBackdropTintOpacity, ui.BackdropTintOpacity);
                Assert.Equal(UiSettings.DefaultBackdropTintLuminosityOpacity, ui.BackdropTintLuminosityOpacity);
                Assert.Equal(UiSettings.DefaultControlTintOpacity, ui.ControlTintOpacity);
                Assert.Equal(UiSettings.DefaultControlTintLuminosityOpacity, ui.ControlTintLuminosityOpacity);
            }
        });

        Test("详情卡片：重复和无效键不占位，补全遗漏且不改变用户隐藏状态", () =>
        {
            DetailCardSetting[] saved =
            [
                null!, new() { Key = null! }, new() { Key = "missing" },
                new() { Key = "cast", Visible = false }, new() { Key = "cast", Visible = true },
                new() { Key = "info", Visible = false }
            ];
            var plan = DetailLayout.Plan(EmbyItemType.Series, saved);
            Assert.Equal("cast,info,episodes,seasons,similar", Keys(plan));
            Assert.False(plan[0].Visible);
            Assert.False(plan[1].Visible);
            Assert.True(plan.Skip(2).All(row => row.Visible));
            Assert.Equal("演职人员", DetailLayout.Title(plan[0].Key));
        });

        Test("详情卡片：电影忽略集列表和剧季键，不产生不可用选项", () =>
        {
            var plan = DetailLayout.Plan(EmbyItemType.Movie,
            [new() { Key = "seasons", Visible = false }, new() { Key = "similar", Visible = false }, new() { Key = "episodes" }]);
            Assert.Equal("similar,info,cast", Keys(plan));
            Assert.False(plan[0].Visible);
            Assert.True(plan.Skip(1).All(row => row.Visible));
        });

        Test("详情卡片：集页没有更多类似，存档里的旧勾选被丢掉", () =>
        {
            // 2026-11-09 用户令「Emby、飞牛、Jellyfin 的集页面统一不显示更多类似」：目录里没有这一条，
            // 于是老 settings.json 里那份勾选不占位、设置页也不再列出来 —— 集页收尾的是演职人员。
            var plan = DetailLayout.Plan(EmbyItemType.Episode,
            [
                new() { Key = "episodes" }, new() { Key = "info" },
                new() { Key = "cast" }, new() { Key = "similar", Visible = true }
            ]);
            Assert.Equal("episodes,info,cast", Keys(plan));
            Assert.True(!plan.Any(row => row.Key == DetailLayout.Similar), "集页目录里还留着更多类似");

            // 剧页和电影页不受影响：那一排还在目录里，也还听用户的勾选。
            Assert.True(DetailLayout.For(new UiSettings(), EmbyItemType.Series).Any(row => row.Key == DetailLayout.Similar),
                "剧页的更多类似跟着集页一起没了");
            Assert.True(DetailLayout.For(new UiSettings(), EmbyItemType.Movie).Any(row => row.Key == DetailLayout.Similar),
                "电影页的更多类似跟着集页一起没了");
        });

        Test("详情卡片：季页和其他页面不继承任何自定义顺序或隐藏", () =>
        {
            var ui = Customized();
            foreach (var type in new[] { EmbyItemType.Season, EmbyItemType.Person, EmbyItemType.Video, "unknown", null! })
            {
                var plan = DetailLayout.For(ui, type);
                Assert.Equal("episodes,info,seasons,cast,similar", Keys(plan));
                Assert.True(plan.All(row => row.Visible));
                Assert.Equal("episodes,info,seasons,cast,similar", Keys(DetailLayout.Plan(type, ui.SeriesCards)));
            }
        });

        Test("详情卡片：三种页面偏好独立，归一化结果不引用存档中的可变对象", () =>
        {
            var ui = Customized();
            var series = DetailLayout.For(ui, EmbyItemType.Series);
            Assert.Equal("cast,episodes,info,seasons,similar", Keys(series));
            Assert.Equal("episodes,info,cast", Keys(DetailLayout.For(ui, EmbyItemType.Episode)));
            Assert.Equal("similar,info,cast", Keys(DetailLayout.For(ui, EmbyItemType.Movie)));
            series[0].Visible = true;
            series[0].Key = "info";
            Assert.Equal("cast", ui.SeriesCards[0].Key);
            Assert.False(ui.SeriesCards[0].Visible);
        });

        Test("详情卡片：JSON null 列表和行可恢复，保存后重读保持偏好", () =>
        {
            var settings = SettingsMigration.FromJson("""
                {
                  "SchemaVersion":27,
                  "Ui": {
                    "SeriesCards":null,
                    "EpisodeCards":[null,{"Key":"cast","Visible":false},{"Key":"cast"},{"Key":"bad"}],
                    "MovieCards":[{"Key":"similar","Visible":false}],
                    "ControlBackgroundOpacity":37.5,"BackdropTintOpacity":43,"AlwaysImmersive":true
                  }
                }
                """, PassthroughSecretProtector.Instance);
            Assert.Equal("episodes,info,seasons,cast,similar", Keys(settings.Ui.SeriesCards));
            Assert.Equal("cast,episodes,info", Keys(settings.Ui.EpisodeCards));
            Assert.False(settings.Ui.EpisodeCards[0].Visible);
            var restored = SettingsMigration.FromJson(JsonSerializer.Serialize(settings), PassthroughSecretProtector.Instance);
            Assert.Equal("similar,info,cast", Keys(restored.Ui.MovieCards));
            Assert.False(restored.Ui.MovieCards[0].Visible);
            Assert.Equal(37.5, restored.Ui.ControlBackgroundOpacity);
            Assert.Equal(43d, restored.Ui.BackdropTintOpacity);
            Assert.False(restored.Ui.EnableImmersive);
        });

        Test("详情染色：超界夹取、非有限值回各自出厂，零和小数原样保留", () =>
        {
            foreach (var input in new[]
            {
                -1d, 101d, 0d, 37.5, 100d,
                double.NaN, double.PositiveInfinity, double.NegativeInfinity
            })
            {
                var settings = new AppSettings
                {
                    Ui = new UiSettings
                    {
                        ControlBackgroundOpacity = input,
                        BackdropTintOpacity = input,
                        BackdropTintLuminosityOpacity = input
                    }
                };
                SettingsMigration.Normalize(settings);
                Assert.Equal(Math.Clamp(double.IsFinite(input) ? input : 0, 0, 100), settings.Ui.ControlBackgroundOpacity);
                Assert.Equal(
                    double.IsFinite(input) ? Math.Clamp(input, 0, 100) : UiSettings.DefaultBackdropTintOpacity,
                    settings.Ui.BackdropTintOpacity);
                Assert.Equal(
                    double.IsFinite(input) ? Math.Clamp(input, 0, 100) : UiSettings.DefaultBackdropTintLuminosityOpacity,
                    settings.Ui.BackdropTintLuminosityOpacity);
                _ = JsonSerializer.Serialize(settings);
            }
        });

        Test("详情偏好：备份、导入和恢复默认包含新设置且保留窗口记录", () =>
        {
            var source = new AppSettings { Ui = Customized() };
            source.Ui.ControlBackgroundOpacity = 25;
            source.Ui.BackdropTintOpacity = 60;
            source.Ui.ControlTintLuminosityOpacity = 65;
            source.Ui.EnableImmersive = false;
            var backup = SettingsPreferences.ToBackupDocument(source);
            var live = new AppSettings();
            live.Ui.WindowWidth = 1500;
            live.Ui.WindowHeight = 900;
            SettingsPreferences.Apply(live, backup);
            Assert.Equal("cast,episodes,info,seasons,similar", Keys(live.Ui.SeriesCards));
            Assert.False(live.Ui.SeriesCards[0].Visible);
            Assert.Equal("episodes,info,cast", Keys(live.Ui.EpisodeCards));
            Assert.Equal("similar,info,cast", Keys(live.Ui.MovieCards));
            Assert.Equal(25d, live.Ui.ControlBackgroundOpacity);
            Assert.Equal(60d, live.Ui.BackdropTintOpacity);
            Assert.Equal(65d, live.Ui.ControlTintLuminosityOpacity);
            Assert.False(live.Ui.EnableImmersive);
            Assert.Equal(1500d, live.Ui.WindowWidth);
            SettingsPreferences.Apply(live, new AppSettings());
            Assert.Equal("episodes,info,seasons,cast,similar", Keys(live.Ui.SeriesCards));
            Assert.True(live.Ui.SeriesCards.All(row => row.Visible));
            Assert.Equal(UiSettings.DefaultControlBackgroundOpacity, live.Ui.ControlBackgroundOpacity);
            Assert.Equal(UiSettings.DefaultBackdropTintOpacity, live.Ui.BackdropTintOpacity);
            Assert.Equal(UiSettings.DefaultControlTintLuminosityOpacity, live.Ui.ControlTintLuminosityOpacity);
            Assert.False(live.Ui.EnableImmersive);
            Assert.Equal(1500d, live.Ui.WindowWidth);
            Assert.Equal(900d, live.Ui.WindowHeight);
        });

        Test("沉浸开关：宽窗由开关直接控制，关闭不进入沉浸", () =>
        {
            foreach (var type in new[] { EmbyItemType.Movie, EmbyItemType.Series, EmbyItemType.Season, EmbyItemType.Episode })
            {
                Assert.False(DetailHero.Immersive(false, false, true, type));
                Assert.True(DetailHero.Immersive(true, false, true, type));
            }
        });

        Test("沉浸开关：窄窗、无图及其他类型保留原布局", () =>
        {
            Assert.False(DetailHero.Immersive(true, true, true, EmbyItemType.Movie));
            Assert.False(DetailHero.Immersive(true, false, false, EmbyItemType.Series));
            Assert.False(DetailHero.Immersive(true, true, true, EmbyItemType.Episode));
            Assert.False(DetailHero.Immersive(true, false, false, EmbyItemType.Episode));
            foreach (var type in new[] { EmbyItemType.Person, EmbyItemType.Folder, "unknown", null! })
                Assert.False(DetailHero.Immersive(true, false, true, type));
        });

        Test("背景材料：窄窗不铺亚克力，宽窗按沉浸档或三页卡面", () =>
        {
            // 2026-11-08 用户令「窄窗口的背景图不要加亚克力」：窄窗一律没有这块材料。
            foreach (var immersive in new[] { true, false })
                foreach (var type in new[] { EmbyItemType.Movie, EmbyItemType.Series, EmbyItemType.Season, EmbyItemType.Episode })
                    Assert.False(DetailHero.BackdropMaterial(true, narrow: true, immersive, type));
            // 宽窗：沉浸档四类都有；非沉浸档只有电影、剧、季有，单集的卡面不铺。
            Assert.True(DetailHero.BackdropMaterial(true, false, true, EmbyItemType.Movie));
            Assert.True(DetailHero.BackdropMaterial(true, false, true, EmbyItemType.Episode));
            Assert.False(DetailHero.BackdropMaterial(true, false, false, EmbyItemType.Episode));
            foreach (var type in new[] { EmbyItemType.Movie, EmbyItemType.Series, EmbyItemType.Season })
                Assert.True(DetailHero.BackdropMaterial(true, false, false, type));
            // 没有图就什么都没有。
            Assert.False(DetailHero.BackdropMaterial(false, false, true, EmbyItemType.Series));
        });

        Test("右上角艺术图：电影、剧、集按空间可摆，季页与紧凑版式不摆", () =>
        {
            // 2026-11-08 用户令「沉浸模式下在右上角显示艺术图」：从前这一角按模式分档（集页，以及商店模式的
            // 电影和剧页），沉浸档整页站在背景图上，于是它被一起收掉了。现在模式不再是判据。
            foreach (var type in new[] { EmbyItemType.Movie, EmbyItemType.Series, EmbyItemType.Episode })
            {
                Assert.True(DetailHero.ShowsCorner(true, compact: false, 360, type));
                // 图没到、题栏让不出位置、紧凑版式 —— 三条都收着。
                Assert.False(DetailHero.ShowsCorner(false, compact: false, 360, type));
                Assert.False(DetailHero.ShowsCorner(true, compact: false, 0, type));
                Assert.False(DetailHero.ShowsCorner(true, compact: true, 360, type));
            }
            // 季页没有这一角（取图那一头不为季问艺术图），其余类型同样不摆。
            foreach (var type in new[] { EmbyItemType.Season, EmbyItemType.Person, EmbyItemType.Folder, "unknown", null! })
                Assert.False(DetailHero.ShowsCorner(true, compact: false, 360, type));
        });
    }

    private static string Keys(IEnumerable<DetailCardSetting> rows) => string.Join(',', rows.Select(row => row.Key));

    private static UiSettings Customized() => new()
    {
        SeriesCards = [new() { Key = "cast", Visible = false }],
        EpisodeCards = [new() { Key = "seasons", Visible = false }],
        MovieCards = [new() { Key = "similar", Visible = false }]
    };
}
