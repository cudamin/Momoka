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
        Test("沉浸总开关：缺键默认开启，显式关闭可保存导入并恢复默认", () =>
        {
            var property = typeof(UiSettings).GetProperty("EnableImmersive");
            Assert.NotNull(property, "缺少沉浸模式总开关");
            bool Enabled(UiSettings ui) => (bool)property!.GetValue(ui)!;
            var defaults = SettingsMigration.FromJson("{\"SchemaVersion\":27,\"Ui\":{}}",
                PassthroughSecretProtector.Instance);
            Assert.True(Enabled(defaults.Ui));
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
            Assert.True(Enabled(defaults.Ui));
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
            Assert.True(ui.EnableImmersive);
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
            Assert.True(restored.Ui.EnableImmersive);
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
            Assert.True(live.Ui.EnableImmersive);
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

        Test("右上角艺术图：电影、剧、集两种模式都摆，季页与紧凑版式不摆", () =>
        {
            // 2026-11-08 用户令「沉浸模式下在右上角显示艺术图」：从前这一角按模式分档（集页，以及简洁模式的
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
