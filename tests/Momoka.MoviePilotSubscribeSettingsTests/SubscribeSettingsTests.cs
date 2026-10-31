using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using static Momoka.Tests.SubscriptionFixture;

namespace Momoka.Tests;

internal static class SubscribeSettingsTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> cases)
    {
        void Test(string name, Func<Task> test) => cases.Add((name, test));
        foreach (var mode in Enum.GetValues<MoviePilotSubscribeMode>())
        {
            Test("按季新增 payload/读回 " + mode, async () =>
            {
                using var f = new SubscriptionFixture();
                var change = Change(f.Media(season: 3), "group-a", mode);
                await f.Service.ApplySubscriptionChangeAsync(change, default);
                var body = f.Http.Requests.Single(item => item.Method == "POST" && item.Path == "subscribe/").Body;
                Check.Equal("themoviedb", body.GetProperty("media_source").GetString());
                Check.Equal("42", body.GetProperty("media_id").GetString());
                Check.Equal("电视剧", body.GetProperty("type").GetString());
                Check.Equal(3, body.GetProperty("season").GetInt32());
                Check.Equal("group-a", body.GetProperty("episode_group").GetString());
                Check.Equal(mode == MoviePilotSubscribeMode.Normal ? 0 : 1, body.GetProperty("best_version").GetInt32());
                Check.Equal(mode == MoviePilotSubscribeMode.FullSeason ? 1 : 0, body.GetProperty("best_version_full").GetInt32());
                Check.False(body.TryGetProperty("year", out _));
                Check.Equal(MoviePilotOperationState.Idle, f.Service.SeasonSubscribeState(change.Media, "group-a"));
                Check.Equal(1, f.Http.Count("GET", "subscribe/100"));
            });
            Test("按季更新 payload 仅编号/模式 " + mode, async () =>
            {
                using var f = new SubscriptionFixture();
                var oldMode = mode == MoviePilotSubscribeMode.Normal ? MoviePilotSubscribeMode.FullSeason : MoviePilotSubscribeMode.Normal;
                f.Seed(Subscription(10, season: 3, group: "group-a", mode: oldMode));
                var original = await f.ExistingAsync(10);
                var change = new MoviePilotSubscribeChange(MoviePilotSubscribeChangeKind.Update, original.Media, "group-a", mode, original);
                await f.Service.ApplySubscriptionChangeAsync(change, default);
                var body = f.Http.Requests.Single(item => item.Method == "PUT").Body;
                Check.Equal(3, body.EnumerateObject().Count());
                Check.Equal(10, body.GetProperty("id").GetInt32());
                Check.Equal(mode == MoviePilotSubscribeMode.Normal ? 0 : 1, body.GetProperty("best_version").GetInt32());
                Check.Equal(mode == MoviePilotSubscribeMode.FullSeason ? 1 : 0, body.GetProperty("best_version_full").GetInt32());
                Check.Equal(mode, MoviePilotSubscriptionPlan.Mode(await f.ExistingAsync(10)));
            });
        }
        Test("身份包含来源/类型/编号/季/组，默认组不取消其他组或隐藏季", () =>
        {
            using var f = new SubscriptionFixture();
            var existing = new[] { Subscription(1), Subscription(2, season: 3), Subscription(3, group: "group-a"), Subscription(4, source: "douban"), Subscription(5, type: "电影", season: null) }
                .Select(item => MoviePilotSubscription.Parse(item)!).ToArray();
            var plan = MoviePilotSubscriptionPlan.Build(f.Media(), "", [new(1, false, MoviePilotSubscribeMode.Normal)], existing);
            Check.Equal(1, plan.Count); Check.Equal(1, plan[0].Original!.Id);
            var keys = existing.Select(MoviePilotSubscriptionIdentity.Of).Distinct().Count();
            Check.Equal(5, keys);
            return Task.CompletedTask;
        });
        Test("默认电视剧订阅模式读取公开配置，未设置或不可读回退普通", async () =>
        {
            using var f = new SubscriptionFixture(); await f.ConnectAsync();
            var media = f.Media(season: 1);
            f.Http.DefaultTvSubscribeConfig = Json("""{"success":true,"data":{"value":{"best_version":1,"best_version_full":0}}}""");
            Check.Equal(MoviePilotSubscribeMode.Episodes, await f.Service.DefaultTvSubscribeModeAsync(media, default));
            f.Http.DefaultTvSubscribeConfig = Json("""{"success":true,"data":{"value":{"best_version":1,"best_version_full":1}}}""");
            Check.Equal(MoviePilotSubscribeMode.FullSeason, await f.Service.DefaultTvSubscribeModeAsync(media, default));
            f.Http.DefaultTvSubscribeConfig = Json("""{"success":true,"data":{"value":null}}""");
            Check.Equal(MoviePilotSubscribeMode.Normal, await f.Service.DefaultTvSubscribeModeAsync(media, default));
            Check.Equal(3, f.Http.Count("GET", "system/setting/public/DefaultTvSubscribeConfig"));
            Check.Equal(0, f.Http.Writes);
        });
        Test("稀疏季/特别篇保持原季号，重复季拒绝", () =>
        {
            using var f = new SubscriptionFixture();
            var plan = MoviePilotSubscriptionPlan.Build(f.Media(), "", [new(0, true, MoviePilotSubscribeMode.Episodes), new(7, true, MoviePilotSubscribeMode.FullSeason)], []);
            Check.Equal(0, plan[0].Media.Season); Check.Equal(7, plan[1].Media.Season);
            return Check.ThrowsAsync<MoviePilotException>(() => Task.FromResult(MoviePilotSubscriptionPlan.Build(f.Media(), "", [new(1, true, MoviePilotSubscribeMode.Normal), new(1, false, MoviePilotSubscribeMode.Normal)], [])));
        });
        foreach (var dimension in new[] { "source", "type", "id", "season", "group" })
            Test("伪造取消身份被拒绝 " + dimension, async () =>
            {
                using var f = new SubscriptionFixture();
                f.Seed(Subscription(1));
                var original = await f.ExistingAsync(1);
                var media = original.Media;
                var group = "";
                switch (dimension)
                {
                    case "source": media = media with { MediaSource = "douban" }; break;
                    case "type": media = media with { Type = "电影", Season = null }; break;
                    case "id": media = media with { MediaId = "99" }; break;
                    case "season": media = media with { Season = 3 }; break;
                    case "group": group = "group-a"; break;
                }
                await Check.ThrowsAsync<MoviePilotOperationBlockedException>(() => f.Service.ApplySubscriptionChangeAsync(new(MoviePilotSubscribeChangeKind.Delete, media, group, MoviePilotSubscribeMode.Normal, original), default));
                Check.Equal(0, f.Http.Writes);
            });
        Test("多季已存在仅取消指定季 ID", async () =>
        {
            using var f = new SubscriptionFixture();
            f.Seed(Subscription(1), Subscription(2, season: 3), Subscription(3, group: "group-a"), Subscription(4, source: "douban"));
            var existing = await f.Service.SubscriptionsAsync(default);
            var change = MoviePilotSubscriptionPlan.Build(f.Media(), "", [new(1, false, MoviePilotSubscribeMode.Normal)], existing).Single();
            await f.Service.ApplySubscriptionChangeAsync(change, default);
            Check.Equal("subscribe/1", f.Http.Requests.Single(FakeMoviePilot.IsWrite).Path);
            Check.Equal("2,3,4", string.Join(',', f.Http.Subscriptions.Select(item => item.GetProperty("id").GetInt32())));
        });
        Test("重复精确身份不选第一条，拒绝写入", async () =>
        {
            using var f = new SubscriptionFixture(); f.Seed(Subscription(1), Subscription(2));
            await Check.ThrowsAsync<MoviePilotException>(() => f.Service.ApplySubscriptionChangeAsync(Change(f.Media(season: 1)), default));
            Check.Equal(0, f.Http.Writes);
        });
        Test("电影直接新增、取消，再新增；不同来源同编号保留", async () =>
        {
            using var f = new SubscriptionFixture();
            f.Seed(Subscription(9, type: "电影", source: "douban", season: null));
            var movie = f.Media("电影");
            await f.Service.ApplyMovieSubscriptionAsync(movie, null, default);
            var current = MoviePilotSubscriptionPlan.Find(movie, "", await f.Service.SubscriptionsAsync(default));
            Check.True(current is not null);
            await f.Service.ApplyMovieSubscriptionAsync(movie, current, default);
            Check.Equal(1, f.Http.Subscriptions.Count); Check.Equal(9, f.Http.Subscriptions[0].GetProperty("id").GetInt32());
            await f.Service.ApplyMovieSubscriptionAsync(movie, null, default);
            Check.Equal(2, f.Http.Count("POST", "subscribe/"));
        });
        Test("新增前已存在同模式：不重复POST", async () =>
        {
            using var f = new SubscriptionFixture(); f.Seed(Subscription(1));
            await f.Service.ApplySubscriptionChangeAsync(Change(f.Media(season: 1)), default);
            Check.Equal(0, f.Http.Writes);
        });
        Test("新增前已存在不同模式：不偷改、不POST", async () =>
        {
            using var f = new SubscriptionFixture(); f.Seed(Subscription(1));
            await Check.ThrowsAsync<MoviePilotOperationBlockedException>(() => f.Service.ApplySubscriptionChangeAsync(Change(f.Media(season: 1), mode: MoviePilotSubscribeMode.FullSeason), default));
            Check.Equal(0, f.Http.Writes);
        });
        Test("Service重入：在途只允许一次，读回前仍锁", async () =>
        {
            using var f = new SubscriptionFixture();
            var hold = new TaskCompletionSource<HttpResponseMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Http.Before = (request, _) => request.Method == "POST" && request.Path == "subscribe/" ? hold.Task : Task.FromResult<HttpResponseMessage?>(null);
            var change = Change(f.Media(season: 1));
            var pending = f.Service.ApplySubscriptionChangeAsync(change, default);
            Check.False(pending.IsCompleted);
            Check.Equal(MoviePilotOperationState.Working, f.Service.SeasonSubscribeState(change.Media));
            await Check.ThrowsAsync<MoviePilotOperationBlockedException>(() => f.Service.ApplySubscriptionChangeAsync(change, default));
            Check.Equal(1, f.Http.Count("POST", "subscribe/"));
            hold.SetResult(null); await pending;
        });
        Test("不同剧集组的确定身份独立，正常新增不被其他组状态冒充", async () =>
        {
            using var f = new SubscriptionFixture();
            await f.Service.ApplySubscriptionChangeAsync(Change(f.Media(season: 1), "group-a"), default);
            await f.Service.ApplySubscriptionChangeAsync(Change(f.Media(season: 1), "group-b"), default);
            Check.Equal(2, f.Http.Writes);
            Check.Equal(2, f.Http.Subscriptions.Count);
        });
        Test("回执缺ID：跨组不明锁，其他季仍独立", async () =>
        {
            using var f = new SubscriptionFixture(); f.Http.NoId = true;
            var change = Change(f.Media(season: 1), "group-a");
            await Check.ThrowsAsync<MoviePilotException>(() => f.Service.ApplySubscriptionChangeAsync(change, default));
            await Check.ThrowsAsync<MoviePilotOperationBlockedException>(() => f.Service.ApplySubscriptionChangeAsync(change with { EpisodeGroup = "group-b" }, default));
            Check.Equal(1, f.Http.Writes);
            f.Http.NoId = false;
            await f.Service.ApplySubscriptionChangeAsync(Change(f.Media(season: 3), "group-b"), default);
            Check.Equal(2, f.Http.Writes);
        });
        Test("明确核对只解指定组的不明回执", async () =>
        {
            using var f = new SubscriptionFixture(); f.Http.NoId = true;
            var change = Change(f.Media(season: 1), "group-a");
            await Check.ThrowsAsync<MoviePilotException>(() => f.Service.ApplySubscriptionChangeAsync(change, default));
            f.Service.ConfirmSeasonSubscriptionReviewed(change.Media, "group-b");
            Check.Equal(MoviePilotOperationState.Uncertain, f.Service.SeasonSubscribeState(change.Media, "group-a"));
            f.Service.ConfirmSeasonSubscriptionReviewed(change.Media, "group-a");
            Check.Equal(MoviePilotOperationState.Idle, f.Service.SeasonSubscribeState(change.Media, "group-a"));
        });
        Test("200错误/未变模式读回不当成功；锁住更新重试", async () =>
        {
            using var f = new SubscriptionFixture(); f.Seed(Subscription(1));
            var original = await f.ExistingAsync(1); f.Http.KeepWrongMode = true;
            var change = new MoviePilotSubscribeChange(MoviePilotSubscribeChangeKind.Update, original.Media, "", MoviePilotSubscribeMode.FullSeason, original);
            await Check.ThrowsAsync<MoviePilotException>(() => f.Service.ApplySubscriptionChangeAsync(change, default));
            Check.True(f.Service.SubscriptionNeedsReview(original));
            await Check.ThrowsAsync<MoviePilotOperationBlockedException>(() => f.Service.ApplySubscriptionChangeAsync(change, default));
            Check.Equal(1, f.Http.Writes);
        });
        Test("DELETE成功但记录仍在：不报取消成功、不继续删其他ID", async () =>
        {
            using var f = new SubscriptionFixture(); f.Seed(Subscription(1), Subscription(2, season: 3)); f.Http.RetainDeleted = true;
            var original = await f.ExistingAsync(1);
            var change = new MoviePilotSubscribeChange(MoviePilotSubscribeChangeKind.Delete, original.Media, "", MoviePilotSubscribeMode.Normal, original);
            await Check.ThrowsAsync<MoviePilotException>(() => f.Service.ApplySubscriptionChangeAsync(change, default));
            Check.Equal(MoviePilotOperationState.Uncertain, f.Service.SeasonSubscribeState(original.Media));
            Check.Equal(1, f.Http.Writes); Check.Equal(2, f.Http.Subscriptions.Count);
        });
        Test("422明确拒绝可重试，不保留不明锁", async () =>
        {
            using var f = new SubscriptionFixture();
            f.Http.Before = (request, _) => Task.FromResult<HttpResponseMessage?>(FakeMoviePilot.IsWrite(request) ? FakeMoviePilot.Reply(new { detail = "fixture validation" }, HttpStatusCode.UnprocessableEntity) : null);
            var change = Change(f.Media(season: 1));
            await Check.ThrowsAsync<MoviePilotException>(() => f.Service.ApplySubscriptionChangeAsync(change, default));
            Check.Equal(MoviePilotOperationState.Idle, f.Service.SeasonSubscribeState(change.Media));
            f.Http.Before = null;
            await f.Service.ApplySubscriptionChangeAsync(change, default); Check.Equal(2, f.Http.Writes);
        });
        Test("旧连接订阅不发写，取消令牌不发请求", async () =>
        {
            using var f = new SubscriptionFixture(); f.Seed(Subscription(1));
            var original = await f.ExistingAsync(1); f.Settings.MoviePilot.Username = "new-placeholder";
            await Check.ThrowsAsync<MoviePilotOperationBlockedException>(() => f.Service.ApplySubscriptionChangeAsync(new(MoviePilotSubscribeChangeKind.Delete, original.Media, "", MoviePilotSubscribeMode.Normal, original), default));
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            await Check.ThrowsAsync<OperationCanceledException>(() => f.Service.ApplySubscriptionChangeAsync(Change(f.Media(season: 1)), cancel.Token));
            Check.Equal(0, f.Http.Writes);
        });
        Test("普通电影payload明确模式且无季号", async () =>
        {
            using var f = new SubscriptionFixture(); await f.Service.ApplyMovieSubscriptionAsync(f.Media("电影"), null, default);
            var body = f.Http.Requests.Single(item => item.Method == "POST" && item.Path == "subscribe/").Body;
            Check.Equal(JsonValueKind.Null, body.GetProperty("season").ValueKind);
            Check.Equal(0, body.GetProperty("best_version").GetInt32()); Check.Equal(0, body.GetProperty("best_version_full").GetInt32());
        });
        Test("非法kind/mode/剧集组/季号均不发请求", async () =>
        {
            using var f = new SubscriptionFixture();
            foreach (var change in new[]
            {
                Change(f.Media(season: 1)) with { Kind = (MoviePilotSubscribeChangeKind)99 },
                Change(f.Media(season: 1), mode: (MoviePilotSubscribeMode)99),
                Change(f.Media("电影"), "group-a"), Change(f.Media(source: "douban", season: 1), "group-a"),
                Change(f.Media(season: -1)), Change(f.Media())
            }) await Check.ThrowsAsync<MoviePilotException>(() => f.Service.ApplySubscriptionChangeAsync(change, default));
            Check.Equal(0, f.Http.Requests.Count);
        });
        Test("读取后订阅身份漂移：不能取消相同ID的其他组", async () =>
        {
            using var f = new SubscriptionFixture(); f.Seed(Subscription(1)); var original = await f.ExistingAsync(1);
            f.Http.Before = (request, _) => Task.FromResult<HttpResponseMessage?>(request.Path == "subscribe/1"
                ? FakeMoviePilot.Reply(Subscription(1, group: "group-a")) : null);
            await Check.ThrowsAsync<MoviePilotOperationBlockedException>(() => f.Service.ApplySubscriptionChangeAsync(new(MoviePilotSubscribeChangeKind.Delete, original.Media, "", MoviePilotSubscribeMode.Normal, original), default));
            Check.Equal(0, f.Http.Writes);
        });
        Test("提交结果网络不明：实际锁住组/季、不降级重试", async () =>
        {
            using var f = new SubscriptionFixture();
            f.Http.Before = (request, _) => FakeMoviePilot.IsWrite(request) ? Task.FromException<HttpResponseMessage?>(new HttpRequestException("anonymous response lost")) : Task.FromResult<HttpResponseMessage?>(null);
            var change = Change(f.Media(season: 1), "group-a");
            await Check.ThrowsAsync<MoviePilotOperationUncertainException>(() => f.Service.ApplySubscriptionChangeAsync(change, default));
            Check.False(f.Service.Available); Check.Equal(MoviePilotOperationState.Uncertain, f.Service.SeasonSubscribeState(change.Media, "group-a"));
            f.Http.Before = null; await f.ConnectAsync();
            await Check.ThrowsAsync<MoviePilotOperationBlockedException>(() => f.Service.ApplySubscriptionChangeAsync(change, default)); Check.Equal(1, f.Http.Writes);
        });
        Test("写后切换连接：迟到回执不恢复旧可用性", async () =>
        {
            using var f = new SubscriptionFixture();
            var hold = new TaskCompletionSource<HttpResponseMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Http.Before = (request, _) => FakeMoviePilot.IsWrite(request) ? hold.Task : Task.FromResult<HttpResponseMessage?>(null);
            var media = f.Media(season: 1); var pending = f.Service.ApplySubscriptionChangeAsync(Change(media), default);
            f.Settings.MoviePilot.Username = "other-anonymous"; hold.SetResult(null);
            await Check.ThrowsAsync<MoviePilotOperationUncertainException>(() => pending);
            Check.False(f.Service.Available); Check.False(f.Service.IsCurrentConnection(media.ConnectionStamp)); Check.Equal(1, f.Http.Writes);
        });
        Test("写接口仍拒绝无success裸对象，读取POST允许官方裸统计", async () =>
        {
            using var f = new SubscriptionFixture(); var detail = new MoviePilotMediaDetail(f.Media(), Json("{}"));
            var stats = await f.Service.MediaExistingEpisodesAsync(detail, "", default); Check.Equal(2, stats[3].Count);
            f.Http.Before = (request, _) => Task.FromResult<HttpResponseMessage?>(FakeMoviePilot.IsWrite(request) ? FakeMoviePilot.Reply(Json("""{"id":123}""")) : null);
            await Check.ThrowsAsync<MoviePilotOperationUncertainException>(() => f.Service.ApplyMovieSubscriptionAsync(f.Media("电影"), null, default)); Check.Equal(1, f.Http.Writes);
        });
        Test("空或坏统计：官方空对象可缺失，坏对象不能报全缺", async () =>
        {
            using var f = new SubscriptionFixture(); var detail = new MoviePilotMediaDetail(f.Media(), Json("{}"));
            f.Http.Before = (request, _) => Task.FromResult<HttpResponseMessage?>(request.Path == "mediaserver/exists_remote" ? FakeMoviePilot.Reply(Json("{}")) : null);
            Check.Equal(0, (await f.Service.MediaExistingEpisodesAsync(detail, "", default)).Count);
            f.Http.Before = (request, _) => Task.FromResult<HttpResponseMessage?>(request.Path == "mediaserver/exists_remote" ? FakeMoviePilot.Reply(Json("""{"error":"unknown"}""")) : null);
            await Check.ThrowsAsync<MoviePilotException>(() => f.Service.MediaExistingEpisodesAsync(detail, "", default));
        });
        SearchTests(Test);
        MediaLoadingTests(Test);
        PickerTests(Test);
    }

    private static MoviePilotSubscribeChange Change(MoviePilotMedia media, string group = "", MoviePilotSubscribeMode mode = MoviePilotSubscribeMode.Normal) =>
        new(MoviePilotSubscribeChangeKind.Add, media, group, mode, null);

    private static void SearchTests(Action<string, Func<Task>> Test)
    {
        Test("搜索电影实际命令不确认/不建对话框，按钮订阅→取消→订阅", async () =>
        {
            ContentDialog.Reset(); using var f = new SubscriptionFixture(); using var vm = new MoviePilotSearchViewModel(); vm.Attach(f.Service);
            var confirms = 0; var pickers = 0;
            vm.UseConfirm((_, _, _) => { confirms++; return Task.FromResult(true); });
            vm.UseSeasonPicker((_, _) => { pickers++; return Task.CompletedTask; });
            await vm.SearchAsync("匿名"); var row = vm.Results.Single(); Check.Equal("订阅", row.SubscribeLabel);
            await row.SubscribeCommand.ExecuteAsync(null); Check.Equal("取消订阅", row.SubscribeLabel); Check.True(row.CanSubscribe);
            Check.Contains("取消订阅（TMDB）", row.SubscribeAutomationName);
            await row.SubscribeCommand.ExecuteAsync(null); Check.Equal("订阅", row.SubscribeLabel);
            Check.Equal(2, f.Http.Writes); Check.Equal(0, confirms); Check.Equal(0, pickers); Check.Equal(0, ContentDialog.Created);
        });
        Test("搜索已有多季电视剧开picker，不直接取消任何季", async () =>
        {
            using var f = new SubscriptionFixture(); f.Http.SearchResults = Json("""[{"title":"匿名示例","type":"电视剧","media_source":"themoviedb","media_id":"42"}]""");
            f.Seed(Subscription(1), Subscription(2, season: 3)); using var vm = new MoviePilotSearchViewModel(); vm.Attach(f.Service);
            var opened = 0; vm.UseSeasonPicker((media, _) => { opened++; Check.Equal("电视剧", media.Type); return Task.CompletedTask; });
            await vm.SearchAsync("匿名"); var row = vm.Results.Single(); Check.Contains("选择季", row.SubscribeLabel);
            await row.SubscribeCommand.ExecuteAsync(null); Check.Equal(1, opened); Check.Equal(0, f.Http.Writes); Check.Equal(MoviePilotSubscribeState.Done, row.State);
        });
        Test("两张相同身份卡防重复点击，只开一个picker", async () =>
        {
            using var f = new SubscriptionFixture(); f.Http.SearchResults = Json("""[{"title":"匿名示例","type":"电视剧","media_source":"themoviedb","media_id":"42"},{"title":"重复卡","type":"电视剧","media_source":"themoviedb","media_id":"42"}]""");
            using var vm = new MoviePilotSearchViewModel(); vm.Attach(f.Service);
            var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var opened = 0;
            vm.UseSeasonPicker((_, _) => { opened++; return hold.Task; }); await vm.SearchAsync("匿名");
            var first = vm.Results[0].SubscribeCommand.ExecuteAsync(null); await vm.Results[1].SubscribeCommand.ExecuteAsync(null);
            Check.Equal(1, opened); hold.SetResult(); await first; Check.Equal(0, f.Http.Writes);
        });
        Test("电影写失败后实际重读，外部已订阅不显示为重试新增", async () =>
        {
            using var f = new SubscriptionFixture(); using var vm = new MoviePilotSearchViewModel(); vm.Attach(f.Service); await vm.SearchAsync("匿名");
            var row = vm.Results.Single();
            f.Http.Before = (request, _) =>
            {
                if (!FakeMoviePilot.IsWrite(request)) return Task.FromResult<HttpResponseMessage?>(null);
                f.Seed(Subscription(77, type: "电影", season: null));
                return Task.FromResult<HttpResponseMessage?>(FakeMoviePilot.Reply(new { detail = "fixture rejection" }, HttpStatusCode.UnprocessableEntity));
            };
            await row.SubscribeCommand.ExecuteAsync(null);
            Check.Equal("取消订阅", row.SubscribeLabel); Check.Equal(77, row.Subscription!.Id); Check.True(vm.NoticeOpen);
        });
        Test("搜索状态读取失败，第一次点击仅重读不新增", async () =>
        {
            using var f = new SubscriptionFixture();
            f.Http.Before = (request, _) => Task.FromResult<HttpResponseMessage?>(request.Method == "GET" && request.Path == "subscribe/" ? FakeMoviePilot.Reply(new { detail = "fixture forbidden" }, HttpStatusCode.Forbidden) : null);
            using var vm = new MoviePilotSearchViewModel(); vm.Attach(f.Service); await vm.SearchAsync("匿名");
            var row = vm.Results.Single(); Check.Equal(MoviePilotSubscribeState.Unknown, row.State); f.Http.Before = null;
            await row.SubscribeCommand.ExecuteAsync(null); Check.Equal(0, f.Http.Writes); Check.Equal(MoviePilotSubscribeState.Idle, row.State);
        });
        Test("断线当场清搜索结果、旧命令不写", async () =>
        {
            using var f = new SubscriptionFixture(); using var vm = new MoviePilotSearchViewModel(); vm.Attach(f.Service); await vm.SearchAsync("匿名");
            var row = vm.Results.Single(); f.Settings.MoviePilot.Enabled = false; Check.Equal(0, vm.Results.Count);
            await row.SubscribeCommand.ExecuteAsync(null); Check.Equal(0, f.Http.Writes);
        });
        Test("离页后迟到picker不回写新结果，不误取消电影", async () =>
        {
            using var f = new SubscriptionFixture(); f.Http.SearchResults = Json("""[{"title":"匿名示例","type":"电视剧","media_source":"themoviedb","media_id":"42"}]""");
            using var vm = new MoviePilotSearchViewModel(); vm.Attach(f.Service);
            var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); vm.UseSeasonPicker((_, _) => hold.Task);
            await vm.SearchAsync("匿名"); var old = vm.Results.Single(); var pending = old.SubscribeCommand.ExecuteAsync(null);
            vm.Cancel(); f.Http.SearchResults = Json("""[{"title":"新电影","type":"电影","media_source":"douban","media_id":"42"}]""");
            await vm.SearchAsync("新词"); var fresh = vm.Results.Single(); hold.SetResult(); await pending;
            Check.True(ReferenceEquals(fresh, vm.Results.Single())); Check.Equal(MoviePilotSubscribeState.Idle, fresh.State); Check.Equal(0, f.Http.Writes);
        });
        Test("电影不明结果的显式恢复键只读，恢复后可明确取消", async () =>
        {
            using var f = new SubscriptionFixture(); f.Http.NoId = true; using var vm = new MoviePilotSearchViewModel(); vm.Attach(f.Service); await vm.SearchAsync("匿名");
            var row = vm.Results.Single(); await row.SubscribeCommand.ExecuteAsync(null); Check.Equal(Visibility.Visible, row.ReviewVisibility); Check.True(row.CanReview);
            await row.ReviewCommand.ExecuteAsync(null); Check.Equal(1, f.Http.Writes); Check.Equal("取消订阅", row.SubscribeLabel); Check.Equal(Visibility.Collapsed, row.ReviewVisibility);
            await row.SubscribeCommand.ExecuteAsync(null); Check.Equal(2, f.Http.Writes); Check.Equal("订阅", row.SubscribeLabel);
        });
        Test("电影重入：同一键还在读回，重复执行不反向取消", async () =>
        {
            using var f = new SubscriptionFixture(); using var vm = new MoviePilotSearchViewModel(); vm.Attach(f.Service); await vm.SearchAsync("匿名");
            var hold = new TaskCompletionSource<HttpResponseMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Http.Before = (request, _) => FakeMoviePilot.IsWrite(request) ? hold.Task : Task.FromResult<HttpResponseMessage?>(null);
            var row = vm.Results.Single(); var pending = row.SubscribeCommand.ExecuteAsync(null); Check.False(row.CanSubscribe);
            await row.SubscribeCommand.ExecuteAsync(null); Check.Equal(1, f.Http.Writes); hold.SetResult(null); await pending;
            Check.Equal("取消订阅", row.SubscribeLabel); Check.Equal(1, f.Http.Writes);
        });
        Test("同步Busy通知重入新查询，旧请求不覆盖代次与电影按钮", async () =>
        {
            using var f = new SubscriptionFixture(); using var vm = new MoviePilotSearchViewModel(); vm.Attach(f.Service);
            Task? replacement = null; var once = false;
            vm.PropertyChanged += (_, args) =>
            {
                if (!once && args.PropertyName == nameof(vm.Busy) && vm.Busy)
                {
                    once = true; replacement = vm.SearchAsync("新词");
                }
            };
            await vm.SearchAsync("旧词"); await replacement!;
            var row = vm.Results.Single(); await row.SubscribeCommand.ExecuteAsync(null); Check.Equal("取消订阅", row.SubscribeLabel);
            Check.Equal(1, f.Http.Count("GET", "media/search")); Check.Equal(1, f.Http.Writes);
        });
        Test("排队断线旧通知不清后来新查询", async () =>
        {
            using var f = new SubscriptionFixture(); using var vm = new MoviePilotSearchViewModel(); var queue = new Queue<Action>(); vm.Attach(f.Service, queue.Enqueue);
            await vm.SearchAsync("旧词"); while (queue.TryDequeue(out var action)) action();
            f.Settings.MoviePilot.Enabled = false; f.Settings.MoviePilot.Enabled = true; await f.ConnectAsync();
            await vm.SearchAsync("新词"); var fresh = vm.Results.Single(); while (queue.TryDequeue(out var action)) action();
            Check.True(ReferenceEquals(fresh, vm.Results.Single())); Check.Equal(0, f.Http.Writes);
        });
        Test("电影收到无ID回执后只准核对，不自动重发或取消", async () =>
        {
            using var f = new SubscriptionFixture(); f.Http.NoId = true; using var vm = new MoviePilotSearchViewModel(); vm.Attach(f.Service); await vm.SearchAsync("匿名");
            var row = vm.Results.Single(); await row.SubscribeCommand.ExecuteAsync(null); Check.Equal(MoviePilotSubscribeState.Uncertain, row.State);
            await row.SubscribeCommand.ExecuteAsync(null); Check.Equal(1, f.Http.Writes); Check.Equal(MoviePilotSubscribeState.Uncertain, row.State);
        });
        Test("点搜索结果打开对应媒体：唯一订阅随行，多季或未订阅进媒体页", async () =>
        {
            using var f = new SubscriptionFixture();
            f.Seed(Subscription(7, type: "电影", season: null));
            using var vm = new MoviePilotSearchViewModel(); vm.Attach(f.Service);
            var opened = new List<(MoviePilotMedia Media, MoviePilotSubscription? Subscription)>();
            vm.UseMediaOpener((media, subscription) => opened.Add((media, subscription)));
            await vm.SearchAsync("匿名");
            var movie = vm.Results.Single();
            vm.Open(movie);
            Check.Equal(1, opened.Count); Check.Equal(7, opened[0].Subscription!.Id);
            Check.Equal(movie.Media, opened[0].Media);

            // 不在当前结果集里的旧卡不打开。
            await vm.SearchAsync("");
            vm.Open(movie);
            Check.Equal(1, opened.Count);

            // 电视剧只对得上一条订阅：连同订阅一起打开订阅详情。
            f.Http.SearchResults = Json("""[{"title":"匿名示例","type":"电视剧","media_source":"themoviedb","media_id":"43"}]""");
            f.Seed(Subscription(8, type: "电视剧", mediaId: "43", season: 1));
            await vm.SearchAsync("匿名");
            var series = vm.Results.Single();
            vm.Open(series);
            Check.Equal(2, opened.Count); Check.Equal(8, opened[1].Subscription!.Id);

            // 同一部剧有多季订阅：没有唯一订阅，交给媒体页。
            f.Http.SearchResults = Json("""[{"title":"匿名示例","type":"电视剧","media_source":"themoviedb","media_id":"44"}]""");
            f.Seed(Subscription(9, type: "电视剧", mediaId: "44", season: 1), Subscription(10, type: "电视剧", mediaId: "44", season: 2));
            await vm.SearchAsync("匿名");
            vm.Open(vm.Results.Single());
            Check.Equal(3, opened.Count); Check.True(opened[2].Subscription is null);

            // 断线后旧卡不再打开。
            f.Settings.MoviePilot.Enabled = false;
            vm.Open(series);
            Check.Equal(3, opened.Count);
        });
    }

    private static void MediaLoadingTests(Action<string, Func<Task>> Test)
    {
        Test("订阅页首次读取媒体详情显示加载动画，读完收起；刷新已有内容不再显示", async () =>
        {
            using var f = new SubscriptionFixture(); await f.ConnectAsync();
            var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Http.Before = async (request, _) =>
            {
                if (request.Method == "GET" && request.Path == "media/42") await hold.Task;
                return null;
            };
            using var vm = new MoviePilotMediaViewModel();
            vm.Attach(f.Service, f.Media());
            var loading = vm.ReloadAsync();
            Check.True(vm.InitialLoading); Check.Equal(Visibility.Visible, vm.InitialLoadingVisibility);
            hold.SetResult();
            await loading;
            Check.True(vm.Detail is not null);
            Check.False(vm.InitialLoading); Check.Equal(Visibility.Collapsed, vm.InitialLoadingVisibility);

            // 刷新既有内容：不盖转圈。
            var refresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Http.Before = async (request, _) =>
            {
                if (request.Method == "GET" && request.Path == "media/42") await refresh.Task;
                return null;
            };
            var reloading = vm.ReloadAsync();
            Check.True(vm.Busy); Check.False(vm.InitialLoading);
            Check.Equal(Visibility.Collapsed, vm.InitialLoadingVisibility);
            refresh.SetResult();
            await reloading;
            Check.False(vm.InitialLoading);
        });
        Test("媒体详情读取失败也收起加载动画", async () =>
        {
            using var f = new SubscriptionFixture(); await f.ConnectAsync();
            var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Http.Before = async (request, _) =>
            {
                if (request.Method != "GET" || request.Path != "media/42") return null;
                await hold.Task;
                return FakeMoviePilot.Reply(new { detail = "fixture forbidden" }, HttpStatusCode.Forbidden);
            };
            using var vm = new MoviePilotMediaViewModel();
            vm.Attach(f.Service, f.Media());
            var loading = vm.ReloadAsync();
            Check.True(vm.InitialLoading);
            hold.SetResult();
            await loading;
            Check.False(vm.InitialLoading); Check.Equal(Visibility.Collapsed, vm.InitialLoadingVisibility);
            Check.True(vm.NoticeOpen);
        });
    }

    private static void PickerTests(Action<string, Func<Task>> Test)
    {
        Test("季设置默认组可见，首播/集数/缺失/模式/按季开关都有状态", async () =>
        {
            using var f = new SubscriptionFixture(); f.Http.Groups = Json("[]"); await f.ConnectAsync(); using var vm = Picker(f);
            await vm.ReloadAsync(); Check.Equal(Visibility.Visible, vm.GroupsVisibility); Check.Equal(1, vm.Groups.Count);
            Check.False(vm.Seasons.Any(row => row.Selected)); Check.False(vm.CanSubmit); Check.Equal("请选择订阅季", vm.SubmitButtonText);
            var row = vm.Seasons.Single(item => item.Season.Number == 1); Check.Equal("缺失", row.Status);
            Check.Contains("8 集", row.Summary); Check.Contains("首播于 2026-07-31", row.Summary);
            row.Selected = true; Check.True(vm.CanSubmit); Check.Equal("提交订阅", vm.SubmitButtonText);
            Check.Equal(Visibility.Collapsed, row.SubscriptionVisibility);
            row.FullMode = true; Check.Equal(2, row.ModeIndex); Check.True(row.FullMode); Check.False(row.NormalMode);
            row.Selected = false; Check.Equal(Visibility.Collapsed, row.SelectedVisibility); Check.False(vm.CanSubmit); Check.Equal("请选择订阅季", vm.SubmitButtonText);
            row.Selected = true; Check.Equal(2, row.ModeIndex);
        });
        Test("入口指定季才预选；已入库季默认全集洗版，其余跟随公开默认模式", async () =>
        {
            using var f = new SubscriptionFixture();
            f.Http.DefaultTvSubscribeConfig = Json("""{"success":true,"data":{"value":{"best_version":1,"best_version_full":0}}}""");
            f.Http.Before = (request, _) => Task.FromResult<HttpResponseMessage?>(request.Path == "mediaserver/exists_remote"
                ? FakeMoviePilot.Reply(Json("""{"1":[1,2,3,4,5,6,7,8],"3":[1,2],"0":[]}""")) : null);
            await f.ConnectAsync();
            using (var vm = Picker(f))
            {
                await vm.ReloadAsync();
                Check.False(vm.Seasons.Any(row => row.Selected));
                Check.Equal(2, vm.Seasons.Single(row => row.Season.Number == 1).ModeIndex);
                Check.Equal(1, vm.Seasons.Single(row => row.Season.Number == 3).ModeIndex);
                Check.Equal(1, vm.Seasons.Single(row => row.Season.Number == 0).ModeIndex);
            }
            var preselected = new MoviePilotMediaViewModel();
            preselected.Attach(f.Service, f.Media(), picker: true, preselect: 3);
            try
            {
                await preselected.ReloadAsync();
                Check.True(preselected.Seasons.Single(row => row.Season.Number == 3).Selected);
                Check.False(preselected.Seasons.Single(row => row.Season.Number == 1).Selected);
                Check.Equal("提交订阅", preselected.SubmitButtonText);
            }
            finally { preselected.Cancel(); }
        });
        Test("默认订阅配置读取失败仍可打开季选择并按普通订阅", async () =>
        {
            using var f = new SubscriptionFixture();
            f.Http.Before = (request, _) => Task.FromResult<HttpResponseMessage?>(request.Path == "system/setting/public/DefaultTvSubscribeConfig"
                ? FakeMoviePilot.Reply(new { detail = "not found" }, HttpStatusCode.NotFound) : null);
            await f.ConnectAsync(); using var vm = Picker(f); await vm.ReloadAsync();
            Check.Equal(3, vm.Seasons.Count); Check.Equal(0, vm.Seasons[0].ModeIndex);
            vm.Seasons[0].Selected = true; Check.True(vm.CanSubmit);
        });
        Test("已订阅季保留服务器模式，不被入库状态默认值覆盖", async () =>
        {
            using var f = new SubscriptionFixture();
            f.Seed(Subscription(1, season: 1, mode: MoviePilotSubscribeMode.Normal));
            f.Http.DefaultTvSubscribeConfig = Json("""{"success":true,"data":{"value":{"best_version":1,"best_version_full":1}}}""");
            f.Http.Before = (request, _) => Task.FromResult<HttpResponseMessage?>(request.Path == "mediaserver/exists_remote"
                ? FakeMoviePilot.Reply(Json("""{"1":[1,2,3,4,5,6,7,8]}""")) : null);
            await f.ConnectAsync(); using var vm = Picker(f); await vm.ReloadAsync();
            var row = vm.Seasons.Single(item => item.Season.Number == 1);
            Check.True(row.Selected); Check.Equal(0, row.ModeIndex); Check.Equal(Visibility.Visible, row.SubscriptionVisibility);
            Check.Contains("已订阅 · 普通订阅", row.SubscribeLabel);
        });
        Test("已有组和多季：季开关/模式来自精确身份，不污染默认组", async () =>
        {
            using var f = new SubscriptionFixture(); f.Seed(Subscription(1, season: 3, mode: MoviePilotSubscribeMode.FullSeason), Subscription(2, group: "group-a"), Subscription(3, source: "douban"));
            await f.ConnectAsync(); using var vm = Picker(f); await vm.ReloadAsync();
            Check.False(vm.Seasons.Single(item => item.Season.Number == 1).Selected);
            var third = vm.Seasons.Single(item => item.Season.Number == 3); Check.True(third.Selected); Check.Equal(2, third.ModeIndex); Check.False(vm.CanSubmit);
            third.Selected = false; var plan = vm.Plan(); Check.Equal(1, plan.Count); Check.Equal(1, plan[0].Original!.Id);
        });
        Test("切组保各组草稿和模式，稀疏季7不改为1", async () =>
        {
            using var f = new SubscriptionFixture(); await f.ConnectAsync(); using var vm = Picker(f); await vm.ReloadAsync();
            var original = vm.Seasons.Single(item => item.Season.Number == 1); original.EpisodesMode = true; original.MarkModePicked();
            await vm.SelectGroupAsync(vm.Groups.Single(item => item.Group.Id == "group-a"));
            Check.Equal(7, vm.Seasons.Single().Season.Number); vm.Seasons.Single().Selected = true; vm.Seasons.Single().FullMode = true; vm.Seasons.Single().MarkModePicked();
            await vm.SelectGroupAsync(vm.Groups.Single(item => item.Group.Id == "")); Check.Equal(1, vm.Seasons.First().Season.Number); Check.Equal(1, vm.Seasons.First().ModeIndex);
            await vm.SelectGroupAsync(vm.Groups.Single(item => item.Group.Id == "group-a")); Check.Equal(2, vm.Seasons.Single().ModeIndex); Check.True(vm.Seasons.Single().Selected);
            Check.Equal(0, f.Http.Writes);
        });
        Test("季列表迟到：切B后A响应不能改组/季/入库状态", async () =>
        {
            using var f = new SubscriptionFixture(); await f.ConnectAsync(); using var vm = Picker(f); await vm.ReloadAsync();
            var hold = new TaskCompletionSource<HttpResponseMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Http.Before = (request, _) => request.Path == "media/group/seasons/group-a" ? hold.Task : Task.FromResult<HttpResponseMessage?>(null);
            var pending = vm.SelectGroupAsync(vm.Groups.Single(item => item.Group.Id == "group-a")); Check.False(vm.CanSubmit);
            await vm.SelectGroupAsync(vm.Groups.Single(item => item.Group.Id == "group-b")); var row = vm.Seasons.Single();
            hold.SetResult(FakeMoviePilot.Reply(Json("""[{"season_number":99,"episode_count":1}]"""))); await pending;
            Check.Equal("group-b", vm.GroupId); Check.True(ReferenceEquals(row, vm.Seasons.Single())); Check.Equal(7, row.Season.Number);
        });
        Test("失败后真状态回读：第一季已成功不重发，后续季停下可重试", async () =>
        {
            using var f = new SubscriptionFixture(); await f.ConnectAsync(); using var vm = Picker(f); await vm.ReloadAsync();
            vm.Seasons.Single(item => item.Season.Number == 1).Selected = true;
            vm.Seasons.Single(item => item.Season.Number == 3).Selected = true;
            f.Http.Before = (request, _) => Task.FromResult<HttpResponseMessage?>(request.Method == "POST" && request.Path == "subscribe/" && request.Body.GetProperty("season").GetInt32() == 3
                ? FakeMoviePilot.Reply(new { detail = "fixture rejection" }, HttpStatusCode.UnprocessableEntity) : null);
            Check.False(await vm.SubmitAsync()); Check.False(vm.Submitted);
            Check.True(vm.Seasons.Single(item => item.Season.Number == 1).CurrentSubscription is not null);
            Check.False(vm.Seasons.Single(item => item.Season.Number == 3).Selected); Check.Contains("重新读取", vm.SubmitSummary);
            f.Http.Before = null; vm.Seasons.Single(item => item.Season.Number == 3).Selected = true;
            Check.True(await vm.SubmitAsync());
            Check.Equal(1, f.Http.Requests.Count(item => item.Method == "POST" && item.Path == "subscribe/" && item.Body.GetProperty("season").GetInt32() == 1));
        });
        Test("picker重入只提交一次；提交中切组无效", async () =>
        {
            using var f = new SubscriptionFixture(); await f.ConnectAsync(); using var vm = Picker(f); await vm.ReloadAsync();
            vm.Seasons.Single(item => item.Season.Number == 1).Selected = true;
            var hold = new TaskCompletionSource<HttpResponseMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Http.Before = (request, _) => request.Method == "POST" && request.Path == "subscribe/" ? hold.Task : Task.FromResult<HttpResponseMessage?>(null);
            var pending = vm.SubmitAsync(); Check.False(vm.CanSubmit); Check.Equal(Visibility.Visible, vm.SubscribeProgressVisibility);
            Check.False(await vm.SubmitAsync()); await vm.SelectGroupAsync(vm.Groups.Single(item => item.Group.Id == "group-a")); Check.Equal("", vm.GroupId);
            hold.SetResult(null); Check.True(await pending); Check.Equal(1, f.Http.Writes);
        });
        Test("入库统计错误不显示成缺失，选择模式前不提交", async () =>
        {
            using var f = new SubscriptionFixture();
            f.Http.Before = (request, _) => Task.FromResult<HttpResponseMessage?>(request.Path == "mediaserver/exists_remote" ? FakeMoviePilot.Reply(new { detail = "fixture forbidden" }, HttpStatusCode.Forbidden) : null);
            await f.ConnectAsync(); using var vm = Picker(f); await vm.ReloadAsync();
            var row = vm.Seasons[0]; Check.Equal("入库状态未知", row.Status);
            row.Selected = true; Check.False(vm.CanSubmit); Check.Equal(Visibility.Visible, vm.SeasonStateRetryVisibility);
            row.EpisodesMode = true; row.MarkModePicked(); Check.True(vm.CanSubmit);
            f.Http.Before = null;
            await vm.RetrySeasonStatesAsync();
            Check.Equal("缺失", row.Status); Check.Equal(Visibility.Collapsed, vm.SeasonStateRetryVisibility);
        });
        Test("季读取断线/离页：迟到响应不重建季和可提交按钮", async () =>
        {
            using var f = new SubscriptionFixture(); await f.ConnectAsync(); using var vm = Picker(f); await vm.ReloadAsync();
            var hold = new TaskCompletionSource<HttpResponseMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Http.Before = (request, _) => request.Path == "media/group/seasons/group-a" ? hold.Task : Task.FromResult<HttpResponseMessage?>(null);
            var pending = vm.SelectGroupAsync(vm.Groups.Single(item => item.Group.Id == "group-a")); vm.Cancel();
            hold.SetResult(null); await pending; Check.Equal(0, vm.Seasons.Count); Check.False(vm.CanSubmit); Check.Equal(0, f.Http.Writes);
        });
        Test("提交同步绑定取消：封存计划不发送，迟到不能成功关闭", async () =>
        {
            using var f = new SubscriptionFixture(); await f.ConnectAsync(); using var vm = Picker(f); await vm.ReloadAsync();
            vm.Seasons.Single(item => item.Season.Number == 1).Selected = true;
            vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(vm.Submitting) && vm.Submitting) vm.Cancel(); };
            Check.False(await vm.SubmitAsync()); Check.Equal(0, f.Http.Writes); Check.False(vm.CanSubmit);
        });
        Test("已保存组草稿与写后真实模式冲突，review必须采用真实模式", async () =>
        {
            using var f = new SubscriptionFixture(); f.Http.KeepWrongMode = true; await f.ConnectAsync(); using var vm = Picker(f); await vm.ReloadAsync();
            await vm.SelectGroupAsync(vm.Groups.Single(item => item.Group.Id == "group-a")); vm.Seasons.Single().Selected = true; vm.Seasons.Single().FullMode = true; vm.Seasons.Single().MarkModePicked();
            await vm.SelectGroupAsync(vm.Groups.Single(item => item.Group.Id == "")); await vm.SelectGroupAsync(vm.Groups.Single(item => item.Group.Id == "group-a"));
            Check.False(await vm.SubmitAsync()); Check.True(vm.Submitted); Check.Equal(0, vm.Seasons.Single().ModeIndex);
            vm.ReviewConfirmed = true; await vm.ReviewAsync(); Check.Equal(0, vm.Seasons.Single().ModeIndex); Check.False(vm.CanSubmit); Check.Equal(1, f.Http.Writes);
        });
        Test("提交不明后真实订阅可见但不解锁，明确review只读恢复", async () =>
        {
            using var f = new SubscriptionFixture(); f.Http.NoId = true; await f.ConnectAsync(); using var vm = Picker(f); await vm.ReloadAsync();
            vm.Seasons.Single(item => item.Season.Number == 1).Selected = true;
            Check.False(await vm.SubmitAsync()); Check.True(vm.Submitted); Check.False(vm.CanSubmit);
            Check.True(vm.Seasons.Single(item => item.Season.Number == 1).CurrentSubscription is not null); Check.Equal(Visibility.Visible, vm.ReviewVisibility);
            vm.ReviewConfirmed = true; await vm.ReviewAsync(); Check.False(vm.Submitted); Check.False(vm.CanSubmit); Check.Equal(1, f.Http.Writes);
            Check.Equal(MoviePilotOperationState.Idle, f.Service.SeasonSubscribeState(f.Media(season: 1)));
        });
        Test("载入Busy同步离页不读取已释放lifetime、不保留旧季", async () =>
        {
            using var f = new SubscriptionFixture(); await f.ConnectAsync(); using var vm = Picker(f);
            vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(vm.Busy) && vm.Busy) vm.Cancel(); };
            await vm.ReloadAsync(); Check.False(vm.CanSubmit); Check.Equal(0, vm.Seasons.Count); Check.Equal(0, f.Http.Count("GET", "media/42"));
        });
        Test("入库统计迟到：A组库状态不污染B组", async () =>
        {
            using var f = new SubscriptionFixture(); await f.ConnectAsync(); using var vm = Picker(f); await vm.ReloadAsync();
            var hold = new TaskCompletionSource<HttpResponseMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Http.Before = (request, _) => request.Path == "mediaserver/exists_remote" && request.Body.GetProperty("episode_group").GetString() == "group-a"
                ? hold.Task : Task.FromResult<HttpResponseMessage?>(null);
            var pending = vm.SelectGroupAsync(vm.Groups.Single(item => item.Group.Id == "group-a"));
            await vm.SelectGroupAsync(vm.Groups.Single(item => item.Group.Id == "group-b")); var row = vm.Seasons.Single(); Check.Equal("缺失", row.Status);
            hold.SetResult(FakeMoviePilot.Reply(Json("""{"7":[1,2,3,4,5,6,7,8,9,10]}"""))); await pending;
            Check.True(ReferenceEquals(row, vm.Seasons.Single())); Check.Equal("缺失", row.Status);
        });
        Test("外部模式变更后提交不偷覆盖，读取后按钮同步真模式", async () =>
        {
            using var f = new SubscriptionFixture(); f.Seed(Subscription(1)); await f.ConnectAsync(); using var vm = Picker(f); await vm.ReloadAsync();
            vm.Seasons.Single(item => item.Season.Number == 1).FullMode = true;
            f.Http.Subscriptions[0] = Subscription(1, mode: MoviePilotSubscribeMode.Episodes);
            Check.False(await vm.SubmitAsync()); Check.Equal(0, f.Http.Writes);
            Check.Equal(1, vm.Seasons.Single(item => item.Season.Number == 1).ModeIndex); Check.False(vm.Submitted);
        });
        Test("取消对话只读无写；电影误入Show不构造dialog", async () =>
        {
            using var f = new SubscriptionFixture(); await f.ConnectAsync(); ContentDialog.Reset();
            ContentDialog.ShowEndpoint = async dialog => { var picker = (MoviePilotSeasonPicker)dialog; await picker.ViewModel.ReloadAsync(); return ContentDialogResult.None; };
            Check.False(await MoviePilotSeasonPicker.ShowAsync(new FrameworkElement(), f.Service, f.Media(), "", null));
            Check.Equal(0, f.Http.Writes); Check.Equal(1, ContentDialog.Created);
            Check.False(await MoviePilotSeasonPicker.ShowAsync(new FrameworkElement(), f.Service, f.Media("电影"), "", null));
            Check.Equal(1, ContentDialog.Created); ContentDialog.Reset();
        });
        Test("选季结果仅真实提交成功为true，失败留窗且取消仍为false", async () =>
        {
            foreach (var fail in new[] { false, true })
            {
                using var f = new SubscriptionFixture(); await f.ConnectAsync(); ContentDialog.Reset();
                f.Http.NoId = fail;
                ContentDialog.ShowEndpoint = async dialog =>
                {
                    var picker = (MoviePilotSeasonPicker)dialog;
                    await picker.ViewModel.ReloadAsync();
                    picker.ViewModel.Seasons.Single(row => row.Season.Number == 1).Selected = true;
                    var args = new ContentDialogButtonClickEventArgs();
                    typeof(MoviePilotSeasonPicker).GetMethod("OnSubmit", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(picker, [picker, args]);
                    await args.Completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Check.Equal(fail, args.Cancel);
                    Check.Equal(!fail, picker.Succeeded);
                    return args.Cancel ? ContentDialogResult.None : ContentDialogResult.Primary;
                };
                Check.Equal(!fail, await MoviePilotSeasonPicker.ShowAsync(new FrameworkElement(), f.Service, f.Media(), "", null));
                Check.Equal(1, f.Http.Writes);
                ContentDialog.Reset();
            }
        });
        Test("季卡元数据：篇名日期评分拆开，缺数据不伪造", () =>
        {
            var row = new MoviePilotMediaSeasonRow(new(2, "学前篇", 6, "2026-10-30", "", 8), null, _ => { }, _ => { });
            Check.Equal("第 2 季 · 学前篇", row.PickerTitle);
            Check.Equal("首播于 2026年10月30日", row.AirDateText); Check.Equal("★ 8", row.RatingText); Check.Equal("6 集", row.EpisodeCountText);
            var missing = new MoviePilotMediaSeasonRow(new(3, "Season 3", 0, "", "", null), null, _ => { }, _ => { });
            Check.Equal("第 3 季", missing.PickerTitle); Check.Equal(Visibility.Collapsed, missing.RatingVisibility); Check.Equal(Visibility.Collapsed, missing.AirDateVisibility);
            return Task.CompletedTask;
        });
        Test("对话打开期间断线立即隐藏，恢复不接受迟到Primary", async () =>
        {
            using var f = new SubscriptionFixture(); await f.ConnectAsync(); ContentDialog.Reset();
            var hold = new TaskCompletionSource<ContentDialogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            MoviePilotSeasonPicker? active = null;
            ContentDialog.ShowEndpoint = dialog => { active = (MoviePilotSeasonPicker)dialog; return hold.Task; };
            var pending = MoviePilotSeasonPicker.ShowAsync(new FrameworkElement(), f.Service, f.Media(), "", null);
            Check.True(active is not null); f.Settings.MoviePilot.Enabled = false; Check.True(active!.Hidden);
            f.Settings.MoviePilot.Enabled = true; await f.ConnectAsync(); hold.SetResult(ContentDialogResult.Primary); Check.False(await pending);
            Check.False(active.ViewModel.CanSubmit); Check.Equal(0, f.Http.Writes); ContentDialog.Reset();
        });
        Test("订阅搜索：移除来源筛选与空列，保留标题和结果计数", () =>
        {
            var xaml = XDocument.Parse(Resource("Production.MoviePilotSearchPanel.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            Check.False(xaml.Descendants().Any(item => (string?)item.Attribute(x + "Name") == "SourceFilter"));
            Check.False(xaml.Descendants().Any(item => (string?)item.Attribute("Header") == "来源"));
            var content = xaml.Descendants().Single(item => (string?)item.Attribute(x + "Name") == "SearchContent");
            var heading = content.Elements().Single(item => (string?)item.Attribute("Grid.Row") == "2");
            Check.Equal("StackPanel", heading.Name.LocalName);
            Check.Equal(2, heading.Elements().Count());
            Check.True(heading.Elements().Any(item => ((string?)item.Attribute("Text"))?.Contains("ViewModel.Heading", StringComparison.Ordinal) == true));
            Check.True(heading.Elements().Any(item => ((string?)item.Attribute("Text"))?.Contains("ViewModel.ResultCount", StringComparison.Ordinal) == true));
            var code = Resource("Production.MoviePilotSearchPanel.xaml.cs");
            Check.False(code.Contains("SourceFilter", StringComparison.Ordinal));
            Check.False(code.Contains("OnSourceChanged", StringComparison.Ordinal));
            return Task.CompletedTask;
        });
        Test("订阅搜索：首次、刷新和清空重搜不发送来源筛选或来源目录请求", async () =>
        {
            using var f = new SubscriptionFixture();
            using var vm = new MoviePilotSearchViewModel();
            vm.Attach(f.Service);
            await vm.SearchAsync(" 匿名 ");
            await vm.ReloadAsync();
            await vm.SearchAsync("");
            Check.Equal(0, vm.Results.Count);
            await vm.SearchAsync("再次");
            var searches = f.Http.Requests.Where(item => item.Method == "GET" && item.Path == "media/search").ToArray();
            Check.Equal(3, searches.Length);
            Check.True(searches.All(item => !item.Query.Contains("media_source", StringComparison.Ordinal)));
            Check.Equal(0, f.Http.Count("GET", "media/source"));
            Check.Equal(0, f.Http.Writes);
        });
        Test("订阅搜索：默认范围返回的同号跨来源结果保持独立身份", async () =>
        {
            using var f = new SubscriptionFixture();
            f.Http.SearchResults = Json("""[{"title":"匿名示例","type":"电影","media_source":"themoviedb","media_id":"42"},{"title":"匿名示例","type":"电影","media_source":"douban","media_id":"42"}]""");
            using var vm = new MoviePilotSearchViewModel();
            vm.Attach(f.Service);
            await vm.SearchAsync("匿名");
            Check.Equal(2, vm.Results.Count);
            Check.Equal("themoviedb", vm.Results[0].Media.MediaSource);
            Check.Equal("douban", vm.Results[1].Media.MediaSource);
            Check.True(vm.Results.All(item => item.Media.MediaId == "42" && item.SourceLabel.Length > 0));
            Check.Equal(0, f.Http.Writes);
        });
        Test("实际XAML合同：取消/下方提交/季开关/三个模式/状态海报可定位", () =>
        {
            var xaml = XDocument.Parse(Resource("Production.MoviePilotSeasonPicker.xaml"));
            var root = xaml.Root!; Check.Equal("取消", (string?)root.Attribute("CloseButtonText"));
            Check.Equal("None", (string?)root.Attribute("DefaultButton"));
            Check.Contains("EgPrimaryButtonStyle", root.Attribute("PrimaryButtonStyle")?.Value ?? "");
            Check.Contains("SubmitButtonText", root.Attribute("PrimaryButtonText")?.Value ?? "");
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var names = xaml.Descendants().Select(item => (string?)item.Attribute(x + "Name")).ToHashSet();
            foreach (var name in new[] { "SubscribeSeasons", "PickerEpisodeGroups", "SelectSubscribeSeason", "NormalSubscribeMode", "EpisodeUpgradeMode", "FullSeasonUpgradeMode", "RetrySeasonStates" }) Check.True(names.Contains(name), name);
            Check.True(names.Contains("DismissSeasonPicker"));
            foreach (var key in new[] { "AccentButtonBackground", "ToggleSwitchFillOn" })
                Check.True(xaml.Descendants().Any(item => (string?)item.Attribute(x + "Key") == key && (string?)item.Attribute("ResourceKey") == "EgAccentBrush"));
            Check.True(xaml.Descendants().Any(item => ((string?)item.Attribute("Text"))?.Contains("AirDateText", StringComparison.Ordinal) == true
                && (string?)item.Attribute("TextWrapping") == "Wrap"));
            var groups = xaml.Descendants().Single(item => (string?)item.Attribute(x + "Name") == "PickerEpisodeGroups");
            Check.True(groups.Descendants().Any(item => item.Name.LocalName == "StackLayout" && (string?)item.Attribute("Orientation") == "Horizontal"));
            var modes = xaml.Descendants().Single(item => (string?)item.Attribute(x + "Name") == "SeasonModes");
            Check.Equal("Segmented", modes.Name.LocalName);
            Check.Contains("ModeIndex", modes.Attribute("SelectedIndex")?.Value ?? "");
            Check.True(xaml.Descendants().Any(item => (string?)item.Attribute(x + "Name") == "ToggleSeasonCard" && (string?)item.Attribute("Click") == "OnToggleSeasonCard"));
            var text = Resource("Production.MoviePilotSearchPanel.xaml.cs"); Check.Contains("UseSeasonPicker", text); Check.Contains("MoviePilotSeasonPicker.ShowAsync", text);
            var page = Resource("Production.MoviePilotSubscriptionPage.xaml.cs"); Check.Contains("ApplyMovieSubscriptionAsync", page);
            // 2026-10-28：搜索结果卡可调用（点封面进订阅页），订阅页首次读取有转圈、且不再有细进度条。
            var search = XDocument.Parse(Resource("Production.MoviePilotSearchPanel.xaml"));
            var results = search.Descendants().Single(item => (string?)item.Attribute(x + "Name") == "Results");
            Check.Equal("True", (string?)results.Attribute("IsItemInvokedEnabled"));
            Check.Contains("OnResultInvoked", results.Attribute("ItemInvoked")?.Value ?? "");
            Check.Contains("ViewModel.Open(", text);
            var subscriptionPage = XDocument.Parse(Resource("Production.MoviePilotSubscriptionPage.xaml"));
            var loadingRing = subscriptionPage.Descendants().Single(item => (string?)item.Attribute(x + "Name") == "DetailLoadingRing");
            Check.Contains("InitialLoading", loadingRing.Attribute("IsActive")?.Value ?? "");
            var loadingPanel = subscriptionPage.Descendants().Single(item => (string?)item.Attribute(x + "Name") == "DetailLoading");
            Check.Contains("InitialLoadingVisibility", loadingPanel.Attribute("Visibility")?.Value ?? "");
            Check.False(subscriptionPage.Descendants().Any(item => item.Name.LocalName == "ProgressBar"),
                "订阅页又画了那条细进度条");
            return Task.CompletedTask;
        });
    }
    private static MoviePilotMediaViewModel Picker(SubscriptionFixture f)
    {
        var vm = new MoviePilotMediaViewModel(); vm.Attach(f.Service, f.Media(), picker: true); return vm;
    }
    private static string Resource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name) ?? throw new InvalidOperationException(name);
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
}
