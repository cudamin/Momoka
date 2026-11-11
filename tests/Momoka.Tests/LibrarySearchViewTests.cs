using System.Collections;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Momoka.Emby;
using static Momoka.Tests.LibraryResumePagingTests;

namespace Momoka.Tests;

internal static class LibrarySearchViewTests
{
    internal static void Register(ShellFixture shell)
    {
        Case("搜索真实VM：Jellyfin热门两路独立分页，无重复漏项，分组与来源保持", async () =>
        {
            using var fixture = new Fixture(shell, 0, firstKind: MediaServerKind.Jellyfin);
            var requests = new List<Uri>();
            fixture.Transport.SearchReply = uri =>
            {
                requests.Add(uri);
                var people = uri.AbsolutePath.EndsWith("/Persons", StringComparison.Ordinal);
                return Task.FromResult(Reply(uri, people ? 3 : 23, people ? "Person" : "Movie"));
            };
            var vm = fixture.CreateSearchView("词");
            try
            {
                await (Task)shell.Invoke(vm, "LoadAsync", true)!;
                Assert.Equal(23, Cards(vm).Count);
                Assert.Equal("共 26 项", shell.Get<string>(vm, "TotalLabel"));
                Assert.Equal("Visible", shell.Get<object>(vm, "ExtendedSearchCategoriesVisibility").ToString());
                Assert.True(shell.Get<bool>(vm, "HasMore"));
                var groups = Sequence(shell.Get<object>(vm, "SearchGroups"));
                Assert.Equal("影片,人物", string.Join(',', groups.Select(group => shell.Get<string>(group, "Title"))));
                var cards = shell.Get<object>(vm, "Cards");
                await (Task)shell.Invoke(vm, "LoadAsync", false)!;
                Assert.True(ReferenceEquals(cards, shell.Get<object>(vm, "Cards")));
                Assert.Equal(26, Cards(vm).Count);
                Assert.Equal(26, Cards(vm).Select(card => shell.Get<EmbyItem>(card, "Item").IdentityKey).Distinct().Count());
                Assert.False(shell.Get<bool>(vm, "HasMore"));
                Assert.Equal("0,20", string.Join(',', requests.Where(uri => uri.AbsolutePath.EndsWith("/Items", StringComparison.Ordinal))
                    .Select(uri => Query(uri)["StartIndex"])));
                Assert.Equal(1, requests.Count(uri => uri.AbsolutePath.EndsWith("/Persons", StringComparison.Ordinal)));
                Assert.True(Cards(vm).All(card => shell.Get<EmbyItem>(card, "Item").Source?.Key == fixture.Source("a").Key));
            }
            finally { ((IDisposable)vm).Dispose(); }
        });

        Case("搜索真实VM：人物分类只问有能力账号，跨源同ID仍独立，旧响应不回新词", async () =>
        {
            using var fixture = new Fixture(shell, 0, secondSource: true, firstKind: MediaServerKind.Jellyfin);
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Transport.SearchReply = async uri =>
            {
                if (Query(uri).GetValueOrDefault("SearchTerm") == "旧")
                {
                    entered.TrySetResult();
                    await gate.Task;
                }
                return Reply(uri, 1, "Person", commonId: true);
            };
            var vm = fixture.CreateSearchView("旧");
            try
            {
                SetCategory(vm, "People");
                var old = (Task)shell.Invoke(vm, "LoadAsync", true)!;
                await entered.Task;
                shell.Invoke(vm, "PrepareSearch", "新");
                await (Task)shell.Invoke(vm, "LoadAsync", true)!;
                Assert.Equal(2, Cards(vm).Count);
                Assert.Equal(2, Cards(vm).Select(card => shell.Get<EmbyItem>(card, "Item").IdentityKey).Distinct().Count());
                gate.TrySetResult();
                await old;
                Assert.Equal(2, Cards(vm).Count);
                Assert.True(Cards(vm).All(card => shell.Get<EmbyItem>(card, "Item").Name == "新"));
                Assert.Equal("共 2 项", shell.Get<string>(vm, "TotalLabel"));
            }
            finally { gate.TrySetResult(); ((IDisposable)vm).Dispose(); }
        });

        Case("搜索真实VM：删除已读媒体后重新核对两路偏移，下一页不会遗漏末项", async () =>
        {
            using var fixture = new Fixture(shell, 0, firstKind: MediaServerKind.Jellyfin);
            var removed = false;
            fixture.Transport.SearchReply = uri => Task.FromResult(Reply(uri,
                uri.AbsolutePath.EndsWith("/Persons", StringComparison.Ordinal) ? 3 : 23,
                uri.AbsolutePath.EndsWith("/Persons", StringComparison.Ordinal) ? "Person" : "Movie", omitFirst: removed));
            var vm = fixture.CreateSearchView("词");
            try
            {
                await (Task)shell.Invoke(vm, "LoadAsync", true)!;
                var card = Cards(vm).Single(card => shell.Get<EmbyItem>(card, "Item").Id == "Movie-0");
                removed = true;
                await (Task<bool>)shell.Invoke(vm, "RemoveAsync", card, shell.Get<CancellationToken>(vm, "QueryToken"), true)!;
                await (Task)shell.Invoke(vm, "LoadAsync", false)!;
                Assert.Equal(25, Cards(vm).Count);
                Assert.True(Cards(vm).Any(card => shell.Get<EmbyItem>(card, "Item").Id == "Movie-22"));
                Assert.False(Cards(vm).Any(card => shell.Get<EmbyItem>(card, "Item").Id == "Movie-0"));
                Assert.False(shell.Get<bool>(vm, "HasMore"));
            }
            finally { ((IDisposable)vm).Dispose(); }
        });

        foreach (var kind in new[] { MediaServerKind.Plex, MediaServerKind.FnNas })
            Case($"搜索真实VM：纯{kind}收起扩展分类，旧不可用分类不发请求", async () =>
            {
                using var fixture = new Fixture(shell, 0, firstKind: kind);
                var vm = fixture.CreateSearchView("词");
                try
                {
                    Assert.Equal("Collapsed", shell.Get<object>(vm, "ExtendedSearchCategoriesVisibility").ToString());
                    await (Task)shell.Invoke(vm, "ChooseSearchCategoryAsync", Category("People"))!;
                    Assert.Equal(0, fixture.RequestCount);
                    SetCategory(vm, "People");
                    await (Task)shell.Invoke(vm, "LoadAsync", true)!;
                    Assert.Equal(0, fixture.RequestCount);
                    Assert.Equal(0, Cards(vm).Count);
                    Assert.False(shell.Get<bool>(vm, "HasMore"));
                }
                finally { ((IDisposable)vm).Dispose(); }
            });

        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
            Case($"搜索导航真实VM：{kind}播放列表用成员协议并收起无效排序筛选和字母", async () =>
            {
                using var fixture = new Fixture(shell, 0, firstKind: kind);
                var requests = new List<Uri>();
                fixture.Transport.SearchReply = uri =>
                {
                    requests.Add(uri);
                    return Task.FromResult(Reply(uri, 3, "Movie"));
                };
                var vm = fixture.CreateLibraryView(collectionType: null, parentType: EmbyItemType.Playlist);
                try
                {
                    await (Task)shell.Invoke(vm, "LoadAsync", true)!;
                    Assert.Equal(3, Cards(vm).Count);
                    Assert.Equal("Collapsed", shell.Get<object>(vm, "SortFilterVisibility").ToString());
                    Assert.Equal("Collapsed", shell.Get<object>(vm, "FilterVisibility").ToString());
                    Assert.Equal("Collapsed", shell.Get<object>(vm, "AlphaVisibility").ToString());
                    Assert.Equal(kind == MediaServerKind.Jellyfin ? "/emby/Playlists/library/Items" : "/emby/Users/fixture-user/Items", requests.Single().AbsolutePath);
                    if (kind == MediaServerKind.Emby) Assert.Equal("ListItemOrder", Query(requests.Single())["SortBy"]);
                }
                finally { ((IDisposable)vm).Dispose(); }
            });

        List<object> Cards(object vm) => Sequence(shell.Get<object>(vm, "Cards"));
    }

