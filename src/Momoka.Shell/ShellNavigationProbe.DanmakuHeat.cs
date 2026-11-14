using System.Diagnostics;
using System.Globalization;
using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Configuration;
using Momoka.Danmaku;
using Momoka.Diagnostics;
using Momoka.Infrastructure;
using Momoka.Mpv;
using Momoka.Playback;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Windows.Foundation;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>
    /// 正式集成页面 + 正式 LoadDanmakuAsync/偏好事件/悬停入口，只替换网络和在播句柄边界。
    /// 不改产品控件，不复制 Gallery 交互；该匿名诊断没有对应的 Gallery 弹幕样例。
    /// </summary>
    private async Task DanmakuHeatAsync()
    {
        using var fixture = CreateFixture();
        fixture.Settings.Mpv.Pipeline = VideoPipelineKind.Integrated;
        fixture.Settings.Danmaku.Enabled = true;
        fixture.Settings.Danmaku.Source = DanmakuSourceMode.DandanPlay;
        fixture.Settings.Danmaku.Show = true;
        fixture.Settings.Danmaku.ShowHeatmap = true;
        fixture.Settings.Danmaku.AutoMatch = false;
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, "danmaku-heat"));
        paths.EnsureCreated();
        using var transport = new DanmakuHeatTransport();
        using var client = new DandanPlayClient(fixture.Settings, Path.Combine(paths.Root, "cache"), transport,
            credentials: new DanmakuProbeCredentials("offline-app", "offline-secret"));
        var shaders = new ShaderGroupResolver(fixture.Settings.Shaders);
        var playback = new PlaybackService(fixture.Session, fixture.Settings,
            () => throw new InvalidOperationException("热力检查禁止创建真实播放后端"),
            new PlaybackPlanner(fixture.Settings, shaders, paths.ShaderCacheDirectory, paths.ScreenshotDirectory), allowPlayback: false);
        var vm = new PlayerViewModel(playback, fixture.SettingsService, fixture.Session, fixture.Images,
            shaders, fixture.Dispatcher, client);
        vm.SetTransportProbeStatus(new PlayerStatus { Position = 150, Duration = 1200, Loaded = true }, []);
        // 仅借匿名句柄满足正式加载入口的在播守卫；媒体后端工厂始终拒绝。
        Field(playback, "_current", new DanmakuProbeHandle());
        vm.Title = "弹幕热力 · 匿名加载与悬停检查";
        var page = new PlayerPage { IsHitTestVisible = false };
        page.Attach(vm, new ShellPage(), _window!);
        // 正式偏好通知初始化来源身份；后面的热力开关不会被误判成首次连接变化。
        vm.ChangeDanmakuPreferences(settings => settings.Show = true);
        _root.Children.Add(page);
        var readings = new List<string>();
        var originalTheme = ThemeHost.Current.Id;
        var originalMotion = HomeMotion.PreferenceEnabled;
        var originalTopMost = _window!.TopMost;
        HomeMotion.ApplyPreference(false);
        _window.TopMost = true;
        try
        {
            var track = Get<Grid>(page, "SeekTrack");
            var heat = Get<Canvas>(page, "DanmakuHeat");
            var peek = Get<TextBlock>(page, "DanmakuHeatPeek");
            var children = track.Children;
            Require(ReferenceEquals(heat.Parent, track)
                && children.IndexOf(Get<FrameworkElement>(page, "SeekSlider")) < children.IndexOf(heat)
                && children.IndexOf(heat) < children.IndexOf(Get<Canvas>(page, "CacheBar"))
                && children.IndexOf(heat) < children.IndexOf(Get<Canvas>(page, "TimelineSections")),
                "热力实际视觉树没有处在滑杆之上、缓存和章节色块之下");
            Require(!heat.IsHitTestVisible && new FrameworkElementAutomationPeer(heat).GetAutomationId() == "DanmakuHeat"
                && new FrameworkElementAutomationPeer(peek).GetAutomationId() == "DanmakuHeatPeek",
                "热力装饰抢输入，或现有 x:Name 无法稳定定位");
            ResizeProbeClient(1200, 720);
            await LayoutAsync(page);
            page.ShowChromeForShot(null);
            await LayoutAsync(page);
            RequireClear("initial-empty");
            await vm.LoadDanmakuAsync(new(730001, "匿名热力夹具", "140条"));
            await LayoutAsync(page);
            Require(vm.DanmakuHeatmap.Total == 140 && transport.Reads == 1, "正式加载没有产生140条热力快照");
            foreach (var width in new[] { 1200, 760 })
            {
                ResizeProbeClient(width, 720);
                await LayoutAsync(page);
                page.ShowChromeForShot(null);
                await LayoutAsync(page);
                // PlayerPage直接挂在_root，没有外壳侧栏占宽；控件彼此同宽不能证明窗口真的到了请求档位。
                DanmakuHeatProbeEvidence.RequireWidth(width, page.ActualWidth, _window.ClientSize.Width,
                    _root.XamlRoot.RasterizationScale);
                Call(page, "UpdateTimelineHover", 0d, 0d);
                Call(page, "HideChapterPeek");
                RequireBars("loaded-" + width);
                vm.ChangeDanmakuPreferences(settings => settings.ShowHeatmap = false);
                await LayoutAsync(page);
                RequireClear("heat-off-" + width);
                await WaitForDiagnosticsFrameAsync();
                var before = CompositionPlaybackProbe.CaptureResizeFrame(_window.Handle, Stopwatch.GetTimestamp());
                vm.ChangeDanmakuPreferences(settings => settings.ShowHeatmap = true);
                await LayoutAsync(page);
                RequireBars("heat-restored-" + width);
                await WaitForDiagnosticsFrameAsync();
                var after = CompositionPlaybackProbe.CaptureResizeFrame(_window.Handle, Stopwatch.GetTimestamp());
                Require(before.Stable && after.Stable && before.Width == after.Width && before.Height == after.Height,
                    "热力实屏前后客户区尺寸不稳定");
                var bounds = track.TransformToVisual(_root).TransformBounds(new Rect(0, 0, track.ActualWidth, track.ActualHeight));
                var scale = _root.XamlRoot.RasterizationScale;
                var pixels = DanmakuHeatProbeEvidence.ChangedPixels(before.Bytes, after.Bytes, after.Width, after.Height,
                    (int)Math.Ceiling(bounds.Left * scale), (int)Math.Ceiling(bounds.Top * scale),
                    (int)Math.Floor(bounds.Right * scale), (int)Math.Floor(bounds.Bottom * scale));
                readings.Add($"{width}: actual-client={after.Width}x{after.Height};scale={scale};visible-heat-pixels={pixels}");
                if (pixels < 20)
                {
                    await before.SaveAsync(Path.Combine(_options.Paths.LogDirectory, $"danmaku-heat-hidden-failed-{width}-screen.png"));
                    await after.SaveAsync(Path.Combine(_options.Paths.LogDirectory, $"danmaku-heat-visible-failed-{width}-screen.png"));
                }
                Require(pixels >= 20, "热力属性虽有柱子，实际轨道实屏没有足够的可见颜色变化");
                Call(page, "UpdateTimelineHover", track.ActualWidth * 0.25, track.ActualHeight / 2);
                await LayoutAsync(page);
                Require(peek.Visibility == Visibility.Visible && peek.Text == "此处约 41 条弹幕"
                    && vm.DanmakuHeatCaption == peek.Text && Get<Border>(page, "ChapterPeek").Visibility == Visibility.Visible,
                    "正式章节悬停入口没有在真实文本控件显示41条读数");
                Require(Descendants(Get<Border>(page, "ChapterPeek")).Contains(peek), "弹幕读数没有住在实际章节预览里");
                await SaveProbeScreenAsync($"danmaku-heat-{width}-screen.png");
                Call(page, "HideChapterPeek");
                Require(peek.Visibility == Visibility.Collapsed && vm.DanmakuHeatCaption is null, "离开预览后残留热力读数");
                vm.ChangeDanmakuPreferences(settings => settings.Show = false);
                await LayoutAsync(page);
                RequireClear("show-off-" + width);
                vm.ChangeDanmakuPreferences(settings => settings.Show = true);
                await LayoutAsync(page);
                RequireBars("show-restored-" + width);
            }

            // 过滤与外观只改变弹幕OSD；热力仍统计原加载数据且不多发HTTP。
            var snapshot = vm.DanmakuHeatmap;
            vm.ChangeDanmakuPreferences(settings =>
            {
                settings.BlockedWords = "热力夹具";
                settings.Color = settings.Top = settings.Bottom = false;
            });
            await LayoutAsync(page);
            Require(ReferenceEquals(snapshot, vm.DanmakuHeatmap) && snapshot.Total == 140 && transport.Reads == 1,
                "类型/颜色/屏蔽设置重算了不该变的热力数据或重复下载");
            foreach (var theme in Momoka.Theming.UiThemes.All)
            {
                ThemeHost.Apply(theme.Id);
                await LayoutAsync(page);
                RequireBars("theme-" + theme.Id);
            }
            await vm.LoadDanmakuAsync(new(730002, "匿名空弹幕夹具", "零条"));
            await LayoutAsync(page);
            RequireClear("loaded-empty");
            Require(vm.DanmakuHeatmap.IsEmpty && transport.Reads == 2, "成功零条没有清掉旧快照");
            await vm.LoadDanmakuAsync(new(730003, "匿名加载失败夹具", "404"));
            await LayoutAsync(page);
            RequireClear("load-failed");
            Require(vm.DanmakuStatus.Contains("HTTP 404", StringComparison.Ordinal) && transport.Reads == 3,
                "失败请求没有报告错误或保留了旧热力");
            Require(fixture.Actions.Plays == 0 && fixture.Transport.Requests.Count == 0
                && transport.Writes == 0 && !fixture.SettingsService.HasUnsavedChanges,
                "热力检查触发真实媒体入口、媒体服务器请求、写请求或未保存的偏好");

            void RequireBars(string stage)
            {
                var brush = page.Resources["PlayerDanmakuHeatBrush"];
                var bars = heat.Children.OfType<Border>().Select(bar => new DanmakuHeatBarReading(
                    Canvas.GetLeft(bar), Canvas.GetTop(bar), bar.Width, bar.Height,
                    ReferenceEquals(bar.Background, brush))).ToArray();
                readings.Add($"{stage}: track={track.ActualWidth:F2}x{track.ActualHeight:F2};canvas={heat.ActualWidth:F2}x{heat.ActualHeight:F2};bars={bars.Length};total={vm.DanmakuHeatmap.Total}");
                Require(vm.DanmakuHeatVisible && heat.Visibility == Visibility.Visible
                    && Math.Abs(heat.ActualWidth - track.ActualWidth) < 0.1 && Math.Abs(heat.ActualHeight - track.ActualHeight) < 0.1
                    && heat.Children.Count == bars.Length && DanmakuHeatProbeEvidence.Matches(vm.DanmakuHeatmap,
                        track.ActualWidth, track.ActualHeight, bars), "热力实际柱子数量/底对齐/最高60%轨高/宽度/画刷不符：" + stage);
                // XAML 布局按设备像素取整；属性判据仍严格，实际画面尺寸最多容纳一个DIP取整差。
                Require(heat.Children.OfType<Border>().All(bar => bar.ActualWidth > 0 && bar.ActualHeight > 0
                    && Math.Abs(bar.ActualWidth - bar.Width) <= 1 && Math.Abs(bar.ActualHeight - bar.Height) <= 1),
                    "热力柱子的实际布局尺寸没有对应声明尺寸：" + stage);
            }

            void RequireClear(string stage)
            {
                readings.Add($"{stage}: visible={heat.Visibility};children={heat.Children.Count}");
                Require(heat.Visibility == Visibility.Collapsed && heat.Children.Count == 0, "关闭或空数据仍有热力柱子：" + stage);
            }
        }
        finally
        {
            try { File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, "danmaku-heat.txt"), readings); }
            finally
            {
                await DanmakuHeatProbeEvidence.ShutdownAndRestoreAsync(async () =>
                {
                    Field(playback, "_current", null);
                    try
                    {
                        page.Detach();
                        page.Shutdown();
                    }
                    finally
                    {
                        _root.Children.Remove(page);
                        await vm.ShutdownAsync();
                    }
                }, () => ThemeHost.Apply(originalTheme), () => HomeMotion.ApplyPreference(originalMotion),
                    () => _window.TopMost = originalTopMost);
            }
        }
    }

    private sealed class DanmakuHeatTransport : HttpMessageHandler
    {
        internal int Reads { get; private set; }
        internal int Writes { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get) Writes++;
            Require(request.Method == HttpMethod.Get && request.RequestUri?.Host == "api.dandanplay.net",
                "热力夹具只接受内存GET，不转发真实网络或写请求");
            Reads++;
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/730003", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            if (path.EndsWith("/730002", StringComparison.Ordinal))
                return Task.FromResult(Json(new { comments = Array.Empty<object>() }));
            Require(path.EndsWith("/730001", StringComparison.Ordinal), "热力夹具收到未知读取路径");
            var comments = Enumerable.Range(0, 100).Select(index => new
            {
                p = $"{index * 12 + 2},1,255,anonymous",
                m = "热力夹具"
            }).Concat(Enumerable.Range(0, 40).Select(index => new
            {
                p = (300 + index * 0.005).ToString(CultureInfo.InvariantCulture) + ",5,255,anonymous",
                m = "热力夹具"
            })).ToArray();
            return Task.FromResult(Json(new { comments }));
        }
    }
}
