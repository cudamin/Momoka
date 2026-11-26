using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.MoviePilot;
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
            fixture.Settings.Scrobbling.NotifyOnFailure = true;
            fixture.Refresh();
            fixture.Douban.LoginCheck = _ => Task.FromResult(false);
            await fixture.Service.CheckConnectionsAsync(CancellationToken.None);
            Assert.Equal(ScrobblingConnectionState.SignedOut, fixture.Service.DoubanConnection);
            fixture.Douban.LoginCheck = _ => throw new HttpRequestException("synthetic failure");
            await fixture.Service.CheckConnectionsAsync(CancellationToken.None);
            Assert.Equal(ScrobblingConnectionState.Failed, fixture.Service.DoubanConnection);
            Assert.Equal(0, fixture.Service.GetRecords(ScrobblingProvider.Douban).Count);
            Assert.Equal(0, fixture.Notifications.Count);
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
            Assert.Equal(0, fixture.Identity.Requests.Count);
            Assert.Equal(0, fixture.Douban.Marks.Count);
            Assert.Equal(0, fixture.Douban.SubjectReads.Count);
            Assert.Equal(0, fixture.BangumiTransport.Requests.Count);
        });

        Case("打格子服务：起播按 MoviePilot 确认的豆瓣作品标记在看", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.Refresh();
            var recorded = Signal();
            fixture.Service.Changed += () =>
            {
                if (fixture.Service.GetLatestRecord(ScrobblingProvider.Douban)?.Outcome == ScrobblingRecordOutcome.Watching)
                    recorded.TrySetResult();
            };
            var attempt = fixture.Create(fixture.Ticket());
            attempt.Observe(5, true);
            try { await recorded.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
            finally { await attempt.FinishAsync(5, true); }
            Assert.Equal("8:doing", fixture.Douban.Describe());
            Assert.Equal(0, fixture.Douban.Searches.Count);
            Assert.Equal(0, fixture.Douban.SubjectReads.Count);
            Assert.Equal("Offline Series", fixture.Identity.Requests.Single().Title);
            Assert.Equal(1, fixture.Playback.Transport.Requests.Count(request => request.Path.EndsWith("/Users/a/Items/series", StringComparison.Ordinal)));
            var record = fixture.Service.GetRecords(ScrobblingProvider.Douban).Single();
            Assert.Equal(ScrobblingRecordOutcome.Watching, record.Outcome);
            Assert.Null(record.EpisodeNumber);
            Assert.DoesNotContain("Bangumi", string.Join(" ", fixture.Service.GetRecords(ScrobblingProvider.Douban).Select(item => item.Detail)));
            Assert.Equal(0, fixture.Service.GetRecords(ScrobblingProvider.Bangumi).Count);
        });

        Case("打格子服务：多季且本地缺集时，豆瓣在实际起播后记录在看", async () =>
        {
            using var fixture = new Fixture
            {
                SeasonNumbers = [1, 2],
                EpisodeSeason = 2,
                MissingEpisodeNumber = 4,
                EpisodeCount = 8
            };
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.SeriesProviderIds["Douban"] = "77";
            fixture.Douban.SubjectEpisodeCount = null;
            fixture.Refresh();

            await fixture.Create(fixture.Ticket(8, season: 2)).FinishAsync(5, true);

            var record = fixture.Service.GetRecords(ScrobblingProvider.Douban).Single();
            Assert.Equal(ScrobblingRecordOutcome.Watching, record.Outcome, record.Detail);
            Assert.Equal("77:doing", fixture.Douban.Describe());
        });

        Case("打格子服务：豆瓣起播不等季集或 Bangumi，末集复核与 Bangumi 独立", async () =>
        {
            using var responses = new StubTransport()
                .Answer("/login/access-token", """{"access_token":"anonymous-only"}""")
                .Answer("/dashboard/system", "{}")
                .Answer("/media/42?", """{"media_source":"themoviedb","media_id":42,"type":"电视剧"}""")
                .Answer("/tmdb/42/1", """[{"id":700,"show_id":42,"season_number":1,"episode_number":1}]""")
                .Answer("/media/84?", """{"media_source":"douban","media_id":84,"type":"电视剧","tmdb_id":42,"season":1}""");
            using var transport = new PendingMoviePilotDetail(responses);
            using var client = new MoviePilotClient(transport);
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Bangumi);
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.Settings.MoviePilot.Enabled = true;
            fixture.Settings.MoviePilot.Url = "https://fixture.invalid:3001";
            fixture.Settings.MoviePilot.Username = "fixture";
            fixture.Settings.Scrobbling.NotifyOnFailure = true;
            var credentials = new MoviePilotCredentials(PassthroughSecretProtector.Instance);
            credentials.SetPassword(fixture.Settings.MoviePilot, "anonymous-only");
            using var moviePilot = new MoviePilotService(client, credentials, fixture.Settings);
            Assert.True(await moviePilot.RefreshAvailabilityAsync());
            fixture.SeriesProviderIds["Tmdb"] = "42";
            fixture.SeriesProviderIds["Douban"] = "84";
            fixture.SeriesProviderIds["Bangumi"] = "101";
            fixture.Douban.SubjectEpisodeCount = 1;
            var watching = Signal();
            var watched = Signal();
            using var service = new ScrobblingService(fixture.Settings, PassthroughSecretProtector.Instance,
                fixture.Bangumi, fixture.Douban,
                (provider, outcome, detail) => fixture.Notifications.Enqueue((provider, outcome, detail)), identityLookup: moviePilot);
            service.Changed += () =>
            {
                var outcome = service.GetLatestRecord(ScrobblingProvider.Douban)?.Outcome;
                if (outcome == ScrobblingRecordOutcome.Watching) watching.TrySetResult();
                if (outcome == ScrobblingRecordOutcome.Watched) watched.TrySetResult();
            };
            var attempt = service.CreateAttempt(fixture.Ticket(), fixture.Scope, Request())!;
            attempt.Observe(5, true);
            try
            {
                await watching.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.False(transport.EpisodesEntered.IsCompleted);
                Assert.False(transport.BangumiEntered.IsCompleted);
                var finish = attempt.FinishAsync(90, true);
                await transport.EpisodesEntered.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.Equal("84:doing", fixture.Douban.Describe());
                transport.ReleaseEpisodes();
                await transport.BangumiEntered.WaitAsync(TimeSpan.FromSeconds(3));
                await watched.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.False(finish.IsCompleted);
                transport.ReleaseBangumi();
                await finish;
            }
            finally
            {
                transport.ReleaseEpisodes();
                transport.ReleaseBangumi();
                await attempt.FinishAsync(90, true);
            }

            Assert.Equal("84:doing,84:done", fixture.Douban.Describe());
            Assert.Equal(ScrobblingProvider.Bangumi, fixture.Notifications.Single().Provider);
            Assert.Equal(ScrobblingRecordOutcome.Failed, service.GetRecords(ScrobblingProvider.Bangumi).Single().Outcome);
            Assert.Equal(0, fixture.BangumiTransport.Requests.Count);
            Assert.Equal(1, fixture.Playback.Transport.Requests.Count(request => request.Path.EndsWith("/Items/series", StringComparison.Ordinal)));
        });

        Case("打格子服务：豆瓣按已确认作品记在看，单集坐标不完整时不标看过", async () =>
        {
            foreach (var issue in new[] { "unknown-number", "unknown-season", "special", "merged", "unknown-tmdb-season-end" })
            {
                using var fixture = new Fixture();
                fixture.Enable(ScrobblingProvider.Douban);
                fixture.Enable(ScrobblingProvider.Bangumi);
                fixture.SeriesProviderIds["Douban"] = "77";
                fixture.Douban.SubjectEpisodeCount = 8;
                fixture.Refresh();
                var ticket = fixture.Ticket(8, season: issue == "special" ? 0 : 1);
                if (issue is "unknown-number" or "unknown-season")
                {
                    if (issue == "unknown-number") ticket.Item.IndexNumber = null;
                    else ticket.Item.ParentIndexNumber = null;
                    fixture.EpisodeDetail = new()
                    {
                        Id = ticket.Item.Id,
                        Type = EmbyItemType.Episode,
                        SeriesId = ticket.Item.SeriesId,
                        ParentIndexNumber = ticket.Item.ParentIndexNumber,
                        IndexNumber = ticket.Item.IndexNumber
                    };
                }
                if (issue == "merged") ticket.Item.IndexNumberEnd = 9;
                if (issue == "unknown-tmdb-season-end")
                {
                    fixture.SeriesProviderIds["Tmdb"] = "123";
                    fixture.Identity.TmdbSeasonLastEpisode = null;
                    fixture.Identity.HasTmdbEpisode = false;
                }

                await fixture.Create(ticket).FinishAsync(90, true);

                Assert.Equal("77:doing", fixture.Douban.Describe(), issue);
                Assert.Equal(0, fixture.Douban.SubjectReads.Count, issue);
                var match = fixture.Identity.Requests.Single(request => request.Douban && request.Episode == 0);
                Assert.Equal(issue is "unknown-season" or "special" ? 0 : 1, match.Season, issue);
                Assert.False(match.Bangumi, issue);
                Assert.Equal(issue == "unknown-tmdb-season-end" ? 1 : 0,
                    fixture.Identity.Requests.Count(request => request.Bangumi), issue);
                Assert.Equal(issue == "unknown-tmdb-season-end" ? 1 : 0,
                    fixture.Identity.Requests.Count(request => request.Douban && request.Episode > 0), issue);
                var record = fixture.Service.GetRecords(ScrobblingProvider.Douban).Single();
                Assert.Equal(ScrobblingRecordOutcome.Watching, record.Outcome, issue);
                Assert.Null(record.EpisodeNumber);
                Assert.False(fixture.BangumiTransport.Requests.Any(request => request.Method == "PATCH"), issue);
            }
        });

        Case("打格子服务：未连接、识别不到、识别异常都留记录且不写豆瓣", async () =>
        {
            foreach (var issue in new[] { "disconnected", "no-match", "lookup-error", "wrong-coordinate" })
            {
                using var fixture = new Fixture();
                fixture.Enable(ScrobblingProvider.Douban);
                if (issue == "disconnected") fixture.Identity.Stamp = "";
                else if (issue == "no-match") fixture.Identity.Reply = (_, _) => Task.FromResult<MoviePilotScrobblingMatch?>(null);
                else if (issue == "lookup-error") fixture.Identity.Reply = (_, _) => throw new IOException("synthetic-private-error");
                else fixture.Identity.Reply = (_, _) => Task.FromResult<MoviePilotScrobblingMatch?>(
                    new(fixture.Identity.Stamp, "themoviedb", "11", 11, 9, 1, 100, null, 8));
                fixture.Refresh();

                await fixture.Create(fixture.Ticket()).FinishAsync(5, true);

                Assert.Equal("", fixture.Douban.Describe(), issue);
                Assert.Equal(0, fixture.Douban.Searches.Count);
                var record = fixture.Service.GetRecords(ScrobblingProvider.Douban).Single();
                Assert.Equal(issue == "lookup-error" ? ScrobblingRecordOutcome.Failed : ScrobblingRecordOutcome.Skipped,
                    record.Outcome, issue);
                Assert.DoesNotContain("synthetic-private-error", record.Detail);
            }
        });

        Case("打格子服务：连载第八集跳过特别篇，Bangumi 按集标记且豆瓣保持在看", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Bangumi);
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.SeriesStatus = "Continuing";
            fixture.EpisodeCount = 8;
            fixture.SeasonNumbers = [0, 1];
            fixture.IncludeInterleavedSpecial = true;
            fixture.Refresh();
            await fixture.Create(fixture.Ticket(8)).FinishAsync(90, true);

            Assert.Equal("8:doing", fixture.Douban.Describe());
            var record = fixture.Service.GetRecords(ScrobblingProvider.Bangumi).Single();
            Assert.Equal(ScrobblingRecordOutcome.Watched, record.Outcome);
            Assert.Equal(8, record.EpisodeNumber);
            var update = fixture.BangumiTransport.Requests.Single(request => request.Key == "PATCH /v0/users/-/collections/101/episodes");
            using var body = JsonDocument.Parse(update.Body);
            Assert.Equal(1008L, body.RootElement.GetProperty("episode_id")[0].GetInt64());
            fixture.Settings.Scrobbling.Douban.Enabled = false;
            fixture.Refresh();
            Assert.Null(fixture.Service.CreateAttempt(fixture.Ticket(1, season: 0), fixture.Scope, Request()));
        });

        Case("打格子服务：实际末集由 MoviePilot 季和豆瓣条目核对，不看本地库存", async () =>
        {
            foreach (var count in new int?[] { null, 7, 8 })
            {
                using var fixture = new Fixture { EpisodeCount = 4, SeasonNumbers = [1, 2], EpisodeSeason = 2 };
                fixture.Enable(ScrobblingProvider.Douban);
                fixture.SeriesProviderIds["Tmdb"] = "123";
                fixture.Identity.TmdbSeasonLastEpisode = 8;
                fixture.Douban.SubjectEpisodeCount = count;
                fixture.Refresh();

                await fixture.Create(fixture.Ticket(8, season: 2)).FinishAsync(90, true);

                Assert.Equal(count == 8 ? "8:doing,8:done" : "8:doing", fixture.Douban.Describe());
                Assert.Equal(count == 8 ? ScrobblingRecordOutcome.Watched : ScrobblingRecordOutcome.Watching,
                    fixture.Service.GetLatestRecord(ScrobblingProvider.Douban)!.Outcome);
                Assert.True(fixture.Service.GetRecords(ScrobblingProvider.Douban).All(record => record.EpisodeNumber is null));
                Assert.False(fixture.Playback.Transport.Requests.Any(request => request.Path.Contains("/Shows/series/", StringComparison.Ordinal)));
            }
        });

        Case("打格子服务：末集核对时豆瓣条目改了，保留起播时的在看状态", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.Settings.Scrobbling.NotifyOnFailure = true;
            fixture.Identity.Reply = (request, _) => Task.FromResult<MoviePilotScrobblingMatch?>(new(
                fixture.Identity.Stamp, "themoviedb", "123", 123, request.Season, request.Episode,
                request.Episode == 0 ? null : 999, null, request.Episode == 0 ? 8 : 9, TmdbSeasonLastEpisode: 12));
            fixture.Refresh();

            await fixture.Create(fixture.Ticket(12)).FinishAsync(90, true);

            Assert.Equal("8:doing", fixture.Douban.Describe());
            Assert.Equal(0, fixture.Douban.SubjectReads.Count);
            Assert.Equal(2, fixture.Identity.Requests.Count);
            Assert.Equal(ScrobblingRecordOutcome.Skipped, fixture.Notifications.Single().Outcome);
            Assert.True(fixture.Service.GetRecords(ScrobblingProvider.Douban).Select(record => record.Outcome)
                .SequenceEqual([ScrobblingRecordOutcome.Skipped, ScrobblingRecordOutcome.Watching]));
        });

        Case("打格子服务：末集查询故障只通知一次，保留豆瓣在看", async () =>
        {
            foreach (var failed in new[] { false, true })
            {
                using var fixture = new Fixture();
                fixture.Enable(ScrobblingProvider.Douban);
                fixture.Settings.Scrobbling.NotifyOnFailure = true;
                fixture.Identity.Reply = (request, _) => Task.FromResult<MoviePilotScrobblingMatch?>(new(
                    fixture.Identity.Stamp, "themoviedb", "123", 123, request.Season, request.Episode,
                    null, null, 8, TmdbSeasonLookupFailed: failed && request.Episode > 0));
                fixture.Refresh();

                await fixture.Create(fixture.Ticket(12)).FinishAsync(90, true);

                Assert.Equal("8:doing", fixture.Douban.Describe());
                Assert.Equal(0, fixture.Douban.SubjectReads.Count);
                Assert.Equal(2, fixture.Identity.Requests.Count);
                var records = fixture.Service.GetRecords(ScrobblingProvider.Douban);
                Assert.Equal(ScrobblingRecordOutcome.Watching, records.Last().Outcome);
                Assert.Equal(failed ? 2 : 1, records.Count);
                if (failed)
                {
                    Assert.Equal(ScrobblingRecordOutcome.Failed, records[0].Outcome);
                    Assert.Equal((ScrobblingProvider.Douban, ScrobblingRecordOutcome.Failed, records[0].Detail), fixture.Notifications.Single());
                }
                else Assert.Equal(0, fixture.Notifications.Count);
            }
        });

        Case("打格子服务：豆瓣直给的后续季再次核实同一作品和站点公开集数后才标看过", async () =>
        {
            foreach (var (lastEpisode, completionId, count) in new (int?, long, int?)[]
            {
                (8, 77, 8), (null, 77, 8), (8, 78, 8), (8, 77, 12), (8, 77, null)
            })
            {
                using var fixture = new Fixture { EpisodeCount = 4, SeasonNumbers = [1, 2], EpisodeSeason = 2 };
                fixture.Enable(ScrobblingProvider.Douban);
                fixture.SeriesProviderIds["Douban"] = "77";
                fixture.Douban.SubjectEpisodeCount = count;
                fixture.Identity.Reply = (request, _) => Task.FromResult<MoviePilotScrobblingMatch?>(new(
                    fixture.Identity.Stamp, "douban", "77", null, request.Season, request.Episode, null, null,
                    request.Episode == 0 ? 77 : completionId, DoubanEpisode: request.Episode == 0 ? null : lastEpisode));
                fixture.Refresh();

                await fixture.Create(fixture.Ticket(8, season: 2)).FinishAsync(90, true);

                Assert.Equal(lastEpisode == 8 && completionId == 77 && count == 8 ? "77:doing,77:done" : "77:doing",
                    fixture.Douban.Describe());
                Assert.True(fixture.Identity.Requests.Select(request => (request.Season, request.Episode))
                    .SequenceEqual([(2, 0), (2, 8)]));
                Assert.Equal(lastEpisode == 8 && completionId == 77 ? 1 : 0, fixture.Douban.SubjectReads.Count);
            }
        });

        Case("打格子服务：豆瓣已经看过时保留看过，末集不重复写入", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.Douban.AlreadyWatched = true;
            fixture.Refresh();

            await fixture.Create(fixture.Ticket(12)).FinishAsync(90, true);

            var record = fixture.Service.GetRecords(ScrobblingProvider.Douban).Single();
            Assert.Equal(ScrobblingRecordOutcome.Watched, record.Outcome);
            Assert.Null(record.EpisodeNumber);
            Assert.Equal(0, fixture.Douban.Marks.Count);
            Assert.Equal(0, fixture.Douban.SubjectReads.Count);
        });

        Case("打格子服务：详情补齐父剧再匹配，父剧缺失或集号冲突会记录跳过", async () =>
        {
            foreach (var issue in new[] { "ready", "no-data", "wrong-episode" })
            {
                using var fixture = new Fixture();
                fixture.Enable(ScrobblingProvider.Douban);
                fixture.EpisodeDetail = issue switch
                {
                    "ready" => new() { Id = "episode-1", Type = EmbyItemType.Episode, SeriesId = "series", ParentIndexNumber = 1, IndexNumber = 1 },
                    "wrong-episode" => new() { Id = "episode-1", Type = EmbyItemType.Episode, SeriesId = "series", ParentIndexNumber = 1, IndexNumber = 2 },
                    _ => null
                };
                fixture.Refresh();
                var ticket = fixture.Ticket(1);
                ticket.Item.SeriesId = null;
                ticket.Item.ParentIndexNumber = null;

                await fixture.Create(ticket).FinishAsync(5, true);

                Assert.Equal(issue == "ready" ? "8:doing" : "", fixture.Douban.Describe(), issue);
                Assert.Equal(issue == "ready" ? ScrobblingRecordOutcome.Watching : ScrobblingRecordOutcome.Skipped,
                    fixture.Service.GetLatestRecord(ScrobblingProvider.Douban)!.Outcome, issue);
                Assert.Equal(issue == "ready" ? 1 : 0, fixture.Identity.Requests.Count);
            }
        });

        Case("打格子通知：共享媒体库读取失败每站只记一次，两站只通知一条且不带响应正文", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Bangumi);
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.Settings.Scrobbling.NotifyOnFailure = true;
            fixture.Playback.Transport.Reply = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("synthetic server response")
            });
            fixture.Refresh();
            await fixture.Create(fixture.Ticket(8)).FinishAsync(90, true);

            Assert.Equal(0, fixture.Douban.Marks.Count);
            Assert.Equal(0, fixture.BangumiTransport.Requests.Count);
            foreach (var provider in new[] { ScrobblingProvider.Bangumi, ScrobblingProvider.Douban })
            {
                var record = fixture.Service.GetRecords(provider).Single();
                Assert.Equal(ScrobblingRecordOutcome.Failed, record.Outcome);
                Assert.Equal("自动匹配读取媒体库信息失败，已跳过同步", record.Detail);
            }
            var notification = fixture.Notifications.Single();
            Assert.Null(notification.Provider);
            Assert.Equal(ScrobblingRecordOutcome.Failed, notification.Outcome);
            Assert.Equal("自动匹配读取媒体库信息失败，已跳过同步", notification.Detail);
        });

        Case("打格子通知：默认关闭，播放中改开关依然记录失败并按新值通知", async () =>
        {
            const string AnonymousResponse = "synthetic-secret-for-notice";
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.Refresh();
            Assert.False(fixture.Settings.Scrobbling.NotifyOnFailure);

            fixture.Douban.Write = (_, _, _) => throw new IOException(AnonymousResponse);
            await fixture.Create(fixture.Ticket()).FinishAsync(5, true);
            Assert.Equal(ScrobblingRecordOutcome.Failed, fixture.Service.GetRecords(ScrobblingProvider.Douban).Single().Outcome);
            Assert.Equal(0, fixture.Notifications.Count);

            foreach (var enabled in new[] { true, false })
            {
                var entered = Signal();
                var release = Signal();
                fixture.Douban.Write = async (_, _, token) =>
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(token);
                    throw new IOException(AnonymousResponse);
                };
                var attempt = fixture.Create(fixture.Ticket(12));
                var finish = attempt.FinishAsync(90, true);
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                    fixture.Settings.Scrobbling.NotifyOnFailure = enabled;
                }
                finally { release.TrySetResult(); }
                await finish;
                Assert.Equal(1, fixture.Notifications.Count);
            }

            var notification = fixture.Notifications.Single();
            Assert.Equal(ScrobblingProvider.Douban, notification.Provider);
            Assert.Equal("未能确认同步结果，请到站点核对", notification.Detail);
            Assert.DoesNotContain(AnonymousResponse, notification.Detail);
            Assert.DoesNotContain("Offline Series", notification.Detail);
            var records = fixture.Service.GetRecords(ScrobblingProvider.Douban);
            Assert.Equal(3, records.Count);
            Assert.True(records.All(record => record.Outcome == ScrobblingRecordOutcome.Failed));
            Assert.Equal("8:doing,8:doing,8:doing", fixture.Douban.Describe(), "结果未确认时不能继续写入看过状态");
        });

        Case("打格子通知：成功保持静默，无法可靠匹配只提示一次", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.Settings.Scrobbling.NotifyOnFailure = true;
            fixture.Refresh();
            await fixture.Create(fixture.Ticket()).FinishAsync(5, true);
            Assert.Equal(0, fixture.Notifications.Count);
            fixture.Identity.Reply = (_, _) => Task.FromResult<MoviePilotScrobblingMatch?>(null);
            await fixture.Create(fixture.Ticket()).FinishAsync(90, true);

            Assert.True(fixture.Service.GetRecords(ScrobblingProvider.Douban).Select(record => record.Outcome)
                .SequenceEqual([ScrobblingRecordOutcome.Skipped, ScrobblingRecordOutcome.Watching]));
            Assert.Equal("8:doing", fixture.Douban.Describe());
            var notification = fixture.Notifications.Single();
            Assert.Equal(ScrobblingProvider.Douban, notification.Provider);
            Assert.Equal(ScrobblingRecordOutcome.Skipped, notification.Outcome);
            Assert.Equal(fixture.Service.GetLatestRecord(ScrobblingProvider.Douban)!.Detail, notification.Detail);
            fixture.Settings.Scrobbling.NotifyOnFailure = false;
            await fixture.Create(fixture.Ticket()).FinishAsync(90, true);
            Assert.Equal(1, fixture.Notifications.Count);
        });

        Case("打格子通知：豆瓣已确认退出时提示重新登录且不继续写入", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.Settings.Scrobbling.NotifyOnFailure = true;
            fixture.Douban.Write = (_, _, _) => throw new HttpRequestException("synthetic-private-response", null, HttpStatusCode.Unauthorized);
            fixture.Refresh();

            await fixture.Create(fixture.Ticket(12)).FinishAsync(90, true);

            const string Reason = "豆瓣拒绝请求，请重新登录";
            Assert.Equal((ScrobblingProvider.Douban, ScrobblingRecordOutcome.Failed, Reason), fixture.Notifications.Single());
            Assert.Equal(Reason, fixture.Service.GetRecords(ScrobblingProvider.Douban).Single().Detail);
            Assert.Equal("8:doing", fixture.Douban.Describe());
        });

        Case("打格子通知：只报告超时站点，晚到回执不改记录且不重发", async () =>
        {
            var notified = Signal();
            using var fixture = new Fixture(TimeSpan.FromMilliseconds(500), () => notified.TrySetResult());
            fixture.Enable(ScrobblingProvider.Bangumi);
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.Settings.Scrobbling.NotifyOnFailure = true;
            var bangumiCompleted = Signal();
            fixture.Service.Changed += () =>
            {
                if (fixture.Service.GetLatestRecord(ScrobblingProvider.Bangumi)?.Outcome == ScrobblingRecordOutcome.Watched)
                    bangumiCompleted.TrySetResult();
            };
            var entered = Signal();
            // Run the fake response's awaiting continuation inline when releasing the already timed-out write.
            var lateReply = new TaskCompletionSource();
            fixture.Douban.Write = (_, completed, _) =>
            {
                if (!completed) return Task.CompletedTask;
                entered.TrySetResult();
                return lateReply.Task;
            };
            fixture.Refresh();
            var attempt = fixture.Create(fixture.Ticket(12));
            var finish = attempt.FinishAsync(90, true);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                await bangumiCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3));
                await notified.Task.WaitAsync(TimeSpan.FromSeconds(3));
                await finish;
            }
            finally { lateReply.TrySetResult(); }

            var notification = fixture.Notifications.Single();
            Assert.Equal(ScrobblingProvider.Douban, notification.Provider);
            Assert.Equal("同步超时，结果未确认，请到站点核对", notification.Detail);
            var doubanRecords = fixture.Service.GetRecords(ScrobblingProvider.Douban);
            Assert.True(doubanRecords.Select(record => record.Outcome)
                .SequenceEqual([ScrobblingRecordOutcome.Failed, ScrobblingRecordOutcome.Watching]));
            Assert.Equal(notification.Detail, doubanRecords[0].Detail);
            Assert.Equal(ScrobblingRecordOutcome.Watched, fixture.Service.GetRecords(ScrobblingProvider.Bangumi).Single().Outcome);
            Assert.Equal("8:doing,8:done", fixture.Douban.Describe());
            Assert.Equal(1, fixture.BangumiTransport.Requests.Count(request => request.Method == "PATCH"));
        });

        Case("打格子服务：末集达到标记已看进度后才标记看过", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.Refresh();
            await fixture.Create(fixture.Ticket(11)).FinishAsync(90, true);
            await fixture.Create(fixture.Ticket(12)).FinishAsync(89.999, true);
            await fixture.Create(fixture.Ticket(12)).FinishAsync(90, true);
            Assert.Equal("8:doing,8:doing,8:doing,8:done", fixture.Douban.Describe());
        });

        Case("打格子服务：豆瓣集数未知仍可在看，不标看过", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.Douban.SubjectEpisodeCount = null;
            fixture.Refresh();
            await fixture.Create(fixture.Ticket(12)).FinishAsync(90, true);
            Assert.Equal("8:doing", fixture.Douban.Describe());
            Assert.Equal(ScrobblingRecordOutcome.Watching, fixture.Service.GetLatestRecord(ScrobblingProvider.Douban)!.Outcome);
        });

        Case("打格子服务：用 MoviePilot 识别的 Bangumi 作品按集标记", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Bangumi);
            fixture.Refresh();
            await fixture.Create(fixture.Ticket(1)).FinishAsync(89, true);
            Assert.Equal(0, fixture.BangumiTransport.Requests.Count);
            await fixture.Create(fixture.Ticket(1)).FinishAsync(90, true);
            var sent = fixture.BangumiTransport.Requests.ToArray();
            Assert.False(sent.Any(request => request.Key == "POST /v0/search/subjects"));
            Assert.Equal(1, fixture.Identity.Requests.Count);
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

        Case("打格子服务：TMDB 第 8 集与第 13 集精确对应不同的 Bangumi 条目", async () =>
        {
            foreach (var (number, subject, episodeId) in new[] { (8, 524707, 1521517), (13, 587454, 1568817) })
            {
                using var fixture = new Fixture();
                fixture.Enable(ScrobblingProvider.Bangumi);
                fixture.SeriesProviderIds["Tmdb"] = "277513";
                fixture.SeriesProviderIds["Bangumi"] = "";
                fixture.SeriesYear = 2025;
                fixture.SeriesName = "我们不可能成为恋人！绝对不行。（※似乎可行！？）";
                fixture.EpisodeCount = number == 8 ? 17 : 13;
                fixture.MissingEpisodeNumber = number == 13 ? 4 : null;
                fixture.BangumiTransport.SubjectId = subject;
                fixture.BangumiTransport.SubjectEpisodeCount = number == 8 ? 12 : 5;
                fixture.BangumiTransport.EpisodeDetails[episodeId] = (subject, 0);
                fixture.Refresh();

                await fixture.Create(fixture.Ticket(number)).FinishAsync(90, true);

                var record = fixture.Service.GetRecords(ScrobblingProvider.Bangumi).Single();
                Assert.Equal(ScrobblingRecordOutcome.Watched, record.Outcome, record.Detail);
                Assert.Equal(number, record.EpisodeNumber);
                Assert.False(fixture.BangumiTransport.Requests.Any(request => request.Key == "POST /v0/search/subjects"));
                var update = fixture.BangumiTransport.Requests.Single(request => request.Method == "PATCH");
                Assert.Equal($"PATCH /v0/users/-/collections/{subject}/episodes", update.Key);
                using var body = JsonDocument.Parse(update.Body);
                Assert.Equal((long)episodeId, body.RootElement.GetProperty("episode_id")[0].GetInt64());
            }
        });

        Case("打格子服务：TMDB 精确对应无法核实或本地集号冲突时只记录跳过", async () =>
        {
            foreach (var issue in new[] { "unmapped", "subject-404", "episode-404", "wrong-subject", "special" })
            {
                using var fixture = new Fixture();
                fixture.Enable(ScrobblingProvider.Bangumi);
                fixture.Settings.Scrobbling.NotifyOnFailure = true;
                fixture.SeriesProviderIds["Tmdb"] = "277513";
                fixture.EpisodeCount = 18;
                fixture.BangumiTransport.SubjectId = issue == "subject-404" ? 101 : 524707;
                if (issue != "episode-404")
                    fixture.BangumiTransport.EpisodeDetails[1521517] = (issue == "wrong-subject" ? 587454 : 524707,
                        issue == "special" ? 1 : 0);
                fixture.Refresh();
                var ticket = fixture.Ticket(issue == "unmapped" ? 18 : 8);

                await fixture.Create(ticket).FinishAsync(90, true);

                Assert.Equal(ScrobblingRecordOutcome.Skipped, fixture.Service.GetRecords(ScrobblingProvider.Bangumi).Single().Outcome, issue);
                Assert.False(fixture.BangumiTransport.Requests.Any(request => request.Method != "GET"), issue);
                Assert.Equal(ScrobblingRecordOutcome.Skipped, fixture.Notifications.Single().Outcome, issue);
            }
        });

        Case("打格子服务：TMDB 定位多季 Bangumi 单集，豆瓣缺少季关联时跳过", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Bangumi);
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.Settings.Scrobbling.NotifyOnFailure = true;
            fixture.SeriesProviderIds["Tmdb"] = "137";
            fixture.Identity.DoubanId = null;
            fixture.SeasonNumbers = [1, 2];
            fixture.EpisodeSeason = 2;
            fixture.BangumiTransport.SubjectId = 307402;
            fixture.BangumiTransport.EpisodeDetails[954032] = (307402, 0);
            fixture.Refresh();
            await fixture.Create(fixture.Ticket(8, season: 2)).FinishAsync(90, true);

            Assert.Equal(ScrobblingRecordOutcome.Watched, fixture.Service.GetRecords(ScrobblingProvider.Bangumi).Single().Outcome);
            Assert.Equal(ScrobblingRecordOutcome.Skipped, fixture.Service.GetRecords(ScrobblingProvider.Douban).Single().Outcome);
            Assert.Equal(0, fixture.Douban.Searches.Count);
            Assert.Equal(0, fixture.Douban.Marks.Count);
            Assert.Equal(ScrobblingProvider.Douban, fixture.Notifications.Single().Provider);
            var update = fixture.BangumiTransport.Requests.Single(request => request.Method == "PATCH");
            Assert.Equal("PATCH /v0/users/-/collections/307402/episodes", update.Key);
            using var body = JsonDocument.Parse(update.Body);
            Assert.Equal(954032L, body.RootElement.GetProperty("episode_id")[0].GetInt64());
        });

        Case("打格子服务：截图的飙马野郎由 MoviePilot 核实后只写同一确切 Bangumi 单集", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Bangumi);
            fixture.SeriesProviderIds["Tmdb"] = "45790";
            fixture.BangumiTransport.SubjectId = 639938;
            fixture.BangumiTransport.EpisodeDetails[1716920] = (639938, 0);
            fixture.Refresh();

            await fixture.Create(fixture.Ticket(10, season: 6)).FinishAsync(90, true);

            Assert.Equal((6, 10), (fixture.Identity.Requests.Single().Season, fixture.Identity.Requests.Single().Episode));
            Assert.Equal(ScrobblingRecordOutcome.Watched, fixture.Service.GetLatestRecord(ScrobblingProvider.Bangumi)!.Outcome);
            var write = fixture.BangumiTransport.Requests.Single(request => request.Method == "PATCH");
            Assert.Equal("PATCH /v0/users/-/collections/639938/episodes", write.Key);
            using var body = JsonDocument.Parse(write.Body);
            Assert.Equal(1716920L, body.RootElement.GetProperty("episode_id")[0].GetInt64());
        });

        Case("打格子服务：MoviePilot 连接代次改变，旧识别结果不能提交", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Douban);
            var entered = Signal();
            var release = Signal();
            fixture.Identity.Reply = async (request, _) =>
            {
                entered.TrySetResult();
                await release.Task.ConfigureAwait(false);
                return new("offline-mp", "themoviedb", "11", 11, request.Season, request.Episode, 1234, null, 8);
            };
            fixture.Refresh();
            var sync = fixture.Create(fixture.Ticket()).FinishAsync(5, true);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                fixture.Identity.Stamp = "replacement-mp";
            }
            finally { release.TrySetResult(); }
            await sync;
            Assert.Equal(0, fixture.Douban.Marks.Count);
            Assert.Equal(0, fixture.Service.GetRecords(ScrobblingProvider.Douban).Count);
        });

        Case("打格子服务：创建场次后起播前切换 MoviePilot，旧场次不向新连接发请求", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Bangumi);
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.Settings.Scrobbling.NotifyOnFailure = true;
            fixture.Refresh();
            var attempt = fixture.Create(fixture.Ticket(12));
            fixture.Identity.Stamp = "replacement-mp";

            await attempt.FinishAsync(90, true);

            Assert.Equal(0, fixture.Identity.Requests.Count);
            Assert.Equal(0, fixture.BangumiTransport.Requests.Count);
            Assert.Equal(0, fixture.Douban.Marks.Count);
            Assert.Equal(0, fixture.Notifications.Count);
            Assert.Equal(0, fixture.Service.GetRecords(ScrobblingProvider.Douban).Count);
            Assert.Equal(0, fixture.Service.GetRecords(ScrobblingProvider.Bangumi).Count);
        });

        Case("打格子通知：已匹配 Bangumi 作品但找不到对应集时提醒，零收藏写入", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Bangumi);
            fixture.Settings.Scrobbling.NotifyOnFailure = true;
            fixture.SeriesProviderIds["Bangumi"] = "101";
            fixture.BangumiTransport.Episodes = [(1001, 1)];
            fixture.Refresh();
            await fixture.Create(fixture.Ticket(8)).FinishAsync(90, true);
            Assert.Equal(ScrobblingRecordOutcome.Skipped, fixture.Service.GetRecords(ScrobblingProvider.Bangumi).Single().Outcome);
            Assert.Equal((ScrobblingProvider.Bangumi, ScrobblingRecordOutcome.Skipped, "对应集号无法确认，已跳过"),
                fixture.Notifications.Single());
            Assert.True(fixture.BangumiTransport.Requests.All(request => request.Method == "GET"));
        });

        Case("打格子服务：同一集的两站记录互相隔离", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Bangumi);
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.Refresh();
            await fixture.Create(fixture.Ticket(8)).FinishAsync(90, true);
            var bangumi = fixture.Service.GetRecords(ScrobblingProvider.Bangumi).Single();
            var douban = fixture.Service.GetRecords(ScrobblingProvider.Douban).Single();
            Assert.Equal(ScrobblingRecordOutcome.Watched, bangumi.Outcome);
            Assert.Equal(ScrobblingRecordOutcome.Watching, douban.Outcome);
            Assert.Equal(8, bangumi.EpisodeNumber);
            Assert.Null(douban.EpisodeNumber);
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

        Case("打格子通知：配置修改或退出媒体账号取消在途写入，迟到失败不会通知", async () =>
        {
            foreach (var revokeAccount in new[] { false, true })
            {
                using var fixture = new Fixture();
                fixture.Enable(ScrobblingProvider.Douban);
                fixture.Settings.Scrobbling.NotifyOnFailure = true;
                var entered = Signal();
                var cancelled = Signal();
                var lateReply = new TaskCompletionSource();
                fixture.Douban.Write = async (_, _, token) =>
                {
                    using var registration = token.Register(() => cancelled.TrySetResult());
                    entered.TrySetResult();
                    await lateReply.Task.ConfigureAwait(false);
                };
                fixture.Refresh();
                var finish = fixture.Create(fixture.Ticket(1)).FinishAsync(5, true);
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                    if (revokeAccount) fixture.Playback.Session.SignOut();
                    else
                    {
                        fixture.Settings.Scrobbling.Douban.Enabled = false;
                        fixture.Refresh();
                    }
                    await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
                }
                finally { lateReply.TrySetException(new IOException("synthetic response after cancellation")); }
                await finish;
                Assert.Equal(1, fixture.Douban.Marks.Count);
                Assert.Equal(0, fixture.Service.GetRecords(ScrobblingProvider.Douban).Count);
                Assert.Equal(0, fixture.Notifications.Count);
            }
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

        Case("打格子通知：Bangumi 和通知宿主失败不阻止豆瓣自动完成", async () =>
        {
            using var fixture = new Fixture(onNotification: () => throw new IOException("synthetic notification failure"));
            fixture.Enable(ScrobblingProvider.Bangumi);
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.Settings.Scrobbling.NotifyOnFailure = true;
            fixture.BangumiTransport.Reject = true;
            fixture.Refresh();
            await fixture.Create(fixture.Ticket(12)).FinishAsync(100, true);
            Assert.Equal("8:doing,8:done", fixture.Douban.Describe());
            Assert.True(fixture.BangumiTransport.Requests.Count > 0);
            Assert.Equal(ScrobblingRecordOutcome.Failed, fixture.Service.GetRecords(ScrobblingProvider.Bangumi).Single().Outcome);
            Assert.Equal(ScrobblingProvider.Bangumi, fixture.Notifications.Single().Provider);
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
        public string SeriesName { get; set; } = "Offline Series";
        public int? SeriesYear { get; set; } = 2024;
        public string? SeriesStatus { get; set; }
        public Dictionary<string, string> SeriesProviderIds { get; } = new(StringComparer.Ordinal);
        public IReadOnlyList<int> SeasonNumbers { get; set; } = [1];
        public bool IncludeInterleavedSpecial { get; set; }
        public int? MissingEpisodeNumber { get; set; }
        public int EpisodeCount { get; set; } = 12;
        public int EpisodeSeason { get; set; } = 1;
        public AppSettings Settings => Playback.Settings;
        public EmbySessionScope Scope { get; }
        public FakeDouban Douban { get; } = new();
        public FakeIdentityLookup Identity { get; } = new();
        public EmbyItem? EpisodeDetail { get; set; }
        public BangumiTransport BangumiTransport { get; } = new();
        public HttpClient Http { get; }
        public BangumiClient Bangumi { get; }
        public ScrobblingService Service { get; }
        public ConcurrentQueue<(ScrobblingProvider? Provider, ScrobblingRecordOutcome Outcome, string Detail)> Notifications { get; } = new();

        public Fixture(TimeSpan? syncTimeout = null, Action? onNotification = null)
        {
            Scope = Playback.Session.Capture();
            Playback.Transport.Reply = ReplyPlaybackAsync;
            Http = new HttpClient(BangumiTransport);
            Bangumi = new BangumiClient(Http);
            Service = new ScrobblingService(Settings, PassthroughSecretProtector.Instance, Bangumi, Douban,
                (provider, outcome, detail) =>
                {
                    Notifications.Enqueue((provider, outcome, detail));
                    onNotification?.Invoke();
                }, syncTimeout, Identity);
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
            var ticket = PlaybackLifecycleTests.Ticket($"episode-{episode}");
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
            if (sent.Path.EndsWith($"/Items/{EpisodeDetail?.Id ?? "episode-1"}", StringComparison.Ordinal))
                return Task.FromResult(Json(JsonSerializer.Serialize(EpisodeDetail ?? new EmbyItem())));
            if (sent.Path.EndsWith("/Items/series", StringComparison.Ordinal))
                return Task.FromResult(Json(JsonSerializer.Serialize(new
                {
                    Id = "series",
                    Type = "Series",
                    Name = SeriesName,
                    OriginalTitle = "",
                    ProductionYear = SeriesYear,
                    Status = SeriesStatus,
                    ProviderIds = SeriesProviderIds
                })));
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
                    ParentIndexNumber = EpisodeSeason
                }).ToList();
                if (IncludeInterleavedSpecial) episodes.Add(new
                {
                    Id = "special-1",
                    Type = "Episode",
                    IndexNumber = 1,
                    ParentIndexNumber = 0
                });
                return Task.FromResult(Json(JsonSerializer.Serialize(new { Items = episodes })));
            }
            return Task.FromResult(Json("{}"));
        }
    }

    private sealed class PendingMoviePilotDetail(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private readonly TaskCompletionSource _episodesEntered = Signal();
        private readonly TaskCompletionSource _bangumiEntered = Signal();
        private readonly TaskCompletionSource _releaseEpisodes = Signal();
        private readonly TaskCompletionSource _releaseBangumi = Signal();

        public Task EpisodesEntered => _episodesEntered.Task;
        public Task BangumiEntered => _bangumiEntered.Task;
        public void ReleaseEpisodes() => _releaseEpisodes.TrySetResult();
        public void ReleaseBangumi() => _releaseBangumi.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath;
            if (path?.EndsWith("/media/101", StringComparison.Ordinal) == true)
            {
                _bangumiEntered.TrySetResult();
                await _releaseBangumi.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return new(HttpStatusCode.ServiceUnavailable);
            }
            if (path?.EndsWith("/tmdb/42/1", StringComparison.Ordinal) == true)
            {
                _episodesEntered.TrySetResult();
                await _releaseEpisodes.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed record LookupRequest(string Title, int Season, int Episode, bool Bangumi, bool Douban);

    private sealed class FakeIdentityLookup : IScrobblingIdentityLookup
    {
        public ConcurrentQueue<LookupRequest> Requests { get; } = new();
        public string Stamp { get; set; } = "offline-mp";
        public long? DoubanId { get; set; } = 8;
        public int? TmdbSeasonLastEpisode { get; set; } = 12;
        public bool HasTmdbEpisode { get; set; } = true;
        public Func<LookupRequest, CancellationToken, Task<MoviePilotScrobblingMatch?>>? Reply { get; set; }
        public string ScrobblingConnectionStamp => Stamp;

        public bool IsCurrentScrobblingConnection(string stamp) => Stamp.Length > 0 && stamp == Stamp;

        public Task<MoviePilotScrobblingMatch?> ResolveScrobblingEpisodeAsync(string title, string? originalTitle,
            int? year, IReadOnlyDictionary<string, string>? providerIds, int season, int episode,
            bool includeBangumi, bool includeDouban, CancellationToken cancellationToken, string expectedConnectionStamp)
        {
            if (!IsCurrentScrobblingConnection(expectedConnectionStamp)) return Task.FromResult<MoviePilotScrobblingMatch?>(null);
            var request = new LookupRequest(title, season, episode, includeBangumi, includeDouban);
            Requests.Enqueue(request);
            if (Reply is { } reply) return reply(request, cancellationToken);
            var tmdbId = Number("Tmdb");
            var source = tmdbId.HasValue ? "themoviedb" : includeDouban ? "douban" : "bangumi";
            var mediaId = tmdbId ?? (includeDouban ? Number("Douban") ?? DoubanId : Number("Bangumi") ?? 101);
            var match = new MoviePilotScrobblingMatch(Stamp, source, mediaId?.ToString() ?? "11",
                tmdbId is { } id ? (int)id : null, season, episode, tmdbId is not null && HasTmdbEpisode ? 999 : null,
                includeBangumi ? Number("Bangumi") ?? 101 : null, includeDouban ? Number("Douban") ?? DoubanId : null,
                TmdbSeasonLastEpisode: tmdbId is null ? null : TmdbSeasonLastEpisode);
            return Task.FromResult<MoviePilotScrobblingMatch?>(match);

            long? Number(string key) => long.TryParse(providerIds?.GetValueOrDefault(key), out var value) ? value : null;
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
        public int? SubjectEpisodeCount { get; set; } = 12;
        public bool AlreadyWatched { get; set; }
        public Func<long, bool, CancellationToken, Task>? Write { get; set; }

        public Task<IReadOnlyList<ScrobblingCandidate>> SearchSubjectsAsync(string query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Searches.Enqueue(query);
            return Task.FromResult<IReadOnlyList<ScrobblingCandidate>>([]);
        }

        public Task<ScrobblingSubject> SubjectAsync(long id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SubjectReads.Enqueue(id);
            return Task.FromResult(new ScrobblingSubject(id, "Offline Series", SubjectEpisodeCount));
        }

        public Task MarkAsync(long id, bool completed, Func<bool> stillAllowed, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!stillAllowed()) throw new OperationCanceledException(cancellationToken);
            Marks.Enqueue(new(id, completed));
            return Write?.Invoke(id, completed, cancellationToken) ?? Task.CompletedTask;
        }

        public async Task<bool> MarkEpisodeAsync(long id, int episodeNumber, bool completed,
            Func<bool> stillAllowed, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (AlreadyWatched) return true;
            if (completed && (await SubjectAsync(id, cancellationToken)).EpisodeCount != episodeNumber) return false;
            await MarkAsync(id, completed, stillAllowed, cancellationToken);
            return completed;
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
        public int SubjectId { get; set; } = 101;
        public int SubjectEpisodeCount { get; set; } = 12;
        public Dictionary<int, (int SubjectId, int Type)> EpisodeDetails { get; } = [];
        public IReadOnlyList<(int Id, int Number)> Episodes { get; set; } = Enumerable.Range(1, 12)
            .Select(number => (1000 + number, number)).ToArray();

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
            if (sent.Key == $"GET /v0/subjects/{SubjectId}")
                return Json(JsonSerializer.Serialize(new { id = SubjectId, type = 2, name_cn = "Offline Series", eps = SubjectEpisodeCount }));
            if (sent.Key.StartsWith("GET /v0/episodes/", StringComparison.Ordinal)
                && int.TryParse(uri.Segments[^1], out var id) && EpisodeDetails.TryGetValue(id, out var detail))
                return Json(JsonSerializer.Serialize(new { id, subject_id = detail.SubjectId, type = detail.Type }));
            if (sent.Key == "GET /v0/episodes")
            {
                var episodes = Episodes.Select(episode => new { id = episode.Id, sort = episode.Number, type = 0 }).ToArray();
                return Json(JsonSerializer.Serialize(new { data = episodes, total = episodes.Length }));
            }
            if (sent.Key == "GET /v0/me") return Json("{\"username\":\"bangumi-fixture\"}");
            if (sent.Key == $"GET /v0/users/bangumi-fixture/collections/{SubjectId}")
                return Json("{\"type\":2,\"rate\":8,\"comment\":\"keep\",\"tags\":[\"keep\"]}", CollectionExists ? HttpStatusCode.OK : HttpStatusCode.NotFound);
            if (sent.Key == $"POST /v0/users/-/collections/{SubjectId}") return Json("{}", HttpStatusCode.NoContent);
            if (sent.Key.StartsWith("GET /v0/users/-/collections/-/episodes/", StringComparison.Ordinal))
            {
                var episodeId = long.Parse(sent.Key[(sent.Key.LastIndexOf('/') + 1)..]);
                return Json(JsonSerializer.Serialize(new { episode = new { id = episodeId }, type = EpisodeWatched ? 2 : 0 }));
            }
            if (sent.Key == $"PATCH /v0/users/-/collections/{SubjectId}/episodes") return Json("{}", HttpStatusCode.NoContent);
            return Json("{}", HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
