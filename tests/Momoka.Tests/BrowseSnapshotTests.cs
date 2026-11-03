using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 暖缓存的两块地基：卡片指纹只对展示内容敏感，快照仓库只对同一登录代次、同一来源集合和未过期的查询命中。
/// 这里用匿名 DTO 和显式时钟覆盖命中、过期、淘汰、来源换代与超限，不启动页面或网络。
/// </summary>
internal static class BrowseSnapshotTests
{
    public static void Register()
    {
        Test("快照指纹：身份相同、展示字段不同即视为需要换卡", () =>
        {
            var item = Item("movie-1");
            var copy = Item("movie-1");
            Assert.True(ItemFingerprint.Same(item, copy), "同样内容的两份条目不应被判成变化");

            copy.UserData = new EmbyUserData { Played = true, PlaybackPositionTicks = 42 };
            Assert.True(!ItemFingerprint.Same(item, copy), "观看状态变化必须换卡");

            var retagged = Item("movie-1");
            retagged.PrimaryImageTag = "new-poster";
            Assert.True(!ItemFingerprint.Same(item, retagged), "封面标签变化必须换卡");

            var resized = Item("movie-1");
            resized.MediaSources[0].Size = 9_999;
            Assert.True(!ItemFingerprint.Same(item, resized), "文件大小变化必须换卡");
        });

        Test("快照指纹：身份不同不会冒充同一条", () =>
        {
            var left = Item("a");
            var right = Item("b");
            Assert.True(!ItemFingerprint.Same(left, right), "条目编号不同不能按内容相同处理");
        });

        Test("快照仓库：同一登录代次的媒体库快照可命中，来源换代即失效", () =>
        {
            var store = new BrowseSnapshotStore();
            var login = new object();
            var sources = new[] { new SnapshotSource("server:account", login) };
            var snapshot = Library(new[] { Item("a-1"), Item("a-2") });

            store.StoreLibrary("movies", sources, snapshot);
            Assert.True(store.TryGetLibrary("movies", sources, out var hit), "同一登录代次应命中");
            Assert.Equal(2, hit.Items.Count);
            Assert.True(!ReferenceEquals(snapshot.Items, hit.Items), "仓库持有自己的列表副本");

            var relogin = new[] { new SnapshotSource("server:account", new object()) };
            Assert.True(!store.TryGetLibrary("movies", relogin, out _), "退出再登录不能命中上一代内容");
            Assert.Equal(0, store.LibraryKeys.Count, "失效项应立即移除");
        });

        Test("快照仓库：来源集合增删不命中", () =>
        {
            var store = new BrowseSnapshotStore();
            var owner = new object();
            var one = new[] { new SnapshotSource("a", owner) };
            var two = new[] { new SnapshotSource("a", owner), new SnapshotSource("b", owner) };
            store.StoreHome("home", one, Home(new[] { Item("a-1") }));

            Assert.True(!store.TryGetHome("home", two, out _), "多出一个在线来源时旧快照不能冒充当前聚合");
            Assert.True(!store.TryGetHome("home", [], out _), "来源全部离线时不能命中");
        });

        Test("快照仓库：过期与最近使用淘汰", () =>
        {
            var now = new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
            var store = new BrowseSnapshotStore(homeCapacity: 2, lifetime: TimeSpan.FromMinutes(5), clock: () => now);
            var sources = new[] { new SnapshotSource("a", new object()) };

            store.StoreHome("one", sources, Home([Item("1")]));
            now += TimeSpan.FromMinutes(4);
            store.StoreHome("two", sources, Home([Item("2")]));
            store.StoreHome("three", sources, Home([Item("3")]));

            Assert.True(!store.TryGetHome("one", sources, out _), "超过容量应淘汰最久未用的一份");
            Assert.True(store.TryGetHome("two", sources, out _), "较新的一份应保留");

            now += TimeSpan.FromMinutes(6);
            Assert.True(!store.TryGetHome("two", sources, out _), "过期快照不能继续命中");
            Assert.True(!store.HomeKeys.Contains("two"), "被核对到的过期项应立即移除");
            Assert.True(!store.TryGetHome("three", sources, out _), "同一时刻的另一份也已过期");
            Assert.Equal(0, store.HomeKeys.Count, "过期项都不再可命中");
        });

        Test("快照仓库：超过条目上限不写入，并清掉同键旧快照", () =>
        {
            var store = new BrowseSnapshotStore();
            var sources = new[] { new SnapshotSource("a", new object()) };
            store.StoreLibrary("big", sources, Library([Item("old")]));

            var tooMany = Enumerable.Range(0, BrowseSnapshotStore.MaxItemsPerSnapshot + 1)
                .Select(index => Item($"item-{index}")).ToArray();
            store.StoreLibrary("big", sources, Library(tooMany));

            Assert.True(!store.TryGetLibrary("big", sources, out _), "超限内容不能截断后冒充完整一页");
            Assert.Equal(0, store.LibraryKeys.Count);
        });

        Test("快照仓库：显式失效只丢对应查询", () =>
        {
            var store = new BrowseSnapshotStore();
            var sources = new[] { new SnapshotSource("a", new object()) };
            store.StoreLibrary("movies", sources, Library([Item("m")]));
            store.StoreLibrary("shows", sources, Library([Item("s")]));

            store.InvalidateLibrary("movies");

            Assert.True(!store.TryGetLibrary("movies", sources, out _));
            Assert.True(store.TryGetLibrary("shows", sources, out _), "无关查询的缓存不能被一起清掉");
        });

        Test("LRU：删除指定项不影响其他项与顺序", () =>
        {
            var cache = new Momoka.Infrastructure.LruCache<string, int>(3);
            cache.Set("a", 1);
            cache.Set("b", 2);
            cache.Set("c", 3);

            Assert.True(cache.Remove("b"), "已存在的键应能删除");
            Assert.True(!cache.Remove("b"), "重复删除应返回 false");
            Assert.True(cache.Keys.SequenceEqual(["c", "a"]), "删除不应打乱其余最近使用顺序");
        });
    }

    private static EmbyItem Item(string id)
    {
        var item = new EmbyItem
        {
            Id = id,
            Name = $"条目 {id}",
            Type = EmbyItemType.Movie,
            PrimaryImageTag = "poster",
            ProductionYear = 2024,
            RunTimeTicks = 7_200_000_000,
            UserData = new EmbyUserData { Played = false }
        };
        item.MediaSources.Add(new MediaSource { Id = "source", Size = 1_000, Container = "mkv" });
        return item;
    }

    private static LibrarySnapshot Library(IReadOnlyList<EmbyItem> items) => new()
    {
        Items = items,
        Total = items.Count,
        FirstPage = true,
        UpcomingReadCount = 0,
        ResumeRead = [],
        Cursor = null
    };

    private static HomeSnapshot Home(IReadOnlyList<EmbyItem> items) => new()
    {
        Rows = [new HomeRowSnapshot("resume", items, null)],
        SlideItems = [],
        LoadedCount = items.Count,
        Subheading = null,
        Banner = true
    };
}
