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
            Require(dialog.DefaultButton == ContentDialogButton.None, "下载对话框不能由普通回车触发默认提交");
            InvokeDialog(dialog, "CloseButton");
            await cancelled;
            Require(server.Downloads.Count == 0 && row.State == MoviePilotDownloadState.Idle, "取消下载仍提交或锁住行");

            var submitted = row.DownloadCommand.ExecuteAsync(null);
            dialog = await DownloadDialogAsync();
            await UntilAsync(() => !dialog.ViewModel.Busy);
            Require(dialog.ViewModel.CanSubmit && Get<StackPanel>(dialog, "DownloadAdvanced").Visibility == Microsoft.UI.Xaml.Visibility.Collapsed,
                "默认下载不应要求修改识别，高级选项应默认收起");
            server.FailSources = true;
            await dialog.ViewModel.ToggleAdvancedCommand.ExecuteAsync(null);
            Require(dialog.ViewModel.NoticeOpen, "数据源失败未显示恢复入口");
            server.FailSources = false;
            server.FailPaths = true;
            await dialog.ViewModel.RefreshCommand.ExecuteAsync(null);
            Require(dialog.ViewModel.NoticeOpen && dialog.ViewModel.NoticeTitle?.Contains("下载选项", StringComparison.Ordinal) == true,
                "刷新来源掩盖了下载目录加载失败");
            server.FailPaths = false;
            await dialog.ViewModel.RefreshCommand.ExecuteAsync(null);
            Require(dialog.ViewModel.Sources.Count == 2 && dialog.ViewModel.MediaId == "42" && dialog.ViewModel.CanSubmit,
                "刷新未重试数据源，改变原媒体身份或没有剔除音乐来源");
            Get<ComboBox>(dialog, "DownloadMediaSource").SelectedItem = dialog.ViewModel.Sources.Single(source => source.Id == "anilist");
            Require(dialog.ViewModel.MediaId.Length == 0 && !dialog.IsPrimaryButtonEnabled,
                "切换来源没有清空旧编号并等待核对");
            Get<TextBox>(dialog, "DownloadMediaId").Text = "999";
            await dialog.ViewModel.ResolveCommand.ExecuteAsync(null);
            Require(dialog.ViewModel.NoticeOpen && !dialog.IsPrimaryButtonEnabled, "错误媒体编号没有阻止下载");
            dialog.ViewModel.ResetIdentityCommand.Execute(null);
            Require(!dialog.ViewModel.NoticeOpen && dialog.ViewModel.CanSubmit && dialog.ViewModel.Options.Media is null &&
                dialog.ViewModel.Source?.Id == "themoviedb" && dialog.ViewModel.MediaId == "42", "恢复原识别未清除错误或仍携带覆盖身份");
            Get<ComboBox>(dialog, "DownloadMediaSource").SelectedItem = dialog.ViewModel.Sources.Single(source => source.Id == "anilist");
            Get<TextBox>(dialog, "DownloadMediaId").Text = "73";
            await dialog.ViewModel.ResolveCommand.ExecuteAsync(null);
            Require(dialog.ViewModel.CanSubmit && dialog.ViewModel.Options.Media?.Id == "73", "手填编号未核对为完整媒体");
            await dialog.ViewModel.ToggleAdvancedCommand.ExecuteAsync(null);
            Require(Get<StackPanel>(dialog, "DownloadAdvanced").Visibility == Microsoft.UI.Xaml.Visibility.Collapsed &&
                dialog.ViewModel.Options.Media?.Id == "73", "收起高级选项清空已核对身份");
            await dialog.ViewModel.ToggleAdvancedCommand.ExecuteAsync(null);
            dialog.ViewModel.ToggleSearchCommand.Execute(null);
            Get<TextBox>(dialog, "DownloadSearchText").Text = "核对剧集";
            await dialog.ViewModel.SearchCommand.ExecuteAsync(null);
            Require(dialog.ViewModel.Matches.Count == 1, "按片名搜索没有限定来源和类型");
            await dialog.ViewModel.SelectMatchAsync(dialog.ViewModel.Matches[0]);
            Require(dialog.ViewModel.Options.Media?.Id == "74" && !dialog.ViewModel.SearchOpen && dialog.ViewModel.CanSubmit,
                "选择片名结果没有核对并回填编号");
            Get<ComboBox>(dialog, "DownloadClient").SelectedItem = "下载器乙";
            Get<ComboBox>(dialog, "DownloadPath").SelectedItem = "rclone:/media/动画";
            await LayoutAsync(dialog);
            Require(Get<ComboBox>(dialog, "DownloadPath").Text == "rclone:/media/动画", "选择保存路径未进入编辑框");
            await SaveProbeScreenAsync("mp-download-options-screen.png");
            foreach (var width in new[] { 720, 420 })
            {
                ResizeInspect(width, 900);
                await LayoutAsync(dialog);
                await Task.Delay(250);
                Require(Get<ComboBox>(dialog, "DownloadClient").ActualWidth > 100 && Get<ComboBox>(dialog, "DownloadPath").ActualWidth > 100,
                    "窄窗压扁下载器或保存目录");
                await SaveProbeScreenAsync($"mp-download-{width}-screen.png");
            }
            ResizeInspect(720, 980);
            var originalTheme = ThemeHost.Current.Id;
            foreach (var theme in Momoka.Theming.UiThemes.All)
            {
                ThemeHost.Apply(theme.Id);
                await LayoutAsync(dialog);
                var primary = Descendants(dialog).OfType<Button>().Single(button => button.Name == "PrimaryButton");
                var close = Descendants(dialog).OfType<Button>().Single(button => button.Name == "CloseButton");
                Require(primary.Background is SolidColorBrush primaryBrush && close.Background is SolidColorBrush closeBrush &&
                    primaryBrush.Color != closeBrush.Color, "开始下载与取消使用了相同的强调底色");
                await Task.Delay(160);
                await SaveProbeScreenAsync($"mp-download-{theme.Id}-screen.png");
            }
            ThemeHost.Apply(originalTheme);
            InvokeDialog(dialog, "PrimaryButton");
            await submitted;
            Require(server.Downloads.Count == 1 && row.State == MoviePilotDownloadState.Done, "确认未提交到假下载器");
            var body = server.Downloads[0];
            Require(body.GetProperty("downloader").GetString() == "下载器乙" &&
                body.GetProperty("save_path").GetString() == "rclone:/media/动画" &&
                body.GetProperty("torrent_in").GetProperty("site_name").GetString() == "站点乙" &&
                body.GetProperty("media_in").GetProperty("media_source").GetString() == "anilist" &&
                body.GetProperty("media_in").GetProperty("media_id").GetString() == "74", "下载选项、核对媒体或跨站点资源传错");

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
        internal bool FailSources { get; set; }
        internal bool FailPaths { get; set; }
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
            if (path.EndsWith("download/paths", StringComparison.Ordinal) && FailPaths)
                return new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest);
            if (path.EndsWith("download/paths", StringComparison.Ordinal)) return Json(new[] { new { save_path = "/downloads" }, new { save_path = "rclone:/media/动画" } });
            if (path.EndsWith("media/source", StringComparison.Ordinal) && FailSources)
                return new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest);
            if (path.EndsWith("media/source", StringComparison.Ordinal)) return Json(new[]
            {
                new { name = "TMDB", media_source = "themoviedb", media_types = new[] { "电影", "电视剧" } },
                new { name = "AniList", media_source = "anilist", media_types = new[] { "电影", "电视剧" } },
                new { name = "音乐", media_source = "musicbrainz", media_types = new[] { "音乐" } }
            });
            if (path.Contains("/media/search?", StringComparison.Ordinal)) return Json(new[]
            {
                new { title = "核对剧集", type = "电视剧", media_source = "anilist", media_id = "74" },
                new { title = "错来源", type = "电视剧", media_source = "themoviedb", media_id = "74" },
                new { title = "错类型", type = "电影", media_source = "anilist", media_id = "75" }
            });
            if (path.Contains("/media/73?", StringComparison.Ordinal) || path.Contains("/media/74?", StringComparison.Ordinal))
                return Json(new { title = "核对剧集", type = "电视剧", media_source = "anilist", media_id = path.Contains("/73?", StringComparison.Ordinal) ? "73" : "74" });
            if (path.Contains("/media/999?", StringComparison.Ordinal)) return Json(new { title = "错误结果", type = "电视剧", media_source = "anilist", media_id = "998" });
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
                peers = 5,
                description = "完整剧集 / 2026 1080p / 类型：剧情 / 简体中文字幕与原声音轨",
                downloadvolumefactor = factor,
                page_url = "https://site.invalid/" + site + "/" + episode
            },
            meta_info = new { begin_season = season, begin_episode = episode, resource_pix = "1080p" },
            media_info = new { title = "测试剧集", type = "电视剧", media_source = "themoviedb", media_id = "42" }
        };
    }
}
