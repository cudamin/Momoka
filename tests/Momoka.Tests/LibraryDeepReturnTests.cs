using System.Collections;
using System.Net;
using System.Reflection;
using Momoka.Emby;
using static Momoka.Tests.LibraryResumePagingTests;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>Actual Release LibraryViewModel and CardItem, with read-only anonymous HTTP and a UI queue.</summary>
internal static class LibraryDeepReturnTests
{
    public static int Run(string shellPath, string root)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var shell = new ShellFixture(shellPath, root);
        Case("深返回：2000 项库在 1500 附近只恢复有限窗口且首个核对不读前缀", async () =>
        {
            using var fixture = Movies(shell);
            var store = new BrowseSnapshotStore();
            var first = fixture.CreateLibraryView(store);
            object request;
            try
            {
                await Load(first, true);
                for (var page = 1; page < 80; page++) await Load(first, false);
                Assert.Equal(1600, Cards(first).Count);
                Assert.Equal(1, store.LibraryKeys.Count, "深读后必须保留有限快照，不能因历史超过 400 清掉整个缓存");
                shell.Invoke(first, "CaptureAnchor", 1500, -19d, 6);
                request = Request(first);
            }
            finally { ((IDisposable)first).Dispose(); }
            Assert.True(store.TryGetLibrary(store.LibraryKeys.Single(), [SnapshotSource.Of(fixture.Source("a"))], out var snapshot));
            Assert.True(snapshot.Items.Count <= 400, "每份快照最多 400 项");
            var savedStart = (int)snapshot.GetType().GetProperty("StartIndex")!.GetValue(snapshot)!;
            Assert.True(savedStart is >= 1100 and <= 1500, "快照必须来自锚点附近");
            var second = fixture.CreateLibraryView(store, request);
            try
            {
                var before = fixture.Transport.LibraryReads.Count;
                Assert.True((bool)shell.Invoke(second, "RestoreSnapshot")!);
                Assert.Equal(before, fixture.Transport.LibraryReads.Count, "恢复窗口不能先读 HTTP");
                Assert.True(Cards(second).Any(card => Id(card) == "movie-1500"));
                Assert.Equal(savedStart, shell.Get<int>(second, "WindowStart"));
                Assert.Equal(savedStart + Cards(second).Count, shell.Get<int>(second, "LoadedCount"));
                var identities = Cards(second).ToArray();
                await (Task)shell.Invoke(second, "RevalidateAsync")!;
                Assert.Equal(before + 1, fixture.Transport.LibraryReads.Count, "相同窗口核对只需一个有限请求");
                Assert.Equal(savedStart, fixture.Transport.LibraryReads.ToArray()[before].Start);
                Assert.True(Cards(second).SequenceEqual(identities), "未变卡片必须保留实例");
            }
            finally { ((IDisposable)second).Dispose(); }
        });

        Case("深返回：双向加载用绝对偏移且整行前插，不替换绑定集合", async () =>
        {
            using var fixture = Movies(shell);
            var (vm, _) = await Window(fixture, 1500);
            try
            {
                var collection = shell.Get<object>(vm, "Cards");
                var retained = Cards(vm).ToArray();
                shell.Invoke(vm, "ResizeGrid", 1100d);
                var start = shell.Get<int>(vm, "WindowStart");
                var columns = shell.Get<int>(vm, "GridColumns");
                var before = fixture.Transport.LibraryReads.Count;
                await (Task)shell.Invoke(vm, "LoadPreviousAsync")!;
                var inserted = start - shell.Get<int>(vm, "WindowStart");
                Assert.True(inserted > 0 && inserted % columns == 0, "前插必须按当前列数补整行");
                Assert.Equal(start - inserted, fixture.Transport.LibraryReads.ToArray()[before].Start);
                Assert.True(ReferenceEquals(collection, shell.Get<object>(vm, "Cards")));
                Assert.True(Cards(vm).Skip(inserted).Take(retained.Length).SequenceEqual(retained));
                var end = shell.Get<int>(vm, "WindowEnd");
                await Load(vm, false);
                Assert.Equal(end, fixture.Transport.LibraryReads.ToArray()[before + 1].Start);
                Assert.True((bool)shell.Get<object>(vm, "HasPrevious"));
                Assert.Equal(Cards(vm).Count, Cards(vm).Select(Id).Distinct().Count());
            }
            finally { ((IDisposable)vm).Dispose(); }
        });

