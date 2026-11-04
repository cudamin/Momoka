using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>
    /// 删记录和库文件、删记录及两端完成之后，主页不能还挂着已经删掉的海报和元数据。
    /// 用户令 2026-10-31：这两种删除的验收标准是「删除后主页不再显示被删内容」；2026-11-03 追加：
    /// 不重读整页、保持当前页与滚动位置，用离场动画原地摘卡，且重读也不能把它带回来（服务器缓存要等下次扫描）。
    /// <para>
    /// 假 Emby 在删除后**仍然**把这一条放进继续观看和最近添加：真实服务器上元数据常常比文件晚一步消失。
    /// 这一条因此同时验证「没有重读」「滚动没动」「动画真的起了」和「重读也带不回来」。
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
                var scope = fixture.Session.Capture();
                var item = scope.Bind(DeleteRefreshProbeServer.Movie());

                // 离场动画本身：不只看「起过一次」，还要看到卡片真的淡下去、缩进去，再能还原给回收池。
                // 系统关掉动画时这一条走另一档（直接完成），不把系统设置当成产品缺陷。
                if (HomeMotion.AnimationsEnabled)
                {
                    var target = home.ViewModel.CardsOf(item).FirstOrDefault();
                    Require(target is not null, "前置条件不成立：找不到待删除条目在屏上的卡");

                    // 容器是 ItemsRepeater 建出来的：卡片可能已经绑上条目、开始拉图，而 Loaded 还没到。
                    // PosterCard 按自己的 _live 判能不能演，此刻开播会直接返回、屏上一点变化都没有 —— 先等它上树。
                    PosterCard? poster = null;
                    var mounted = await WithinAsync(() =>
                    {
                        poster = (PosterCard?)Call(home, "PosterOf", target!);
                        return poster is not null && Get<bool>(poster, "_live");
                    });
                    Require(mounted, "前置条件不成立：这张卡的容器一直没上树");

                    var live = poster!;
                    var play = (Task)Call(live, "PlayRemovalAsync")!;

                    // 淡出要真的看到中间状态：按帧等它掉下来，不拿固定延时当证据（机器忙时首帧会晚到）。
                    Require(await WithinAsync(() => Get<FrameworkElement>(live, "Root").Opacity < 0.98),
                        "离场动画没有淡出");
                    await play;
                    var motion = Get<Microsoft.UI.Xaml.Media.CompositeTransform>(live, "CardMotion");
                    Require(Get<FrameworkElement>(live, "Root").Opacity < 0.05 && motion.ScaleX < 0.95,
                        "离场动画没有落到透明与收缩");
                    Call(live, "ResetRemoval");
                    Require(Math.Abs(Get<FrameworkElement>(live, "Root").Opacity - 1) < 0.001 &&
                        Math.Abs(motion.ScaleX - 1) < 0.001, "离场动画没有还原卡片状态");
                }

                var scroller = Get<ScrollView>(home, "Scroller");
                await ScrollProbeAsync(scroller, Math.Min(140, scroller.ScrollableHeight));
                await LayoutAsync(home);
                var offset = scroller.VerticalOffset;
                var suffix = action == MoviePilotHistoryAction.DeleteAll ? "all" : "destination";
                await SaveProbeScreenAsync($"mp-delete-refresh-{suffix}-before.png");
                var readsBefore = server.ResumeReads;

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

                // 用户令 2026-11-03：删除之后不重读整页、不翻页，只把那一件原地摘掉 —— 重读一页海报会把
                // 滚动位置推回顶上。这一条同时钉住「没有重读」和「滚动位置没动」。
                Require(server.ResumeReads == readsBefore, "删除后重读了整页；应该原地摘卡并保持当前页");
                Require(Math.Abs(scroller.VerticalOffset - offset) < 0.6, "删除后滚动位置变了");
                Require(home.RemovalMotions == 1, "删除没有走主页的离场动画");
                Require(Cards(home).All(card => card.Item.Id != DeleteRefreshProbeServer.MovieId),
                    "删除后主页货架仍显示被删条目");
                Require(Cards(home).Any(card => card.Item.Id == DeleteRefreshProbeServer.OtherId),
                    "删除后主页误删了没有被删的条目");
                Require(home.ViewModel.Slides.All(slide => slide.Item.Id != DeleteRefreshProbeServer.MovieId),
                    "删除后主页轮播仍显示被删条目");
                Require(home.ViewModel.Slides.Any(slide => slide.Item.Id == DeleteRefreshProbeServer.OtherId),
                    "删除后主页轮播误删了没有被删的条目");
                await LayoutAsync(home);
                await SaveProbeScreenAsync($"mp-delete-refresh-{suffix}-after.png");

                // 服务器元数据要等下一次扫描才消失：假 Emby 在删除后仍然返回这一条。整页重读一次，
                // 被删的那一件必须继续藏着（本次运行内），对照条目照旧在 —— 隐藏状态只在内存里。
                await home.ViewModel.ReloadAsync();
                await UntilAsync(() => server.ResumeReads > readsBefore);
                await LayoutAsync(home);
                Require(Cards(home).All(card => card.Item.Id != DeleteRefreshProbeServer.MovieId),
                    "重读把已删条目又带回了主页货架");
                Require(home.ViewModel.Slides.All(slide => slide.Item.Id != DeleteRefreshProbeServer.MovieId),
                    "重读把已删条目又带回了主页轮播");
                Require(Cards(home).Any(card => card.Item.Id == DeleteRefreshProbeServer.OtherId),
                    "重读误删了没有被删的条目");
                await SaveProbeScreenAsync($"mp-delete-refresh-{suffix}-reloaded.png");
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

        /// <summary>等到条件成立为止；用在「动画/容器就绪」这类按帧推进的读数上，超时返回 false 交给调用方报错。</summary>
        static async Task<bool> WithinAsync(Func<bool> ready)
        {
            for (var attempt = 0; attempt < 80; attempt++)
            {
                if (ready()) return true;
                await Task.Delay(25);
            }

            return false;
        }
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
