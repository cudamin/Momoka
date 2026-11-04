using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>真实 Shell 协调单集直接删除；仅匿名内存传输，不创建确认窗或操作真实文件。</summary>
    private async Task MoviePilotEpisodeDeleteAsync()
    {
        foreach (var action in new[] { MoviePilotHistoryAction.DeleteRecord, MoviePilotHistoryAction.DeleteSource,
            MoviePilotHistoryAction.DeleteDestination, MoviePilotHistoryAction.DeleteAll })
            await Check(action, "");
        foreach (var fault in new[] { "missing-receipt", "lost-receipt", "partial", "navigate", "content-return", "connection" })
            await Check(MoviePilotHistoryAction.DeleteAll, fault);

        async Task Check(MoviePilotHistoryAction action, string fault)
        {
            var server = new EpisodeDeleteProbeServer { Fault = fault };
            using var fixture = CreateFixture(server);
            await fixture.SelectAsync();
            await ConnectMoviePilotAsync(fixture);
            var service = (MoviePilotService)fixture.Services.GetService(typeof(MoviePilotService))!;
            await service.HistoryCapabilitiesAsync(CancellationToken.None);
            fixture.Transport.Reply = (request, _) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("/Items/mp-episode", StringComparison.Ordinal)) return Task.FromResult(Json(Subject(EmbyItemType.Episode)));
                if (path.EndsWith("/Items/mp-series", StringComparison.Ordinal)) return Task.FromResult(Json(Subject(EmbyItemType.Series)));
                return Task.FromResult(FakeTransport.Standard(request));
            };
            var shell = new ShellPage();
            shell.Attach(fixture.Services);
            _root.Children.Add(shell);
            var frame = Get<Frame>(shell, "ContentFrame");
            frame.Content = new Page();
            var page = frame.Content;
            var scope = fixture.Session.Capture();
            var item = scope.Bind(Subject(EmbyItemType.Episode));
            var popupsBefore = VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot).Count;
            var toast = Get<InfoBar>(shell, "Toast");
            server.BeforeHistory = () =>
            {
                switch (fault)
                {
                    case "navigate": frame.Navigate(typeof(Page)); break;
                    case "content-return": frame.Content = new Page(); frame.Content = page; break;
                    case "connection": fixture.Settings.MoviePilot.Username = "replacement"; break;
                }
            };
            try
            {
                await LayoutAsync(shell);
                var operation = (Task)Call(shell, "ShowMoviePilotHistoryActionAsync", scope, item, action)!;
                await UntilAsync(() => operation.IsCompleted);
                await operation;
                Require(Get<ContentDialog?>(shell, "_moviePilotDialog") is null &&
                    VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot).Count == popupsBefore,
                    "单集删除仍创建或显示了确认弹窗");
                Require(Get<CancellationTokenSource?>(shell, "_moviePilotEpisodeDelete") is null &&
                    !Get<bool>(shell, "_moviePilotHistoryShowing"), "单集删除结束后仍持有在途所有者");
                var canceled = fault is "navigate" or "content-return" or "connection";
                Require(server.Writes == (canceled ? 0 : 1), "单集删除请求次数不符：" + fault);
                if (!canceled)
                {
                    Require(server.LastPath == "/api/v1/history/transfer?deletesrc=" +
                        (action is MoviePilotHistoryAction.DeleteSource or MoviePilotHistoryAction.DeleteAll ? "true" : "false") +
                        "&deletedest=" + (action is MoviePilotHistoryAction.DeleteDestination or MoviePilotHistoryAction.DeleteAll ? "true" : "false") &&
                        server.LastBody == "{\"id\":2}", "单集删除未按点击范围执行 DELETE");
                    Require(toast.IsOpen && toast.Message.Contains(item.Name, StringComparison.Ordinal) &&
                        toast.Severity == (fault.Length == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning), "单集删除结果未提示正确状态");
                    if (fault.Length > 0) Require(toast.Message.Contains("核对", StringComparison.Ordinal), "不明回执未要求核对");
                    await (Task)Call(shell, "ShowMoviePilotHistoryActionAsync", scope, item, MoviePilotHistoryAction.DeleteRecord)!;
                    Require(server.Writes == 1, "单集删除重复点击或换档重发了已发请求");
                }
                else if (fault is "navigate" or "content-return") Require(!toast.IsOpen, "旧单集操作在返回后的页面回填通知");
            }
            finally
            {
                server.BeforeHistory = null;
                Call(shell, "StopMoviePilotMonitoring");
                Call(shell, "ReleaseContent");
                shell.SignInRoot.Detach();
                _root.Children.Remove(shell);
            }
        }
    }

    private sealed class EpisodeDeleteProbeServer : HttpMessageHandler
    {
        public string Fault { get; init; } = "";
        public Action? BeforeHistory { get; set; }
        public int Writes { get; private set; }
        public string LastPath { get; private set; } = "";
        public string LastBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Require(request.RequestUri!.Host == "moviepilot.invalid", "单集删除探针拒绝真实 MP 网络");
            token.ThrowIfCancellationRequested();
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("login/access-token", StringComparison.Ordinal)) return Json(new { access_token = "fixture-token" });
            if (path.EndsWith("dashboard/system", StringComparison.Ordinal)) return Json(new { version = "fixture" });
            if (path.EndsWith("user/current", StringComparison.Ordinal)) return Json(new { is_active = true, is_superuser = true });
            if (path.EndsWith("system/global/user", StringComparison.Ordinal)) return Json(new { AI_AGENT_ENABLE = true });
            if (path == "/api/v1/history/transfer" && request.Method == HttpMethod.Get)
            {
                var before = BeforeHistory;
                BeforeHistory = null;
                before?.Invoke();
                token.ThrowIfCancellationRequested();
                return Json(new
                {
                    success = true,
                    data = new
                    {
                        total = 1,
                        list = new[] { new
                        {
                            id = 2, title = "离线测试剧集", type = "电视剧", status = true, mode = "softlink",
                            media_source = "themoviedb", media_id = "1", seasons = "S01", episodes = "E01",
                            src = "/downloads/series.mkv", dest = "/library/series.mkv",
                            src_fileitem = new { storage = "local", type = "file", path = "/downloads/series.mkv" },
                            dest_fileitem = new { storage = "local", type = "file", path = "/library/series.mkv" }
                        } }
                    }
                });
            }
            if (path == "/api/v1/history/transfer" && request.Method == HttpMethod.Delete)
            {
                Writes++;
                LastPath = request.RequestUri.PathAndQuery;
                LastBody = await request.Content!.ReadAsStringAsync(token);
                if (Fault == "lost-receipt") throw new HttpRequestException("匿名删除回执丢失");
                if (Fault == "missing-receipt") return Json(new { success = true, data = new { } });
                return Json(new
                {
                    success = true,
                    data = new
                    {
                        source = new { status = request.RequestUri.Query.Contains("deletesrc=true", StringComparison.Ordinal) ? "deleted" : "not_requested" },
                        destination = new { status = Fault == "partial" ? "failed" : request.RequestUri.Query.Contains("deletedest=true", StringComparison.Ordinal) ? "deleted" : "not_requested" },
                        history = Fault == "partial" ? "retained" : "deleted"
                    }
                });
            }
            if (path.EndsWith("/subscribe/", StringComparison.Ordinal)) return Json(Array.Empty<object>());
            throw new InvalidOperationException("单集删除探针拒绝未登记请求：" + request.Method + " " + path);
        }
    }
}
