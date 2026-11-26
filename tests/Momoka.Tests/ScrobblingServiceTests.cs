using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Playback;
using Momoka.Scrobbling;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class ScrobblingServiceTests
{
    private const long DurationTicks = TimeSpan.TicksPerSecond * 100;

    public static void Register()
    {
        Case("打格子验证：两站只读检查成功，不读取作品或写收藏", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Bangumi);
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.Refresh();
            await fixture.Service.CheckConnectionsAsync(CancellationToken.None);
            Assert.Equal(ScrobblingConnectionState.Connected, fixture.Service.BangumiConnection);
            Assert.Equal(ScrobblingConnectionState.Connected, fixture.Service.DoubanConnection);
            var sent = fixture.BangumiTransport.Requests.Single();
            Assert.Equal("GET /v0/me", sent.Key);
            Assert.True(sent.HeaderMatched);
            Assert.Equal(0, fixture.Douban.SubjectReads.Count);
            Assert.Equal(0, fixture.Douban.Searches.Count);
            Assert.Equal(0, fixture.Douban.Marks.Count);
        });
        Case("打格子验证：默认关闭不请求，Bangumi无密钥显示未配置", async () =>
        {
            using var fixture = new Fixture();
            fixture.Douban.LoginCheck = _ => throw new InvalidOperationException("unexpected check");
            await fixture.Service.CheckConnectionsAsync(CancellationToken.None);
            Assert.Equal(ScrobblingConnectionState.Unchecked, fixture.Service.DoubanConnection);
            fixture.Settings.Scrobbling.Bangumi.Enabled = true;
            fixture.Refresh();
            await fixture.Service.CheckConnectionsAsync(CancellationToken.None);
            Assert.Equal(ScrobblingConnectionState.MissingCredential, fixture.Service.BangumiConnection);
            Assert.Equal(0, fixture.BangumiTransport.Requests.Count);
        });
        Case("打格子验证：豆瓣明确未登录与网络异常分别显示", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.Refresh();
            fixture.Douban.LoginCheck = _ => Task.FromResult(false);
            await fixture.Service.CheckConnectionsAsync(CancellationToken.None);
            Assert.Equal(ScrobblingConnectionState.SignedOut, fixture.Service.DoubanConnection);
            fixture.Douban.LoginCheck = _ => throw new HttpRequestException("synthetic failure");
            await fixture.Service.CheckConnectionsAsync(CancellationToken.None);
            Assert.Equal(ScrobblingConnectionState.Failed, fixture.Service.DoubanConnection);
        });
        Case("打格子验证：Bangumi失效密钥与服务故障分别显示", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Bangumi);
            fixture.Refresh();
            fixture.BangumiTransport.FailureStatus = HttpStatusCode.Unauthorized;
            await fixture.Service.CheckConnectionsAsync(CancellationToken.None);
            Assert.Equal(ScrobblingConnectionState.SignedOut, fixture.Service.BangumiConnection);
            fixture.BangumiTransport.FailureStatus = HttpStatusCode.ServiceUnavailable;
            await fixture.Service.CheckConnectionsAsync(CancellationToken.None);
            Assert.Equal(ScrobblingConnectionState.Failed, fixture.Service.BangumiConnection);
        });
        Case("打格子验证：较早的成功不能覆盖重新验证的未登录结果", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.Refresh();
            var delayed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Douban.LoginCheck = _ => delayed.Task;
            var old = fixture.Service.CheckConnectionsAsync(CancellationToken.None);
            Assert.Equal(ScrobblingConnectionState.Checking, fixture.Service.DoubanConnection);
            fixture.Douban.LoginCheck = _ => Task.FromResult(false);
            await fixture.Service.CheckConnectionsAsync(CancellationToken.None);
            delayed.SetResult(true);
            await old;
            Assert.Equal(ScrobblingConnectionState.SignedOut, fixture.Service.DoubanConnection);
        });
        Case("打格子验证：改配置和离开设置取消检查，旧结果不能重新显示已登录", async () =>
        {
            foreach (var changeConfiguration in new[] { true, false })
            {
                using var fixture = new Fixture();
                using var activity = new CancellationTokenSource();
                fixture.Enable(ScrobblingProvider.Douban);
                fixture.Refresh();
                var delayed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                fixture.Douban.LoginCheck = _ => delayed.Task;
                var check = fixture.Service.CheckConnectionsAsync(activity.Token);
                if (changeConfiguration) fixture.Service.SetDoubanSessionChanging(true);
                else activity.Cancel();
                delayed.SetResult(true);
                await check;
                Assert.Equal(ScrobblingConnectionState.Unchecked, fixture.Service.DoubanConnection);
            }
        });
        Case("打格子服务：默认关闭，启用后所有媒体服务器账号均接纳场次", async () =>
        {
            using var fixture = new Fixture();
            var ticket = fixture.Ticket();
            fixture.Refresh();
            Assert.Null(fixture.Service.CreateAttempt(ticket, fixture.Scope, Request()));

            fixture.Enable(ScrobblingProvider.Douban);
            fixture.Refresh();
            await fixture.Create(ticket).FinishAsync(null, false);
            fixture.Enable(ScrobblingProvider.Bangumi);
            fixture.Refresh();
            await fixture.Create(ticket).FinishAsync(null, false);

            fixture.Enable(ScrobblingProvider.Douban);
            fixture.Refresh();
            var allowed = fixture.Create(ticket);
            await allowed.FinishAsync(null, false);
            Assert.Equal(0, fixture.Douban.SubjectReads.Count);
            Assert.Equal(0, fixture.BangumiTransport.Requests.Count);
        });

        Case("打格子服务：唯一标题和年份自动匹配，豆瓣先标记在看", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.Douban.SearchResults = [new(8, "Offline-Series", 2024, null)];
            fixture.Refresh();
            await fixture.Create(fixture.Ticket(1)).FinishAsync(5, true);
            Assert.Equal("8:doing", fixture.Douban.Describe());
            Assert.True(fixture.Douban.Searches.SequenceEqual(["Offline Series"]));
            Assert.Equal(1, fixture.Playback.Transport.Requests.Count(request => request.Path.EndsWith("/Users/a/Items/series", StringComparison.Ordinal)));
            var record = fixture.Service.GetRecords(ScrobblingProvider.Douban).Single();
            Assert.Equal(ScrobblingRecordOutcome.Watching, record.Outcome);
            Assert.Equal(1, record.EpisodeNumber);
            Assert.DoesNotContain("Bangumi", string.Join(" ", fixture.Service.GetRecords(ScrobblingProvider.Douban).Select(item => item.Detail)));
            Assert.Equal(0, fixture.Service.GetRecords(ScrobblingProvider.Bangumi).Count);
        });

        Case("打格子服务：候选歧义、年份冲突或条目集数不匹配时不写入", async () =>
        {
            foreach (var issue in new[] { "ambiguous", "wrong-year", "wrong-count", "missing-year", "search-failure" })
            {
                using var fixture = new Fixture();
                fixture.Enable(ScrobblingProvider.Douban);
                if (issue == "ambiguous") fixture.Douban.SearchResults =
                    [new(8, "Offline Series", 2024, null), new(9, "Offline-Series", 2024, null)];
                else if (issue == "wrong-year") fixture.Douban.SearchResults = [new(8, "Offline Series", 2023, null)];
                else if (issue == "wrong-count") fixture.Douban.SubjectEpisodeCount = 11;
                else if (issue == "search-failure") fixture.Douban.SearchFailure = new HttpRequestException("offline fixture failure");
                else fixture.SeriesYear = null;
                fixture.Refresh();
                await fixture.Create(fixture.Ticket(1)).FinishAsync(5, true);
                Assert.Equal("", fixture.Douban.Describe(), issue);
                Assert.Equal(issue == "search-failure" ? ScrobblingRecordOutcome.Failed : ScrobblingRecordOutcome.Skipped,
                    fixture.Service.GetRecords(ScrobblingProvider.Douban).Single().Outcome, issue);
            }
        });

        Case("打格子服务：优先用媒体库 Douban 编号并在站点核对集数", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.SeriesProviderIds["Douban"] = "77";
            fixture.Refresh();
            await fixture.Create(fixture.Ticket(1)).FinishAsync(5, true);
            Assert.Equal("77:doing", fixture.Douban.Describe());
            Assert.Equal(0, fixture.Douban.Searches.Count);

            fixture.Douban.SubjectEpisodeCount = 11;
            fixture.Douban.Marks.Clear();
            await fixture.Create(fixture.Ticket(1)).FinishAsync(5, true);
            Assert.Equal("", fixture.Douban.Describe());
        });

        Case("打格子服务：复杂季度和不连续、合并集号都跳过", async () =>
        {
            foreach (var issue in new[] { "second-season", "special-season", "missing-number", "merged-episode", "extra-gap" })
            {
                using var fixture = new Fixture();
                fixture.Enable(ScrobblingProvider.Douban);
                if (issue == "second-season") fixture.SeasonNumbers = [1, 2];
                if (issue == "special-season") fixture.SeasonNumbers = [0];
                if (issue == "missing-number") fixture.MissingEpisodeNumber = 3;
                if (issue == "merged-episode") fixture.MergedEpisodeNumber = 1;
                if (issue == "extra-gap") fixture.EpisodeCount = 11;
                fixture.Refresh();
                await fixture.Create(fixture.Ticket(1)).FinishAsync(5, true);
                Assert.Equal("", fixture.Douban.Describe(), issue);
            }
        });

        Case("打格子服务：末集达到标记已看进度后才标记看过", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.Refresh();
            await fixture.Create(fixture.Ticket(11)).FinishAsync(90, true);
            await fixture.Create(fixture.Ticket(12)).FinishAsync(89.999, true);
            await fixture.Create(fixture.Ticket(12)).FinishAsync(90, true);
            Assert.Equal("8:doing,8:doing,8:doing,8:doing,8:done", fixture.Douban.Describe());
        });

        Case("打格子服务：总集数未知时不能自动匹配", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.Douban.SubjectEpisodeCount = null;
            fixture.Refresh();
            await fixture.Create(fixture.Ticket(12)).FinishAsync(5, true);
            Assert.Equal("", fixture.Douban.Describe());
        });

        Case("打格子服务：Bangumi按标题自动匹配并按集标记", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Bangumi);
            fixture.Refresh();
            await fixture.Create(fixture.Ticket(1)).FinishAsync(89, true);
            Assert.Equal(0, fixture.BangumiTransport.Requests.Count);
            await fixture.Create(fixture.Ticket(1)).FinishAsync(90, true);
            var sent = fixture.BangumiTransport.Requests.ToArray();
            Assert.True(sent.Any(request => request.Key == "POST /v0/search/subjects"));
            Assert.True(sent.Any(request => request.Key == "GET /v0/subjects/101"));
            Assert.True(sent.Any(request => request.Key == "GET /v0/episodes"));
            Assert.True(sent.Any(request => request.Key == "GET /v0/users/-/collections/-/episodes/1001"));
            var createRequest = sent.Single(request => request.Key == "POST /v0/users/-/collections/101");
            using var create = JsonDocument.Parse(createRequest.Body);
            Assert.Equal(3, create.RootElement.GetProperty("type").GetInt32());
            var updateRequest = sent.Single(request => request.Key == "PATCH /v0/users/-/collections/101/episodes");
            using var update = JsonDocument.Parse(updateRequest.Body);
            Assert.Equal(2, update.RootElement.GetProperty("type").GetInt32());
            Assert.Equal(1001L, update.RootElement.GetProperty("episode_id")[0].GetInt64());
            foreach (var request in sent)
            {
                Assert.Equal("api.bgm.tv", request.Host);
                Assert.Equal("Bearer", request.Scheme);
                Assert.True(request.HeaderMatched);
                Assert.DoesNotContain("synthetic-bangumi-placeholder", request.Query);
                Assert.DoesNotContain("synthetic-bangumi-placeholder", request.Body);
            }
            var record = fixture.Service.GetRecords(ScrobblingProvider.Bangumi).Single();
            Assert.Equal(ScrobblingRecordOutcome.Watched, record.Outcome);
            Assert.Equal(1, record.EpisodeNumber);
            Assert.Equal(0, fixture.Service.GetRecords(ScrobblingProvider.Douban).Count);
        });

        Case("打格子服务：同一集的两站记录互相隔离", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Bangumi);
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.Refresh();
            await fixture.Create(fixture.Ticket(8)).FinishAsync(90, true);
            var bangumi = fixture.Service.GetRecords(ScrobblingProvider.Bangumi).Single();
            var douban = fixture.Service.GetRecords(ScrobblingProvider.Douban).First();
            Assert.Equal(ScrobblingRecordOutcome.Watched, bangumi.Outcome);
            Assert.Equal(ScrobblingRecordOutcome.Watching, douban.Outcome);
            Assert.Equal(8, bangumi.EpisodeNumber);
            Assert.Equal(8, douban.EpisodeNumber);
            Assert.True(fixture.Service.GetRecords(ScrobblingProvider.Douban)
                .All(record => record.Outcome == ScrobblingRecordOutcome.Watching));
            Assert.Equal(ScrobblingProvider.Bangumi, bangumi.Provider);
            Assert.Equal(ScrobblingProvider.Douban, douban.Provider);
        });

        Case("打格子服务：Bangumi 编号优先，保留已有收藏和已看集", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Bangumi);
            fixture.SeriesProviderIds["Bangumi"] = "101";
            fixture.BangumiTransport.CollectionExists = true;
            fixture.BangumiTransport.EpisodeWatched = true;
            fixture.Refresh();
            await fixture.Create(fixture.Ticket(1)).FinishAsync(100, true);
            Assert.Equal(5, fixture.BangumiTransport.Requests.Count);
            Assert.False(fixture.BangumiTransport.Requests.Any(request => request.Method != "GET"));
        });

        Case("打格子服务：固定播放来源独立于后来切换的默认媒体账号", async () =>
        {
            using var fixture = new Fixture();
            fixture.Settings.AggregateMode = true;
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.Refresh();
            var original = fixture.Scope;
            var account = new AccountProfile { Id = "second-account", Username = "second", UserId = "second", ProtectedAccessToken = "offline-second" };
            var server = new ServerProfile { Id = "second-server", Url = "https://second.invalid", Accounts = [account] };
            Assert.True(await fixture.Playback.Session.TryRestoreAsync(server, account, CancellationToken.None));
            Assert.True(original.IsAvailable);
            Assert.Equal("second", fixture.Playback.Session.Capture().UserName);
            fixture.Refresh();
            await fixture.Create(fixture.Ticket(1), original).FinishAsync(5, true);
            Assert.Equal("8:doing", fixture.Douban.Describe());
            var second = fixture.Playback.Session.Capture();
            await fixture.Create(fixture.Ticket(scope: second), second).FinishAsync(5, true);
            Assert.Equal("8:doing,8:doing", fixture.Douban.Describe());
        });

        Case("打格子服务：配置修改取消在途自动匹配并阻止写入", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Douban);
            var entered = Signal();
            var cancelled = Signal();
            fixture.Douban.Read = async (id, token) =>
            {
                using var registration = token.Register(() => cancelled.TrySetResult());
                entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
                return new(id, "Offline Series", 12);
            };
            fixture.Refresh();
            var attempt = fixture.Create(fixture.Ticket(1));
            attempt.Observe(5, true);
            var finish = attempt.FinishAsync(5, true);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            fixture.Settings.Scrobbling.Douban.Enabled = false;
            fixture.Refresh();
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await finish;
            Assert.Equal(0, fixture.Douban.Marks.Count);
        });

        Case("打格子服务：豆瓣切换登录期间撤销旧任务并恢复后自动标记", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.Refresh();
            var previous = fixture.Create(fixture.Ticket(1));
            fixture.Service.SetDoubanSessionChanging(true);
            Assert.Null(fixture.Service.CreateAttempt(fixture.Ticket(), fixture.Scope, Request()));
            await previous.FinishAsync(5, true);
            Assert.Equal(0, fixture.Douban.Marks.Count);
            fixture.Service.SetDoubanSessionChanging(false);
            await fixture.Create(fixture.Ticket(1)).FinishAsync(5, true);
            Assert.Equal("8:doing", fixture.Douban.Describe());
        });

        Case("打格子服务：场次捕获起播集号和剧集ID快照", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.Refresh();
            var ticket = fixture.Ticket(1);
            var attempt = fixture.Create(ticket);
            ticket.Item.IndexNumber = 12;
            ticket.Item.SeriesId = "changed-series";
            ticket.Item.ParentIndexNumber = 99;
            await attempt.FinishAsync(5, true);
            Assert.Equal("8:doing", fixture.Douban.Describe());
            Assert.True(fixture.Playback.Transport.Requests.Any(request => request.Path.EndsWith("/Users/a/Items/series", StringComparison.Ordinal)));
            Assert.False(fixture.Playback.Transport.Requests.Any(request => request.Path.EndsWith("/Users/a/Items/changed-series", StringComparison.Ordinal)));
        });

        Case("打格子服务：Bangumi失败不阻止豆瓣自动完成", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Bangumi);
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.BangumiTransport.Reject = true;
            fixture.Refresh();
            await fixture.Create(fixture.Ticket(12)).FinishAsync(100, true);
            Assert.Equal("8:doing,8:done", fixture.Douban.Describe());
            Assert.True(fixture.BangumiTransport.Requests.Count > 0);
        });

        Case("打格子服务：双次释放和释放后配置更新均安全", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.Refresh();
            var attempt = fixture.Create(fixture.Ticket(1));
            fixture.Service.Dispose();
            fixture.Service.Dispose();
            fixture.Service.ConfigurationChanged();
            await attempt.FinishAsync(5, true);
            Assert.Equal(0, fixture.Douban.Marks.Count);
            Assert.Null(fixture.Service.CreateAttempt(fixture.Ticket(), fixture.Scope, Request()));
        });
    }

    private static PlaybackRequest Request() => new()
    {
        MediaUrl = new Uri("https://media.invalid/offline.mkv"),
        Title = "离线测试",
        ItemId = "episode",
        RunTimeTicks = DurationTicks
    };

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Case(string name, Func<Task> run) => Test(name, () => run().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult());
    private static void Case(string name, Action run) => Test(name, run);

    private sealed class Fixture : IDisposable
    {
        public PlaybackLifecycleTests.Fixture Playback { get; } = new();
        public int? SeriesYear { get; set; } = 2024;
        public Dictionary<string, string> SeriesProviderIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyList<int> SeasonNumbers { get; set; } = [1];
        public int? MissingEpisodeNumber { get; set; }
        public int? MergedEpisodeNumber { get; set; }
        public int EpisodeCount { get; set; } = 12;
        public AppSettings Settings => Playback.Settings;
        public EmbySessionScope Scope { get; }
        public FakeDouban Douban { get; } = new();
        public BangumiTransport BangumiTransport { get; } = new();
        public HttpClient Http { get; }
        public BangumiClient Bangumi { get; }
        public ScrobblingService Service { get; }

        public Fixture()
        {
            Scope = Playback.Session.Capture();
            Playback.Transport.Reply = ReplyPlaybackAsync;
            Http = new HttpClient(BangumiTransport);
            Bangumi = new BangumiClient(Http);
            Service = new ScrobblingService(Settings, PassthroughSecretProtector.Instance, Bangumi, Douban);
        }

        public void Enable(ScrobblingProvider provider)
        {
            if (provider == ScrobblingProvider.Bangumi)
            {
                Settings.Scrobbling.Bangumi.Enabled = true;
                Settings.Scrobbling.Bangumi.ProtectedAccessToken = "synthetic-bangumi-placeholder";
            }
            else
            {
                Settings.Scrobbling.Douban.Enabled = true;
            }
        }

        public PlaybackTicket Ticket(int episode = 1, int season = 1, EmbySessionScope? scope = null)
        {
            var ticket = PlaybackLifecycleTests.Ticket("episode");
            ticket.Item.Type = EmbyItemType.Episode;
            ticket.Item.IndexNumber = episode;
            ticket.Item.ParentIndexNumber = season;
            ticket.Item.SeriesId = "series";
            (scope ?? Scope).Bind(ticket.Item);
            return ticket;
        }

        public PlaybackScrobblingAttempt Create(PlaybackTicket ticket, EmbySessionScope? scope = null)
        {
            var attempt = Service.CreateAttempt(ticket, scope ?? Scope, Request());
            Assert.NotNull(attempt);
            return attempt!;
        }

        public void Refresh() => Service.ConfigurationChanged();
        public void Dispose() { Service.Dispose(); Bangumi.Dispose(); Http.Dispose(); Playback.Dispose(); }

        private Task<HttpResponseMessage> ReplyPlaybackAsync(PlaybackLifecycleTests.Sent sent, CancellationToken token)
        {
            if (sent.Path.EndsWith("/Items/series", StringComparison.Ordinal))
                return Task.FromResult(Json(JsonSerializer.Serialize(new
                { Id = "series", Type = "Series", Name = "Offline Series", OriginalTitle = "", ProductionYear = SeriesYear, ProviderIds = SeriesProviderIds })));
            if (sent.Path.EndsWith("/Shows/series/Seasons", StringComparison.Ordinal))
                return Task.FromResult(Json(JsonSerializer.Serialize(new
                {
                    Items = SeasonNumbers.Select((number, index) => new
                    { Id = $"season-{index + 1}", Type = "Season", IndexNumber = number }).ToArray()
                })));
            if (sent.Path.EndsWith("/Shows/series/Episodes", StringComparison.Ordinal))
            {
                var episodes = Enumerable.Range(1, EpisodeCount).Where(index => index != MissingEpisodeNumber).Select(index => new
                {
                    Id = $"episode-{index}",
                    Type = "Episode",
                    IndexNumber = index,
                    IndexNumberEnd = MergedEpisodeNumber == index ? index + 1 : (int?)null,
                    ParentIndexNumber = 1
                }).ToArray();
                return Task.FromResult(Json(JsonSerializer.Serialize(new { Items = episodes })));
            }
            return Task.FromResult(Json("{}"));
        }
    }

    private sealed record DoubanMark(long Subject, bool Completed);

    private sealed class FakeDouban : IDoubanScrobblingClient
    {
        public Func<CancellationToken, Task<bool>> LoginCheck { get; set; } = _ => Task.FromResult(true);
        public Task<bool> CheckLoginAsync(CancellationToken cancellationToken) => LoginCheck(cancellationToken);
        public ConcurrentQueue<long> SubjectReads { get; } = new();
        public ConcurrentQueue<DoubanMark> Marks { get; } = new();
        public ConcurrentQueue<string> Searches { get; } = new();
        public IReadOnlyList<ScrobblingCandidate> SearchResults { get; set; } = [new(8, "Offline Series", 2024, null)];
        public int? SubjectEpisodeCount { get; set; } = 12;
        public Exception? SearchFailure { get; set; }
        public Func<long, CancellationToken, Task<ScrobblingSubject>>? Read { get; set; }

        public Task<IReadOnlyList<ScrobblingCandidate>> SearchSubjectsAsync(string query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Searches.Enqueue(query);
            if (SearchFailure is not null) throw SearchFailure;
            return Task.FromResult(SearchResults);
        }

        public Task<ScrobblingSubject> SubjectAsync(long id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SubjectReads.Enqueue(id);
            return Read?.Invoke(id, cancellationToken) ?? Task.FromResult(new ScrobblingSubject(id, "Offline Series", SubjectEpisodeCount));
        }

        public Task MarkAsync(long id, bool completed, Func<bool> stillAllowed, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!stillAllowed()) throw new OperationCanceledException(cancellationToken);
            Marks.Enqueue(new(id, completed));
            return Task.CompletedTask;
        }

        public string Describe() => string.Join(",", Marks.Select(mark => $"{mark.Subject}:{(mark.Completed ? "done" : "doing")}"));
    }

    private sealed record Sent(string Method, string Path, string Query, string Body, string Host, string? Scheme, bool HeaderMatched)
    {
        public string Key => Method + " " + Path;
    }

    private sealed class BangumiTransport : HttpMessageHandler
    {
        public ConcurrentQueue<Sent> Requests { get; } = new();
        public bool CollectionExists { get; set; }
        public bool EpisodeWatched { get; set; }
        public bool Reject { get; set; }
        public HttpStatusCode? FailureStatus { get; set; }

        public void ClearRequests()
        {
            while (Requests.TryDequeue(out _)) { }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var sent = new Sent(request.Method.Method, uri.AbsolutePath, uri.Query,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken),
                uri.Host, request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter == "synthetic-bangumi-placeholder");
            Requests.Enqueue(sent);
            if (FailureStatus is { } failure) return Json("{}", failure);
            if (Reject) return Json("{}", HttpStatusCode.ServiceUnavailable);
            if (sent.Key == "POST /v0/search/subjects")
                return Json("{\"data\":[{\"id\":101,\"name_cn\":\"Offline Series\",\"name\":\"Offline Series\",\"date\":\"2024-01-01\",\"eps\":12}]}");
            if (sent.Key == "GET /v0/subjects/101") return Json("{\"id\":101,\"name_cn\":\"Offline Series\",\"eps\":12}");
            if (sent.Key == "GET /v0/episodes")
            {
                var episodes = Enumerable.Range(1, 12)
                    .Select(number => new { id = 1000 + number, sort = number, type = 0 }).ToArray();
                return Json(JsonSerializer.Serialize(new { data = episodes, total = episodes.Length }));
            }
            if (sent.Key == "GET /v0/me") return Json("{\"username\":\"bangumi-fixture\"}");
            if (sent.Key == "GET /v0/users/bangumi-fixture/collections/101")
                return Json("{\"type\":2,\"rate\":8,\"comment\":\"keep\",\"tags\":[\"keep\"]}", CollectionExists ? HttpStatusCode.OK : HttpStatusCode.NotFound);
            if (sent.Key == "POST /v0/users/-/collections/101") return Json("{}", HttpStatusCode.NoContent);
            if (sent.Key.StartsWith("GET /v0/users/-/collections/-/episodes/", StringComparison.Ordinal))
            {
                var episodeId = long.Parse(sent.Key[(sent.Key.LastIndexOf('/') + 1)..]);
                return Json(JsonSerializer.Serialize(new { episode = new { id = episodeId }, type = EpisodeWatched ? 2 : 0 }));
            }
            if (sent.Key == "PATCH /v0/users/-/collections/101/episodes") return Json("{}", HttpStatusCode.NoContent);
            return Json("{}", HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
