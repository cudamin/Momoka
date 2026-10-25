using System.Text.Json;
using Momoka.Configuration;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task MoviePilotResourceOptionsAsync()
    {
        var server = new ResourceOptionsServer();
        using var fixture = CreateFixture(server);
        await fixture.SelectAsync();
        fixture.Settings.MoviePilot.Enabled = true;
        fixture.Settings.MoviePilot.Url = "https://moviepilot.invalid:3001";
        fixture.Settings.MoviePilot.Username = "fixture";
        new MoviePilotCredentials(PassthroughSecretProtector.Instance).SetPassword(fixture.Settings.MoviePilot, "fixture");
        var service = (MoviePilotService)fixture.Services.GetService(typeof(MoviePilotService))!;
        var view = new MoviePilotVersionsView();
        var precise = new MoviePilotResourcePanel();
        _root.Children.Add(view);
        try
        {
            view.Attach(service, fixture.Launcher);
            await view.SearchAsync("测试资源");
            var browser = view.ViewModel.Browser;
            var first = browser.Rows[0];
            Require(browser.Rows.Count == 4, "跨站点资源卡片没有逐条保留");
            var list = Get<MoviePilotResourceBrowser>(view, "Browser");
            await LayoutAsync(view);
            var seasons = Get<MoviePilotResourceFacetPicker>(list, "SeasonFilter");
            Get<Flyout>(seasons, "ChoicesFlyout").ShowAt(Get<DropDownButton>(seasons, "OpenFacet"));
            await LayoutAsync(seasons);
            var chooseSeason = Descendants(Get<ItemsControl>(seasons, "FacetChoices")).OfType<CheckBox>()
                .Single(box => AutomationProperties.GetName(box) == "S02");
            ((IToggleProvider)new CheckBoxAutomationPeer(chooseSeason).GetPattern(PatternInterface.Toggle)).Toggle();
            seasons.ClosePopup();
            Get<ComboBox>(list, "EpisodeFilter").SelectedItem = "E02";
            Require(browser.Rows.Count == 1 && browser.Rows[0].Resource.Seasons.Contains(2), "季集控件未实际筛选结果");
            first.State = MoviePilotDownloadState.Uncertain;
            browser.ResetCommand.Execute(null);
            Require(ReferenceEquals(browser.Rows[0], first) && first.State == MoviePilotDownloadState.Uncertain,
                "清除筛选丢失原行或下载状态");
            Require(Get<ItemsView>(list, "ResourceList").ItemsSource == browser.Rows, "资源列表没有使用卡片结果集合");
            foreach (var width in new[] { 1040, 720, 520 })
            {
                ResizeInspect(width, 980);
                await LayoutAsync(view);
                await Task.Delay(450);
                await SaveReturnFrameAsync(view, $"mp-resources-{width}.png");
                await SaveProbeScreenAsync($"mp-resources-screen-{width}.png");
            }
            ResizeInspect(720, 980);
            await LayoutAsync(view);
            await Task.Delay(450);
            Require(Descendants(list).OfType<Button>().Any(button => button.Name == "DownloadResource" &&
                AutomationProperties.GetName(button).Contains(first.Title, StringComparison.Ordinal)), "卡片下载入口不可定位");
            await SaveProbeScreenAsync("mp-resources-cards-screen.png");

            var row = browser.Rows[1];
            var cancelled = row.DownloadCommand.ExecuteAsync(null);
            var dialog = await DownloadDialogAsync();
            Require(dialog.DefaultButton == ContentDialogButton.Close, "下载对话框默认按钮不是取消");
            InvokeDialog(dialog, "CloseButton");
            await cancelled;
            Require(server.Downloads.Count == 0 && row.State == MoviePilotDownloadState.Idle, "取消下载仍提交或锁住行");

            var submitted = row.DownloadCommand.ExecuteAsync(null);
            dialog = await DownloadDialogAsync();
            await UntilAsync(() => !dialog.ViewModel.Busy);
            Get<ComboBox>(dialog, "DownloadClient").SelectedItem = "下载器乙";
            Get<ComboBox>(dialog, "DownloadPath").SelectedItem = "rclone:/media/动画";
            await LayoutAsync(dialog);
            Require(Get<ComboBox>(dialog, "DownloadPath").Text == "rclone:/media/动画", "选择保存路径未进入编辑框");
            await SaveProbeScreenAsync("mp-download-options-screen.png");
            InvokeDialog(dialog, "PrimaryButton");
            await submitted;
            Require(server.Downloads.Count == 1 && row.State == MoviePilotDownloadState.Done, "确认未提交到假下载器");
            var body = server.Downloads[0];
            Require(body.GetProperty("downloader").GetString() == "下载器乙" &&
                body.GetProperty("save_path").GetString() == "rclone:/media/动画" &&
                body.GetProperty("torrent_in").GetProperty("site_name").GetString() == "站点乙", "下载选项或跨站点资源传错");

            var custom = browser.Rows[3].DownloadCommand.ExecuteAsync(null);
            dialog = await DownloadDialogAsync();
            await UntilAsync(() => !dialog.ViewModel.Busy);
            Get<ComboBox>(dialog, "DownloadPath").Text = "/downloads/custom path";
            InvokeDialog(dialog, "PrimaryButton");
            await custom;
            Require(server.Downloads.Count == 2 && !server.Downloads[1].TryGetProperty("downloader", out _) &&
                server.Downloads[1].GetProperty("save_path").GetString() == "/downloads/custom path", "默认下载器或自定义路径未正确提交");

            server.PendingChoices = Pending<bool>();
            var pending = browser.Rows[2].DownloadCommand.ExecuteAsync(null);
            dialog = await DownloadDialogAsync();
            Require(dialog.ViewModel.Busy && !dialog.IsPrimaryButtonEnabled, "加载选项时允许提前提交");
            view.Release();
            await pending;
            server.PendingChoices.TrySetResult(true);
            await Task.Delay(150);
            Require(server.Downloads.Count == 2 && dialog.ViewModel.Paths.Count == 0, "离页后仍提交下载或旧选项回填");
            _root.Children.Remove(view);
            precise.Margin = new Microsoft.UI.Xaml.Thickness(16, 40, 16, 16);
            _root.Children.Add(precise);
            var before = server.Searches;
            precise.Open(service, new MoviePilotMedia { Title = "测试剧集", Type = "电视剧", MediaSource = "themoviedb", MediaId = "42" }, fixture.Launcher);
            await LayoutAsync(precise);
            Require(server.Searches == before && precise.ViewModel.ShowEmptyNotice, "点击媒体搜索资源后未经选择立即搜索");
            await precise.ViewModel.SearchSites.EnsureLoadedAsync();
            precise.ViewModel.SearchSites.UseDefault = false;
            precise.ViewModel.SearchSites.Sites[1].Selected = true;
            await SaveProbeScreenAsync("mp-search-before-submit-screen.png");
            await precise.ViewModel.SearchAsync();
            Require(server.LastSearch.Contains("sites=2", StringComparison.Ordinal), "首次精确搜索未带入所选站点");
            Require(fixture.Actions.Plays == 0, "资源操作不应播放");
        }
        finally
        {
            view.Release();
            precise.Release();
            _root.Children.Remove(view);
            _root.Children.Remove(precise);
            ResizeInspect(1420, 980);
        }

        async Task<MoviePilotDownloadDialog> DownloadDialogAsync()
        {
            MoviePilotDownloadDialog? dialog = null;
            await UntilAsync(() => (dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot)
                .SelectMany(popup => Descendants(popup.Child)).OfType<MoviePilotDownloadDialog>().FirstOrDefault()) is not null);
            await LayoutAsync(dialog!);
            return dialog!;
        }
        static void InvokeDialog(ContentDialog dialog, string name)
        {
            var button = Descendants(dialog).OfType<Button>().Single(button => button.Name == name);
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        }
    }

    private sealed class ResourceOptionsServer : HttpMessageHandler
    {
        internal List<JsonElement> Downloads { get; } = [];
        internal List<JsonElement> SubtitleDownloads { get; } = [];
        internal int Searches { get; private set; }
        internal string LastSearch { get; private set; } = "";
        internal TaskCompletionSource<bool>? PendingChoices { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Require(request.RequestUri!.Host == "moviepilot.invalid", "资源选项探针拒绝真实网络");
            var path = request.RequestUri.PathAndQuery;
            if (path.EndsWith("login/access-token", StringComparison.Ordinal)) return Json(new { access_token = "fixture" });
            if (request.Method == HttpMethod.Post && path.EndsWith("/download/subtitle", StringComparison.Ordinal))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                SubtitleDownloads.Add(body.RootElement.Clone());
                return Json(new { success = true, data = new { files = new[] { "/subtitles/episode.srt" } } });
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/download/", StringComparison.Ordinal))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Downloads.Add(body.RootElement.Clone());
                return Json(new { success = true });
            }
            Require(request.Method == HttpMethod.Get, "未登记的资源写入请求");
            if (path.Contains("/download/", StringComparison.Ordinal) && PendingChoices is { } pending) await pending.Task;
            if (path.EndsWith("download/clients", StringComparison.Ordinal)) return Json(new[] { new { name = "下载器甲" }, new { name = "下载器乙" } });
            if (path.EndsWith("download/paths", StringComparison.Ordinal)) return Json(new[] { new { save_path = "/downloads" }, new { save_path = "rclone:/media/动画" } });
            if (path.Contains("/site/", StringComparison.Ordinal)) return Json(new[] { new { id = 1, name = "站点甲", is_active = true }, new { id = 2, name = "站点乙", is_active = true } });
            if (path.Contains("/search/subtitle/", StringComparison.Ordinal)) return Json(new[] { new { title = "第一集 简体中文字幕", site_name = "站点甲", language = "简中", site = 1,
                enclosure = "https://moviepilot.invalid/subtitle?signature=fixture", season_episode = "S01E01", description = "离线字幕夹具" } });
            if (path.Contains("/search/", StringComparison.Ordinal))
            {
                Searches++;
                LastSearch = path;
                return Json(new[] { Resource("测试剧集 S01E01 1080p WEB-DL-GROUP", "站点甲", 1, 1, 0),
                    Resource("测试剧集 S01E01 1080p WEB-DL-GROUP", "站点乙", 1, 1, 1),
                    Resource("测试剧集 S02E02 1080p WEB-DL-GROUP", "站点甲", 2, 2, 0),
                    Resource("测试剧集 S01E03 2160p WEB-DL-OTHER", "站点丙", 1, 3, 0.5) });
            }
            throw new InvalidOperationException("未登记的资源读取请求");
        }
        private static object Resource(string title, string site, int season, int episode, double factor) => new
        {
            torrent_info = new
            {
                title,
                site_name = site,
                size = 2147483648L,
                seeders = 12,
                downloadvolumefactor = factor,
                page_url = "https://site.invalid/" + site + "/" + episode
            },
            meta_info = new { begin_season = season, begin_episode = episode, resource_pix = "1080p" },
            media_info = new { title = "测试剧集", type = "电视剧", media_source = "themoviedb", media_id = "42" }
        };
    }
}
