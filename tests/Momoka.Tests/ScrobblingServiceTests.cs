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
            Assert.Null(fixture.Service.CreateAttempt(fixture.Ticket(1, season: 0), fixture.Scope, Request()));
        });

        Case("打格子服务：已完结或状态未知的缺集、连载超出站点总集数均跳过", async () =>
        {
            foreach (var (status, count) in new (string?, int)[] { ("Ended", 8), (null, 8), ("Continuing", 13) })
            {
                using var fixture = new Fixture();
                fixture.Enable(ScrobblingProvider.Douban);
                fixture.SeriesStatus = status;
                fixture.EpisodeCount = count;
                fixture.Refresh();
                await fixture.Create(fixture.Ticket(8)).FinishAsync(90, true);
                Assert.Equal(0, fixture.Douban.Marks.Count);
                var record = fixture.Service.GetRecords(ScrobblingProvider.Douban).Single();
                Assert.Equal(ScrobblingRecordOutcome.Skipped, record.Outcome);
                Assert.Equal("站点总集数与媒体库不一致，已跳过同步", record.Detail);
            }
        });

        Case("打格子服务：复杂季度和不连续、合并集号都跳过", async () =>
        {
            foreach (var issue in new[] { "second-season", "special-season", "missing-number", "merged-episode", "episode-id-mismatch" })
            {
                using var fixture = new Fixture();
                fixture.Enable(ScrobblingProvider.Bangumi);
                fixture.Enable(ScrobblingProvider.Douban);
                fixture.Settings.Scrobbling.NotifyOnFailure = true;
                fixture.SeriesStatus = "Continuing";
                if (issue == "second-season") fixture.SeasonNumbers = [1, 2];
                if (issue == "special-season") fixture.SeasonNumbers = [0];
                if (issue == "missing-number") fixture.MissingEpisodeNumber = 3;
                if (issue == "merged-episode") fixture.MergedEpisodeNumber = 1;
                fixture.Refresh();
                var ticket = fixture.Ticket(1);
                if (issue == "episode-id-mismatch") ticket.Item.Id = "different-episode";
                await fixture.Create(ticket).FinishAsync(90, true);
                Assert.Equal(0, fixture.Douban.SubjectReads.Count, issue);
                Assert.Equal(0, fixture.BangumiTransport.Requests.Count, issue);
                foreach (var provider in new[] { ScrobblingProvider.Bangumi, ScrobblingProvider.Douban })
                    Assert.Equal(ScrobblingRecordOutcome.Skipped, fixture.Service.GetRecords(provider).Single().Outcome, issue);
                var notification = fixture.Notifications.Single();
                Assert.Null(notification.Provider);
                Assert.Equal(ScrobblingRecordOutcome.Skipped, notification.Outcome);
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
            fixture.Douban.SearchResults = [];
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
            foreach (var issue in new[] { "unmapped", "subject-404", "episode-404", "wrong-subject", "special", "local-mismatch", "merged" })
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
                if (issue == "merged") fixture.MergedEpisodeNumber = 8;
                fixture.Refresh();
                var ticket = fixture.Ticket(issue == "unmapped" ? 18 : 8);
                if (issue == "local-mismatch") ticket.Item.Id = "wrong-episode";

                await fixture.Create(ticket).FinishAsync(90, true);

                Assert.Equal(ScrobblingRecordOutcome.Skipped, fixture.Service.GetRecords(ScrobblingProvider.Bangumi).Single().Outcome, issue);
                Assert.False(fixture.BangumiTransport.Requests.Any(request => request.Method != "GET"), issue);
                Assert.Equal(ScrobblingRecordOutcome.Skipped, fixture.Notifications.Single().Outcome, issue);
            }
        });

        Case("打格子服务：TMDB 按当前正规季定位单集，豆瓣仍验证单一第一季", async () =>
        {
            using var fixture = new Fixture();
            fixture.Enable(ScrobblingProvider.Bangumi);
            fixture.Enable(ScrobblingProvider.Douban);
            fixture.Settings.Scrobbling.NotifyOnFailure = true;
            fixture.SeriesProviderIds["Tmdb"] = "137";
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

        Case("打格子服务：库内明确 Bangumi 编号优先，无 TMDB 对照时仍可严格匹配标题", async () =>
        {
            foreach (var explicitSiteId in new[] { true, false })
            {
                using var fixture = new Fixture();
                fixture.Enable(ScrobblingProvider.Bangumi);
                fixture.SeriesProviderIds["Tmdb"] = explicitSiteId ? "277513" : int.MaxValue.ToString();
                if (explicitSiteId) fixture.SeriesProviderIds["Bangumi"] = "101";
                fixture.Refresh();
                await fixture.Create(fixture.Ticket(8)).FinishAsync(90, true);
                Assert.Equal(ScrobblingRecordOutcome.Watched, fixture.Service.GetRecords(ScrobblingProvider.Bangumi).Single().Outcome);
                Assert.Equal(!explicitSiteId, fixture.BangumiTransport.Requests.Any(request => request.Key == "POST /v0/search/subjects"));
                Assert.Equal("PATCH /v0/users/-/collections/101/episodes",
                    fixture.BangumiTransport.Requests.Single(request => request.Method == "PATCH").Key);
            }
        });

        Case("打格子服务：冲突的 Bangumi 或 TMDB 编号不回退标题", async () =>
        {
            foreach (var provider in new[] { "Bangumi", "Tmdb" })
            {
                using var fixture = new Fixture();
                fixture.Enable(ScrobblingProvider.Bangumi);
                fixture.SeriesProviderIds["Tmdb"] = "277513";
                if (provider == "Bangumi") fixture.SeriesProviderIds[provider] = "101";
                fixture.SeriesProviderIds[provider.ToLowerInvariant()] = "102";
                fixture.Refresh();
                await fixture.Create(fixture.Ticket(8)).FinishAsync(90, true);
                Assert.Equal(ScrobblingRecordOutcome.Skipped, fixture.Service.GetRecords(ScrobblingProvider.Bangumi).Single().Outcome);
                Assert.Equal(0, fixture.BangumiTransport.Requests.Count);
            }
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
            Assert.Equal(8, douban.EpisodeNumber);
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

        Case("打格子通知：配置修改或退出媒体账号取消在途匹配，迟到失败不会通知", async () =>
        {
            foreach (var revokeAccount in new[] { false, true })
            {
                using var fixture = new Fixture();
                fixture.Enable(ScrobblingProvider.Douban);
                fixture.Settings.Scrobbling.NotifyOnFailure = true;
                var entered = Signal();
                var cancelled = Signal();
                var lateReply = new TaskCompletionSource<ScrobblingSubject>();
                fixture.Douban.Read = async (_, token) =>
                {
                    using var registration = token.Register(() => cancelled.TrySetResult());
                    entered.TrySetResult();
                    return await lateReply.Task.ConfigureAwait(false);
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
                Assert.Equal(0, fixture.Douban.Marks.Count);
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
        public int? MergedEpisodeNumber { get; set; }
        public int EpisodeCount { get; set; } = 12;
        public int EpisodeSeason { get; set; } = 1;
        public AppSettings Settings => Playback.Settings;
        public EmbySessionScope Scope { get; }
        public FakeDouban Douban { get; } = new();
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
                }, syncTimeout);
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
                    IndexNumberEnd = MergedEpisodeNumber == index ? index + 1 : (int?)null,
                    ParentIndexNumber = EpisodeSeason
                }).ToList();
                if (IncludeInterleavedSpecial) episodes.Add(new
                {
                    Id = "special-1",
                    Type = "Episode",
                    IndexNumber = 1,
                    IndexNumberEnd = (int?)null,
                    ParentIndexNumber = 0
                });
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
        public Func<long, bool, CancellationToken, Task>? Write { get; set; }

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
            return Write?.Invoke(id, completed, cancellationToken) ?? Task.CompletedTask;
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