        Case("深返回：新打开同库无锚点不能误命中上一访问的中段快照", async () =>
        {
            using var fixture = Movies(shell);
            var (returned, store) = await Window(fixture, 1500);
            ((IDisposable)returned).Dispose();
            var fresh = fixture.CreateLibraryView(store);
            try
            {
                Assert.True(!(bool)shell.Invoke(fresh, "RestoreSnapshot")!, "新打开库应从第一页开始");
                var before = fixture.Transport.LibraryReads.Count;
                await (Task)shell.Invoke(fresh, "LoadForNavigationAsync")!;
                Assert.Equal(0, fixture.Transport.LibraryReads.ToArray()[before].Start);
                Assert.Equal("movie-0", Id(Cards(fresh)[0]));
            }
            finally { ((IDisposable)fresh).Dispose(); }
        });

        Case("深返回：快照过期返回仍按同查询锚点直读，来源换代或查询改变归零", async () =>
        {
            using var fixture = Movies(shell);
            var (first, store) = await Window(fixture, 1500);
            var request = Request(first);
            ((IDisposable)first).Dispose();
            store.Clear();
            var cold = fixture.CreateLibraryView(store, request);
            try
            {
                var before = fixture.Transport.LibraryReads.Count;
                Assert.True(!(bool)shell.Invoke(cold, "RestoreSnapshot")!);
                await (Task)shell.Invoke(cold, "LoadForNavigationAsync")!;
                Assert.True(fixture.Transport.LibraryReads.Count - before == 1);
                Assert.True(fixture.Transport.LibraryReads.ToArray()[before].Start >= 1400);
                Assert.True(Cards(cold).Any(card => Id(card) == "movie-1500"));
                shell.Set(request, "AnchorQueryKey", "different-query");
            }
            finally { ((IDisposable)cold).Dispose(); }
            var changed = fixture.CreateLibraryView(store, request);
            try
            {
                var before = fixture.Transport.LibraryReads.Count;
                await (Task)shell.Invoke(changed, "LoadForNavigationAsync")!;
                Assert.Equal(0, fixture.Transport.LibraryReads.ToArray()[before].Start);
                Assert.Equal(-1, shell.Get<int>(request, "AnchorIndex"));
            }
            finally { ((IDisposable)changed).Dispose(); }
        });

        Case("深返回：活动窗口右半段变化和锚点删除会更新内容并落在近邻", async () =>
        {
            using var fixture = Movies(shell);
            var (vm, _) = await Window(fixture, 1500);
            try
            {
                var anchor = Cards(vm).Single(card => Id(card) == "movie-1500");
                var last = shell.Get<EmbyItem>(Cards(vm)[^1], "Item");
                fixture.Transport.Rows["a"].Single(item => item.Id == last.Id).Name = "Changed at window end";
                await (Task)shell.Invoke(vm, "RevalidateAsync")!;
                Assert.Equal("Changed at window end", shell.Get<EmbyItem>(Cards(vm)[^1], "Item").Name);
                var absolute = shell.Get<int>(Request(vm), "AnchorIndex");
                fixture.Transport.Rows["a"].RemoveAll(item => item.Id == "movie-1500");
                await (Task)shell.Invoke(vm, "RevalidateAsync")!;
                Assert.True(Cards(vm).All(card => Id(card) != "movie-1500"));
                Assert.Equal(absolute, shell.Get<int>(Request(vm), "AnchorIndex"));
                Assert.Equal("movie-1501", Id(Cards(vm)[(int)shell.Invoke(vm, "AnchorLocalIndex")!]));
                Assert.True(Cards(vm).All(card => !ReferenceEquals(card, anchor)));
            }
            finally { ((IDisposable)vm).Dispose(); }
        });

