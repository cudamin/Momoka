using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Playback;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests
{
    internal static class ItemBatchPlaybackTests
    {
        internal static void Register()
        {
            Test("批量播放：收集服务可独立调用", () =>
                Assert.NotNull(typeof(EmbyItem).Assembly.GetType("Momoka.Emby.ItemBatchPlayback")));
            Test("批量播放：仅所选文件、保持首次顺序且去重", () => Run(async () =>
            {
                using var fixture = new Fixture();
                var first = fixture.Item("first");
                var second = fixture.Item("second");
                var result = await Collect(fixture.Source, [second, first, second]);
                EqualIds("second,first", result);
                Assert.Equal(0, fixture.Http.Requests.Count);
                Assert.True(result.All(item => ReferenceEquals(item.Source!.UserStateOwner, fixture.Source.UserStateOwner)));
            }));
            Test("批量播放：选中子集后再选剧、父子只播放一次", () => Run(async () =>
            {
                using var fixture = new Fixture();
                fixture.Http.Read = _ => Page(3,
                    Episode("episode-3", 3), Episode("episode-1", 1), Episode("episode-2", 2));
                var result = await Collect(fixture.Source,
                    [fixture.Item("episode-2", EmbyItemType.Episode), fixture.Item("series", EmbyItemType.Series), fixture.Item("movie")]);
                EqualIds("episode-2,episode-1,episode-3,movie", result);
                var query = fixture.Http.Requests.Single();
                Assert.Equal("series", query.Parameters["ParentId"]);
                Assert.Equal("Episode", query.Parameters["IncludeItemTypes"]);
                Assert.Equal("true", query.Parameters["Recursive"]);
            }));
            Test("批量播放：选中季只展开本季并保留集号顺序", () => Run(async () =>
            {
                using var fixture = new Fixture();
                fixture.Http.Read = _ => Page(2, Episode("first", 2), Episode("second", 1));
                var result = await Collect(fixture.Source, [fixture.Item("season", EmbyItemType.Season)]);
                EqualIds("first,second", result);
                var query = fixture.Http.Requests.Single();
                Assert.Equal("season", query.Parameters["ParentId"]);
                Assert.Equal(EmbySortBy.Number, query.Parameters["SortBy"]);
            }));
            foreach (var type in new[] { EmbyItemType.Folder, EmbyItemType.BoxSet, EmbyItemType.CollectionFolder })
                Test($"批量播放：{type}递归分页不混入未选目录", () => Run(async () =>
                {
                    using var fixture = new Fixture();
                    fixture.Http.Read = request => request.Parameters["StartIndex"] == "0"
                        ? Page(120, Enumerable.Range(0, 100).Select(index => Movie("item-" + index)).ToArray())
                        : Page(120, Enumerable.Range(100, 20).Select(index => Movie("item-" + index)).ToArray());
                    var result = await Collect(fixture.Source, [fixture.Item("selected", type)]);
                    Assert.Equal(120, result.Count);
                    Assert.Equal(2, fixture.Http.Requests.Count);
                    Assert.True(fixture.Http.Requests.All(request => request.Parameters["ParentId"] == "selected"
                        && request.Parameters["Recursive"] == "true" && request.Parameters["Limit"] == "100"));
                    Assert.Equal("100", fixture.Http.Requests[1].Parameters["StartIndex"]);
                }));
            Test("批量播放：超过一页的剧全部收集后按播出时间排列", () => Run(async () =>
            {
                using var fixture = new Fixture();
                fixture.Http.Read = request => request.Parameters["StartIndex"] == "0"
                    ? Page(120, Enumerable.Range(20, 100).Select(index => Episode("episode-" + index, index)).ToArray())
                    : Page(120, Enumerable.Range(0, 20).Select(index => Episode("episode-" + index, index)).ToArray());
                var result = await Collect(fixture.Source, [fixture.Item("series", EmbyItemType.Series)]);
                Assert.Equal(120, result.Count);
                Assert.Equal("episode-0", result[0].Id);
                Assert.Equal("episode-119", result[^1].Id);
                Assert.Equal(2, fixture.Http.Requests.Count);
            }));
            foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
                Test($"批量播放：{kind}播放列表保留成员顺序与正式路由", () => Run(async () =>
                {
                    using var fixture = new Fixture(kind);
                    fixture.Http.Read = _ => Page(2, Movie("z"), Movie("a"));
                    var result = await Collect(fixture.Source, [fixture.Item("list", EmbyItemType.Playlist)]);
                    EqualIds("z,a", result);
                    var request = fixture.Http.Requests.Single();
                    if (kind == MediaServerKind.Emby)
                    {
                        Assert.Equal("ListItemOrder", request.Parameters["SortBy"]);
                        Assert.Equal("false", request.Parameters["Recursive"]);
                        Assert.Equal("list", request.Parameters["ParentId"]);
                    }
                    else Assert.Equal("/Playlists/list/Items", request.Path);
                }));
            Test("批量播放：后页失败不返回已收集的部分队列", () => Run(async () =>
            {
                using var fixture = new Fixture();
                fixture.Http.Read = request => request.Parameters["StartIndex"] == "0"
                    ? Page(2, Movie("first")) : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                await Expect<EmbyApiException>(() => Collect(fixture.Source,
                    [fixture.Item("movie"), fixture.Item("folder", EmbyItemType.Folder), fixture.Item("other", EmbyItemType.Folder)]));
                Assert.Equal(2, fixture.Http.Requests.Count);
                Assert.True(fixture.Http.Requests.All(request => request.Parameters["ParentId"] == "folder"));
            }));
            Test("批量播放：声明仍有剩余但空页或重复页时拒绝半队列", () => Run(async () =>
            {
                foreach (var repeated in new[] { false, true })
                {
                    using var fixture = new Fixture();
                    fixture.Http.Read = request => request.Parameters["StartIndex"] == "0" || repeated
                        ? Page(3, Movie("first")) : Page(3);
                    await Expect<InvalidOperationException>(() => Collect(fixture.Source, [fixture.Item("folder", EmbyItemType.Folder)]));
                    Assert.Equal(2, fixture.Http.Requests.Count);
                }
            }));
            Test("批量播放：空容器不能静默只播其他所选电影", () => Run(async () =>
            {
                using var fixture = new Fixture();
                fixture.Http.Read = _ => Page(0);
                await Expect<InvalidOperationException>(() => Collect(fixture.Source,
                    [fixture.Item("movie"), fixture.Item("empty", EmbyItemType.Series)]));
            }));
            Test("批量播放：开始前取消或查询失效时零请求", () => Run(async () =>
            {
                using var fixture = new Fixture();
                Assert.Equal(0, (await Collect(fixture.Source, [fixture.Item("folder", EmbyItemType.Folder)], token: new(true))).Count);
                Assert.Equal(0, (await Collect(fixture.Source, [fixture.Item("folder", EmbyItemType.Folder)], () => false)).Count);
                Assert.Equal(0, fixture.Http.Requests.Count);
            }));
            Test("批量播放：读取中取消使实际HTTP令牌撤销且无部分队列", () => Run(async () =>
            {
                using var fixture = new Fixture();
                using var cancellation = new CancellationTokenSource();
                fixture.Http.Before = request =>
                {
                    cancellation.Cancel();
                    Assert.True(request.Token.IsCancellationRequested);
                };
                fixture.Http.Read = _ => Page(2, Movie("first"));
                var result = await Collect(fixture.Source,
                    [fixture.Item("movie"), fixture.Item("folder", EmbyItemType.Folder)], token: cancellation.Token);
                Assert.Equal(0, result.Count);
                Assert.Equal(1, fixture.Http.Requests.Count);
            }));
            Test("批量播放：查询在读取中换代或账号退出拒绝迟到结果", () => Run(async () =>
            {
                foreach (var revoke in new[] { false, true })
                {
                    using var fixture = new Fixture();
                    var current = true;
                    fixture.Http.Before = _ =>
                    {
                        current = false;
                        if (revoke) fixture.Source.IdentityState.Lifetime.Cancel();
                    };
                    fixture.Http.Read = _ => Page(2, Movie("first"));
                    var result = await Collect(fixture.Source, [fixture.Item("folder", EmbyItemType.Folder)], () => current);
                    Assert.Equal(0, result.Count);
                    Assert.Equal(1, fixture.Http.Requests.Count);
                }
            }));
            Test("批量播放：来源相同键但不同登录所有者与未绑定条目都拒绝", () => Run(async () =>
            {
                using var fixture = new Fixture();
                using var another = new Fixture();
                Assert.Equal(fixture.Source.Key, another.Source.Key);
                Assert.False(ReferenceEquals(fixture.Source.UserStateOwner, another.Source.UserStateOwner));
                await Expect<InvalidOperationException>(() => Collect(fixture.Source,
                    [fixture.Item("folder", EmbyItemType.Folder), another.Item("foreign")]));
                await Expect<InvalidOperationException>(() => Collect(fixture.Source, [Movie("unbound")]));
                Assert.Equal(0, fixture.Http.Requests.Count);
                Assert.Equal(0, another.Http.Requests.Count);
            }));
            Test("批量播放：默认管理来源切换不借用新账号", () => Run(async () =>
            {
                using var fixture = new Fixture();
                using var another = new Fixture();
                fixture.Http.Before = _ => typeof(EmbySession).GetField("_current", Fixture.Flags)!.SetValue(fixture.Session, another.Source);
                fixture.Http.Read = _ => Page(1, Movie("own-child"));
                var result = await Collect(fixture.Source, [fixture.Item("folder", EmbyItemType.Folder)]);
                EqualIds("own-child", result);
                Assert.True(ReferenceEquals(result[0].Source!.UserStateOwner, fixture.Source.UserStateOwner));
                Assert.Equal(0, another.Http.Requests.Count);
            }));
            Test("批量播放：不支持或仅展示条目先拒绝且不读取前面的目录", () => Run(async () =>
            {
                using var fixture = new Fixture();
                foreach (var invalid in new[]
                {
                fixture.Item("person", EmbyItemType.Person), fixture.Item("audio", EmbyItemType.Audio),
                fixture.Item("unknown", "Unknown"), fixture.Item(""), fixture.Item("display")
                })
                {
                    if (invalid.Id == "display") invalid.IsDisplayOnly = true;
                    await Expect<InvalidOperationException>(() => Collect(fixture.Source, [fixture.Item("folder", EmbyItemType.Folder), invalid]));
                }
                Assert.Equal(0, fixture.Http.Requests.Count);
            }));
            Test("批量播放：其他后端不猜测Emby容器协议", () => Run(async () =>
            {
                using var fixture = new Fixture(MediaServerKind.Plex);
                await Expect<InvalidOperationException>(() => Collect(fixture.Source, [fixture.Item("series", EmbyItemType.Series)]));
                Assert.Equal(0, fixture.Http.Requests.Count);
                EqualIds("movie", await Collect(fixture.Source, [fixture.Item("movie")]));
            }));
            Test("批量播放：翻页总数变化不能用新的空页伪装收集成功", () => Run(async () =>
            {
                using var fixture = new Fixture();
                fixture.Http.Read = request => request.Parameters["StartIndex"] == "0" ? Page(2, Movie("first")) : Page(0);
                await Expect<InvalidOperationException>(() => Collect(fixture.Source, [fixture.Item("folder", EmbyItemType.Folder)]));
            }));
            Test("批量播放：异步读取中选中身份发生变化时拒绝旧结果", () => Run(async () =>
            {
                using var fixture = new Fixture();
                var selected = fixture.Item("folder", EmbyItemType.Folder);
                fixture.Http.Before = _ => selected.Id = "replacement";
                fixture.Http.Read = _ => Page(1, Movie("first"));
                Assert.Equal(0, (await Collect(fixture.Source, [selected])).Count);
            }));
            RegisterPage();
        }

        private static void RegisterPage()
        {
            Test("批量播放页面：只选中的卡片一次交接，退出选择不取消起播", () => Run(async () =>
            {
                using var fixture = new Fixture();
                using var query = new CancellationTokenSource();
                var actions = new BatchPlaybackActions();
                var first = new CardItem(fixture.Item("first"));
                var second = new CardItem(fixture.Item("second"));
                var unselected = new CardItem(fixture.Item("unselected"));
                var page = new LibraryPage([second, first, unselected], actions, query.Token);
                page.CardWall.SelectedItems.Remove(unselected);
                actions.BeforePlay = () =>
                {
                    Assert.False(page.Selecting);
                    Assert.False(page.Busy);
                    Assert.False(query.IsCancellationRequested);
                };
                await page.PlayCardsForFixture([second, first]);
                Assert.Equal(1, actions.Plays.Count);
                Assert.Equal("second", actions.Plays[0].Item.Id);
                Assert.True(actions.Plays[0].Queue is UserPlaybackQueue);
                EqualIds("second,first", actions.Plays[0].Queue!);
                Assert.Null(actions.Plays[0].Choice);
                Assert.Equal(1, page.EndCount);
                Assert.Equal(0, fixture.Http.Requests.Count);
            }));
            Test("批量播放页面：从头开始只传正式零起点选择", () => Run(async () =>
            {
                using var fixture = new Fixture();
                var item = fixture.Item("first");
                item.UserData = new() { PlaybackPositionTicks = 150 * TimeSpan.TicksPerSecond };
                var actions = new BatchPlaybackActions();
                var page = new LibraryPage([new CardItem(item)], actions, default);
                await page.PlayForFixture(fromBeginning: true);
                var choice = actions.Plays.Single().Choice;
                Assert.NotNull(choice);
                Assert.Equal(0L, choice!.StartTicks);
                Assert.Null(choice.Source);
                Assert.Null(choice.AudioStreamIndex);
                Assert.Null(choice.SubtitleStreamIndex);
                Assert.Equal(150 * TimeSpan.TicksPerSecond, item.ResumeTicks);
            }));
            Test("批量播放页面：随机保持完整集合和显式队列，生命周期等待不保持忙碌", () => Run(async () =>
            {
                using var fixture = new Fixture();
                var actions = new BatchPlaybackActions { Completion = new(TaskCreationOptions.RunContinuationsAsynchronously) };
                var page = new LibraryPage(Enumerable.Range(0, 12).Select(index => new CardItem(fixture.Item(index.ToString()))).ToArray(), actions, default);
                var run = page.PlayForFixture(shuffle: true);
                Assert.False(run.IsCompleted, "正式起播路由返回的是播放生命周期任务");
                Assert.False(page.Busy, "收集完成不能一直忙碌到影片结束");
                Assert.False(page.Selecting);
                var play = actions.Plays.Single();
                Assert.True(play.Queue is UserPlaybackQueue);
                Assert.Equal(12, play.Queue!.Count);
                Assert.Equal(12, play.Queue.Select(item => item.IdentityKey).Distinct().Count());
                Assert.Equal(play.Queue[0].IdentityKey, play.Item.IdentityKey);
                actions.Completion!.TrySetResult();
                await run;
            }));
            Test("批量播放页面：收集时拒绝重入并提供实际取消令牌", () => Run(async () =>
            {
                using var fixture = new Fixture();
                var actions = new BatchPlaybackActions();
                var page = new LibraryPage([new CardItem(fixture.Item("folder", EmbyItemType.Folder))], actions, default);
                fixture.Http.Before = request =>
                {
                    Assert.True(page.Busy);
                    var another = page.PlayForFixture();
                    Assert.True(another.IsCompleted);
                    page.CancelForFixture();
                    Assert.True(request.Token.IsCancellationRequested);
                };
                fixture.Http.Read = _ => Page(2, Movie("first"));
                await page.PlayForFixture();
                Assert.Equal(0, actions.Plays.Count);
                Assert.Equal(0, actions.Notices.Count);
                Assert.Equal(1, fixture.Http.Requests.Count);
                Assert.True(page.Selecting);
                Assert.False(page.Busy);
            }));
            foreach (var change in new[] { "query", "visit", "selection", "release", "deselect", "remove" })
                Test($"批量播放页面：收集中{change}变化拒绝迟到起播", () => Run(async () =>
                {
                    using var fixture = new Fixture();
                    using var query = new CancellationTokenSource();
                    var actions = new BatchPlaybackActions();
                    var card = new CardItem(fixture.Item("folder", EmbyItemType.Folder));
                    var page = new LibraryPage([card], actions, query.Token);
                    fixture.Http.Before = _ =>
                    {
                        switch (change)
                        {
                            case "query": query.Cancel(); break;
                            case "visit": page.ChangeVisitForFixture(); break;
                            case "selection": page.ChangeSelectionForFixture(); break;
                            case "release": page.ReleaseForFixture(); break;
                            case "deselect": page.CardWall.SelectedItems.Clear(); break;
                            case "remove": page.ViewModel.Cards.Clear(); break;
                        }
                    };
                    fixture.Http.Read = _ => Page(1, Movie("first"));
                    await page.PlayForFixture();
                    Assert.Equal(0, actions.Plays.Count);
                    Assert.Equal(0, actions.Notices.Count);
                    Assert.False(page.Busy);
                }));
            Test("批量播放页面：失败保留选择并显示结果，不起播半队列", () => Run(async () =>
            {
                using var fixture = new Fixture();
                var actions = new BatchPlaybackActions();
                var page = new LibraryPage([new CardItem(fixture.Item("movie")), new CardItem(fixture.Item("folder", EmbyItemType.Folder))], actions, default);
                fixture.Http.Read = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                await page.PlayForFixture();
                Assert.Equal(0, actions.Plays.Count);
                Assert.True(page.LibraryBatchResult.IsOpen);
                Assert.True(page.LibraryBatchResult.Message.Length > 0);
                Assert.True(page.Selecting);
                Assert.False(page.Busy);
            }));
            Test("批量播放页面：退出选择后的路由失败通过外壳通知", () => Run(async () =>
            {
                using var fixture = new Fixture();
                var actions = new BatchPlaybackActions { Failure = new IOException("route failed") };
                var page = new LibraryPage([new CardItem(fixture.Item("movie"))], actions, default);
                await page.PlayForFixture();
                Assert.False(page.Selecting);
                Assert.Equal(1, actions.Notices.Count);
                Assert.Contains("route failed", actions.Notices[0]);
                Assert.False(page.LibraryBatchResult.IsOpen);
            }));
            Test("批量播放页面：非Emby、未选卡片、异账号调用不起播", () => Run(async () =>
            {
                using var fixture = new Fixture();
                using var foreign = new Fixture();
                using var jellyfin = new Fixture(MediaServerKind.Jellyfin);
                var actions = new BatchPlaybackActions();
                var own = new CardItem(fixture.Item("movie"));
                var other = new CardItem(foreign.Item("movie"));
                var page = new LibraryPage([own, other], actions, default);
                await page.PlayForFixture();
                var jellyPage = new LibraryPage([new CardItem(jellyfin.Item("movie"))], actions, default);
                await jellyPage.PlayForFixture();
                page.CardWall.SelectedItems.Clear();
                await page.PlayCardsForFixture([own]);
                Assert.Equal(0, actions.Plays.Count);
                Assert.Equal(0, fixture.Http.Requests.Count);
                Assert.Equal(0, foreign.Http.Requests.Count);
            }));
        }

        private static Task<IReadOnlyList<EmbyItem>> Collect(EmbySessionScope source, IReadOnlyList<EmbyItem> selected,
            Func<bool>? isCurrent = null, CancellationToken token = default)
        {
            var method = typeof(ItemBatchPlayback).GetMethod("CollectAsync", BindingFlags.Static | BindingFlags.Public);
            Assert.NotNull(method, "缺少完整收集所选媒体的公开入口");
            return (Task<IReadOnlyList<EmbyItem>>)method!.Invoke(null, [source, selected, isCurrent ?? (() => true), token])!;
        }

        private static void EqualIds(string expected, IReadOnlyList<EmbyItem> items) => Assert.Equal(expected, string.Join(',', items.Select(item => item.Id)));
        private static void Run(Func<Task> test) => test().GetAwaiter().GetResult();
        private static async Task Expect<T>(Func<Task<IReadOnlyList<EmbyItem>>> test) where T : Exception
        {
            try { await test(); }
            catch (T) { return; }
            throw new AssertionException("期望抛出 " + typeof(T).Name);
        }
        private static EmbyItem Movie(string id) => new() { Id = id, Name = id, Type = EmbyItemType.Movie };
        private static EmbyItem Episode(string id, int number) => new()
        {
            Id = id,
            Name = id,
            Type = EmbyItemType.Episode,
            SeriesId = "series",
            SeasonId = "season",
            ParentIndexNumber = 1,
            IndexNumber = number,
            PremiereDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(number)
        };
        private static HttpResponseMessage Page(int total, params EmbyItem[] items) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new ItemsResult { Items = items.ToList(), TotalRecordCount = total }), Encoding.UTF8, "application/json")
        };

        private sealed class Fixture : IDisposable
        {
            internal const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
            internal Transport Http { get; } = new();
            internal EmbySession Session { get; }
            internal EmbySessionScope Source { get; }
            internal Fixture(MediaServerKind kind = MediaServerKind.Emby)
            {
                var secrets = new DenySecrets();
                var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "batch-playback-forbidden|no-io"));
                Session = new(new AppSettings(), new SettingsStore(paths, secrets), new CredentialVault(secrets),
                    DeviceIdentity.Create("batch-playback", "0"), Http);
                var transport = (EmbyHttp)typeof(EmbySession).GetField("_http", Flags)!.GetValue(Session)!;
                var connection = new EmbyConnection(new Uri(kind == MediaServerKind.Emby
                    ? "https://batch-playback.invalid/emby/" : "https://batch-playback.invalid/"), "placeholder", "user", "anonymous",
                    "anonymous", DeviceIdentity.Create("batch-playback", "0"))
                { ServerKind = kind };
                Source = new(Session, new EmbySessionScope.Identity(new(transport, connection),
                    new ServerProfile { Id = "server", Name = "anonymous", Url = "https://batch-playback.invalid/", ServerKind = kind },
                    new AccountProfile { Id = "account", UserId = "user", Username = "anonymous" }));
                ((Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Flags)!.GetValue(Session)!).Add(Source.Key, Source);
                typeof(EmbySession).GetField("_current", Flags)!.SetValue(Session, Source);
            }
            internal EmbyItem Item(string id, string type = EmbyItemType.Movie) => Source.Bind(new EmbyItem { Id = id, Name = id, Type = type });
            public void Dispose() => Session.Dispose();
        }

        private sealed class DenySecrets : ISecretProtector
        {
            public string Protect(string value) => throw new InvalidOperationException("禁止任何凭据写入");
            public string Unprotect(string value) => throw new InvalidOperationException("禁止任何凭据读取");
        }

        private sealed class Transport : HttpMessageHandler
        {
            internal sealed record Request(string Path, Dictionary<string, string> Parameters, CancellationToken Token);
            internal List<Request> Requests { get; } = [];
            internal Func<Request, HttpResponseMessage>? Read { get; set; }
            internal Action<Request>? Before { get; set; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken cancellationToken)
            {
                Assert.Equal(HttpMethod.Get, message.Method, "批量播放收集只能读取");
                Assert.Equal("batch-playback.invalid", message.RequestUri!.Host, "禁止真实网络与回退");
                var parameters = message.RequestUri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(parameter => parameter.Split('=', 2)).ToDictionary(parameter => Uri.UnescapeDataString(parameter[0]),
                        parameter => Uri.UnescapeDataString(parameter.Length == 2 ? parameter[1] : ""), StringComparer.Ordinal);
                var request = new Request(message.RequestUri.AbsolutePath, parameters, cancellationToken);
                Requests.Add(request);
                Before?.Invoke(request);
                return Task.FromResult(Read?.Invoke(request) ?? throw new AssertionException("不允许未登记的匿名请求"));
            }
        }
    }
}

