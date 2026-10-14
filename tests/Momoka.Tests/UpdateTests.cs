using Momoka.Configuration;
using Momoka.Updates;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 「开启应用后自动检查有没有新版本」那件事的判据：发布页上的 tag 怎么读、和手上这一版怎么比、清单怎么挑。
/// <para>
/// 每一处错法屏上都不出声。tag 认错了就是永远查不到更新（用户以为这个功能坏了，或者以为确实没有新版）；
/// 版本号不补齐就是同一版被反复提示；把 <c>draft</c> 当成新版就是把一个还没发布的构建推给用户；而 <c>prerelease</c>
/// 反过来 —— 本项目的 Release 全被标成了它，按它过滤同样等于永远查不到。这几条都在这儿钉着，网络那一头不测。
/// </para>
/// </summary>
internal static class UpdateTests
{
    public static void Register()
    {
        Test("检查更新：认得出发布页上的 tag", () =>
        {
            // 发布页上的 tag 带 v，本地手上这一版不带 —— 两种写法都要认。
            Assert.True(UpdateVersion.TryParseTag("v0.1.6", out var tagged), "v0.1.6");
            Assert.True(UpdateVersion.TryParseTag("0.1.6", out var plain), "0.1.6");
            Assert.Equal(0, UpdateVersion.Compare(tagged, plain), "带不带 v 是同一版");

            Assert.True(UpdateVersion.TryParseTag("  V1.0  ", out var two), "两段也认、两头空白不算");
            Assert.Equal("1.0.0.0", two.ToString(), "缺的两段补齐成 0");

            // 认不出来的 tag 一律跳过：发布页上可能有 nightly、也可能有带后缀的预览 tag，猜一个比跳过危险。
            Assert.False(UpdateVersion.TryParseTag("nightly", out _), "不是版本号的 tag");
            Assert.False(UpdateVersion.TryParseTag("v0.1.6-beta", out _), "带后缀的预览 tag");
            Assert.False(UpdateVersion.TryParseTag("1", out _), "只有一段不算版本号");
            Assert.False(UpdateVersion.TryParseTag("", out _), "空 tag");
            Assert.False(UpdateVersion.TryParseTag(null, out _), "没有 tag");
        });

        Test("检查更新：版本比大小", () =>
        {
            // 段是按数字比的，不是按字面比 —— 按字面比的话 0.1.9 会比 0.1.10 新，而这种事只在跨十位上发作。
            Assert.True(UpdateVersion.Compare(new Version(0, 1, 10), new Version(0, 1, 9)) > 0, "0.1.10 比 0.1.9 新");

            // 缺省的段是 -1 而不是 0：不补齐的话 0.1.6 与 0.1.6.0 比出来不相等，同一版会被反复当成新版提示。
            Assert.Equal(0, UpdateVersion.Compare(new Version(0, 1, 6), new Version(0, 1, 6, 0)), "0.1.6 与 0.1.6.0 是同一版");
            Assert.Equal(0, UpdateVersion.Compare(new Version(0, 1), new Version(0, 1, 0, 0)), "0.1 与 0.1.0.0 是同一版");

            Assert.True(UpdateVersion.Compare(new Version(0, 2, 0), new Version(0, 1, 6)) > 0, "0.2.0 比 0.1.6 新");
            Assert.True(UpdateVersion.Compare(new Version(0, 1, 6), new Version(0, 2, 0)) < 0, "反过来就是旧");
            Assert.Equal(0, UpdateVersion.Compare(new Version(1, 0, 0, 0), new Version(1, 0, 0, 0)), "同一版");
        });

        Test("检查更新：清单里挑出比当前新的最高一版", () =>
        {
            var current = new Version(0, 1, 6);
            var releases = new[]
            {
                new AppRelease(new Version(0, 1, 5, 0), "v0.1.5", "https://example/0.1.5"),
                new AppRelease(new Version(0, 2, 0, 0), "v0.2.0", "https://example/0.2.0"),
                new AppRelease(new Version(0, 1, 7, 0), "v0.1.7", "https://example/0.1.7"),
                new AppRelease(new Version(0, 1, 6, 0), "v0.1.6", "https://example/0.1.6")
            };

            var newest = UpdateVersion.NewestNewer(releases, current);
            Assert.NotNull(newest, "清单里有比 0.1.6 新的");
            Assert.Equal("v0.2.0", newest!.Tag, "取的是最高那一版，不是清单里的第一条");

            // 只有当前这一版和更旧的：答「没有新版」而不是硬挑一个 —— 挑错了用户会去下一份和手上一样的东西。
            Assert.Null(
                UpdateVersion.NewestNewer([new AppRelease(new Version(0, 1, 6, 0), "v0.1.6", "https://example")], current),
                "清单里只有当前这一版");

            Assert.Null(UpdateVersion.NewestNewer([], current), "空清单");
        });

        Test("检查更新：发布清单读得懂，坏的那几条跳过", () =>
        {
            // 形状照 GitHub 的 /releases 接口，只留这一版用得到的那几栏，另加几栏证明多余的字段不影响解析。
            const string json = """
                [
                  {"tag_name":"v0.1.6","html_url":"https://github.com/cudamin/Momoka/releases/tag/v0.1.6","draft":false,"prerelease":true,"name":"v0.1.6"},
                  {"tag_name":"v0.1.5","html_url":"https://github.com/cudamin/Momoka/releases/tag/v0.1.5","draft":false,"prerelease":true},
                  {"tag_name":"v0.9.0","html_url":"https://github.com/cudamin/Momoka/releases/tag/v0.9.0","draft":true,"prerelease":false},
                  {"tag_name":"nightly","html_url":"https://github.com/cudamin/Momoka/releases/tag/nightly","draft":false,"prerelease":true}
                ]
                """;

            var releases = UpdateClient.Parse(json);
            Assert.NotNull(releases, "这是一份发布清单");
            Assert.Equal(2, releases!.Count, "草稿和不是版本号的 tag 都跳过");

            // pre-release 照收：本项目的 Release 全被标成它，按它过滤就等于永远查不到更新。
            Assert.Equal("v0.1.6", releases[0].Tag, "带 prerelease 的照收");
            Assert.Equal("0.1.6", releases[0].VersionText, "给人看的是三段版本号");

            // 读不出来与「一版都没有」是两件事：混成一个的话，发布页抽风时手动检查会答「已是最新版本」。
            Assert.Null(UpdateClient.Parse("""{"message":"Not Found"}"""), "不是发布数组的正文");
            Assert.Null(UpdateClient.Parse("not json at all"), "根本不是 JSON");
            Assert.Equal(0, UpdateClient.Parse("[]")!.Count, "空数组是「一版都没有」");
        });

        Test("检查更新：这一格装机默认开着，恢复默认也回到开", () =>
        {
            // 用户令 2026-10-14：这一格默认要开启。缺这个键的旧设置文件读出来也必须是开 —— 它不显式写出来，
            // 以后有人把它改回 false 时，从旧版本升上来的人就再也收不到更新提示，而屏上没有任何一处会红。
            Assert.True(new AppSettings().Ui.AutoCheckUpdates, "装机默认开");

            var live = new AppSettings();
            live.Ui.AutoCheckUpdates = false;
            SettingsPreferences.Apply(live, new AppSettings());
            Assert.True(live.Ui.AutoCheckUpdates, "恢复默认把这一格拨回开");
        });
    }
}
