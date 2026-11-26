using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Danmaku;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class DanmakuAutoMatchOwnershipTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private static readonly DandanPlayEpisode Replacement = new(30, "New work", "Selected");

    internal static void Register()
    {
        foreach (var (race, count) in new[] { ("换片", 0), ("换片", 2), ("换片", -1), ("手动重选", 0), ("换连接", 0) })
        {
            Test($"Q-014 {race}后的旧加载不能启动兜底，新加载条数 {count}", () => Run(async (vm, transport) =>
            {
                // 醒来的旧加载曾经成功零条，但等待图层期间所有者已变化。
                await SupersedeZeroAsync(vm, transport, race, count, afterFallbackSearch: false);
            }));
        }
        Test("Q-014 换片后清空回执迟到，旧自动匹配不能借用新片的零条状态", () => Run(async (vm, transport) =>
        {
            await SupersedeZeroAsync(vm, transport, "换片", 0, afterFallbackSearch: false, beforeComments: true);
        }));
        foreach (var race in new[] { "手动重选", "换连接", "手动搜索" })
        {
            Test($"Q-014 {race}后迟到的候选搜索不能改变结果或读取候选", () => Run(async (vm, transport) =>
            {
                await SupersedeZeroAsync(vm, transport, race, 0, afterFallbackSearch: true);
            }));
        }
        foreach (var alternateCount in new[] { 0, 2, -1 })
        {
            Test($"Q-014 当前自动匹配零条时尝试同作品候选，候选条数 {alternateCount}", () => Run(async (vm, transport) =>
            {
                var item = Movie();
                transport.AlternateCount = alternateCount;
                vm.SwitchForFixture(item);
                await vm.MatchForFixture(item);
                Assert.Equal(alternateCount > 0 ? 20L : 10L, vm.CustomEpisode!.EpisodeId);
                Assert.Equal(Math.Max(0, alternateCount), vm.Applied.Count);
                Assert.Equal(alternateCount > 0 ? 0 : 1, vm.Candidates.Count);
                Assert.Equal(1, transport.Calls.Count(r => r.Path.EndsWith("/comment/20", StringComparison.Ordinal)));
            }));
        }
        Test("Q-014 当前匹配加载失败不触发零条兜底", () => Run(async (vm, transport) =>
        {
            var item = Movie();
            transport.InitialCount = -1;
            vm.SwitchForFixture(item);
            await vm.MatchForFixture(item);
            Assert.False(vm.Fetched);
            Assert.Null(vm.CustomEpisode);
            Assert.Equal(0, vm.Candidates.Count);
            Assert.Equal(0, transport.Calls.Count(r => r.Path.Contains("/search/", StringComparison.Ordinal)));
        }));
        foreach (var (parentKind, mode, fileName, mediaReads) in new[]
        {
            ("same", DanmakuSourceMode.DanmuApi, "Series title (2001) S01E02", 0),
            ("same", DanmakuSourceMode.Both, "Series title (2001) S01E02", 0),
            ("different series", DanmakuSourceMode.DanmuApi, "Series title (2018) S01E02", 1),
            ("different account", DanmakuSourceMode.DanmuApi, "Series title (2018) S01E02", 1),
            ("none", DanmakuSourceMode.DanmuApi, "Series title (2018) S01E02", 1),
            ("missing series", DanmakuSourceMode.DanmuApi, "Series title (2026) S01E02", 0),
            ("movie", DanmakuSourceMode.DanmuApi, "New work (2025)", 0),
        })
        {
            Test($"Q-015 匹配年份使用当前播放来源和所属剧，父级 {parentKind}，模式 {mode}", () =>
                Run(async (vm, transport) =>
                {
                    vm.Settings.Danmaku.Source = mode;
                    transport.MatchFound = false;
                    var source = transport.Scope("fixture-media");
                    var item = parentKind == "movie" ? Movie() : new EmbyItem
                    {
                        Id = "episode",
                        Name = "Episode title",
                        Type = EmbyItemType.Episode,
                        SeriesId = parentKind == "missing series" ? null : "current-series",
                        SeriesName = "Series title",
                        ParentIndexNumber = 1,
                        IndexNumber = 2,
                        ProductionYear = 2026
                    };
                    source.Bind(item);
                    var parent = new EmbyItem
                    {
                        Id = parentKind == "different series" ? "previous-series" : "current-series",
                        Type = EmbyItemType.Series,
                        ProductionYear = 2001
                    };
                    (parentKind == "different account" ? transport.Scope("other-media") : source).Bind(parent);

                    vm.SwitchForFixture(item, parentKind == "none" ? null : parent);
                    await vm.MatchForFixture(item).WaitAsync(Deadline);
                    Assert.Equal(fileName, transport.MatchFileName);
                    Assert.Equal(mediaReads, transport.Calls.Count(r => r.Host == "fixture-media.invalid"));
                    Assert.Equal(0, transport.Calls.Count(r => r.Host == "other-media.invalid"));
                }));
        }
    }

    private static void Run(Func<PlayerViewModel, MatchTransport, Task> test)
    {
        using var context = new FifoContext();
        var settings = new AppSettings
        {
            Danmaku = new()
            {
                Enabled = true,
                AutoMatch = false,
                Source = DanmakuSourceMode.DanmuApi,
                DanmuApiBaseUrl = "https://old-danmaku.invalid/"
            }
        };
        using var transport = new MatchTransport(settings);
        using var client = new DandanPlayClient(settings, Path.Combine(Program.OutputRoot, Guid.NewGuid().ToString("N")),
            transport, credentials: new DanmakuFixture.FakeCredentials());
        using var vm = new PlayerViewModel(client, settings, transport.Session);
        context.InvokeAsync(async () =>
        {
            await test(vm, transport);
            return true;
        }).WaitAsync(Deadline).GetAwaiter().GetResult();
    }

    private static async Task SupersedeZeroAsync(PlayerViewModel vm, MatchTransport transport, string race,
        int newCount, bool afterFallbackSearch, bool beforeComments = false)
    {
        var item = Movie("Original work");
        vm.SwitchForFixture(item);
        transport.NewCount = newCount;
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task Hold()
        {
            reached.TrySetResult();
            return release.Task;
        }

        if (beforeComments) vm.ClearHook = Hold;
        else if (afterFallbackSearch) transport.HoldSearch = Hold;
        else vm.PresentHook = Hold;
        var old = vm.MatchForFixture(item);
        try
        {
            await reached.Task.WaitAsync(Deadline);
            vm.ClearHook = vm.PresentHook = null;
            if (race == "换片") vm.SwitchForFixture(Movie());
            else if (race == "换连接") vm.ChangeConnectionForFixture("https://new-danmaku.invalid/");

            var replacement = race == "手动搜索"
                ? vm.SearchDanmakuAsync("Manual work", null, DanmakuSource.DanmuApi)
                : vm.LoadDanmakuAsync(Replacement, DanmakuSource.DanmuApi);
            if (!afterFallbackSearch) await replacement.WaitAsync(Deadline);
            var status = vm.DanmakuStatus;
            release.TrySetResult();
            await Task.WhenAll(old, replacement).WaitAsync(Deadline);
            if (race == "手动搜索") Assert.Contains("没有找到对应剧集", vm.DanmakuStatus);
            else
            {
                if (afterFallbackSearch) Assert.Contains("New work", vm.DanmakuStatus);
                else Assert.Equal(status, vm.DanmakuStatus);
                Assert.Equal(Math.Max(0, newCount), vm.Applied.Count);
                Assert.Equal(newCount >= 0, vm.Fetched);
                Assert.Equal(newCount >= 0 ? 30L : (long?)null, vm.CustomEpisode?.EpisodeId);
            }
            Assert.Equal(0, vm.Candidates.Count);
            Assert.Equal(afterFallbackSearch ? 1 : 0, transport.Calls.Count(r => r.Query.Contains("Original%20work", StringComparison.Ordinal)));
            Assert.Equal(0, transport.Calls.Count(r => r.Path.EndsWith("/comment/20", StringComparison.Ordinal)));
        }
        finally
        {
            release.TrySetResult();
            await old.WaitAsync(Deadline);
        }
    }

    private static EmbyItem Movie(string name = "New work") =>
        new() { Id = name, Name = name, Type = EmbyItemType.Movie, ProductionYear = 2025 };

    private sealed class MatchTransport : HttpMessageHandler
    {
        private static readonly BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly HttpMessageHandler _media;
        internal EmbySession Session { get; }
        internal Func<Task>? HoldSearch { get; set; }
        internal int InitialCount { get; set; }
        internal int AlternateCount { get; set; } = 2;
        internal int NewCount { get; set; }
        internal bool MatchFound { get; set; } = true;
        internal string? MatchFileName { get; private set; }
        internal ConcurrentQueue<(string Host, string Path, string Query)> Calls { get; } = new();

        internal MatchTransport(AppSettings settings)
        {
            _media = new MediaTransport(Calls);
            Session = (EmbySession)Activator.CreateInstance(typeof(EmbySession), Members, null,
                [settings, null, new CredentialVault(new NoSecrets()), new DeviceIdentity("anonymous", "fixture", "fixture", "0"), _media], null)!;
        }

        internal EmbySessionScope Scope(string name)
        {
            var account = new AccountProfile { Id = "anonymous", Username = "anonymous" };
            var server = new ServerProfile { Id = name, Name = name, Url = $"https://{name}.invalid/", Accounts = [account] };
            var connection = new EmbyConnection(new Uri(server.Url), "placeholder-only", "anonymous", "anonymous", name, Session.Device);
            var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Members)!.GetValue(Session)!;
            var identityType = typeof(EmbySessionScope).GetNestedType("Identity", BindingFlags.NonPublic)!;
            var identity = Activator.CreateInstance(identityType, Members | BindingFlags.Public, null, [new EmbyClient(http, connection), server, account], null)!;
            var scope = (EmbySessionScope)Activator.CreateInstance(typeof(EmbySessionScope), Members, null, [Session, identity], null)!;
            var sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Members)!.GetValue(Session)!;
            sources.Add(scope.Key, scope);
            return scope;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Calls.Enqueue((uri.Host, uri.AbsolutePath, uri.Query));
            switch (uri.AbsolutePath)
            {
                case "/api/v2/match":
                    using (var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)))
                        MatchFileName = json.RootElement.GetProperty("fileName").GetString();
                    return Reply(MatchFound
                        ? """{"isMatched":true,"matches":[{"episodeId":10,"animeTitle":"Matched work","episodeTitle":"Selected"}]}"""
                        : """{"isMatched":false}""");
                case "/api/v2/search/episodes":
                    var title = Uri.UnescapeDataString(uri.Query.Split('&')[0]["?anime=".Length..]);
                    if (title == "Original work" && HoldSearch is { } hold) await hold();
                    return Reply(title == "Manual work" || uri.Host == "api.dandanplay.net"
                        ? """{"animes":[]}"""
                        : JsonSerializer.Serialize(new
                        {
                            animes = new[] { new { animeTitle = title, episodes = new[] { new { episodeId = 20, episodeTitle = "Alternate" } } } }
                        }));
                case "/api/v2/comment/10": return Comments(InitialCount);
                case "/api/v2/comment/20": return Comments(AlternateCount);
                case "/api/v2/comment/30": return Comments(NewCount);
                default: throw new AssertionException("未登记的匿名弹幕请求 " + uri.AbsolutePath);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Session.Dispose();
            base.Dispose(disposing);
        }

        private static HttpResponseMessage Comments(int count) => count < 0
            ? new(HttpStatusCode.InternalServerError)
            : new(HttpStatusCode.OK) { Content = new ByteArrayContent(DanmakuFixture.Body(count)) };

        private static HttpResponseMessage Reply(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        private sealed class MediaTransport(ConcurrentQueue<(string Host, string Path, string Query)> calls) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var uri = request.RequestUri!;
                calls.Enqueue((uri.Host, uri.AbsolutePath, uri.Query));
                if (uri.Host != "fixture-media.invalid" || !uri.AbsolutePath.EndsWith("/Items/current-series", StringComparison.Ordinal))
                    throw new AssertionException("未登记的匿名媒体读取 " + uri.AbsolutePath);
                return Task.FromResult(Reply("""{"Id":"current-series","Type":"Series","ProductionYear":2018}"""));
            }
        }

        private sealed class NoSecrets : ISecretProtector
        {
            public string Protect(string plainText) => throw new AssertionException("不允许写入测试凭据");
            public string Unprotect(string cipherText) => throw new AssertionException("不允许读取测试凭据");
        }
    }
}