        Case("深返回：单集异常结果保留窗口且刷新可退回完整单集路径", async () =>
        {
            using var fixture = Movies(shell);
            var (vm, _) = await Window(fixture, 1500);
            try
            {
                var kept = Cards(vm).ToArray();
                foreach (var item in fixture.Transport.Rows["a"]) item.Type = EmbyItemType.Episode;
                await (Task)shell.Invoke(vm, "RevalidateAsync")!;
                Assert.True(Cards(vm).SequenceEqual(kept));
                Assert.True(shell.Get<bool>(vm, "NoticeOpen"));
                var before = fixture.Transport.LibraryReads.Count;
                await (Task)shell.Invoke(vm, "ReloadAsync")!;
                Assert.Equal(0, fixture.Transport.LibraryReads.ToArray()[before].Start);
                Assert.Equal(0, shell.Get<int>(vm, "WindowStart"));
                Assert.True(!shell.Get<bool>(vm, "WindowEligible"));
                Assert.Equal("movie-0", Id(Cards(vm)[0]));
                Assert.True(shell.Invoke(vm, "Episodes") is not null);
            }
            finally { ((IDisposable)vm).Dispose(); }
        });

        Case("深返回：刷新选范围前捕获当前锚点，后续浏览不跳回上次返回点", async () =>
        {
            using var fixture = Movies(shell);
            var (vm, _) = await Window(fixture, 1500);
            try
            {
                while (shell.Get<int>(vm, "WindowEnd") <= 1910) await Load(vm, false);
                var notified = 0;
                Action capture = () =>
                {
                    notified++;
                    var index = Cards(vm).FindIndex(card => Id(card) == "movie-1900");
                    if (index >= 0) shell.Invoke(vm, "CaptureAnchor", index, -11d, 4);
                };
                var changing = vm.GetType().GetEvent("WindowAnchorRequested", BindingFlags.NonPublic | BindingFlags.Instance)!;
                changing.GetAddMethod(true)!.Invoke(vm, [capture]);
                try
                {
                    var before = fixture.Transport.LibraryReads.Count;
                    await (Task)shell.Invoke(vm, "ReloadAsync")!;
                    Assert.True(notified > 0);
                    Assert.True(fixture.Transport.LibraryReads.ToArray()[before].Start >= 1500);
                    Assert.True(Cards(vm).Any(card => Id(card) == "movie-1900"), "刷新窗口必须包含刚捕获的当前卡片");
                }
                finally { changing.GetRemoveMethod(true)!.Invoke(vm, [capture]); }
            }
            finally { ((IDisposable)vm).Dispose(); }
        });

        Case("深返回：二次HTTP期间A到B到C的最新锚点不被旧返回窗口覆盖", async () =>
        {
            using var fixture = Movies(shell);
            var (vm, _) = await Window(fixture, 1500);
            try
            {
                while (shell.Get<int>(vm, "WindowEnd") < 2000) await Load(vm, false);
                var retained = Cards(vm).ToArray();
                var originalStart = shell.Get<int>(vm, "WindowStart");
                var captures = 0;
                var first = fixture.Transport.LibraryGate = new Gate(true);
                var second = new Gate(true);
                var anchorRequested = vm.GetType().GetEvent("WindowAnchorRequested", BindingFlags.NonPublic | BindingFlags.Instance)!;
                Action capture = () =>
                {
                    captures++;
                    var id = captures == 1 ? "movie-1500" : captures == 2 ? "movie-1900" : "movie-1600";
                    var local = Cards(vm).FindIndex(card => Id(card) == id);
                    Assert.True(local >= 0, "三次锚点都必须来自当前实际已载卡片");
                    shell.Invoke(vm, "CaptureAnchor", local, -31d, 4);
                    if (captures == 2) fixture.Transport.LibraryGate = second;
                };
                anchorRequested.GetAddMethod(true)!.Invoke(vm, [capture]);
                var before = fixture.Transport.LibraryReads.Count;
                try
                {
                    var refresh = (Task)shell.Invoke(vm, "RevalidateAsync")!;
                    await first.Entered.Task;
                    first.Release.TrySetResult();
                    await second.Entered.Task;
                    Assert.Equal(2, captures, "二次请求必须按HTTP后新捕获的B锚点选择范围");
                    second.Release.TrySetResult();
                    await refresh;
                    Assert.Equal(3, captures, "二次HTTP后也要纯捕获当前C锚点");
                    Assert.True(Cards(vm).SequenceEqual(retained), "C在二次返回区间外，保留原卡片与当前位置");
                    Assert.Equal(originalStart, shell.Get<int>(vm, "WindowStart"));
                    Assert.Equal(1600, shell.Get<int>(Request(vm), "AnchorIndex"));
                    Assert.Equal(-31d, shell.Get<double>(Request(vm), "AnchorY"));
                    Assert.Equal(2, fixture.Transport.LibraryReads.Count - before, "最多重读一次，不继续追逐滚动请求");
                }
                finally
                {
                    first.Release.TrySetResult();
                    second.Release.TrySetResult();
                    anchorRequested.GetRemoveMethod(true)!.Invoke(vm, [capture]);
                }
            }
            finally { ((IDisposable)vm).Dispose(); }
        });

