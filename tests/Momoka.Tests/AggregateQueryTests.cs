using Momoka.Configuration;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class AggregateQueryTests
{
    public static void Register()
    {
        Case("聚合分页：不同来源并发读取、交错呈现，保留相同条目 ID", async () =>
        {
            var cursor = new AggregateQueryCursor(["a", "b"]);
            var gate = new TaskCompletionSource<ItemsResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = new List<string>();
            var pending = cursor.ReadAsync((key, start, _) =>
            {
                calls.Add(key);
                Assert.Equal(0, start);
                return gate.Task;
            }, CancellationToken.None);
            Assert.Equal("a,b", string.Join(',', calls), "首个来源还未返回时其他来源已经开始读取");
            gate.SetResult(Page(2, "same", "second"));
            var result = await pending;
            Assert.Equal("same,same,second,second", string.Join(',', result.Result.Items.Select(item => item.Id)));
            Assert.Equal(4, result.Result.TotalRecordCount);
            Assert.False(cursor.HasMore);
        });

        Case("聚合分页：各来源独立 cursor，短页不会让另一个来源跳过结果", async () =>
        {
            var cursor = new AggregateQueryCursor(["a", "b"]);
            var calls = new List<string>();
            Task<ItemsResult> Read(string key, int offset, CancellationToken _)
            {
                calls.Add($"{key}:{offset}");
                return Task.FromResult(key == "a" ? Page(1, "a0")
                    : offset == 0 ? Page(3, "b0", "b1") : Page(3, "b2"));
            }
            var first = await cursor.ReadAsync(Read, CancellationToken.None);
            Assert.Equal("a0,b0,b1", string.Join(',', first.Result.Items.Select(item => item.Id)));
            Assert.True(cursor.HasMore);
            var second = await cursor.ReadAsync(Read, CancellationToken.None);
            Assert.Equal("b2", second.Result.Items.Single().Id);
            Assert.Equal("a:0,b:0,b:2", string.Join(',', calls));
            Assert.Equal(4, second.Result.TotalRecordCount);
            Assert.False(cursor.HasMore);
        });

        Case("聚合分页：一个来源离线仍返回其他来源，刷新使用新游标重试", async () =>
        {
            var cursor = new AggregateQueryCursor(["offline", "online"]);
            var result = await cursor.ReadAsync((key, _, _) => key == "offline"
                ? Task.FromException<ItemsResult>(new HttpRequestException("fixture offline"))
                : Task.FromResult(Page(1, "kept")), CancellationToken.None);
            Assert.Equal("kept", result.Result.Items.Single().Id);
            Assert.True(result.Failures.ContainsKey("offline"));
            Assert.False(cursor.HasMore);
            var refreshed = new AggregateQueryCursor(["offline", "online"]);
            var retry = await refreshed.ReadAsync((key, _, _) => Task.FromResult(Page(1, key)), CancellationToken.None);
            Assert.Equal(2, retry.Result.Items.Count);
            Assert.Equal(0, retry.Failures.Count);
        });

        Case("聚合分页：取消后的迟到结果不消费任何来源的游标", async () =>
        {
            var cursor = new AggregateQueryCursor(["a", "b"]);
            using var cancellation = new CancellationTokenSource();
            var gate = new TaskCompletionSource<ItemsResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = cursor.ReadAsync((_, _, _) => gate.Task, cancellation.Token);
            cancellation.Cancel();
            gate.SetResult(Page(2, "late"));
            var cancelled = false;
            try { await pending; } catch (OperationCanceledException) { cancelled = true; }
            Assert.True(cancelled);
            var positions = new List<int>();
            await cursor.ReadAsync((_, offset, _) =>
            {
                positions.Add(offset);
                return Task.FromResult(Page(0));
            }, CancellationToken.None);
            Assert.True(positions.SequenceEqual([0, 0]));
        });

        Case("聚合分页：继续观看条目移除仅回退对应来源，下一页不漏项", async () =>
        {
            var cursor = new AggregateQueryCursor(["a", "b"]);
            await cursor.ReadAsync((key, _, _) => Task.FromResult(Page(4, key + "0", key + "1")), CancellationToken.None);
            cursor.RemoveConsumed("a");
            var positions = new List<string>();
            await cursor.ReadAsync((key, offset, _) =>
            {
                positions.Add($"{key}:{offset}");
                return Task.FromResult(Page(0));
            }, CancellationToken.None);
            Assert.Equal("a:1,b:2", string.Join(',', positions));
        });

        Test("聚合主页：相同库 ID 按服务器账号保留独立货架，key 可还原原始 ID", () =>
        {
            var first = Scope("a", "user").Bind(new EmbyItem { Id = "same:id", Name = "电影" });
            var second = Scope("a", "other").Bind(new EmbyItem { Id = "same:id", Name = "电影" });
            var third = Scope("b", "user").Bind(new EmbyItem { Id = "same:id", Name = "电影" });
            var rows = HomeLayout.Plan([], [first, second, third]).Where(row => HomeLayout.LibraryId(row.Key) is not null).ToArray();
            Assert.Equal(3, rows.Length);
            Assert.Equal(3, rows.Select(row => row.Key).Distinct().Count());
            Assert.True(rows.All(row => HomeLayout.LibraryId(row.Key) == "same:id"));
            Assert.True(rows.All(row => row.Title == HomeLayout.LibraryTitle(first.Name)));
            Assert.Equal(rows[0].Title, rows[1].Title);
            Assert.DoesNotContain("user", rows[0].Title);
            Assert.DoesNotContain("other", rows[1].Title);
        });

        Test("来源文字：只追加服务器名称，隐藏、空内容与无来源不留下分隔符", () =>
        {
            var source = Scope("家庭库", "账号甲");
            Assert.Equal("2024 · 100 分钟 · 家庭库", SourceCaption.Append("2024 · 100 分钟", source));
            Assert.Equal("2024 · 100 分钟", SourceCaption.Append("2024 · 100 分钟", source, showSource: false));
            Assert.Equal("家庭库", SourceCaption.Append(null, source));
            Assert.Equal("家庭库", SourceCaption.Append(" ", source));
            Assert.Equal("", SourceCaption.Append(null, source, showSource: false));
            Assert.Equal("2024", SourceCaption.Append("2024", null));
            Assert.Equal("", SourceCaption.Append(null, null));
            Assert.Equal("家庭库 · 账号甲", source.DisplayName, "内容显示不改变账号管理身份");

            Assert.Equal("家庭库 · 电影", SourceCaption.Prefix("电影", source));
            Assert.Equal("最近添加 · 电影", SourceCaption.Prefix("最近添加 · 电影", source, showSource: false));
            Assert.Equal("电影", SourceCaption.Prefix("电影", null));
            Assert.Equal("家庭库", SourceCaption.Prefix("", source));
            Assert.Equal("", SourceCaption.Prefix(null, source, showSource: false));
        });

        Test("聚合主页：移除来源文字仍保留独立货架、排列和勾选", () =>
        {
            var first = Scope("a", "user").Bind(new EmbyItem { Id = "same", Name = "电影" });
            var second = Scope("a", "other").Bind(new EmbyItem { Id = "same", Name = "电影" });
            var single = HomeLayout.Plan([new HomeRowSetting { Key = HomeLayout.LibraryKey(first), Visible = false,
                Title = "最近添加 · 电影 · a · user" }], [first]);
            Assert.Equal("最近添加 · 电影", single[0].Title);
            var multiple = HomeLayout.Plan(HomeLayout.Save(single), [first, second]);
            Assert.Equal("最近添加 · 电影", multiple[0].Title);
            Assert.False(multiple[0].Visible);
            var restored = HomeLayout.Plan(HomeLayout.Save(multiple), [first, second]);
            Assert.True(multiple.Select(row => row.Key).SequenceEqual(restored.Select(row => row.Key)));
            Assert.False(restored[0].Visible);
            Assert.Equal(2, restored.Count(row => HomeLayout.LibraryId(row.Key) == "same"));
            Assert.True(restored.Where(row => HomeLayout.LibraryId(row.Key) is not null).All(row => row.Title == "最近添加 · 电影"));
        });

        Test("聚合轮播：同一来源的同剧合并，不同来源相同剧 ID 分别保留", () =>
        {
            var first = Scope("a", "user");
            var second = Scope("b", "user");
            EmbyItem Episode(EmbySessionScope source, string id) => source.Bind(new EmbyItem
            {
                Id = id,
                Type = EmbyItemType.Episode,
                SeriesId = "same-series",
                BackdropImageTags = ["backdrop"]
            });
            var slides = HomeCarousel.Slides([Episode(first, "1"), Episode(first, "2"), Episode(second, "1")]);
            Assert.Equal(2, slides.Count);
            Assert.False(slides[0].IdentityKey == slides[1].IdentityKey);
        });

        Test("聚合主页：迁移旧库偏好，来源临时离线后保留版面位置和勾选", () =>
        {
            var first = Scope("a", "user").Bind(new EmbyItem { Id = "same", Name = "电影" });
            var second = Scope("b", "user").Bind(new EmbyItem { Id = "same", Name = "电影" });
            var plan = HomeLayout.Plan([new HomeRowSetting { Key = HomeLayout.LibraryKey("same"), Visible = false }], [first, second]);
            Assert.Equal(HomeLayout.LibraryKey(first), plan[0].Key);
            Assert.False(plan[0].Visible);
            var offline = HomeLayout.Plan(HomeLayout.Save(plan), [second]);
            Assert.Equal(HomeLayout.LibraryKey(first), offline[0].Key);
            Assert.False(offline[0].Visible);
            var restored = HomeLayout.Plan(HomeLayout.Save(offline), [first, second]);
            Assert.Equal(HomeLayout.LibraryKey(first), restored[0].Key);
            Assert.False(restored[0].Visible);
        });
    }

    private static ItemsResult Page(int total, params string[] ids) => new()
    {
        Items = ids.Select(id => new EmbyItem { Id = id }).ToList(),
        TotalRecordCount = total
    };

    private static EmbySessionScope Scope(string server, string account)
    {
        var connection = new EmbyConnection(new Uri($"https://{server}.invalid/"), "fixture", account, account,
            server, DeviceIdentity.Create("aggregate-fixture", "test"));
        return new EmbySessionScope(null!, new EmbySessionScope.Identity(new EmbyClient(null!, connection),
            new ServerProfile { Id = server, Name = server }, new AccountProfile { Id = account, Username = account }));
    }

    private static void Case(string name, Func<Task> run) =>
        Test(name, () => run().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
}