    private static void Case(string name, Func<Task> body) => TestHarness.Test(name, () => SingleThread.Run(body));
    private static List<object> Sequence(object value) => ((IEnumerable)value).Cast<object>().ToList();
    private static LibrarySearchCategory Category(string value)
    {
        Assert.True(Enum.TryParse<LibrarySearchCategory>(value, out var category), "缺少搜索分类 " + value);
        return category;
    }
    private static void SetCategory(object vm, string category)
    {
        var request = vm.GetType().GetField("_request", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
        request.GetType().GetProperty("SearchCategory")!.SetValue(request, Category(category));
    }
    private static Dictionary<string, string> Query(Uri uri) => uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(value => value.Split('=', 2)).ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]));
    private static HttpResponseMessage Reply(Uri uri, int count, string type, bool commonId = false, bool omitFirst = false)
    {
        var query = Query(uri);
        var start = int.Parse(query.GetValueOrDefault("StartIndex") ?? "0", System.Globalization.CultureInfo.InvariantCulture);
        var limit = int.Parse(query["Limit"], System.Globalization.CultureInfo.InvariantCulture);
        var rows = Enumerable.Range(0, count).Select(index => new EmbyItem
        { Id = commonId ? "same" : type + "-" + index, Name = query.GetValueOrDefault("SearchTerm") ?? "匿名", Type = type }).ToList();
        if (omitFirst && type == "Movie") rows.RemoveAt(0);
        var items = limit == 0 ? rows : rows.Skip(start).Take(limit).ToList();
        return new(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(new ItemsResult { Items = items, TotalRecordCount = rows.Count }), Encoding.UTF8, "application/json") };
    }
}