#if ITEM_BATCH_PLAYBACK_FIXTURE
namespace Microsoft.UI.Xaml.Controls
{
    public enum InfoBarSeverity { Informational, Success, Warning, Error }
}

namespace Momoka.Shell.Views
{
    // Only external/UI values are substitutes. PlaySelectedAsync is compile-linked production source.
    public sealed class CardItem(EmbyItem item)
    {
        internal EmbyItem Item { get; } = item;
        internal EmbySessionScope? Scope => Item.Source;
        internal bool IsCurrent => Scope?.IsAvailable == true;
    }

    internal interface IShellActions
    {
        Task PlayAsync(EmbyItem item, EmbyItem? parent = null, PlaybackChoice? choice = null, IReadOnlyList<EmbyItem>? episodes = null);
        void Notify(string message, InfoBarSeverity severity = InfoBarSeverity.Informational);
    }

    internal sealed class BatchPlaybackActions : IShellActions
    {
        internal List<(EmbyItem Item, PlaybackChoice? Choice, IReadOnlyList<EmbyItem>? Queue)> Plays { get; } = [];
        internal List<string> Notices { get; } = [];
        internal Action? BeforePlay { get; set; }
        internal TaskCompletionSource? Completion { get; set; }
        internal Exception? Failure { get; set; }
        public Task PlayAsync(EmbyItem item, EmbyItem? parent = null, PlaybackChoice? choice = null, IReadOnlyList<EmbyItem>? episodes = null)
        {
            Plays.Add((item, choice, episodes));
            BeforePlay?.Invoke();
            if (Failure is { } failure) return Task.FromException(failure);
            return Completion?.Task ?? Task.CompletedTask;
        }
        public void Notify(string message, InfoBarSeverity severity = InfoBarSeverity.Informational) => Notices.Add(message);
    }

