using System.Net;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.Views;
using Momoka.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>
    /// 手动离线试用，不是自动测试：父入口必须单独调用，不放进 CaseAsync 或全量分区。
    /// HTTP 全由内存 handler 回答；海报 URL 留空，避免 BitmapImage 绕过假传输联网。
    /// </summary>
    private async Task SearchPreviewAsync()
    {
        var moviePilot = new SearchPreviewMoviePilotServer();
        var aniBt = new SearchPreviewAniBtServer();
        using var fixture = CreateFixture(moviePilot, aniBt);
        // 连假媒体服务器也只允许 GET；默认 FakeTransport 的已看写响应不用于试用。
        fixture.Transport.Reply = (request, _) =>
        {
            Require(request.Method == HttpMethod.Get, "搜索离线试用禁止媒体服务器写入");
            if (request.RequestUri!.AbsolutePath.EndsWith("/Items", StringComparison.Ordinal))
                return Task.FromResult(Json(new
                {
                    Items = new[]
                    {
                        new EmbyItem { Id = "preview-series", Name = "离线示例 · 星光旅程", Type = EmbyItemType.Series },
                        new EmbyItem { Id = "preview-movie", Name = "离线示例 · 夏日放映室", Type = EmbyItemType.Movie }
                    },
                    TotalRecordCount = 2
                }));
            return Task.FromResult(FakeTransport.Standard(request));
        };
        await fixture.SelectAsync();
        await ConnectMoviePilotAsync(fixture);
        var service = (MoviePilotService)fixture.Services.GetService(typeof(MoviePilotService))!;
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var controls = new WrapRow { Spacing = 8, RowSpacing = 8, Margin = new Thickness(12, 40, 12, 8) };
        var frame = new Frame();
        Grid.SetRow(frame, 1);
        var footer = new WrapRow { Spacing = 12, RowSpacing = 8, Margin = new Thickness(12) };
        Grid.SetRow(footer, 2);
        var status = new TextBlock
        {
            Text = "仅假数据 · 零联网 · 不操作订阅、下载或播放",
            Style = (Style)Application.Current.Resources["EgCaptionStyle"],
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        var done = new Button { Content = "结束试用" };
        AutomationProperties.SetAutomationId(done, "SearchPreviewDone");
        footer.Children.Add(done);
        footer.Children.Add(status);
        layout.Children.Add(controls);
        layout.Children.Add(frame);
        layout.Children.Add(footer);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var idle = Task.CompletedTask;
        var busy = false;
        var stopping = false;
        void Finish()
        {
            stopping = true;
            finished.TrySetResult();
        }
        done.Click += (_, _) => Finish();
        var window = _window ?? throw new InvalidOperationException("搜索试用窗口尚未创建");
        var app = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(window.Handle));
        var previousTitle = app.Title;
        var previousTheme = ThemeHost.Current;
        var previousTopMost = window.TopMost;
        window.Closed += Finish;
        app.Title = "Momoka · 搜索离线试用";
        window.TopMost = true;
        // RunAsync 已用 Show(false, ...) 显示探针窗口；这里不创建第二扇窗口。
        _root.Children.Add(layout);
        void Add(string id, string label, Func<Task> action)
        {
            var button = new Button { Content = label };
            AutomationProperties.SetAutomationId(button, id);
            button.Click += async (_, _) =>
            {
                if (busy || stopping) return;
                busy = true;
                var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                idle = settled.Task;
                try { await action(); }
                catch (Exception error) { status.Text = "离线操作失败：" + error.GetType().Name; }
                finally { busy = false; settled.TrySetResult(); }
            };
            controls.Children.Add(button);
        }
        foreach (var theme in UiThemes.All)
            Add("SearchPreviewTheme-" + theme.Id, theme.Name, () =>
            {
                ThemeHost.Apply(theme);
                return Task.CompletedTask;
            });
        void Resize(int width)
        {
            var scale = _root.XamlRoot.RasterizationScale;
            var work = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(app.Id,
                Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
            var physicalWidth = Math.Min(work.Width, Math.Max(1, (int)Math.Round(width * scale)));
            var physicalHeight = Math.Min(work.Height, Math.Max(1, (int)Math.Round(900 * scale)));
            ResizeInspect(physicalWidth, physicalHeight);
            app.Move(new Windows.Graphics.PointInt32(work.X + (work.Width - physicalWidth) / 2,
                work.Y + (work.Height - physicalHeight) / 2));
        }
        Add("SearchPreviewWide", "宽窗口", () => { Resize(1420); return Task.CompletedTask; });
        Add("SearchPreviewNarrow", "窄窗口", () => { Resize(520); return Task.CompletedTask; });
        Add("SearchPreviewMoviePilot", "MP 断线 / 恢复", async () =>
        {
            moviePilot.Offline = !moviePilot.Offline;
            var available = await service.RefreshAvailabilityAsync();
            status.Text = available ? "假 MP 已恢复 · 搜索仍由用户提交" : "假 MP 已断线 · 恢复入口保留";
        });
        Add("SearchPreviewAniBt", "AniBT 开 / 关", () =>
        {
            fixture.Settings.MoviePilot.AniBtEnabled = !fixture.Settings.MoviePilot.AniBtEnabled;
            status.Text = fixture.Settings.MoviePilot.AniBtEnabled ? "假 AniBT 已开启" : "假 AniBT 已关闭";
            return Task.CompletedTask;
        });
        try
        {
            frame.Navigate(typeof(LibraryPage), LibraryRequest.Search(fixture.Services));
            var page = (LibraryPage)frame.Content;
            await LayoutAsync(page);
            Resize(1420);
            Get<CommunityToolkit.WinUI.Controls.Segmented>(page, "SourceTabs").SelectedIndex = 2;
            Get<AutoSuggestBox>(page, "SearchInput").Text = "离线示例";
            await page.SubmitSearchAsync("离线示例");
            await LayoutAsync(page);
            // 单独的试用回执，不产生通过计数；窗口等底部按钮，不自动操作结果卡片。
            File.WriteAllText(Path.Combine(_options.Paths.LogDirectory, "search-preview-ready.txt"),
                $"pid={Environment.ProcessId}; hwnd=0x{window.Handle.ToInt64():X}; fake-only=true");
            await finished.Task;
            await idle;
        }
        finally
        {
            window.Closed -= Finish;
            if (frame.Content is IShellContent page) page.Release();
            _root.Children.Remove(layout);
            if (window.Handle != IntPtr.Zero)
            {
                app.Title = previousTitle;
                window.TopMost = previousTopMost;
            }
            ThemeHost.Apply(previousTheme);
        }
    }

    /// <summary>只读搜索假服务。唯一允许的 POST 是现有连接 helper 的占位登录。</summary>
    private sealed class SearchPreviewMoviePilotServer : HttpMessageHandler
    {
        internal bool Offline { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(request.RequestUri!.Host == "moviepilot.invalid", "搜索试用拒绝真实 MP 目标");
            var path = request.RequestUri.AbsolutePath;
            var login = path.EndsWith("/login/access-token", StringComparison.Ordinal) && request.Method == HttpMethod.Post;
            Require(login || request.Method == HttpMethod.Get, "搜索试用禁止订阅、下载和服务器写入");
            if (Offline) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            if (login) return Task.FromResult(Json(new { access_token = "fixture-token" }));
            if (path.EndsWith("/dashboard/system", StringComparison.Ordinal)) return Task.FromResult(Json(new { version = "offline-preview" }));
            if (path.EndsWith("/site/", StringComparison.Ordinal) || path.EndsWith("/site/media/movie", StringComparison.Ordinal)
                || path.EndsWith("/site/media/tv", StringComparison.Ordinal))
                return Task.FromResult(Json(Enumerable.Range(1, 13).Select(id => new
                {
                    id,
                    name = id == 13 ? "离线站点 13 · 很长的站点名称用于检查换行与裁切" : $"离线站点 {id:00}",
                    is_active = true
                }).ToArray()));
            if (path.EndsWith("/media/source", StringComparison.Ordinal)) return Task.FromResult(SearchProbeServer.Sources());
            if (path.EndsWith("/media/search", StringComparison.Ordinal)) return Task.FromResult(Json(Media()));
            if (path.EndsWith("/search/title", StringComparison.Ordinal) || path.Contains("/search/media/", StringComparison.Ordinal))
                return Task.FromResult(Json(Resources()));
            throw new InvalidOperationException("搜索试用拒绝未登记的 MP 请求");
        }

        private static object[] Media() =>
        [
            new { title = "离线示例 · 星光旅程", type = "电视剧", media_source = "themoviedb", media_id = "42001", year = "2026", vote_average = 8.6,
                overview = "假媒体结果，仅用于搜索布局与来源切换。" },
            new { title = "离线示例 · 夏日放映室", type = "电影", media_source = "douban", media_id = "42002", year = "2024", vote_average = 7.8,
                overview = "所有卡片都是离线假数据，不提供远程海报。" },
            new { title = "离线示例 · 很长的中文片名用于检查窄窗口卡片标题与简介", type = "电视剧", media_source = "themoviedb", media_id = "42003", year = "2025", vote_average = 9.1,
                overview = "不点击订阅、下载或播放；正常切换来源、输入和筛选由人操作。" }
        ];

        private static object[] Resources() => Enumerable.Range(0, 8).Select(index => (object)new
        {
            torrent_info = new
            {
                title = $"[离线组{index % 3 + 1}] 离线示例 星光旅程 S{index % 2 + 1:00}E{index + 1:00} [{Resolution(index)}][HEVC][简繁字幕]",
                site = index + 1,
                site_name = $"离线站点 {index + 1:00}",
                size = 536870912L * (index + 1),
                seeders = index * 17,
                peers = index * 3,
                description = "离线假资源 · 多分辨率、季集、标签与做种数示例",
                pubdate = $"2026-10-{20 + index:00}T12:00:00+08:00",
                labels = index % 2 == 0 ? new[] { "简中", "繁中", "内封字幕" } : new[] { "双语", "无水印" },
                downloadvolumefactor = index % 3 == 0 ? 0 : 1,
                uploadvolumefactor = index % 2 == 0 ? 2 : 1,
                page_url = $"https://site.invalid/preview/{index}"
            },
            meta_info = new
            {
                name = "离线示例 · 星光旅程",
                begin_season = index % 2 + 1,
                begin_episode = index + 1,
                season_episode = $"S{index % 2 + 1:00}E{index + 1:00}",
                resource_pix = Resolution(index),
                video_encode = index % 2 == 0 ? "HEVC" : "AVC",
                edition = index % 3 == 0 ? "REMUX" : "WEB-DL",
                resource_type = index % 3 == 0 ? "BluRay" : "WEB-DL",
                resource_effect = index % 2 == 0 ? "HDR" : "SDR",
                resource_team = $"离线组{index % 3 + 1}",
                web_source = index % 2 == 0 ? "离线平台甲" : "离线平台乙"
            },
            media_info = new { title = "离线示例 · 星光旅程", type = "电视剧", media_source = "themoviedb", media_id = "42001" }
        }).ToArray();

        private static string Resolution(int index) => (index % 3) switch { 0 => "2160p", 1 => "1080p", _ => "720p" };
    }

    /// <summary>借用现有 AniBT 假目录/详情/RSS响应，外围再收窄为 GET 白名单。</summary>
    private sealed class SearchPreviewAniBtServer : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _inner = new(new AniBtProbeServer());
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(request.RequestUri!.Host == "anibt.net" && request.Method == HttpMethod.Get,
                "搜索试用 AniBT 只允许假只读请求");
            Require(request.RequestUri.AbsolutePath is "/api/animes/search" or "/api/public/v1/anime" or "/api/anime/groups" or "/rss/anime.xml",
                "搜索试用拒绝种子、图片及未登记的 AniBT 接口");
            return _inner.SendAsync(request, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
