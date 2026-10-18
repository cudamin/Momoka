using Momoka.Configuration;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 「把媒体库的列表也添加到主页之中，新增页里拖拽决定这些列表的顺序，勾选显示或者不勾选取消显示」的那一半规则：
/// <see cref="HomeLayout"/> 把「设置文件里存的那份版面」和「服务器现在有哪几个媒体库」合成这一次要排的几排。
/// <para>
/// 值得单测的正是那三种对不上：新加了一个库、删掉了一个、改了名。屏幕上它们各自看起来都像「主页少了一排」或者
/// 「多了一排空货架」，而这几条断言说得出到底该是什么。
/// </para>
/// </summary>
internal static class HomeLayoutTests
{
    public static void Register()
    {
        Test("主页版面：没有存档时按默认次序，媒体库排在后面", () =>
        {
            var plan = HomeLayout.Plan(null, [Library("1", "电影"), Library("2", "电视节目")]);

            // 整个服务器的「最近添加」那一排 2026-09-12 退役：默认版面里不再有它。
            Assert.Equal(7, plan.Count);
            Assert.Equal("继续观看", plan[0].Title);
            Assert.Equal("媒体库", plan[1].Title);
            Assert.Equal("接下来看", plan[2].Title);
            Assert.Equal("最近添加 · 电影", plan[3].Title);
            Assert.Equal("最近添加 · 电视节目", plan[4].Title);
            Assert.Equal(HomeLayout.MoviePilotMovies, plan[5].Key);
            Assert.Equal(HomeLayout.MoviePilotSeries, plan[6].Key);

            // 默认全部显示：新加的库不该悄悄地不出现。
            Assert.True(plan.All(row => row.Visible), "默认应当全部显示");
        });

        Test("主页版面：存档里那份次序说了算", () =>
        {
            // 用户把媒体库那一排勾掉了。
            List<HomeRowSetting> saved =
            [
                Row(HomeLayout.Libraries, "媒体库", visible: false),
                Row(HomeLayout.Resume, "继续观看"),
                Row(HomeLayout.NextUp, "接下来看")
            ];

            var plan = HomeLayout.Plan(saved, [Library("1", "电影")]);

            Assert.Equal("媒体库", plan[0].Title);
            Assert.False(plan[0].Visible, "勾掉的那一排要留着不显示，而不是消失");
            Assert.Equal("继续观看", plan[1].Title);
            Assert.Equal("接下来看", plan[2].Title);

            // 存档里没有的补在末尾。
            Assert.Equal(6, plan.Count);
            Assert.Equal("最近添加 · 电影", plan[3].Title);
            Assert.True(plan[3].Visible);
        });

        Test("主页版面：退役的整服最近添加那一排，存档里还带着也扔掉", () =>
        {
            // 「去掉最近添加，保留最近添加 电视节目、最近添加 电影」（用户的话，2026-09-12）。
            // 老设置文件里都有这一行（钥匙 latest）：认不出就落不进版面，归一化顺手把它写没。
            List<HomeRowSetting> saved =
            [
                Row(HomeLayout.Resume, "继续观看"),
                Row("latest", "最近添加"),
                Row(HomeLayout.LibraryKey("7"), "最近添加 · 纪录片")
            ];

            var plan = HomeLayout.Plan(saved, [Library("7", "纪录片")]);

            // 存档里没写的固定排照默认补上；退役的那一排两边都不再回来。
            Assert.Equal(6, plan.Count);
            Assert.True(plan.All(row => row.Key != "latest"), "退役的那一排还在版面里");
            Assert.Equal("继续观看", plan[0].Title);
            Assert.Equal("最近添加 · 纪录片", plan[1].Title);
            Assert.Equal("媒体库", plan[2].Title);
            Assert.Equal("接下来看", plan[3].Title);

            // 库列表不在手上时也一样扔；库那一排按存档里记着的名字原地留着。
            var blind = HomeLayout.Plan(saved, null);
            Assert.Equal(6, blind.Count);
            Assert.True(blind.All(row => row.Key != "latest"), "空手时退役的那一排回来了");
            Assert.Equal("最近添加 · 纪录片", blind[1].Title);
        });

        Test("主页版面：服务器上新加的库排到末尾，删掉的那个扔掉", () =>
        {
            List<HomeRowSetting> saved =
            [
                Row(HomeLayout.Resume, "继续观看"),
                Row(HomeLayout.LibraryKey("gone"), "最近添加 · 纪录片", visible: false),
                Row(HomeLayout.Libraries, "媒体库"),
                Row(HomeLayout.NextUp, "接下来看")
            ];

            var plan = HomeLayout.Plan(saved, [Library("new", "电影")]);

            // 那个库已经不在服务器上了：这一排整个扔掉 —— 留着就是一排永远空的货架。
            Assert.Equal(6, plan.Count);
            Assert.True(plan.All(row => HomeLayout.LibraryId(row.Key) != "gone"), "删掉的库还在版面里");

            Assert.Equal("最近添加 · 电影", plan[^3].Title);
            Assert.Equal(HomeLayout.LibraryKey("new"), plan[^3].Key);
        });

        Test("主页版面：库列表不在手上时用存档里记着的名字", () =>
        {
            // 设置窗口是这种情形：它有设置文件，没有服务器。主窗口读库列表失败时也是空手。
            List<HomeRowSetting> saved =
            [
                Row(HomeLayout.LibraryKey("4"), "最近添加 · 电视节目", visible: false),
                Row(HomeLayout.Resume, "继续观看"),
                Row(HomeLayout.Libraries, "媒体库"),
                Row(HomeLayout.NextUp, "接下来看")
            ];

            // 「没有那份列表」和「那份列表是空的」都算空手。
            IReadOnlyList<EmbyItem>?[] empty = [null, []];

            foreach (var unknown in empty)
            {
                var plan = HomeLayout.Plan(saved, unknown);

                Assert.Equal(6, plan.Count);
                Assert.Equal("最近添加 · 电视节目", plan[0].Title);
                Assert.False(plan[0].Visible, "勾没了");

                // 这一份跟存档一模一样，所以主页那一头不会把它当「变了」写回去 —— 那正是抹掉那几排的路。
                Assert.True(HomeLayout.Same(saved, plan.Where(row => !HomeLayout.IsMoviePilot(row.Key)).ToArray()), "空手时不该改动原媒体库排序");
            }

            // 存档里连名字都没记：这一排写不出标题，只能扔。
            var nameless = HomeLayout.Plan([Row(HomeLayout.LibraryKey("4"), "")], null);
            Assert.Equal(5, nameless.Count);
            Assert.True(nameless.All(row => HomeLayout.LibraryId(row.Key) is null), "没有名字的库排进了版面");
        });

        Test("主页版面：库改了名就用新名字", () =>
        {
            List<HomeRowSetting> saved = [Row(HomeLayout.LibraryKey("1"), "最近添加 · 老名字", visible: false)];

            var plan = HomeLayout.Plan(saved, [Library("1", "新名字")]);

            Assert.Equal("最近添加 · 新名字", plan[0].Title);

            // 名字换了，勾没换：那个勾是用户自己按的。
            Assert.False(plan[0].Visible);
        });

        Test("主页版面：认不出来的钥匙和没有 id 的库都不进版面", () =>
        {
            List<HomeRowSetting> saved = [Row("nonsense", "手改坏了的一行"), Row("", "空钥匙")];

            var noId = new EmbyItem { Id = "", Name = "没有 id 的库" };
            var plan = HomeLayout.Plan(saved, [noId, Library("1", "电影")]);

            Assert.Equal(6, plan.Count);
            Assert.True(plan.All(row => row.Key != "nonsense" && row.Key.Length > 0), "认不出的钥匙进了版面");
            Assert.Equal(HomeLayout.LibraryKey("1"), plan[^3].Key);
        });

        Test("主页版面：钥匙认得出媒体库那几排", () =>
        {
            Assert.Equal("library:abc", HomeLayout.LibraryKey("abc"));
            Assert.Equal("abc", HomeLayout.LibraryId("library:abc"));
            Assert.Equal(null, HomeLayout.LibraryId("latest"));
            Assert.Equal(null, HomeLayout.LibraryId(null));

            // 三排固定的各有一句话；媒体库那几排的标题不从这里来。
            Assert.Equal("继续观看", HomeLayout.FixedTitle(HomeLayout.Resume));
            Assert.Equal("媒体库", HomeLayout.FixedTitle(HomeLayout.Libraries));
            Assert.Equal("接下来看", HomeLayout.FixedTitle(HomeLayout.NextUp));

            // 旧版面那把 latest 钥匙现在认不出了 —— 认不出就排不进版面，见上面退役那一条。
            Assert.Equal(null, HomeLayout.FixedTitle("latest"));
            Assert.Equal(null, HomeLayout.FixedTitle("library:1"));
        });

        Test("主页版面：每一排点进哪儿", () =>
        {
            // 2026-09-14「新增点击图中红框的标题可以进入对应的页面」。图里圈了三排，后两排（库那几排）本来
            // 就能点，这一条钉的是「继续观看」那一排也有去处 —— 从前「能不能点」是拿 LibraryId 非空表达的，
            // 而这一排不属于任何库，于是牌子一直是死的。
            Assert.Equal(HomeLayout.HomeRowTarget.Resume, HomeLayout.TargetOf(HomeLayout.Resume));
            Assert.Equal(HomeLayout.HomeRowTarget.NextUp, HomeLayout.TargetOf(HomeLayout.NextUp));

            // 「媒体库」那一排和每个库自己那一排，进的都是库。
            Assert.Equal(HomeLayout.HomeRowTarget.Library, HomeLayout.TargetOf(HomeLayout.Libraries));
            Assert.Equal(HomeLayout.HomeRowTarget.Library, HomeLayout.TargetOf(HomeLayout.LibraryKey("1")));

            // 认不出的钥匙点不动 —— 和它们排不出版面是同一件事。
            Assert.Equal(HomeLayout.HomeRowTarget.None, HomeLayout.TargetOf("latest"));
            Assert.Equal(HomeLayout.HomeRowTarget.None, HomeLayout.TargetOf("library:"));
            Assert.Equal(HomeLayout.HomeRowTarget.None, HomeLayout.TargetOf(null));
            Assert.Equal(HomeLayout.HomeRowTarget.None, HomeLayout.TargetOf(""));
        });

        Test("主页版面：订阅与媒体库混排，隐藏的勾选和次序可以往返", () =>
        {
            var original = HomeLayout.Plan(null, [Library("1", "电影")]);
            var movie = original.Single(row => row.Key == HomeLayout.MoviePilotMovies) with { Visible = false };
            var series = original.Single(row => row.Key == HomeLayout.MoviePilotSeries);
            var mixed = new[] { series, original[0], movie, original[1], original[2], original[3] };
            var saved = HomeLayout.Save(mixed);
            var restored = HomeLayout.Plan(saved, null);
            Assert.True(HomeLayout.Same(saved, restored), "订阅混排没有完整保存");
            Assert.False(restored[2].Visible, "关闭的电影订阅被重新勾选");
            Assert.Equal("MP 电视剧订阅", restored[0].Title);
        });

        Test("主页版面：断线时编辑其他排不抹掉订阅偏好", () =>
        {
            var plan = HomeLayout.Plan([
                Row(HomeLayout.MoviePilotSeries, "旧标题", false),
                Row(HomeLayout.Resume, "继续观看"),
                Row(HomeLayout.MoviePilotMovies, "旧标题"),
                Row(HomeLayout.Libraries, "媒体库"),
                Row(HomeLayout.NextUp, "接下来看")], null);
            var available = plan.Where(row => HomeLayout.IsAvailable(row.Key, false)).Reverse()
                .Select(row => row with { Visible = false }).ToArray();
            Assert.Equal(3, available.Length);
            var changed = HomeLayout.MergeEdits(plan, available);
            Assert.Equal(plan[0], changed[0]);
            Assert.Equal(plan[2], changed[2]);
            Assert.Equal(HomeLayout.NextUp, changed[1].Key);
            Assert.False(changed[1].Visible);
            var restored = HomeLayout.Plan(HomeLayout.Save(changed), null)
                .Where(row => HomeLayout.IsAvailable(row.Key, true)).ToArray();
            Assert.Equal(5, restored.Length);
            Assert.Equal(changed[0], restored[0]);
            Assert.Equal(changed[2], restored[2]);
        });

        Test("主页版面：旧设置页不能复活已删除的库或挤掉现有库", () =>
        {
            var b = new HomeRowPlan(HomeLayout.LibraryKey("b"), "乙", true);
            var c = new HomeRowPlan(HomeLayout.LibraryKey("c"), "丙", true);
            // 「MoviePilot 电影订阅」是改名之前存档里那一份标题（2026-10-13 晚起叫「MP 电影订阅」）：
            // 这一条要的正是「认得出的键，标题照存档里那份留着」—— 拿旧名字当夹具比拿新名字更能说明问题。
            var mp = new HomeRowPlan(HomeLayout.MoviePilotMovies, "MoviePilot 电影订阅", false);
            var changed = HomeLayout.MergeEdits([b, mp, c], [
                new(HomeLayout.LibraryKey("gone"), "已删库", true), c, c with { Visible = false }, b]);
            Assert.Equal(3, changed.Count);
            Assert.Equal(c, changed[0]);
            Assert.Equal(mp, changed[1]);
            Assert.Equal(b, changed[2]);
        });

        Test("主页版面：重复订阅键只保留首份偏好", () =>
        {
            var plan = HomeLayout.Plan([
                Row(HomeLayout.MoviePilotMovies, "旧标题", false),
                Row(HomeLayout.MoviePilotMovies, "重复", true)], null);
            Assert.Equal(1, plan.Count(row => row.Key == HomeLayout.MoviePilotMovies));
            Assert.False(plan[0].Visible);
            Assert.Equal("MP 电影订阅", plan[0].Title);
        });

        Test("主页版面：只有真变了才回写设置文件", () =>
        {
            var libraries = new[] { Library("1", "电影") };
            var plan = HomeLayout.Plan(null, libraries);
            var saved = HomeLayout.Save(plan);

            Assert.True(HomeLayout.Same(saved, plan), "刚存下来的那一份应当算没变");
            Assert.True(HomeLayout.Same(saved, HomeLayout.Plan(saved, libraries)), "再算一遍也该是同一份");

            // 少一排、次序不同、勾不同，三种都算变了。
            Assert.False(HomeLayout.Same(saved.Take(2).ToList(), plan));
            Assert.False(HomeLayout.Same(null, plan));

            var reordered = HomeLayout.Save(plan.Reverse());
            Assert.False(HomeLayout.Same(reordered, plan));

            var unticked = HomeLayout.Save(plan);
            unticked[0].Visible = false;
            Assert.False(HomeLayout.Same(unticked, plan));
        });

        // 「隐藏的行不联网」在 2026-10-16 加的定时检查上破了功：那一趟无条件问了一次 Items/Latest，
        // 几排读服务器条目的货架都收着、轮播也关着的时候，主页上只剩媒体库那几块牌子和订阅，没有可换的东西。
        // 判据搬到 Core 来，探针「隐藏首页行不请求」盯运行面。
        Test("主页自动刷新：读条目清单的那几排都收着、轮播也关着就不问服务器", () =>
        {
            var hidden = new[]
            {
                new HomeRowPlan(HomeLayout.Resume, "继续观看", false),
                new HomeRowPlan(HomeLayout.NextUp, "接下来看", false),
                new HomeRowPlan(HomeLayout.LibraryKey("lib-1"), "最近添加 · 电影", false),
                new HomeRowPlan(HomeLayout.Libraries, "媒体库", true),
                new HomeRowPlan(HomeLayout.MoviePilotMovies, "MP 电影订阅", true)
            };
            Assert.False(HomeLayout.CanRefresh(hidden, banner: false), "没有东西会跟着新入库换，这一拍不该问");
            Assert.False(HomeLayout.CanRefresh([], banner: false), "一排都没有也一样");
        });

        Test("主页自动刷新：轮播开着或者有一排条目货架勾着就照旧问", () =>
        {
            var hidden = new[] { new HomeRowPlan(HomeLayout.Resume, "继续观看", false) };
            var shown = new[] { new HomeRowPlan(HomeLayout.Resume, "继续观看", true) };
            var library = new[] { new HomeRowPlan(HomeLayout.LibraryKey("lib-1"), "最近添加 · 电影", true) };

            Assert.True(HomeLayout.CanRefresh(hidden, banner: true), "轮播开着：服务器新入库要换的是顶上那张大图");
            Assert.True(HomeLayout.CanRefresh(shown, banner: false), "继续观看勾着：那一排要跟着换");
            Assert.True(HomeLayout.CanRefresh(library, banner: false), "媒体库那一排勾着：它装的就是那个库的最近添加");
            Assert.True(HomeLayout.CanRefresh(shown, banner: true));
        });
    }

    private static EmbyItem Library(string id, string name) =>
        new() { Id = id, Name = name, Type = EmbyItemType.CollectionFolder };

    private static HomeRowSetting Row(string key, string title, bool visible = true) =>
        new() { Key = key, Title = title, Visible = visible };
}
