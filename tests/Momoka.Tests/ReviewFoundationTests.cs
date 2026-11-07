using System.Collections;
using Momoka.Emby;
using Momoka.Services;
using static Momoka.Tests.LibraryResumePagingTests;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class ReviewFoundationTests
{
    internal static void RegisterCore()
    {
        Test("R06-006 表格宣传语与概要分别显示，卡片说明仍只取一项", () =>
        {
            var item = new EmbyItem { Id = "fixture", Name = "Movie", Taglines = ["Tagline"], Overview = "Overview" };
            Assert.Equal("Tagline", LibraryCardText.Cell(item, LibraryField.Tagline));
            Assert.Equal("Overview", LibraryCardText.Cell(item, LibraryField.Overview));
            Assert.Equal("Tagline", LibraryCardText.Note(item, [LibraryField.Tagline, LibraryField.Overview]));
            Assert.Equal("", LibraryCardText.Cell(new EmbyItem(), LibraryField.Tagline));
            Assert.Equal("", LibraryCardText.Cell(new EmbyItem(), LibraryField.Overview));
        });
        Test("R06-011 标签改变或顺序改变必须使表格卡片更新", () =>
        {
            var old = new EmbyItem { Id = "fixture", Name = "Movie", Tags = ["first", "second"] };
            var changed = new EmbyItem { Id = "fixture", Name = "Movie", Tags = ["new"] };
            var reordered = new EmbyItem { Id = "fixture", Name = "Movie", Tags = ["second", "first"] };
            Assert.False(ItemFingerprint.Same(old, changed));
            Assert.False(ItemFingerprint.Same(old, reordered));
            Assert.True(ItemFingerprint.Same(old, new EmbyItem { Id = "fixture", Name = "Movie", Tags = ["first", "second"] }));
        });
        SessionTests.Register();
    }

    internal static int RunViews(string shellPath, string root)
    {
        var shell = new ShellFixture(shellPath, root);
        Case("R06-005 NextUp后台失败保留暖缓存，成功空列表才清空", async () =>
        {
            using var fixture = new Fixture(shell, 2, firstKind: MediaServerKind.Jellyfin);
            fixture.Transport.NextUpRows["a"] = [new EmbyItem { Id = "next", Name = "Next", Type = EmbyItemType.Episode }];
            var store = new BrowseSnapshotStore();
            var first = fixture.CreateLoadingHome(snapshots: store);
            await fixture.LoadHome(first);
            ((IDisposable)first).Dispose();
            var home = fixture.CreateLoadingHome(snapshots: store);
            try
            {
                Assert.True((bool)shell.Invoke(home, "RestoreSnapshot")!);
                var shelf = fixture.HomeShelf(home, "接下来看");
                Assert.Equal(1, Cards(shell, shelf).Count);
                fixture.Transport.FailHomeNextUp = true;
                await (Task)shell.Invoke(home, "BackgroundRefreshAsync")!;
                Assert.Equal(1, Cards(shell, shelf).Count);
                fixture.Transport.FailHomeNextUp = false;
                fixture.Transport.NextUpRows["a"] = [];
                await (Task)shell.Invoke(home, "BackgroundRefreshAsync")!;
                Assert.Equal(0, Cards(shell, shelf).Count);
            }
            finally { ((IDisposable)home).Dispose(); }
        });
        Case("R06-010 固定媒体库标题不提供无目标入口，具体库仍可打开", () =>
        {
            Assert.Equal(HomeLayout.HomeRowTarget.None, HomeLayout.TargetOf(HomeLayout.Libraries));
            Assert.Equal(HomeLayout.HomeRowTarget.Library, HomeLayout.TargetOf(HomeLayout.LibraryKey("movies")));
            return Task.CompletedTask;
        });
        foreach (var aggregate in new[] { false, true })
        {
            Case("R06-001 暖缓存少量新增后完整消费批次不漏随后页 " + aggregate, async () =>
            {
                using var fixture = new Fixture(shell, 100, secondSource: aggregate, aggregate: aggregate);
                var store = new BrowseSnapshotStore();
                shell.Invoke(fixture.Vm, "UseSnapshots", store);
                shell.Invoke(fixture.Vm, "RefreshSnapshotKey");
                await fixture.Load();
                shell.Invoke(fixture.Vm, "Cancel");
                fixture.Transport.Rows["a"].Insert(0, new EmbyItem { Id = "a-new", Name = "New", Type = EmbyItemType.Movie });
                var returned = fixture.CreateNormalView(store);
                if (aggregate)
                {
                    ((IDisposable)returned).Dispose();
                    returned = shell.Create(fixture.Session, fixture.Images, fixture.Settings, null, snapshots: store);
                }
                try
                {
                    Assert.True((bool)shell.Invoke(returned, "RestoreSnapshot")!);
                    await (Task)shell.Invoke(returned, "RevalidateAsync")!;
                    await (Task)shell.Invoke(returned, "LoadAsync", false)!;
                    var actual = Cards(shell, returned).Select(card => shell.Get<EmbyItem>(card, "Item").Id).ToArray();
                    Assert.True(actual.Contains("a-20") && actual.Contains("a-38"), "刷新多读部分不能被裁掉后跳过");
                    Assert.Equal(actual.Length, actual.Distinct().Count());
                }
                finally { ((IDisposable)returned).Dispose(); }
            });
        }
        Case("R06-002 媒体库搜索切走切回后可以接续分页", async () =>
        {
            using var fixture = new Fixture(shell, 70);
            var vm = fixture.CreateSearchView("alpha");
            try
            {
                await (Task)shell.Invoke(vm, "LoadAsync", true)!;
                var old = shell.Get<CancellationToken>(vm, "QueryToken");
                shell.Invoke(vm, "Cancel");
                shell.Invoke(vm, "Resume", false);
                shell.Invoke(vm, "SetEmbySourceActive", true);
                await (Task)shell.Invoke(vm, "LoadAsync", false)!;
                Assert.Equal(40, Cards(shell, vm).Count);
                Assert.True(old.IsCancellationRequested);
                Assert.False(shell.Get<CancellationToken>(vm, "QueryToken").IsCancellationRequested);
            }
            finally { ((IDisposable)vm).Dispose(); }
        });
        Case("R06-003 另一来源改词后回库不请求，清除旧结果并对齐查询", async () =>
        {
            using var fixture = new Fixture(shell, 70);
            var vm = fixture.CreateSearchView("alpha");
            try
            {
                await (Task)shell.Invoke(vm, "LoadAsync", true)!;
                shell.Invoke(vm, "Cancel");
                shell.Invoke(vm, "SetEmbySourceActive", false);
                shell.Set(vm, "SearchText", "beta");
                var before = fixture.RequestCount;
                shell.Invoke(vm, "Resume", false);
                shell.Invoke(vm, "SetEmbySourceActive", true);
                Assert.Equal(0, Cards(shell, vm).Count);
                Assert.Equal("beta", ((ItemQuery)shell.Invoke(vm, "BuildQuery", 0)!).SearchTerm);
                Assert.Equal(before, fixture.RequestCount);
            }
            finally { ((IDisposable)vm).Dispose(); }
        });
        Case("R06-004 顶栏收藏切账号不保存不匹配的返回快照", async () =>
        {
            using var fixture = new Fixture(shell, 40, secondSource: true, favorites: true);
            var store = new BrowseSnapshotStore();
            shell.Invoke(fixture.Vm, "UseSnapshots", store);
            shell.Invoke(fixture.Vm, "RefreshSnapshotKey");
            await fixture.Load();
            await fixture.Choose(fixture.Source("b").Key);
            shell.Invoke(fixture.Vm, "Cancel");
            Assert.Equal(0, store.LibraryKeys.Count, "全局收藏同样应排除缓存");
        });
        Case("R06-011 实际表格卡片在仅标签变化后刷新", async () =>
        {
            using var fixture = new Fixture(shell, 70);
            fixture.Transport.Rows["a"].ForEach(item => { item.Type = EmbyItemType.Movie; item.Tags = ["old"]; });
            var store = new BrowseSnapshotStore();
            var firstView = fixture.CreateNormalView(store);
            await (Task)shell.Invoke(firstView, "LoadAsync", true)!;
            ((IDisposable)firstView).Dispose();
            var vm = fixture.CreateNormalView(store);
            try
            {
                Assert.True((bool)shell.Invoke(vm, "RestoreSnapshot")!);
                var first = Cards(shell, vm)[0];
                fixture.Transport.Rows["a"][0].Tags = ["new"];
                await (Task)shell.Invoke(vm, "RevalidateAsync")!;
                Assert.False(ReferenceEquals(first, Cards(shell, vm)[0]));
                Assert.Equal("new", LibraryCardText.Cell(shell.Get<EmbyItem>(Cards(shell, vm)[0], "Item"), LibraryField.Tags));
            }
            finally { ((IDisposable)vm).Dispose(); }
        });
        Case("R06-007 推荐货架确认删除撤掉多排同一条目并拒绝旧查询", async () =>
        {
            using var fixture = new Fixture(shell, 2, firstKind: MediaServerKind.Jellyfin);
            fixture.Transport.LatestReply = _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(fixture.Transport.Rows["a"]))
            };
            var vm = fixture.CreateLibraryView(collectionType: "tvshows", section: LibrarySection.Recommended);
            try
            {
                await (Task)shell.Invoke(vm, "LoadAsync", true)!;
                var shelves = ((IEnumerable)shell.Get<object>(vm, "RecommendedShelves")).Cast<object>().ToArray();
                var target = shelves.SelectMany(shelf => Cards(shell, shelf)).First();
                var identity = shell.Get<EmbyItem>(target, "Item").IdentityKey;
                Assert.True(shelves.Count(shelf => Cards(shell, shelf).Any(card => shell.Get<EmbyItem>(card, "Item").IdentityKey == identity)) > 1);
                var query = shell.Get<CancellationToken>(vm, "QueryToken");
                Assert.True(await (Task<bool>)shell.Invoke(vm, "RemoveAsync", target, query, true)!);
                Assert.True(shelves.All(shelf => Cards(shell, shelf).All(card => shell.Get<EmbyItem>(card, "Item").IdentityKey != identity)));
                Assert.Equal(0, Cards(shell, vm).Count);
                shell.Invoke(vm, "Cancel");
                Assert.False(await (Task<bool>)shell.Invoke(vm, "RemoveAsync", target, query, true)!);
            }
            finally { ((IDisposable)vm).Dispose(); }
        });
        return Run();
    }

    private static List<object> Cards(ShellFixture shell, object vm) => ((IEnumerable)shell.Get<object>(vm, "Cards")).Cast<object>().ToList();
    private static void Case(string name, Func<Task> run) => Test(name, () => SingleThread.Run(run));
}