    public sealed partial class LibraryPage
    {
        private bool _selecting = true;
        private bool _batchBusy;
        private bool _released;
        private int _selectionGeneration = 1;
        private int _confirmationGeneration;
        private int _navigation = 1;
        private readonly CancellationToken _selectionQuery;
        private CancellationTokenSource? _batchCancellation;
        private readonly IShellActions? _actions;
        internal BatchPlaybackViewModel ViewModel { get; }
        internal BatchPlaybackWall CardWall { get; } = new();
        internal BatchPlaybackInfo LibraryBatchResult { get; } = new();
        internal int EndCount { get; private set; }
        internal bool Busy => _batchBusy;
        internal bool Selecting => _selecting;
        internal LibraryPage(CardItem[] cards, IShellActions actions, CancellationToken query)
        {
            _actions = actions;
            _selectionQuery = query;
            ViewModel = new(query);
            ViewModel.Cards.AddRange(cards);
            CardWall.SelectedItems.AddRange(cards);
        }
        internal Task PlayForFixture(bool shuffle = false, bool fromBeginning = false) => PlayCardsForFixture(ViewModel.Cards.ToArray(), shuffle, fromBeginning);
        internal Task PlayCardsForFixture(CardItem[] cards, bool shuffle = false, bool fromBeginning = false)
        {
            var method = typeof(LibraryPage).GetMethod("PlaySelectedAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Momoka.Tests.Assert.NotNull(method, "缺少真实页面批量播放入口");
            return (Task)method!.Invoke(this, [cards, shuffle, fromBeginning])!;
        }
        internal void CancelForFixture() => _batchCancellation?.Cancel();
        internal void ChangeVisitForFixture() => _navigation++;
        internal void ChangeSelectionForFixture() => _selectionGeneration++;
        internal void ReleaseForFixture() { _released = true; _navigation++; EndSelection(); }
        private void RefreshBatchControls() { }
        private void EndSelection()
        {
            _batchCancellation?.Cancel();
            _batchCancellation = null;
            _batchBusy = false;
            _selecting = false;
            _selectionGeneration++;
            _confirmationGeneration++;
            CardWall.SelectedItems.Clear();
            EndCount++;
        }
    }

    internal sealed class BatchPlaybackViewModel(CancellationToken query)
    {
        internal List<CardItem> Cards { get; } = [];
        internal bool Active { get; set; } = true;
        internal bool QueryCurrent(CancellationToken token) => Active && token == query && !token.IsCancellationRequested;
    }
    internal sealed class BatchPlaybackWall
    {
        internal List<CardItem> SelectedItems { get; } = [];
    }
    internal sealed class BatchPlaybackInfo
    {
        internal bool IsOpen { get; set; }
        internal string Message { get; set; } = "";
        internal InfoBarSeverity Severity { get; set; }
    }
}
#endif
