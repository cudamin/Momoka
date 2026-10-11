using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class LibrarySearchTests
{
    public static void Register()
    {
        Test("媒体库搜索：分类改变查询范围且保留服务器相关性顺序", () =>
        {
            foreach (var (category, type) in new[] { (LibrarySearchCategory.Series, "Series"),
                (LibrarySearchCategory.Movies, "Movie"), (LibrarySearchCategory.Episodes, "Episode") })
            {
                var query = LibrarySearch.Query("  再见 & hello  ", category, 40, 20)!;
                var parameters = query.ToParameters().ToDictionary(pair => pair.Key, pair => pair.Value);
                Assert.Equal(type, parameters["IncludeItemTypes"]);
                Assert.Equal("再见 & hello", parameters["SearchTerm"]);
                Assert.Equal("40", parameters["StartIndex"]);
                Assert.Equal("20", parameters["Limit"]);
                Assert.Equal("true", parameters["Recursive"]);
                Assert.False(parameters.ContainsKey("SortBy"));
                Assert.False(parameters.ContainsKey("Filters"));
                Assert.Contains("%26", query.ToQueryString());
            }
            Assert.True(LibrarySearch.Query("  ", LibrarySearchCategory.TopResults, 0, 20) is null);
            Assert.True(LibrarySearch.Query("词", LibrarySearchCategory.TopResults, 0, 20)!.IncludeItemTypes.Contains("Movie"));
            Assert.True(LibrarySearch.Query("词", LibrarySearchCategory.TopResults, 0, 20)!.IncludeItemTypes.Contains("Episode"));
        });
        Test("最近搜索：关键词升级成选中媒体、去重置顶并限制数量", () =>
        {
            var settings = new AppSettings();
            var connection = Connection();
            RecentSearch.Remember(settings.Ui, connection, "  测试作品 ");
            RecentSearch.Remember(settings.Ui, connection, "测试作品", new EmbyItem
            { Id = "item", Name = "测试作品", Type = EmbyItemType.Series, ImageTags = new() { ["Primary"] = "poster" } });
            RecentSearch.Remember(settings.Ui, connection, "测试作品");
            var entry = RecentSearch.Read(settings.Ui, connection).Single();
            Assert.Equal("item", entry.ItemId);
            Assert.Equal("poster", entry.ToItem().PrimaryImageTag);
            for (var index = 0; index < 25; index++) RecentSearch.Remember(settings.Ui, connection, "词" + index);
            Assert.Equal(20, RecentSearch.Read(settings.Ui, connection).Count);
            Assert.Equal("词24", RecentSearch.Read(settings.Ui, connection)[0].Term);
        });
        Test("最近搜索：账号隔离、逐条移除及清除不影响另一个身份", () =>
        {
            var ui = new UiSettings();
            var first = Connection();
            var second = first with { UserId = "other" };
            var server = first with { ApiBase = new Uri("https://other.invalid/") };
            RecentSearch.Remember(ui, first, "甲");
            RecentSearch.Remember(ui, second, "乙");
            Assert.Equal(0, RecentSearch.Read(ui, server).Count);
            RecentSearch.Remove(ui, first, RecentSearch.Read(ui, first).Single());
            Assert.Equal(0, RecentSearch.Read(ui, first).Count);
            Assert.Equal("乙", RecentSearch.Read(ui, second).Single().Term);
            RecentSearch.Remember(ui, first, "丙");
            RecentSearch.Clear(ui, first);
            Assert.Equal("乙", RecentSearch.Read(ui, second).Single().Term);
        });
        Test("最近搜索：保存后恢复，偏好重置保留而导出不携带", () =>
        {
            var settings = new AppSettings();
            RecentSearch.Remember(settings.Ui, Connection(), "测试");
            SettingsPreferences.Apply(settings, new AppSettings());
            var restored = SettingsMigration.FromJson(JsonSerializer.Serialize(settings), PassthroughSecretProtector.Instance);
            Assert.Equal("测试", RecentSearch.Read(restored.Ui, Connection()).Single().Term);
            Assert.Equal(0, SettingsPreferences.ToBackupDocument(settings).Ui.RecentSearches.Count);
            Assert.DoesNotContain("synthetic-secret", JsonSerializer.Serialize(settings.Ui.RecentSearches));
        });
        Test("最近搜索：损坏的空记录不导致搜索页崩溃", () =>
        {
            var settings = new AppSettings();
            settings.Ui.RecentSearches[BrowsingPreferences.IdentityKey(Connection())] = [null!, new(),
                new() { Term = "有效", Name = "有效" }, new() { Term = "有效", Name = "重复" },
                new() { Term = "坏路径", Name = "坏路径", ItemId = "../item" }];
            SettingsMigration.Normalize(settings);
            Assert.Equal("有效", RecentSearch.Read(settings.Ui, Connection()).Single().Term);
        });
    }

    private static EmbyConnection Connection() => new(new Uri("https://search.invalid/media/"), "synthetic-secret",
        "user", "user", "fixture", DeviceIdentity.Create("fixture", "test"));
}
