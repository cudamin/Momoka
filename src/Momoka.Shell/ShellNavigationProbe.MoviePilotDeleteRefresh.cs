using System.Net;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>
    /// 删记录和库文件、删记录及两端完成之后，主页不能还挂着已经删掉的海报和元数据。
    /// 用户令 2026-10-31：这两种删除必须以「关闭回执后主页自动重读」为验收标准。
    /// <para>
    /// 假 Emby 在删除后**仍然**把这一条放进继续观看和最近添加：真实服务器上元数据常常比文件晚一步消失，
    /// 只重读整页会把它原样带回来。这一条因此同时验证「确实重读了」和「这一件不会重新出现」。
    /// </para>
    /// </summary>
    private async Task MoviePilotDeleteRefreshAsync()
    {
        foreach (var action in new[] { MoviePilotHistoryAction.DeleteDestination, MoviePilotHistoryAction.DeleteAll })
        {
            var server = new DeleteRefreshProbeServer();
            using var fixture = CreateFixture(server);
            await fixture.SelectAsync();
            fixture.Settings.Ui.ShowHomeBanner = true;
            fixture.Transport.Reply = server.EmbyAsync;
            await ConnectMoviePilotAsync(fixture);
            var service = (MoviePilotService)fixture.Services.GetService(typeof(MoviePilotService))!;
            await service.HistoryCapabilitiesAsync(CancellationToken.None);
            var shell = new ShellPage();
            shell.Attach(fixture.Services);
            _root.Children.Add(shell);
            MoviePilotMediaManagementDialog? dialog = null;
            try
            {
                await LayoutAsync(shell);
                await (Task)Call(shell, "EnterShellAsync")!;
                await UntilAsync(() => shell.ActiveContentReady);
                var home = (HomePage)Get<Frame>(shell, "ContentFrame").Content;
                await UntilAsync(() => Cards(home).Any(card => card.Item.Id == DeleteRefreshProbeServer.MovieId));
                Require(Cards(home).Any(card => card.Item.Id == DeleteRefreshProbeServer.OtherId),
                    "前置条件不成立：主页没有用来核对「只删这一条」的另一张卡");
                Require(home.ViewModel.Slides.Any(slide => slide.Item.Id == DeleteRefreshProbeServer.MovieId) &&
                    home.ViewModel.Slides.Any(slide => slide.Item.Id == DeleteRefreshProbeServer.OtherId),
                    "前置条件不成立：主页轮播没有待删除条目与对照条目");
                await LayoutAsync(home);
                var suffix = action == MoviePilotHistoryAction.DeleteAll ? "all" : "destination";
                await SaveProbeScreenAsync($"mp-delete-refresh-{suffix}-before.png");
                var readsBefore = server.ResumeReads;

                var scope = fixture.Session.Capture();
                var item = scope.Bind(DeleteRefreshProbeServer.Movie());
                var operation = (Task)Call(shell, "ShowMoviePilotHistoryActionAsync", scope, item, action)!;
                await UntilAsync(() => (dialog = Get<MoviePilotMediaManagementDialog?>(shell, "_moviePilotDialog"))
                    is { IsPrimaryButtonEnabled: true });
                Require(dialog is not null && !dialog.LibraryFilesRemovalSubmitted,
                    "还没确认就把页面当成已删除媒体库文件");
                InvokeMediaCardDialog(dialog!, "PrimaryButton");
                await UntilAsync(() => server.Writes == 1 && dialog!.PrimaryButtonText.Length == 0 &&
                    dialog.CloseButtonText == "关闭");
                Require(server.LastQuery.Contains(action == MoviePilotHistoryAction.DeleteAll
                        ? "deletesrc=true&deletedest=true" : "deletesrc=false&deletedest=true", StringComparison.Ordinal),
                    "删除范围与所选档位不一致");
                Require(dialog!.LibraryFilesRemovalSubmitted, "删除媒体库文件的确认没有通知外壳刷新");

                InvokeMediaCardDialog(dialog, "CloseButton");
                await operation;
                await UntilAsync(() => server.ResumeReads > readsBefore);
                Require(Cards(home).All(card => card.Item.Id != DeleteRefreshProbeServer.MovieId),
                    "删除后主页货架仍显示被删条目");
                Require(Cards(home).Any(card => card.Item.Id == DeleteRefreshProbeServer.OtherId),
                    "刷新后主页误删了没有被删的条目");
                Require(home.ViewModel.Slides.All(slide => slide.Item.Id != DeleteRefreshProbeServer.MovieId),
                    "删除后主页轮播仍显示被删条目");
                Require(home.ViewModel.Slides.Any(slide => slide.Item.Id == DeleteRefreshProbeServer.OtherId),
                    "刷新后主页轮播误删了没有被删的条目");
                await LayoutAsync(home);
                await SaveProbeScreenAsync($"mp-delete-refresh-{suffix}-after.png");
            }
            finally
            {
                dialog?.Hide();
                Call(shell, "StopMoviePilotMonitoring");
                Call(shell, "ReleaseContent");
                shell.SignInRoot.Detach();
                _root.Children.Remove(shell);
            }
        }

        static IEnumerable<CardItem> Cards(HomePage home) => home.ViewModel.Shelves.SelectMany(shelf => shelf.Cards);
    }

    private sealed class DeleteRefreshProbeServer : HttpMessageHandler
    {
        internal const string MovieId = "mp-delete-refresh-movie";
        internal const string OtherId = "mp-delete-refresh-other";
        private const string Title = "离线删除刷新测试电影";
        private const string OtherTitle = "离线删除刷新对照电影";
        public int Writes { get; private set; }
        public int ResumeReads { get; private set; }
        public string LastQuery { get; private set; } = "";

        internal static EmbyItem Movie() => new()
        {
            Id = MovieId,
            Name = Title,
            Type = EmbyItemType.Movie,
            Path = "/library/movie.mkv",
            ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "1" },
            ImageTags = new Dictionary<string, string> { ["Primary"] = "poster" },
            BackdropImageTags = ["backdrop"]
        };

        internal static EmbyItem Other() => new()
        {
            Id = OtherId,
            Name = OtherTitle,
            Type = EmbyItemType.Movie,
            Path = "/library/other.mkv",
            ImageTags = new Dictionary<string, string> { ["Primary"] = "poster" },
            BackdropImageTags = ["backdrop"]
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Require(request.RequestUri!.Host.EndsWith(".invalid", StringComparison.Ordinal), "删除刷新探针拒绝真实网络");
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("login/access-token", StringComparison.Ordinal)) return Json(new { access_token = "fixture-token" });
            if (path.EndsWith("dashboard/system", StringComparison.Ordinal)) return Json(new { version = "fixture" });
            if (path.EndsWith("user/current", StringComparison.Ordinal)) return Json(new { is_active = true, is_superuser = true });
            if (path.EndsWith("system/global/user", StringComparison.Ordinal)) return Json(new { AI_AGENT_ENABLE = true });
            if (path == "/api/v1/history/transfer" && request.Method == HttpMethod.Get)
                return Json(new
                {
                    success = true,
                    data = new
                    {
                        total = 1,
                        list = new[]
                        {
                            new
                            {
                                id = 1, title = Title, type = "电影", status = true, mode = "softlink",
                                media_source = "themoviedb", media_id = "1",
                                src = "/downloads/movie.mkv", dest = "/library/movie.mkv",
                                src_fileitem = new { storage = "local", type = "file", path = "/downloads/movie.mkv" },
                                dest_fileitem = new { storage = "local", type = "file", path = "/library/movie.mkv" }
                            }
                        }
                    }
                });
            if (path == "/api/v1/history/transfer" && request.Method == HttpMethod.Delete)
            {
                Writes++;
                LastQuery = request.RequestUri.Query;
                var deleteSource = LastQuery.Contains("deletesrc=true", StringComparison.Ordinal);
                await request.Content!.ReadAsStringAsync(token);
                return Json(new
                {
                    success = true,
                    data = new
                    {
                        source = new { status = deleteSource ? "deleted" : "not_requested" },
                        destination = new { status = "deleted" },
                        history = "deleted"
                    }
                });
            }
            return Json(Array.Empty<object>());
        }

        internal Task<HttpResponseMessage> EmbyAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/Items/Resume", StringComparison.Ordinal))
            {
                ResumeReads++;
                return Task.FromResult(Json(new { Items = new[] { Movie(), Other() }, TotalRecordCount = 2 }));
            }
            if (path.EndsWith("/Items/Latest", StringComparison.Ordinal)) return Task.FromResult(Json(new[] { Movie(), Other() }));
            if (path.EndsWith("/Shows/NextUp", StringComparison.Ordinal))
                return Task.FromResult(Json(new { Items = Array.Empty<EmbyItem>(), TotalRecordCount = 0 }));
            if (path.EndsWith("/Items/" + MovieId, StringComparison.Ordinal)) return Task.FromResult(Json(Movie()));
            if (path.EndsWith("/Items", StringComparison.Ordinal))
                return Task.FromResult(Json(new { Items = Array.Empty<EmbyItem>(), TotalRecordCount = 0 }));
            return Task.FromResult(FakeTransport.Standard(request));
        }
    }
}