        Case("深返回：旧单集响应排队后换查询不污染新窗口的刷新路径", async () =>
        {
            using var fixture = Movies(shell);
            var (vm, _) = await Window(fixture, 1500);
            try
            {
                foreach (var item in fixture.Transport.Rows["a"]) item.Type = EmbyItemType.Episode;
                var gate = fixture.Transport.LibraryGate = new Gate(true);
                var refresh = (Task)shell.Invoke(vm, "RevalidateAsync")!;
                await gate.Entered.Task;
                gate.Release.TrySetResult();
                SingleThread.WaitForPosts(1);
                shell.Invoke(vm, "Cancel");
                shell.Invoke(vm, "Resume", false);
                await refresh;
                Assert.True(!(bool)vm.GetType().GetField("_windowNeedsPrefix", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(vm)!,
                    "旧查询的单集结果不得写入新查询的回退标记");
            }
            finally { ((IDisposable)vm).Dispose(); }
        });

        Case("深返回：页大小 500 时快照仍有 400 上限，返回分页起点不混用局部数量", async () =>
        {
            using var fixture = Movies(shell);
            fixture.Settings.TestPageSize = 500;
            var (vm, store) = await Window(fixture, 1500);
            try
            {
                Assert.True(store.TryGetLibrary(store.LibraryKeys.Single(), [SnapshotSource.Of(fixture.Source("a"))], out var snapshot));
                Assert.True(snapshot.Items.Count <= 400);
                var before = fixture.Transport.LibraryReads.Count;
                await (Task)shell.Invoke(vm, "RevalidateAsync")!;
                Assert.True(fixture.Transport.LibraryReads.ToArray()[before].Limit <= 400, "后台只核对快照范围");
                Assert.Equal(shell.Get<int>(vm, "WindowStart"), fixture.Transport.LibraryReads.ToArray()[before].Start);
            }
            finally { ((IDisposable)vm).Dispose(); }
        });

        Case("深返回：取消与账号退出拒绝迟到前页，并保留当前窗口", async () =>
        {
            foreach (var revoke in new[] { false, true })
            {
                using var fixture = Movies(shell);
                var (vm, _) = await Window(fixture, 1500);
                try
                {
                    var retained = Cards(vm).ToArray();
                    var start = shell.Get<int>(vm, "WindowStart");
                    var gate = fixture.Transport.LibraryGate = new Gate(true);
                    var pending = (Task)shell.Invoke(vm, "LoadPreviousAsync")!;
                    await gate.Entered.Task;
                    if (revoke) fixture.RevokeHomeSource("a");
                    else shell.Invoke(vm, "Cancel");
                    gate.Release.TrySetResult();
                    await pending;
                    Assert.Equal(start, shell.Get<int>(vm, "WindowStart"));
                    Assert.True(Cards(vm).SequenceEqual(retained), "旧页不能回填取消后的窗口");
                }
                finally { ((IDisposable)vm).Dispose(); }
            }
        });

        Case("深返回：窗口后台插入删除保留锚点和未变卡片，刷新浪费不随历史深度增长", async () =>
        {
            using var fixture = Movies(shell);
            var (vm, _) = await Window(fixture, 1500);
            try
            {
                var anchor = Cards(vm).Single(card => Id(card) == "movie-1500");
                shell.Invoke(vm, "CaptureAnchor", Cards(vm).IndexOf(anchor), -21d, 4);
                var before = fixture.Transport.LibraryReads.Count;
                fixture.Transport.Rows["a"].Insert(0, Movie(-1));
                await (Task)shell.Invoke(vm, "RevalidateAsync")!;
                Assert.True(fixture.Transport.LibraryReads.Count - before <= 3, "插入不重读第一页到第 1500 项");
                Assert.True(ReferenceEquals(anchor, Cards(vm).Single(card => Id(card) == "movie-1500")));
                Assert.Equal(1501, shell.Get<int>(Request(vm), "AnchorIndex"));
                var delete = Id(Cards(vm)[0]);
                fixture.Transport.Rows["a"].RemoveAll(item => item.Id == delete);
                before = fixture.Transport.LibraryReads.Count;
                await (Task)shell.Invoke(vm, "RevalidateAsync")!;
                Assert.True(Cards(vm).All(card => Id(card) != delete));
                Assert.True(ReferenceEquals(anchor, Cards(vm).Single(card => Id(card) == "movie-1500")));
                Assert.True(fixture.Transport.LibraryReads.Count - before <= 3);
                Assert.Equal(1500, shell.Get<int>(Request(vm), "AnchorIndex"));
            }
            finally { ((IDisposable)vm).Dispose(); }
        });

        Case("深返回：容器删除与手动刷新读当前窗口，尾部缩短会夹紧起点", async () =>
        {
            using var fixture = Movies(shell);
            var (vm, _) = await Window(fixture, 1850);
            try
            {
                var before = fixture.Transport.LibraryReads.Count;
                var card = Cards(vm)[0];
                shell.Get<EmbyItem>(card, "Item").Type = EmbyItemType.Series;
                fixture.Transport.Rows["a"].RemoveAll(item => item.Id == Id(card));
                Assert.True(await (Task<bool>)shell.Invoke(vm, "RemoveAsync", card, shell.Get<CancellationToken>(vm, "QueryToken"), true)!);
                Assert.True(fixture.Transport.LibraryReads.ToArray()[before].Start > 1000, "容器删除不能从第一页回填");
                Assert.True(fixture.Transport.LibraryReads.Count - before <= 3);
                fixture.Transport.Rows["a"].RemoveRange(1200, fixture.Transport.Rows["a"].Count - 1200);
                before = fixture.Transport.LibraryReads.Count;
                await (Task)shell.Invoke(vm, "ReloadAsync")!;
                Assert.True(fixture.Transport.LibraryReads.ToArray()[before].Start > 1000, "手动刷新仍从当前窗口起点开始");
                Assert.True(Cards(vm).Count > 0 && shell.Get<int>(vm, "WindowEnd") <= 1200);
                Assert.True(fixture.Transport.LibraryReads.Count - before <= 3, "夹紧起点不能串读全部历史");
            }
            finally { ((IDisposable)vm).Dispose(); }
        });

        Case("深返回：后台空库清空窗口，403 清空并反馈，普通失败保留可读内容", async () =>
        {
            foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.InternalServerError, (HttpStatusCode)0 })
            {
                using var fixture = Movies(shell);
                var (vm, _) = await Window(fixture, 1500);
                try
                {
                    var retained = Cards(vm).ToArray();
                    if (status == 0) fixture.Transport.Rows["a"].Clear();
                    else fixture.Transport.LibraryFailure = status;
                    await (Task)shell.Invoke(vm, "RevalidateAsync")!;
                    if (status == HttpStatusCode.InternalServerError)
                    {
                        Assert.True(Cards(vm).SequenceEqual(retained));
                        Assert.True(shell.Get<bool>(vm, "NoticeOpen"), "后台失败必须有反馈");
                    }
                    else
                    {
                        Assert.Equal(0, Cards(vm).Count);
                        Assert.Equal(0, shell.Get<int>(vm, "WindowStart"));
                        if (status != 0) Assert.True(shell.Get<bool>(vm, "NoticeOpen"));
                    }
                }
                finally { ((IDisposable)vm).Dispose(); }
            }
        });

        Case("深返回：完整播放清单从零读取，不把局部窗口作为前缀", async () =>
        {
            using var fixture = Movies(shell);
            var (vm, _) = await Window(fixture, 1500);
            try
            {
                var cards = Cards(vm).ToArray();
                var before = fixture.Transport.LibraryReads.Count;
                var (items, _) = await (Task<(List<EmbyItem>, CancellationToken)>)shell.Invoke(vm, "PlayableItemsAsync")!;
                Assert.Equal(2000, items.Count);
                Assert.Equal("movie-0", items[0].Id);
                Assert.Equal("movie-1999", items[^1].Id);
                Assert.Equal(0, fixture.Transport.LibraryReads.ToArray()[before].Start);
                Assert.True(Cards(vm).SequenceEqual(cards), "构造播放清单不能消费显示窗口");
            }
            finally { ((IDisposable)vm).Dispose(); }
        });

        Case("深返回：字母跳转按绝对索引读附近窗口，再返回局部滚动索引", async () =>
        {
            using var fixture = Movies(shell);
            for (var index = 0; index < 2000; index++) fixture.Transport.Rows["a"][index].Name = index < 1500 ? "A" + index : "M" + index;
            var vm = fixture.CreateLibraryView(new BrowseSnapshotStore());
            try
            {
                await Load(vm, true);
                var before = fixture.Transport.LibraryReads.Count;
                var jump = await (Task<(int Index, string Letter)?>)shell.Invoke(vm, "JumpToAsync", "M")!;
                Assert.True(jump is not null);
                Assert.Equal("movie-1500", Id(Cards(vm)[jump!.Value.Index]));
                Assert.True(fixture.Transport.LibraryReads.Count - before <= 3, "字母跳转只需两次计数与一次窗口请求");
                Assert.True(fixture.Transport.LibraryReads.ToArray().Skip(before).Where(read => read.Limit > 0).All(read => read.Start >= 1400));
            }
            finally { ((IDisposable)vm).Dispose(); }
        });

        Case("深返回：窗口能力排除 Resume、NextUp、搜索、单集与 Random 查询", async () =>
        {
            using var fixture = Movies(shell);
            Assert.True(!shell.Get<bool>(fixture.Vm, "WindowEligible"));
            var search = fixture.CreateSearchView("fixture");
            var episodes = fixture.CreateLibraryView(collectionType: "tvshows", section: LibrarySection.Episodes);
            var season = fixture.CreateLibraryView(collectionType: null, parentType: EmbyItemType.Season);
            try
            {
                Assert.True(!shell.Get<bool>(search, "WindowEligible"));
                Assert.True(!shell.Get<bool>(episodes, "WindowEligible"));
                Assert.True(!shell.Get<bool>(season, "WindowEligible"));
                await Load(episodes, true);
                Assert.Equal(0, shell.Get<int>(episodes, "WindowStart"));
            }
            finally
            {
                ((IDisposable)search).Dispose();
                ((IDisposable)episodes).Dispose();
                ((IDisposable)season).Dispose();
            }
        });

        return TestHarness.Run();

        Fixture Movies(ShellFixture owner)
        {
            var fixture = new Fixture(owner, 2000);
            fixture.Transport.Rows["a"] = Enumerable.Range(0, 2000).Select(Movie).ToList();
            return fixture;
        }

        async Task<(object Vm, BrowseSnapshotStore Store)> Window(Fixture fixture, int absoluteIndex)
        {
            var store = new BrowseSnapshotStore();
            var first = fixture.CreateLibraryView(store);
            object request;
            try
            {
                await Load(first, true);
                await (Task)shell.Invoke(first, "LoadUntilAsync", absoluteIndex)!;
                var local = absoluteIndex - shell.Get<int>(first, "WindowStart");
                shell.Invoke(first, "CaptureAnchor", local, -17d, 4);
                request = Request(first);
            }
            finally { ((IDisposable)first).Dispose(); }
            var vm = fixture.CreateLibraryView(store, request);
            Assert.True((bool)shell.Invoke(vm, "RestoreSnapshot")!, "前次离开应产生窗口快照");
            return (vm, store);
        }

        Task Load(object vm, bool reset) => (Task)shell.Invoke(vm, "LoadAsync", reset)!;
        List<object> Cards(object vm) => ((IEnumerable)shell.Get<object>(vm, "Cards")).Cast<object>().ToList();
        string Id(object card) => shell.Get<EmbyItem>(card, "Item").Id;
        static object Request(object vm) => vm.GetType().GetField("_request", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
    }

    private static void Case(string name, Func<Task> test) => TestHarness.Test(name, () => SingleThread.Run(test));

    private static EmbyItem Movie(int index) => new()
    {
        Id = "movie-" + index,
        Name = "Movie " + index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture),
        Type = EmbyItemType.Movie,
        UserData = new EmbyUserData()
    };
}
