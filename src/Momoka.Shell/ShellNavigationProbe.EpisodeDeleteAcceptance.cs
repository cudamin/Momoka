using System.Net;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>第五集、唯一集、多历史与版本选择使用真实页面/确认窗，只操作匿名假传输。</summary>
    private async Task MoviePilotEpisodeDeleteAcceptanceAsync()
    {
        foreach (var scenario in new[] { "fifth", "single", "duplicate", "versions-one", "versions-all", "versions-sequential", "cancel", "failed" })
            await Check(scenario);

        async Task Check(string scenario)
        {
            var server = new EpisodeRemovalProbeServer(scenario);
            using var fixture = CreateFixture(server);
            await fixture.SelectAsync();
            await ConnectMoviePilotAsync(fixture);
            var service = (MoviePilotService)fixture.Services.GetService(typeof(MoviePilotService))!;
            await service.HistoryCapabilitiesAsync(CancellationToken.None);
            fixture.Transport.Reply = server.MediaAsync;
            var shell = new ShellPage();
            shell.Attach(fixture.Services);
            _root.Children.Add(shell);
            var frame = Get<Frame>(shell, "ContentFrame");
            var scope = fixture.Session.Capture();
            var item = scope.Bind(server.Current());
            MoviePilotMediaManagementDialog? dialog = null;
            ResizeInspect(1280, 900);
            try
            {
                await LayoutAsync(shell);
                await (Task)Call(shell, "EnterShellAsync")!;
                await UntilAsync(() => shell.ActiveContentReady);
                frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, item));
                var before = (DetailPage)frame.Content;
                await UntilAsync(() => before.IsReady && before.ViewModel.Episodes.Count == server.EpisodeCount);
                await LayoutAsync(shell);
                Require(!shell.SignInVisible && before.ActualWidth > 0 && before.Visibility == Microsoft.UI.Xaml.Visibility.Visible,
                    "详情验收页面被登录页盖住");
                var reads = server.HistoryReads;
                var operation = (Task)Call(shell, "ShowMoviePilotHistoryActionAsync", scope, item,
                    MoviePilotHistoryAction.DeleteDestination)!;
                var multiple = server.Multiple;
                if (multiple)
                {
                    await UntilAsync(() => (dialog = Get<MoviePilotMediaManagementDialog?>(shell, "_moviePilotDialog"))
                        is { IsPrimaryButtonEnabled: true });
                    Require(dialog!.Records.Count == 2 && server.Writes == 0, "多版本或多历史未显示完整选择列表，或提前提交");
                    Require(server.HistoryReads - reads == 2, "选择窗重新读取了已核对候选");
                    await LayoutAsync(dialog);
                    await SaveProbeScreenAsync($"mp-episode-delete-{scenario}-selection-1280.png");
                    if (scenario == "cancel")
                    {
                        InvokeMediaCardDialog(dialog, "CloseButton");
                        await operation;
                        Require(server.Writes == 0 && ReferenceEquals(frame.Content, before),
                            "取消选择仍删除或切页");
                        return;
                    }
                    if (scenario is "versions-one" or "versions-sequential") dialog.Records.Single(row => row.Id == 12).Media.Selected = false;
                    if (scenario == "versions-all")
                    {
                        ResizeInspect(760, 900);
                        await LayoutAsync(dialog);
                        await SaveProbeScreenAsync("mp-episode-delete-versions-selection-760.png");
                    }
                    InvokeMediaCardDialog(dialog, "PrimaryButton");
                    await UntilAsync(() => operation.IsCompleted || dialog.PrimaryButtonText.Length == 0);
                    if (scenario == "failed")
                    {
                        Require(dialog.CompletedResult is { NeedsReview: true } && ReferenceEquals(frame.Content, before),
                            "目标文件删除失败仍自动关闭或导航");
                        await SaveProbeScreenAsync("mp-episode-delete-failed.png");
                        InvokeMediaCardDialog(dialog, "CloseButton");
                    }
                }
                await UntilAsync(() => operation.IsCompleted);
                await operation;
                Require(server.Writes == (scenario is "duplicate" or "versions-all" ? 2 : 1),
                    "删除请求次数与所选记录不符：" + scenario);
                Require(server.Bodies.All(body => body is "{\"id\":11}" or "{\"id\":12}") &&
                    server.Queries.All(query => query == "?deletesrc=false&deletedest=true"), "请求删除了范围外记录或源文件");
                if (scenario == "single") Require(frame.Content is HomePage, "唯一一集删除后没有回主页");
                else if (scenario is "versions-one" or "versions-sequential" or "failed")
                {
                    Require(ReferenceEquals(frame.Content, before) && before.ViewModel.CurrentItem?.Id == item.Id,
                        "只删除一版或删除失败后跳离了当前集");
                    if (scenario is "versions-one" or "versions-sequential")
                        await UntilAsync(() => before.IsReady && before.ViewModel.CurrentItem!.MediaSources.Count == 1);
                }
                else
                {
                    await UntilAsync(() => frame.Content is DetailPage { IsReady: true } landed &&
                        landed.ViewModel.CurrentItem?.IndexNumber == 4);
                    var landed = (DetailPage)frame.Content;
                    Require(landed.ViewModel.Episodes.All(episode => episode.Id != item.Id),
                        "已删第五集被迟到服务器索引带回集列表");
                }
                await LayoutAsync(shell);
                await SaveProbeScreenAsync($"mp-episode-delete-{scenario}-after.png");
                if (scenario == "versions-sequential")
                {
                    await (Task)Call(shell, "ShowMoviePilotHistoryActionAsync", scope, before.ViewModel.CurrentItem!,
                        MoviePilotHistoryAction.DeleteDestination)!;
                    Require(server.Writes == 2 && server.Bodies.Last() == "{\"id\":12}", "删剩余版本重发旧版本或被旧索引阻止");
                    await UntilAsync(() => frame.Content is DetailPage { IsReady: true } sequential &&
                        sequential.ViewModel.CurrentItem?.IndexNumber == 4);
                    await SaveProbeScreenAsync("mp-episode-delete-versions-sequential-complete.png");
                }
                Require(!Get<bool>(shell, "_moviePilotHistoryShowing") &&
                    Get<CancellationTokenSource?>(shell, "_moviePilotEpisodeDelete") is null,
                    "删除完成后操作所有者未释放");
            }
            finally
            {
                dialog?.Hide();
                Call(shell, "StopMoviePilotMonitoring");
                Call(shell, "ReleaseContent");
                shell.SignInRoot.Detach();
                _root.Children.Remove(shell);
                ResizeInspect(1420, 980);
            }
        }
    }

    private sealed class EpisodeRemovalProbeServer(string scenario) : HttpMessageHandler
    {
        internal int EpisodeCount => scenario == "single" ? 1 : 5;
        internal bool Multiple => scenario is "duplicate" or "versions-one" or "versions-all" or "versions-sequential" or "cancel" or "failed";
        private bool Versions => scenario.StartsWith("versions", StringComparison.Ordinal);
        internal int Writes { get; private set; }
        internal int HistoryReads { get; private set; }
        internal List<string> Bodies { get; } = [];
        internal List<string> Queries { get; } = [];
        private static EmbyItem Series() => new()
        {
            Id = "delete-show",
            Name = "离线第五集删除验收",
            Type = EmbyItemType.Series,
            ProviderIds = new() { ["Tmdb"] = "313346" },
            ChildCount = 1
        };
        private static EmbyItem Season() => new()
        {
            Id = "delete-season",
            Name = "第 1 季",
            Type = EmbyItemType.Season,
            IndexNumber = 1,
            SeriesId = "delete-show",
            SeriesName = "离线第五集删除验收"
        };
        internal EmbyItem Current() => Episode(EpisodeCount);
        private EmbyItem Episode(int number)
        {
            var path = $"/library/S01E{number:00}.mkv";
            return new()
            {
                Id = $"delete-e{number}",
                Name = $"第 {number} 集",
                Type = EmbyItemType.Episode,
                SeriesId = "delete-show",
                SeriesName = "离线第五集删除验收",
                SeasonId = "delete-season",
                ParentId = "delete-season",
                IndexNumber = number,
                ParentIndexNumber = 1,
                Path = path,
                PremiereDate = DateTimeOffset.UnixEpoch.AddDays(number),
                RunTimeTicks = TimeSpan.FromMinutes(23).Ticks,
                MediaSources = [.. (Versions && number == EpisodeCount ? new[] { path, "/library/S01E05-alt.mkv" } : new[] { path })
                    .Select((file, index) => new MediaSource
                    {
                        Id = $"source-{number}-{index}", Name = index == 0 ? "1080p" : "720p", Path = file,
                        Container = "mkv", MediaStreams = [new() { Type = "Video", Codec = "h264", Width = 1920, Height = 1080 }]
                    })]
            };
        }
        internal Task<HttpResponseMessage> MediaAsync(HttpRequestMessage request, CancellationToken token)
        {
            Require(request.RequestUri!.Host.EndsWith(".invalid", StringComparison.Ordinal), "第五集验收拒绝真实媒体服务器");
            Require(request.Method == HttpMethod.Get, "第五集验收拒绝媒体服务写入");
            token.ThrowIfCancellationRequested();
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("/Items/delete-show", StringComparison.Ordinal)) return Task.FromResult(Json(Series()));
            if (path.EndsWith("/Items/delete-season", StringComparison.Ordinal)) return Task.FromResult(Json(Season()));
            var number = Enumerable.Range(1, EpisodeCount).FirstOrDefault(value => path.EndsWith($"/Items/delete-e{value}", StringComparison.Ordinal));
            if (number > 0) return Task.FromResult(Json(Episode(number)));
            if (path.Contains("/Shows/delete-show/", StringComparison.Ordinal))
                return Task.FromResult(path.EndsWith("/Seasons", StringComparison.Ordinal)
                    ? Json(new { Items = new[] { Season() }, TotalRecordCount = 1 })
                    : Json(new { Items = Enumerable.Range(1, EpisodeCount).Select(Episode).ToArray(), TotalRecordCount = EpisodeCount }));
            return Task.FromResult(FakeTransport.Standard(request));
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Require(request.RequestUri!.Host == "moviepilot.invalid", "第五集删除验收拒绝真实 MoviePilot");
            token.ThrowIfCancellationRequested();
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("login/access-token", StringComparison.Ordinal)) return Json(new { access_token = "fixture-token" });
            if (path.EndsWith("dashboard/system", StringComparison.Ordinal)) return Json(new { version = "fixture" });
            if (path.EndsWith("user/current", StringComparison.Ordinal)) return Json(new { is_active = true, is_superuser = true });
            if (path.EndsWith("system/global/user", StringComparison.Ordinal)) return Json(new { AI_AGENT_ENABLE = false });
            if (path == "/api/v1/history/transfer" && request.Method == HttpMethod.Get)
            {
                HistoryReads++;
                object History(int id, string target) => new
                {
                    id,
                    title = Series().Name,
                    type = "电视剧",
                    status = true,
                    mode = "copy",
                    media_source = "themoviedb",
                    media_id = "313346",
                    seasons = "S01",
                    episodes = $"E{EpisodeCount:00}",
                    src_fileitem = new { storage = "local", type = "file", path = $"/downloads/source-{id}.mkv" },
                    dest_fileitem = new { storage = "local", type = "file", path = target }
                };
                var current = Current().Path!;
                object[] records = scenario == "versions-sequential" && Writes > 0 ? [History(12, "/library/S01E05-alt.mkv")]
                    : Multiple ? [History(11, current), History(12, Versions ? "/library/S01E05-alt.mkv" : current)] : [History(11, current)];
                return Json(new { success = true, data = new { total = records.Length, list = records } });
            }
            if (path == "/api/v1/history/transfer" && request.Method == HttpMethod.Delete)
            {
                Writes++;
                Bodies.Add(await request.Content!.ReadAsStringAsync(token));
                Queries.Add(request.RequestUri.Query);
                return Json(new
                {
                    success = scenario != "failed",
                    data = new
                    {
                        source = new { status = "not_requested" },
                        destination = new { status = scenario == "failed" ? "failed" : Writes > 1 && !Versions ? "already_missing" : "deleted" },
                        history = scenario == "failed" ? "retained" : "deleted"
                    }
                });
            }
            throw new InvalidOperationException("第五集验收拒绝未登记请求：" + request.Method + " " + path);
        }
    }
}
