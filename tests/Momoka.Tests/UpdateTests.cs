using System.Diagnostics;
using System.Xml.Linq;
using Momoka.Configuration;
using Momoka.Updates;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 「开启应用后自动检查有没有新版本」那件事的判据：发布源上的 tag 怎么读、和手上这一版怎么比、清单怎么挑。
/// <para>
/// 每一处错法屏上都不出声。tag 认错了就是永远查不到更新（用户以为这个功能坏了，或者以为确实没有新版）；
/// 版本号不补齐就是同一版被反复提示；把读不出来的正文当成「一版都没有」就是发布页抽风时答「已是最新版本」。
/// 这几条都在这儿钉着，网络那一头不测。
/// </para>
/// <para>
/// 发布源是 <c>releases.atom</c>，只有已发布的条目 —— 草稿根本不出现在里面，而本项目的 Release 全被标成了
/// pre-release，所以「按 pre-release 过滤」那条老坑在源这一层不再有对应的开关，靠的是「这个源照收 pre-release」
/// 这个事实本身（见 <see cref="UpdateClient.Parse"/> 头上那段）。
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
            // 形状照 releases.atom 的真响应：命名空间、feed 自己那条 alternate、每条里那几栏（content 里的正文
            // 一个字都用不着，留着是为了证明多余的栏不影响解析）。
            const string feed = """
                <?xml version="1.0" encoding="UTF-8"?>
                <feed xmlns="http://www.w3.org/2005/Atom" xmlns:media="http://search.yahoo.com/mrss/" xml:lang="en-US">
                  <id>tag:github.com,2008:https://github.com/cudamin/Momoka/releases</id>
                  <link type="text/html" rel="alternate" href="https://github.com/cudamin/Momoka/releases"/>
                  <title>Release notes from Momoka</title>
                  <updated>2026-10-14T15:36:33Z</updated>
                  <entry>
                    <id>tag:github.com,2008:Repository/1352680664/v0.1.6</id>
                    <updated>2026-10-14T04:52:21Z</updated>
                    <link rel="alternate" type="text/html" href="https://github.com/cudamin/Momoka/releases/tag/v0.1.6"/>
                    <title>v0.1.6</title>
                    <content type="html">&lt;p&gt;Momoka 0.1.6 现已发布&lt;/p&gt;</content>
                    <author><name>cudamin</name></author>
                  </entry>
                  <entry>
                    <id>tag:github.com,2008:Repository/1352680664/v0.1.4</id>
                    <updated>2026-10-11T15:06:50Z</updated>
                    <link rel="alternate" type="text/html" href="https://github.com/cudamin/Momoka/releases/tag/v0.1.4"/>
                    <title>Momoka 0.1.4</title>
                    <content type="html">&lt;p&gt;…&lt;/p&gt;</content>
                  </entry>
                  <entry>
                    <id>tag:github.com,2008:Repository/1352680664/nightly</id>
                    <updated>2026-10-10T04:23:54Z</updated>
                    <link rel="alternate" type="text/html" href="https://github.com/cudamin/Momoka/releases/tag/nightly"/>
                    <title>nightly</title>
                  </entry>
                  <entry>
                    <id>tag:github.com,2008:Repository/1352680664/v0.1.3</id>
                    <updated>2026-10-09T03:11:01Z</updated>
                    <link rel="self" type="application/atom+xml" href="https://github.com/cudamin/Momoka/releases.atom"/>
                    <title>v0.1.3</title>
                  </entry>
                </feed>
                """;

            var releases = UpdateClient.Parse(feed);
            Assert.NotNull(releases, "这是一份发布清单");
            Assert.Equal(2, releases!.Count, "tag 不是版本号的、没有页面地址的都跳过");

            Assert.Equal("v0.1.6", releases[0].Tag, "tag 从页面地址里取");
            Assert.Equal("0.1.6", releases[0].VersionText, "给人看的是三段版本号");
            Assert.Equal("https://github.com/cudamin/Momoka/releases/tag/v0.1.6", releases[0].PageUrl, "打开下载页用的是这条地址");

            // 标题不是 tag：真响应里 v0.1.4 那条的 <title> 写的是「Momoka 0.1.4」。按标题认版本号会漏掉这一版，
            // 而屏上一个字都不说 —— 所以这一条必须靠页面地址认出来。
            Assert.Equal("v0.1.4", releases[1].Tag, "标题不是 tag 的那一条也认得出");

            // 读不出来与「一版都没有」是两件事：混成一个的话，发布页抽风时手动检查会答「已是最新版本」。
            Assert.Null(UpdateClient.Parse("Not Found"), "仓库改名后 GitHub 回的就是这段纯文本");
            Assert.Null(UpdateClient.Parse("<html><body>Not Found</body></html>"), "错误页是 HTML，不是 feed");
            Assert.Equal(0, UpdateClient.Parse("""<?xml version="1.0"?><feed xmlns="http://www.w3.org/2005/Atom"/>""")!.Count, "空 feed 是「一版都没有」");
        });

        Test("发布版本：程序集和文件版本只认共享属性中的版本", () =>
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Momoka.sln")))
                directory = directory.Parent;
            Assert.NotNull(directory, "测试输出必须属于当前工作树");

            var props = XDocument.Load(Path.Combine(directory!.FullName, "Directory.Build.props"));
            var group = props.Root!.Elements("PropertyGroup").Single(value => value.Element("Version") is not null);
            var version = group.Element("Version")!.Value;
            var packageVersion = $"{version}.0";
            Assert.Equal(packageVersion, group.Element("AssemblyVersion")!.Value, "程序集四段版本与发行号一致");
            Assert.Equal(packageVersion, group.Element("FileVersion")!.Value, "文件四段版本与商店包版本一致");

            var assembly = typeof(AppSettings).Assembly;
            Assert.Equal(packageVersion, assembly.GetName().Version!.ToString(), "实际构建不能沿用旧程序集版本");
            Assert.Equal(packageVersion, FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion,
                "实际文件版本必须更新，不能只改源码中的声明");
        });

        Test("检查更新：0.1.7 升至 0.1.9 后不重复提示同一发行版", () =>
        {
            const string feed = """
                <feed xmlns="http://www.w3.org/2005/Atom">
                  <entry><title>Momoka 0.1.7</title>
                    <link rel="alternate" type="text/html" href="https://github.com/cudamin/Momoka/releases/tag/v0.1.7"/>
                  </entry>
                  <entry><title>Momoka 0.1.9</title>
                    <link rel="alternate" type="text/html" href="https://github.com/cudamin/Momoka/releases/tag/v0.1.9"/>
                  </entry>
                </feed>
                """;
            var releases = UpdateClient.Parse(feed);
            Assert.NotNull(releases, "新发行版仍使用既有 Atom 发布源");
            var newest = UpdateVersion.NewestNewer(releases!, new Version(0, 1, 7, 0));
            Assert.NotNull(newest, "0.1.7 必须能发现 0.1.9");
            Assert.Equal("v0.1.9", newest!.Tag);
            Assert.Equal("0.1.9", newest.VersionText);
            Assert.Null(UpdateVersion.NewestNewer(releases!, new Version(0, 1, 9)), "三段本地版本不重复提示");
            Assert.Null(UpdateVersion.NewestNewer(releases!, new Version(0, 1, 9, 0)), "商店四段版本不重复提示");
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
