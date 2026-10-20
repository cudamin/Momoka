using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Playback;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MediaVersionDiscoveryTests
{
    public static void Register()
    {
        RegisterQueueCases();
        Test("跨来源版本：刮削 ID 确认身份，拒绝同名、年份类型或 ID 冲突", () =>
        {
            var original = Movie();
            var matching = Movie();
            matching.Name = "Translated title";
            matching.ProviderIds = new() { ["tmdb"] = "42" };
            Assert.True(MediaVersionDiscovery.Matches(original, matching));
            matching.ProductionYear++;
            Assert.False(MediaVersionDiscovery.Matches(original, matching));
            matching.ProductionYear = original.ProductionYear;
            matching.Type = EmbyItemType.Series;
            Assert.False(MediaVersionDiscovery.Matches(original, matching));
            matching.Type = EmbyItemType.Movie;
            matching.ProviderIds.Clear();
            matching.Name = original.Name;
            Assert.False(MediaVersionDiscovery.Matches(original, matching), "标题相同不是跨服务器身份依据");
            matching.ProviderIds["Tmdb"] = "42";
            original.ProviderIds["Imdb"] = "tt111";
            matching.ProviderIds["Imdb"] = "tt222";
            Assert.False(MediaVersionDiscovery.Matches(original, matching), "一个一致 ID 不能掩盖另一项冲突");
        });

        Test("跨来源版本：单集必须同剧同季同集，合并集与缺失编号不能混入", () =>
        {
            var parent = Movie();
            parent.Type = EmbyItemType.Series;
            var otherParent = Movie();
            otherParent.Type = EmbyItemType.Series;
            var item = Episode();
            var other = Episode();
            Assert.True(MediaVersionDiscovery.Matches(item, other, parent, otherParent));
            other.ParentIndexNumber = 2;
            Assert.False(MediaVersionDiscovery.Matches(item, other, parent, otherParent));
            other.ParentIndexNumber = 1;
            other.IndexNumber = 2;
            Assert.False(MediaVersionDiscovery.Matches(item, other, parent, otherParent));
            other.IndexNumber = 1;
            other.IndexNumberEnd = 2;
            Assert.False(MediaVersionDiscovery.Matches(item, other, parent, otherParent));
            other.IndexNumberEnd = null;
            other.ParentIndexNumber = null;
            Assert.False(MediaVersionDiscovery.Matches(item, other, parent, otherParent));
            other.ParentIndexNumber = 1;
            otherParent.ProviderIds["Tmdb"] = "43";
            Assert.False(MediaVersionDiscovery.Matches(item, other, parent, otherParent));
        });

        Case("聚合模式关闭：另一台保存的服务器不会进入版本列表", async () =>
        {
            using var fixture = new Fixture(aggregateMode: false);
            await fixture.AddAsync("b", "two.invalid");
            var first = await fixture.AddAsync("a", "one.invalid");
            Assert.Equal(1, fixture.Session.Sources.Count);

            var result = await MediaVersionDiscovery.FindAsync(fixture.Session, first.Bind(Movie()),
                MediaSourceOrder.NewestFirst, default);

            Assert.Equal(1, result.Versions.Count);
            Assert.Equal(first.Key, result.Versions[0].Item.Source?.Key);
        });

        Case("跨来源版本：相同服务器不同账号各列一版，媒体源重号不误勾", async () =>
        {
            using var fixture = new Fixture();
            var first = await fixture.AddAsync("a", "one.invalid");
            await fixture.AddAsync("b", "one.invalid");
            await fixture.AddAsync("c", "two.invalid");
            var item = first.Bind(Movie());
            var result = await MediaVersionDiscovery.FindAsync(fixture.Session, item, MediaSourceOrder.NewestFirst, default);
            Assert.Equal(3, result.Versions.Count);
            Assert.Equal(3, result.Versions.Select(version => version.Item.IdentityKey).Distinct().Count());
            foreach (var version in result.Versions)
            {
                Assert.Equal(ItemDetail.SourceLabel(version.Source) + " · " + version.Item.Source!.ServerName, version.Label);
                Assert.DoesNotContain(version.Item.Source.DisplayName, version.Label);
                Assert.Equal(ItemDetail.SourceLabel(version.Source), version.LabelFor(showSource: false));
            }
            Assert.True(result.Versions[0].IsPlaying(item, item.DefaultMediaSource));
            Assert.False(result.Versions[1].IsPlaying(item, item.DefaultMediaSource));
            Assert.Equal(0, result.FailedSources.Count);
        });

        Case("版本名称：单服务器多个账号不标来源，多服务器后置名称，断开后恢复", async () =>
        {
            using var fixture = new Fixture();
            Assert.False(MediaVersionDiscovery.HasMultipleServers(fixture.Session.Sources));
            var first = await fixture.AddAsync("账号甲", "one.invalid");
            Assert.False(MediaVersionDiscovery.HasMultipleServers(fixture.Session.Sources));
            var second = await fixture.AddAsync("账号乙", "one.invalid");
            Assert.False(MediaVersionDiscovery.HasMultipleServers(fixture.Session.Sources));
            var version = new MediaVersion(first.Bind(Movie()), Movie().MediaSources[0]);
            Assert.Equal(ItemDetail.SourceLabel(version.Source), version.LabelFor(MediaVersionDiscovery.HasMultipleServers(fixture.Session.Sources)));
            var third = await fixture.AddAsync("账号丙", "two.invalid");
            Assert.True(MediaVersionDiscovery.HasMultipleServers(fixture.Session.Sources));
            Assert.Equal(ItemDetail.SourceLabel(version.Source) + " · one.invalid",
                version.LabelFor(MediaVersionDiscovery.HasMultipleServers(fixture.Session.Sources)));
            Assert.DoesNotContain("账号甲", version.Label);
            fixture.Session.SignOut(third.IdentityState.Server, third.IdentityState.Account);
            Assert.False(MediaVersionDiscovery.HasMultipleServers(fixture.Session.Sources));
            Assert.True(first.IsAvailable && second.IsAvailable);
            Assert.Equal(ItemDetail.SourceLabel(version.Source), version.LabelFor(MediaVersionDiscovery.HasMultipleServers(fixture.Session.Sources)));
        });

        Case("跨来源版本：一个来源失败仍返回其他来源，错片不并入", async () =>
        {
            using var fixture = new Fixture();
            var first = await fixture.AddAsync("a", "one.invalid");
            await fixture.AddAsync("b", "two.invalid");
            await fixture.AddAsync("c", "three.invalid");
            fixture.Transport.FailedUser = "b";
            fixture.Transport.WrongUser = "c";
            var result = await MediaVersionDiscovery.FindAsync(fixture.Session, first.Bind(Movie()), MediaSourceOrder.NewestFirst, default);
            Assert.Equal(1, result.Versions.Count);
            Assert.Equal(1, result.FailedSources.Count);
            Assert.Equal("two.invalid", result.FailedSources[0]);
        });

        Case("跨账号版本：无刮削 ID 仍按同服务器条目匹配，不同服务器重号不合并", async () =>
        {
            using var fixture = new Fixture();
            var first = await fixture.AddAsync("a", "one.invalid");
            var second = await fixture.AddAsync("b", "one.invalid");
            var unrelated = await fixture.AddAsync("c", "two.invalid");
            fixture.Transport.NoProviderIds = true;
            fixture.Session.SelectSource(first);
            var item = Movie();
            item.ProviderIds.Clear();
            var result = await MediaVersionDiscovery.FindAsync(fixture.Session, item, MediaSourceOrder.Default, default);
            Assert.Equal(first.Key, item.Source?.Key, "未带来源的入口在请求前固定到捕获的来源");
            Assert.Equal(2, result.Versions.Count);
            Assert.Equal(second.Key, result.Versions[1].Item.Source?.Key);
            Assert.False(result.Versions.Any(version => version.Item.Source?.Key == unrelated.Key));
            Assert.Equal(0, result.FailedSources.Count);
        });

        Case("跨账号版本：同服务器 ID 仍核对协议类型、已知服务器标识和媒体年份", async () =>
        {
            using var fixture = new Fixture();
            var first = await fixture.AddAsync("a", "one.invalid");
            var second = await fixture.AddAsync("b", "one.invalid");
            var original = first.Bind(Movie());
            var candidate = second.Bind(Movie());
            original.ProviderIds.Clear();
            candidate.ProviderIds.Clear();
            Assert.True(MediaVersionDiscovery.MatchesServerItem(original, candidate));
            candidate.Type = EmbyItemType.Episode;
            Assert.False(MediaVersionDiscovery.MatchesServerItem(original, candidate));
            candidate.Type = EmbyItemType.Movie;
            candidate.ProductionYear++;
            Assert.False(MediaVersionDiscovery.MatchesServerItem(original, candidate));
            var connection = first.Connection with { ServerId = "known-first" };
            Assert.False(MediaVersionDiscovery.SameServer(connection, connection with { ServerId = "known-second" }));
            Assert.False(MediaVersionDiscovery.SameServer(connection, connection with { ServerKind = MediaServerKind.FnNas }));
            Assert.False(MediaVersionDiscovery.SameServer(connection, connection with { ApiBase = new Uri("https://other.invalid/emby/") }));
        });

        Case("跨来源版本：依据所属剧的刮削 ID 找同一单集并保留新剧来源", async () =>
        {
            using var fixture = new Fixture();
            var first = await fixture.AddAsync("a", "one.invalid");
            var second = await fixture.AddAsync("b", "two.invalid");
            fixture.Transport.EpisodeMode = true;
            var item = first.Bind(Episode());
            var result = await MediaVersionDiscovery.FindAsync(fixture.Session, item, MediaSourceOrder.Default, default);
            Assert.Equal(2, result.Versions.Count);
            var remote = result.Versions[1];
            Assert.Equal(second.Key, remote.Item.Source?.Key);
            Assert.Equal(second.Key, remote.Parent?.Source?.Key);
            Assert.Equal(EmbyItemType.Series, remote.Parent?.Type);
        });

        Case("跨来源播放：新旧版本独立鉴权及上报，续播点保持", async () =>
        {
            using var fixture = new Fixture();
            var firstScope = await fixture.AddAsync("a", "one.invalid");
            var secondScope = await fixture.AddAsync("b", "two.invalid");
            fixture.Settings.Playback.ResumeRewindSeconds = 8;
            var firstItem = firstScope.Bind(Movie());
            var secondItem = secondScope.Bind(Movie());
            var first = new PlaybackLifecycleTests.Handle { Position = 37 };
            var second = new PlaybackLifecycleTests.Handle { Position = 37 };
            var backend = new PlaybackLifecycleTests.Backend();
            backend.Next = (request, _) => Task.FromResult<IPlaybackHandle>(request.MediaUrl.Host == "one.invalid" ? first : second);
            var service = fixture.Playback(backend);
            var old = service.PlayAsync(Ticket(firstItem), default);
            await first.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var next = service.PlayAsync(Ticket(secondItem) with { StartTicks = TimeSpan.TicksPerSecond * 37, ExactStart = true }, default);
            await second.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                var requests = backend.Starts.ToArray();
                Assert.Equal(2, requests.Length);
                Assert.True(requests[0].HttpHeaders.Any(header => header.Value == "offline-a"));
                Assert.True(requests[1].HttpHeaders.Any(header => header.Value == "offline-b"));
                Assert.Equal(37d, requests[1].StartSeconds);
                Assert.True(fixture.Transport.Reports.Any(report => report.Host == "one.invalid" && report.User == "a" && report.Stopped));
                Assert.True(fixture.Transport.Reports.Any(report => report.Host == "two.invalid" && report.User == "b" && !report.Stopped));
            }
            finally { second.End(38); await Task.WhenAll(old, next); }
            Assert.True(fixture.Transport.Reports.Any(report => report.Host == "two.invalid" && report.User == "b" && report.Stopped));
        });

        Case("跨来源播放：条目与强传身份不匹配时，旧播放保持且不建立新后端", async () =>
        {
            using var fixture = new Fixture();
            var firstScope = await fixture.AddAsync("a", "one.invalid");
            var secondScope = await fixture.AddAsync("b", "two.invalid");
            var first = new PlaybackLifecycleTests.Handle();
            var backend = new PlaybackLifecycleTests.Backend { Next = (_, _) => Task.FromResult<IPlaybackHandle>(first) };
            var service = fixture.Playback(backend);
            var playing = service.PlayAsync(Ticket(firstScope.Bind(Movie())), default);
            await first.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                try
                {
                    await service.PlayAsync(Ticket(secondScope.Bind(Movie())), firstScope, default);
                    throw new AssertionException("混用来源必须拒绝");
                }
                catch (InvalidOperationException) { }
                Assert.Equal(1, backend.Starts.Count);
                Assert.Equal(0, first.StopCount);
            }
            finally { first.End(); await playing; }
        });
    }

    private static void RegisterQueueCases()
    {
        Case("聚合播放列表：重号按来源定位，跨来源下一项保留整个队列和目标账号", async () =>
        {
            using var fixture = new Fixture();
            var firstScope = await fixture.AddAsync("a", "one.invalid");
            var secondScope = await fixture.AddAsync("b", "two.invalid");
            var first = firstScope.Bind(Movie());
            var second = secondScope.Bind(Movie());
            var third = secondScope.Bind(Movie());
            third.Id = "third";
            IReadOnlyList<EmbyItem> queue = [first, second, third];
            Assert.True(EpisodeNavigation.HasMultipleSources(queue));
            Assert.Equal(1, EpisodeNavigation.IndexOf(queue, second.IdentityKey));
            Assert.Equal(-1, EpisodeNavigation.IndexOf(queue, second.Id), "绑定来源后不能按裸 ID 命中另一条");
            var next = EpisodeNavigation.StepQueue(queue, first.IdentityKey, 1);
            Assert.NotNull(next);
            Assert.Equal(second.IdentityKey, next!.Episode.IdentityKey);
            Assert.True(ReferenceEquals(queue, next.Siblings));
            fixture.Session.SelectSource(firstScope);
            Assert.Equal(secondScope.Key, fixture.Session.Capture(next.Episode).Key);
            Assert.Equal(first.IdentityKey, EpisodeNavigation.StepQueue(queue, second.IdentityKey, -1)?.Episode.IdentityKey);
            Assert.Equal(third.IdentityKey, EpisodeNavigation.StepQueue(queue, second.IdentityKey, 1)?.Episode.IdentityKey);
            Assert.Null(EpisodeNavigation.StepQueue(queue, third.IdentityKey, 1));
            Assert.Null(EpisodeNavigation.StepQueue(queue, first.IdentityKey, -1));
        });

        Case("聚合单集：同季重号不能跨账号自动串集，错误来源的季列表不能替换落点", async () =>
        {
            using var fixture = new Fixture();
            var firstScope = await fixture.AddAsync("a", "one.invalid");
            var secondScope = await fixture.AddAsync("b", "two.invalid");
            var first = firstScope.Bind(Episode());
            var second = secondScope.Bind(Episode());
            var third = secondScope.Bind(Episode());
            third.Id = "next";
            third.IndexNumber = 2;
            IReadOnlyList<EmbyItem> queue = [first, second, third];
            Assert.Null(EpisodeNavigation.StepInSeason(queue, first.IdentityKey, 1));
            var next = EpisodeNavigation.StepInSeason(queue, second.IdentityKey, 1);
            Assert.Equal(third.IdentityKey, next?.Episode.IdentityKey);
            Assert.Equal(2, next?.Siblings.Count ?? 0);
            var wrong = firstScope.Bind(Episode());
            wrong.Id = third.Id;
            var adjacent = await EpisodeNavigation.ResolveAdjacentAsync([second, third], second.IdentityKey, 1,
                _ => Task.FromResult<IReadOnlyList<EmbyItem>>([wrong]));
            Assert.Equal(third.IdentityKey, adjacent?.Episode.IdentityKey);
            Assert.Equal(2, adjacent?.Siblings.Count ?? 0, "同名条目来自别的账号时保留原来源的季列表");
        });
    }

    private static EmbyItem Movie() => new()
    {
        Id = "same-item",
        Name = "电影",
        Type = EmbyItemType.Movie,
        ProductionYear = 2020,
        ProviderIds = new() { ["Tmdb"] = "42" },
        RunTimeTicks = 100 * TimeSpan.TicksPerSecond,
        MediaSources = [new MediaSource { Id = "same-source", Container = "mkv", Name = "1080p" }]
    };

    private static EmbyItem Episode() => new()
    {
        Id = "episode",
        Name = "第一集",
        Type = EmbyItemType.Episode,
        ParentIndexNumber = 1,
        IndexNumber = 1,
        ProductionYear = 2020,
        SeriesId = "show",
        MediaSources = [new MediaSource { Id = "episode-source", Container = "mkv" }]
    };

    private static PlaybackTicket Ticket(EmbyItem item) => new() { Item = item, Source = item.MediaSources[0] };
    private static void Case(string name, Func<Task> run) => Test(name, () => run().WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult());

    private sealed class Fixture : IDisposable
    {
        public Transport Transport { get; } = new();
        public AppSettings Settings { get; } = new();
        public EmbySession Session { get; }
        private readonly Dictionary<string, ServerProfile> _servers = [];
        public Fixture(bool aggregateMode = true)
        {
            // 这些用例全在问「多个来源同时在线时」的版本发现与播放，聚合模式必须开着；设置里的默认是关。
            Settings.AggregateMode = aggregateMode;
            Settings.Playback.ReportProgressToServer = true;
            Settings.Playback.ResumeRewindSeconds = 0;
            Settings.Playback.ProgressReportIntervalSeconds = 60;
            Session = new EmbySession(Settings,
                new SettingsStore(new AppPaths(Path.Combine(Path.GetTempPath(), "versions-" + Guid.NewGuid().ToString("N"))),
                    PassthroughSecretProtector.Instance),
                new CredentialVault(PassthroughSecretProtector.Instance), DeviceIdentity.Create("offline", "1"), Transport);
        }
        public async Task<EmbySessionScope> AddAsync(string user, string host)
        {
            if (!_servers.TryGetValue(host, out var server))
                _servers[host] = server = new ServerProfile { Url = $"https://{host}", Name = host };
            var account = new AccountProfile { UserId = user, Username = user, ProtectedAccessToken = $"offline-{user}" };
            Assert.True(await Session.TryRestoreAsync(server, account, default));
            return Session.Capture();
        }
        public PlaybackService Playback(IPlaybackBackend backend) => new(Session, Settings, () => backend,
            new PlaybackPlanner(Settings, new ShaderGroupResolver(Settings.Shaders)));
        public void Dispose() => Session.Dispose();
    }

    private sealed record Report(string Host, string User, bool Stopped);
    private sealed class Transport : HttpMessageHandler
    {
        public string? FailedUser { get; set; }
        public string? WrongUser { get; set; }
        public bool EpisodeMode { get; set; }
        public bool NoProviderIds { get; set; }
        public ConcurrentQueue<Report> Reports { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var token = request.Headers.TryGetValues("X-Emby-Token", out var values) ? values.Single() : "";
            var user = token.StartsWith("offline-", StringComparison.Ordinal) ? token[8..] : "";
            var path = uri.AbsolutePath;
            if (path.EndsWith("/Views", StringComparison.Ordinal)) return Reply("{\"Items\":[]}");
            if (path.EndsWith("/System/Info/Public", StringComparison.Ordinal)) return Reply("{}");
            if (path.Contains("/Sessions/Playing", StringComparison.Ordinal))
            {
                Reports.Enqueue(new(uri.Host, user, path.EndsWith("/Stopped", StringComparison.Ordinal)));
                return Reply("{}");
            }
            if (user == FailedUser) return Reply("{}", HttpStatusCode.ServiceUnavailable);
            if (EpisodeMode)
            {
                if (path.EndsWith("/Episodes", StringComparison.Ordinal))
                    return Reply(JsonSerializer.Serialize(new ItemsResult { Items = [Episode()], TotalRecordCount = 1 }));
                if (path.EndsWith("/episode", StringComparison.Ordinal)) return Reply(JsonSerializer.Serialize(Episode()));
                var series = Movie();
                series.Type = EmbyItemType.Series;
                series.Id = "show";
                return path.EndsWith("/Items", StringComparison.Ordinal)
                    ? Reply(JsonSerializer.Serialize(new ItemsResult { Items = [series], TotalRecordCount = 1 }))
                    : Reply(JsonSerializer.Serialize(series));
            }
            var item = Movie();
            if (NoProviderIds) item.ProviderIds.Clear();
            if (user == WrongUser) item.ProviderIds["Tmdb"] = "99";
            return path.EndsWith("/Items", StringComparison.Ordinal)
                ? Reply(JsonSerializer.Serialize(new ItemsResult { Items = [item], TotalRecordCount = 1 }))
                : Reply(JsonSerializer.Serialize(item));
        }
        private static Task<HttpResponseMessage> Reply(string json, HttpStatusCode status = HttpStatusCode.OK) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}
