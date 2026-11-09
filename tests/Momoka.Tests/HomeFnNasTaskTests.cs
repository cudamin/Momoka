using System.Collections;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Momoka.Emby;
using static Momoka.Tests.LibraryResumePagingTests;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class HomeFnNasTaskTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static void Register(ShellFixture shell)
    {
        foreach (var scenario in new[] { "positive", "zero", "empty", "new", "new-retry", "unknown-positive", "failure", "leave" })
        {
            Test("飞牛首页任务完成：" + scenario + " 只更新目录与必要首读", () => SingleThread.Run(async () =>
            {
                using var fixture = new Fixture(shell, 1, firstKind: MediaServerKind.FnNas);
                var source = fixture.Source("a");
                var session = typeof(Fixture).GetField("_session", Members)!.GetValue(fixture)!;
                source.IdentityState.Client = new EmbyClient((EmbyHttp)typeof(EmbySession).GetField("_http", Members)!.GetValue(session)!,
                    source.CurrentConnection with { IsAdministrator = true });
                var home = fixture.CreateLoadingHome();
                foreach (var library in (IReadOnlyList<EmbyItem>)home.GetType().GetField("_libraryViews", Members)!.GetValue(home)!)
                    library.RecursiveItemCount = scenario == "unknown-positive" ? null : 1;
                var entries = (Array)home.GetType().GetField("_all", Members)!.GetValue(home)!;
                var shelves = entries.Cast<object>().Select(entry => entry.GetType().GetField("Item2")!.GetValue(entry)!).ToArray();
                var shelf = shelves.Single(row => shell.Get<string?>(row, "LibraryId") == "library");
                var cards = (IList)shell.Get<object>(shelf, "Cards");
                var card = shell.Invoke(shelf, "NewCard", source.Bind(new EmbyItem
                { Id = "old", Name = "旧媒体", Type = EmbyItemType.Movie }))!;
                if (scenario != "unknown-positive") cards.Add(card);
                shell.Invoke(home, "PublishShelves");
                shell.Set(home, "IsReady", true);
                var updates = 0;
                Action<EmbySessionScope, IReadOnlyList<EmbyItem>> updated = (scope, views) =>
                {
                    Assert.Equal(source.Key, scope.Key);
                    updates++;
                };
                var use = home.GetType().GetMethod("UseLibraryDirectory", Members);
                Assert.True(use is not null, "首页尚未接入飞牛任务目录更新");
                use!.Invoke(home, [updated]);
                var gate = new Gate(true);
                var failedFirst = false;
                var log = new Momoka.Diagnostics.RingBufferLogSink();
                Momoka.Diagnostics.Log.UseSink(log);
                fixture.Transport.FnNasHomeReply = async uri =>
                {
                    if (uri.AbsolutePath.EndsWith("mediadb/list", StringComparison.Ordinal))
                    {
                        if (scenario == "leave") { gate.Entered.TrySetResult(); await gate.Release.Task; }
                        if (scenario == "failure") return new(HttpStatusCode.ServiceUnavailable);
                        return Json(new
                        {
                            code = 0,
                            data = scenario == "empty" ? Array.Empty<object>()
                            : scenario is "new" or "new-retry" ? [new { guid = "library", name = "电影", category = "Movie" }, new { guid = "new", name = "新增库", category = "Movie" }]
                            : new object[] { new { guid = "library", name = "新库名", category = "Movie" } }
                        });
                    }
                    if (uri.AbsolutePath.EndsWith("mediadb/sum", StringComparison.Ordinal))
                        return Json(new { code = 0, data = new Dictionary<string, int> { ["library"] = scenario == "zero" ? 0 : 8, ["new"] = 1 } });
                    if (uri.AbsolutePath.EndsWith("tag/genres", StringComparison.Ordinal))
                        return Json(new { code = 0, data = Array.Empty<object>() });
                    if (uri.AbsolutePath.EndsWith("item/list", StringComparison.Ordinal))
                    {
                        if (scenario == "new-retry" && !failedFirst) { failedFirst = true; return new(HttpStatusCode.ServiceUnavailable); }
                        return Json(new { code = 0, data = new { list = new[] { new { guid = "new-item", type = "Movie", title = "新媒体" } }, total = 1 } });
                    }
                    throw new AssertionException("任务完成不得读取其他媒体接口：" + uri.AbsolutePath);
                };
                Task pending = Task.CompletedTask;
                try
                {
                    pending = (Task)shell.Invoke(home, "RefreshFnNasDirectoryAsync", source.Key)!;
                    if (scenario == "leave") { await gate.Entered.Task; shell.Invoke(home, "Cancel"); gate.Release.TrySetResult(); }
                    await pending;
                    if (scenario == "new-retry") await (Task)shell.Invoke(home, "RefreshFnNasDirectoryAsync", source.Key)!;
                    var liveShelves = ((IEnumerable)shell.Get<object>(home, "Shelves")).Cast<object>().ToArray();
                    if (scenario is "failure" or "leave") Assert.Equal(0, updates);
                    else Assert.Equal(scenario == "new-retry" ? 2 : 1, updates);
                    if (scenario is "positive" or "new" or "new-retry" or "failure")
                        Assert.True(liveShelves.Contains(shelf) && ReferenceEquals(cards[0], card), "已有正数货架与媒体卡保持原对象");
                    if (scenario == "positive") Assert.Equal("8 项", shell.Get<string>(shelf, "Note"));
                    if (scenario is "zero" or "empty") Assert.False(liveShelves.Contains(shelf), "成功空库应隐藏旧货架");
                    Assert.Equal(scenario == "new-retry" ? 2 : scenario is "new" or "unknown-positive" ? 1 : 0, fixture.Transport.Requests.Count(path => path.EndsWith("item/list", StringComparison.Ordinal)),
                        "只有新启用的货架首读，已有货架不重读：" + string.Join(" | ", log.Snapshot().Select(entry => entry.Message)));
                    if (scenario is "new" or "new-retry") Assert.True(liveShelves.Any(row => shell.Get<string?>(row, "LibraryId") == "new"));
                    if (scenario == "leave") Assert.False(fixture.Transport.Requests.Any(path => path.EndsWith("mediadb/sum", StringComparison.Ordinal)),
                        "离页取消后不得继续读取sum");
                }
                finally
                {
                    gate.Release.TrySetResult(); await pending; ((IDisposable)home).Dispose();
                    Momoka.Diagnostics.Log.UseSink(Momoka.Diagnostics.NullLogSink.Instance);
                }
            }));
        }
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(value)) };
}
