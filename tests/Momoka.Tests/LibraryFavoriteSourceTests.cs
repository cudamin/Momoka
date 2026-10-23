using System.Text.Json.Nodes;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.FnNas;
using static Momoka.Tests.LibraryResumePagingTests;

namespace Momoka.Tests;

/// <summary>Same real VM fixture and source/HTTP boundaries as Resume paging; no duplicated VM behavior.</summary>
internal static class LibraryFavoriteSourceTests
{
    internal static int Run(string shellAssemblyPath, string sourceRoot)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("收藏切来源真实 VM 回归（假 HTTP／单线程；无登录、窗口或写入）");
        try
        {
            var shell = new ShellFixture(shellAssemblyPath, sourceRoot);
            Register(shell);
            return TestHarness.Run();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static Fixture New(ShellFixture shell, bool third = false) => new(shell, 1,
        secondSource: true, favorites: true, secondKind: MediaServerKind.FnNas, thirdSource: third);

    private static void Register(ShellFixture shell)
    {
        Case("R-023 Emby播放时长加题材切飞牛，查询合法且实际读取收藏", async () =>
        {
            using var fixture = New(shell);
            await fixture.Load();
            SetEmbyState(fixture);
            Assert.True(await fixture.Choose(fixture.Source("b").Key));
            Assert.True(FnNasMediaAdapter.SupportsSort(fixture.Query().SortBy));
            Assert.True(fixture.Query().Filters?.IsBlank == true);
            Assert.False(fixture.Get<bool>("NoticeOpen"));
            Assert.Equal("b-favorite", fixture.Ids().Single());
            Assert.Equal(1, fixture.Transport.FavoriteBodies.Count);
            var body = JsonNode.Parse(fixture.Transport.FavoriteBodies.Single().Body)!.AsObject();
            Assert.Equal("create_time", body["sort_column"]!.GetValue<string>());
            Assert.Equal(0, fixture.Get<int>("FilterCount"));
            Assert.Equal("筛选", fixture.Get<string>("FilterTooltip"));
        });
        Case("R-023 切飞牛时排序菜单勾选与标签使用飞牛默认", async () =>
        {
            using var fixture = New(shell);
            await fixture.Load();
            SetEmbyState(fixture);
            await fixture.Choose(fixture.Source("b").Key);
            Assert.Equal(FnNasSortCatalog.Default.Value, fixture.Get<string>("SortKey"));
            Assert.Equal(FnNasSortCatalog.Default.Label, fixture.Get<string>("SortLabel"));
            Assert.Equal(FnNasSortCatalog.Default.DescendingByDefault, fixture.Get<bool>("SortDescending"));
            Assert.Equal(0, fixture.Get<int>("FilterCount"));
            Assert.Equal(4, Sequence(fixture.Invoke("SortEntries")!).Count);
        });
        Case("R-023 来回切源重算本页条件，不改既有库偏好或额外保存", async () =>
        {
            using var fixture = New(shell);
            fixture.Settings.Settings.Ui.Sort["unrelated-library"] = new LibrarySort { By = EmbySortBy.Rating, Descending = true };
            var saved = new ItemFilters();
            saved.Genres.Add("persisted");
            fixture.Settings.Settings.Ui.Filters["unrelated-library"] = saved;
            await fixture.Load();
            SetEmbyState(fixture);
            await fixture.Choose(fixture.Source("b").Key);
            await fixture.Choose(fixture.Source("a").Key);
            Assert.True(fixture.Query().Filters?.IsBlank == true);
            Assert.Equal(EmbySortBy.Name, fixture.Query().SortBy);
            Assert.False(fixture.Get<bool>("NoticeOpen"));
            Assert.Equal("a-0", fixture.Ids().Single());
            Assert.True(ReferenceEquals(saved, fixture.Settings.Settings.Ui.Filters["unrelated-library"]));
            Assert.Equal("persisted", saved.Genres.Single());
            Assert.Equal(EmbySortBy.Rating, fixture.Settings.Settings.Ui.Sort["unrelated-library"].By);
        });
        Case("R-023 切全部账号时不再向飞牛发送旧Emby条件", async () =>
        {
            using var fixture = New(shell);
            await fixture.Load();
            SetEmbyState(fixture);
            Assert.True(await fixture.Choose(null));
            Assert.False(fixture.Get<bool>("NoticeOpen"));
            Assert.Equal(2, fixture.Cards.Count);
            Assert.True(fixture.Ids().Contains("a-0"));
            Assert.True(fixture.Ids().Contains("b-favorite"));
        });
        Case("R-023 账号选择复位返回记录，卡片集合保持同一对象", async () =>
        {
            using var fixture = New(shell);
            await fixture.Load();
            var cards = fixture.Get<object>("Cards");
            fixture.Request.GetType().GetProperty("ScrollOffset")!.SetValue(fixture.Request, 400d);
            fixture.Request.GetType().GetProperty("LoadedCount")!.SetValue(fixture.Request, 40);
            await fixture.Choose(fixture.Source("b").Key);
            Assert.True(ReferenceEquals(cards, fixture.Get<object>("Cards")));
            Assert.Equal(0d, fixture.Read<double>(fixture.Request, "ScrollOffset"));
            Assert.Equal(0, fixture.Read<int>(fixture.Request, "LoadedCount"));
            Assert.Equal(fixture.Source("b").Key, fixture.Read<string>(fixture.Request, "FavoriteAccountKey"));
        });
        Case("R-023 无效或重复选择不清条件、不触发HTTP", async () =>
        {
            using var fixture = New(shell);
            await fixture.Load();
            SetEmbyState(fixture);
            var requests = fixture.Transport.Requests.Count;
            Assert.False(await fixture.Choose("unknown-source"));
            Assert.False(await fixture.Choose(fixture.Source("a").Key));
            Assert.Equal(requests, fixture.Transport.Requests.Count);
            Assert.Equal("Runtime,SortName", fixture.Get<string>("SortKey"));
            Assert.Equal("old-genre", fixture.Get<ItemFilters>("Filters").Genres.Single());
        });
        Case("R-023 飞牛换来源重建标签，不沿用旧选择和题材列表", async () =>
        {
            using var fixture = New(shell, third: true);
            await fixture.Load();
            await fixture.Choose(fixture.Source("b").Key);
            await (Task)fixture.Invoke("ToggleFnNasFilterAsync")!;
            Assert.True(Genres(fixture).Contains("b-genre"));
            await fixture.Choose(fixture.Source("c").Key);
            Assert.False(fixture.Get<bool>("FnNasFilterOpen"));
            Assert.Equal(0, FilterRows(fixture).Count);
            await (Task)fixture.Invoke("ToggleFnNasFilterAsync")!;
            Assert.True(Genres(fixture).Contains("c-genre"));
            Assert.False(Genres(fixture).Contains("b-genre"));
            Assert.Equal("b,c", string.Join(',', fixture.Transport.TagHosts));
        });
        foreach (var oldFails in new[] { false, true })
            Case($"R-023 旧标签{(oldFails ? "失败" : "成功")}迟到不覆盖新来源加载状态", async () =>
            {
                using var fixture = New(shell, third: true);
                await fixture.Load();
                await fixture.Choose(fixture.Source("b").Key);
                var oldGate = fixture.Transport.HoldTags("b");
                var old = (Task)fixture.Invoke("ToggleFnNasFilterAsync")!;
                await oldGate.Entered.Task;
                await fixture.Choose(fixture.Source("c").Key);
                var newGate = fixture.Transport.HoldTags("c");
                var current = (Task)fixture.Invoke("ToggleFnNasFilterAsync")!;
                await newGate.Entered.Task;
                fixture.Transport.FailReleasedTag = oldFails;
                oldGate.Release.TrySetResult();
                await old;
                fixture.Transport.FailReleasedTag = false;
                Assert.True(fixture.Get<bool>("FnNasFilterLoading"), "旧finally不能把新请求标记结束");
                Assert.False(fixture.Get<bool>("NoticeOpen"), "旧来源失败不污染新页提示");
                Assert.Equal(0, FilterRows(fixture).Count);
                newGate.Release.TrySetResult();
                await current;
                Assert.False(fixture.Get<bool>("FnNasFilterLoading"));
                Assert.True(Genres(fixture).Contains("c-genre"));
                Assert.False(Genres(fixture).Contains("b-genre"));
            });
        foreach (var oldFails in new[] { false, true })
            Case($"R-023 新来源标签先完成，旧{(oldFails ? "失败" : "成功")}不覆盖已就绪面板", async () =>
            {
                using var fixture = New(shell, third: true);
                await fixture.Load();
                await fixture.Choose(fixture.Source("b").Key);
                var oldGate = fixture.Transport.HoldTags("b");
                var old = (Task)fixture.Invoke("ToggleFnNasFilterAsync")!;
                await oldGate.Entered.Task;
                await fixture.Choose(fixture.Source("c").Key);
                await (Task)fixture.Invoke("ToggleFnNasFilterAsync")!;
                fixture.Transport.FailReleasedTag = oldFails;
                oldGate.Release.TrySetResult();
                await old;
                fixture.Transport.FailReleasedTag = false;
                Assert.False(fixture.Get<bool>("FnNasFilterLoading"));
                Assert.False(fixture.Get<bool>("NoticeOpen"));
                Assert.True(Genres(fixture).Contains("c-genre"));
                Assert.False(Genres(fixture).Contains("b-genre"));
                Assert.Equal("c-favorite", fixture.Ids().Single());
            });
        Case("R-023 VM释放后旧标签finally仍可完成，不读取已释放取消源", async () =>
        {
            using var fixture = New(shell);
            await fixture.Load();
            await fixture.Choose(fixture.Source("b").Key);
            var gate = fixture.Transport.HoldTags("b");
            var pending = (Task)fixture.Invoke("ToggleFnNasFilterAsync")!;
            await gate.Entered.Task;
            fixture.DisposeView();
            gate.Release.TrySetResult();
            await pending;
            Assert.Equal(0, FilterRows(fixture).Count);
            Assert.False(fixture.Get<bool>("FnNasFilterLoading"));
        });
        Case("R-023 隐藏离页后标签迟到不改当前行或进度", async () =>
        {
            using var fixture = New(shell);
            await fixture.Load();
            await fixture.Choose(fixture.Source("b").Key);
            var gate = fixture.Transport.HoldTags("b");
            var pending = (Task)fixture.Invoke("ToggleFnNasFilterAsync")!;
            await gate.Entered.Task;
            fixture.Invoke("Cancel");
            gate.Release.TrySetResult();
            await pending;
            Assert.Equal(0, FilterRows(fixture).Count);
            Assert.False(fixture.Get<bool>("NoticeOpen"));
        });
    }

    private static void SetEmbyState(Fixture fixture)
    {
        fixture.Set("SortKey", "Runtime,SortName");
        fixture.Invoke("ShowSort", "播放时长", true);
        fixture.Get<ItemFilters>("Filters").Genres.Add("old-genre");
        fixture.Invoke("ShowFilterState");
        Assert.Equal(1, fixture.Get<int>("FilterCount"));
    }

    private static List<object> Sequence(object value) => ((System.Collections.IEnumerable)value).Cast<object>().ToList();
    private static List<object> FilterRows(Fixture fixture) => Sequence(fixture.Get<object>("FnNasFilterRows"));
    private static string[] Genres(Fixture fixture)
    {
        var row = FilterRows(fixture).Single(row => fixture.Read<string>(row, "Key") == "genres");
        return Sequence(fixture.Read<object>(row, "Choices")).Select(choice => fixture.Read<string>(choice, "Value")).ToArray();
    }
    private static void Case(string name, Func<Task> body) => TestHarness.Test(name, () => SingleThread.Run(body));
}
