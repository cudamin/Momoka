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
        Test("详情卡片：旧配置缺键保留三个页面的默认顺序与可见性", () =>
        {
            var ui = SettingsMigration.FromJson("{\"SchemaVersion\":26,\"Ui\":{}}",
                PassthroughSecretProtector.Instance).Ui;
            Assert.Equal("episodes,info,seasons,cast,similar", Keys(DetailLayout.For(ui, EmbyItemType.Series)));
            Assert.Equal("episodes,info,seasons,cast,similar", Keys(DetailLayout.For(ui, EmbyItemType.Episode)));
            Assert.Equal("info,cast,similar", Keys(DetailLayout.For(ui, EmbyItemType.Movie)));
            Assert.True(DetailLayout.For(ui, EmbyItemType.Series).All(row => row.Visible));
            Assert.Equal(100d, ui.ControlBackgroundOpacity);
            Assert.Equal(90d, ui.BackdropOpacity);
            Assert.False(ui.AlwaysImmersive);
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
            Assert.Equal("演职员", DetailLayout.Title(plan[0].Key));
        });

        Test("详情卡片：电影忽略集列表和剧季键，不产生不可用选项", () =>
        {
            var plan = DetailLayout.Plan(EmbyItemType.Movie,
            [new() { Key = "seasons", Visible = false }, new() { Key = "similar", Visible = false }, new() { Key = "episodes" }]);
            Assert.Equal("similar,info,cast", Keys(plan));
            Assert.False(plan[0].Visible);
            Assert.True(plan.Skip(1).All(row => row.Visible));
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
            Assert.Equal("seasons,episodes,info,cast,similar", Keys(DetailLayout.For(ui, EmbyItemType.Episode)));
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
                  "SchemaVersion":26,
                  "Ui": {
                    "SeriesCards":null,
                    "EpisodeCards":[null,{"Key":"cast","Visible":false},{"Key":"cast"},{"Key":"bad"}],
                    "MovieCards":[{"Key":"similar","Visible":false}],
                    "ControlBackgroundOpacity":37.5,"BackdropOpacity":43,"AlwaysImmersive":true
                  }
                }
                """, PassthroughSecretProtector.Instance);
            Assert.Equal("episodes,info,seasons,cast,similar", Keys(settings.Ui.SeriesCards));
            Assert.Equal("cast,episodes,info,seasons,similar", Keys(settings.Ui.EpisodeCards));
            Assert.False(settings.Ui.EpisodeCards[0].Visible);
            var restored = SettingsMigration.FromJson(JsonSerializer.Serialize(settings), PassthroughSecretProtector.Instance);
            Assert.Equal("similar,info,cast", Keys(restored.Ui.MovieCards));
            Assert.False(restored.Ui.MovieCards[0].Visible);
            Assert.Equal(37.5, restored.Ui.ControlBackgroundOpacity);
            Assert.Equal(43d, restored.Ui.BackdropOpacity);
            Assert.True(restored.Ui.AlwaysImmersive);
        });

        Test("详情透明度：超界夹取、非有限值回默认，零和小数原样保留", () =>
        {
            foreach (var (input, controls, backdrop) in new (double, double, double)[]
            {
                (-1, 0, 0), (101, 100, 100), (0, 0, 0), (37.5, 37.5, 37.5), (100, 100, 100),
                (double.NaN, 100, 90), (double.PositiveInfinity, 100, 90), (double.NegativeInfinity, 100, 90)
            })
            {
                var settings = new AppSettings { Ui = new UiSettings { ControlBackgroundOpacity = input, BackdropOpacity = input } };
                SettingsMigration.Normalize(settings);
                Assert.Equal(controls, settings.Ui.ControlBackgroundOpacity);
                Assert.Equal(backdrop, settings.Ui.BackdropOpacity);
                _ = JsonSerializer.Serialize(settings);
            }
        });

        Test("详情偏好：备份、导入和恢复默认包含新设置且保留窗口记录", () =>
        {
            var source = new AppSettings { Ui = Customized() };
            source.Ui.ControlBackgroundOpacity = 25;
            source.Ui.BackdropOpacity = 60;
            source.Ui.AlwaysImmersive = true;
            var backup = SettingsPreferences.ToBackupDocument(source);
            var live = new AppSettings();
            live.Ui.WindowWidth = 1500;
            live.Ui.WindowHeight = 900;
            SettingsPreferences.Apply(live, backup);
            Assert.Equal("cast,episodes,info,seasons,similar", Keys(live.Ui.SeriesCards));
            Assert.False(live.Ui.SeriesCards[0].Visible);
            Assert.Equal("seasons,episodes,info,cast,similar", Keys(live.Ui.EpisodeCards));
            Assert.Equal("similar,info,cast", Keys(live.Ui.MovieCards));
            Assert.Equal(25d, live.Ui.ControlBackgroundOpacity);
            Assert.Equal(60d, live.Ui.BackdropOpacity);
            Assert.True(live.Ui.AlwaysImmersive);
            Assert.Equal(1500d, live.Ui.WindowWidth);
            SettingsPreferences.Apply(live, new AppSettings());
            Assert.Equal("episodes,info,seasons,cast,similar", Keys(live.Ui.SeriesCards));
            Assert.True(live.Ui.SeriesCards.All(row => row.Visible));
            Assert.Equal(100d, live.Ui.ControlBackgroundOpacity);
            Assert.Equal(90d, live.Ui.BackdropOpacity);
            Assert.False(live.Ui.AlwaysImmersive);
            Assert.Equal(1500d, live.Ui.WindowWidth);
            Assert.Equal(900d, live.Ui.WindowHeight);
        });

        Test("固定沉浸：普通宽窗有图可启用，关闭继续遵循自动大窗规则", () =>
        {
            foreach (var type in new[] { EmbyItemType.Movie, EmbyItemType.Series, EmbyItemType.Season })
            {
                Assert.True(DetailHero.Immersive(false, false, true, type, always: true));
                Assert.False(DetailHero.Immersive(false, false, true, type));
                Assert.True(DetailHero.Immersive(true, false, true, type));
            }
        });

        Test("固定沉浸：不越过紧凑、无图或单集与其他类型限制", () =>
        {
            Assert.False(DetailHero.Immersive(false, true, true, EmbyItemType.Movie, always: true));
            Assert.False(DetailHero.Immersive(false, false, false, EmbyItemType.Series, always: true));
            foreach (var type in new[] { EmbyItemType.Episode, EmbyItemType.Person, EmbyItemType.Folder, "unknown", null! })
                Assert.False(DetailHero.Immersive(true, false, true, type, always: true));
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
