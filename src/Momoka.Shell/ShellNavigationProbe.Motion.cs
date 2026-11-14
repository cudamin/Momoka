using System.Diagnostics;
using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Shell.Views;
using Windows.Foundation;
using Windows.UI;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>
    /// 固定 Gallery 6de91feccadfbb0a796b71caedead5a2284f730e 的
    /// Samples/PageTransition/PageTransitionPage.xaml/.xaml.cs：Frame.ContentTransitions +
    /// NavigationThemeTransition，Navigate显式Entrance。标记是实际页面根的子元素，随该页面运动。
    /// 本探针评估候选，结果不改变产品路由，也不把候选保留记成原生迁移成功。
    /// </summary>
    private async Task MotionPageTransitionsAsync()
    {
        var original = HomeMotion.PreferenceEnabled;
        var results = new List<object>();
        var inconclusive = new List<string>();
        try
        {
            ResizeProbeClient(1000, 720);
            foreach (var music in new[] { false, true })
            {
                using var fixture = CreateFixture();
                await fixture.SelectAsync();
                var route = music ? "music-library" : "video-library";
                var picture = await FixtureImageAsync(160, 160);
                var item = fixture.Session.Capture().Bind(new EmbyItem
                {
                    Id = "motion-" + route,
                    Name = "匿名动效条目",
                    Type = music ? EmbyItemType.Audio : EmbyItemType.Movie,
                    RunTimeTicks = TimeSpan.FromSeconds(120).Ticks,
                    ImageTags = new() { ["Primary"] = "motion-marker-fixture" }
                });
                fixture.Transport.Reply = (request, _) =>
                {
                    Require(request.Method == HttpMethod.Get, "动效候选只允许匿名读取");
                    if (request.RequestUri!.AbsolutePath.Contains("/Images/", StringComparison.Ordinal))
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(picture) });
                    return Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/Items", StringComparison.Ordinal)
                        ? Json(new ItemsResult { Items = [item], TotalRecordCount = 1 }) : FakeTransport.Standard(request));
                };
                var request = new LibraryRequest
                {
                    Services = fixture.Services,
                    Source = fixture.Session.Capture(),
                    Title = "匿名媒体库动效",
                    Tag = "library-motion-" + route,
                    ParentId = "motion-library-" + route,
                    CollectionType = music ? "music" : "movies",
                    MusicRootId = music ? "motion-library-" + route : null
                };
                var pageType = music ? typeof(MusicLibraryPage) : typeof(LibraryPage);
                var control = await SampleMotionPageAsync(pageType, request, route, "suppress", null);
                var native = await SampleMotionPageAsync(pageType, request, route, "entrance", control[^1]);
                var disabled = await SampleMotionPageAsync(pageType, request, route, "disable-in-flight", control[^1]);
                var history = await SampleMotionPageAsync(pageType, request, route, "forward-disabled", control[^1]);
                var decision = MotionProbeEvidence.Evaluate(control, native, disabled, history);
                results.Add(new
                {
                    route,
                    decision,
                    control,
                    native,
                    disabled,
                    history,
                    nativeMigration = false,
                    systemPreferenceChangeTested = false,
                    sharedLibraryServices = "Emby anonymous fixture only",
                    markerOnlyContentTiming = true,
                    realNetwork = false,
                    playback = false
                });
                Log.Info("动效候选", route + "：" + decision.Outcome + "；" + decision.Reason);
                if (decision.Outcome == "inconclusive") inconclusive.Add(route + "：" + decision.Reason);
                Require(fixture.Actions.Plays == 0 && fixture.Transport.Requests.All(value => value.StartsWith("GET ", StringComparison.Ordinal)),
                    "动效候选发生真实播放或写入");
            }
            Require(inconclusive.Count == 0, "原生候选缺少有效实帧证据：" + string.Join("；", inconclusive));
        }
        finally
        {
            HomeMotion.ApplyPreference(original);
            await WritePerformanceEvidenceAsync("motion-page-transitions.json", results);
        }
    }

    private async Task<IReadOnlyList<MotionMarkerFrame>> SampleMotionPageAsync(Type pageType, LibraryRequest request,
        string route, string trial, MotionMarkerFrame? controlFinal)
    {
        var frame = new Frame { CacheSize = 2 };
        var presentation = new BrowsePresentation(frame);
        var sink = new BrowseTimingSink();
        NavigationTiming.Start(new NavigationTimingRecorder(sink));
        // 逐项沿用 Gallery 公开XAML对应的对象创建；不依赖Frame未配置的默认模板过渡。
        frame.ContentTransitions = new TransitionCollection { new NavigationThemeTransition() };
        _root.Children.Add(frame);
        // 静止Suppress对照也需要真实提交帧；红色2px心跳位于页面外，不参加绿色标记读数。
        // 所有候选使用同一16ms配置，它增加采样负担，本探针不测CPU/GPU/帧率。
        var heartbeat = new Border
        {
            Width = 2, Height = 2, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false,
            Background = new SolidColorBrush(Color.FromArgb(255, 160, 0, 0))
        };
        _root.Children.Add(heartbeat);
        var timer = _root.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(16);
        var pulse = false;
        timer.Tick += (_, _) =>
        {
            pulse = !pulse;
            ((SolidColorBrush)heartbeat.Background).Color = Color.FromArgb(255, pulse ? (byte)160 : (byte)200, 0, 0);
        };
        var samples = new List<MotionMarkerFrame>();
        Border? marker = null;
        Page? target = null;
        var clock = new Stopwatch();
        var collecting = false;
        var afterDisable = false;
        var sampling = false;
        Exception? captureError = null;
        CompositionPlaybackProbe.ResizeScreenFrame? evidence = null;
        var completed = Pending<bool>();
        var topMost = _window!.TopMost;
        var preference = HomeMotion.PreferenceEnabled;
        try
        {
            _window.TopMost = true;
            HomeMotion.ApplyPreference(true);
            Require(HomeMotion.AnimationsEnabled, "Windows已关闭动画，无法证明原生候选中段；未改系统设置");
            Require(frame.Navigate(typeof(Page), null, new SuppressNavigationTransitionInfo()), "动效对照初始页面导航失败");
            await LayoutAsync(frame); // 只用于测量之前的空源页；采样期间不调用固定150ms/300ms等待。
            frame.Navigating += OnNavigating;
            frame.Navigated += OnNavigated;
            if (trial == "forward-disabled")
            {
                Require(frame.Navigate(pageType, request, new EntranceNavigationTransitionInfo()), "历史候选首次前进失败");
                await Task.Delay(800);
                Require(frame.CanGoBack, "原生候选没有真实返回记录");
                frame.GoBack(new SuppressNavigationTransitionInfo());
                await LayoutAsync(frame);
                Require(frame.CanGoForward && frame.ForwardStack[^1].SourcePageType == pageType
                    && ReferenceEquals(frame.ForwardStack[^1].Parameter, request)
                    && frame.ForwardStack[^1].NavigationTransitionInfo is EntranceNavigationTransitionInfo,
                    "原生前进历史未保留页面、实际参数与Entrance；不能伪造GoForward");
                HomeMotion.ApplyPreference(false);
                frame.ContentTransitions.Clear();
                afterDisable = true;
            }
            CompositionTarget.Rendered += OnRendered;
            collecting = true;
            NavigationTiming.Click("motion-" + route + "-" + trial);
            clock.Start();
            timer.Start();
            if (trial == "forward-disabled") frame.GoForward();
            else Require(frame.Navigate(pageType, request, trial == "suppress"
                ? new SuppressNavigationTransitionInfo() : new EntranceNavigationTransitionInfo()), "动效候选目标导航失败");
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(4));
            if (captureError is not null) throw new InvalidOperationException("原生候选实帧捕获失败", captureError);
            Require(target is not null && target.IsLoaded && ReferenceEquals(frame.Content, target)
                && target.IsHitTestVisible, "原生候选末态页面未加载或不可命中");
            Require(samples.Count >= 3 && samples[^1].Visible, "原生候选没有有效提交帧标记");
            var stages = sink.Snapshot();
            var latest = stages.LastOrDefault(entry => entry.Stage == "browse-first-frame");
            Require(latest.RunId is not null && stages.Any(entry => entry.RunId == latest.RunId
                && entry.Stage == "browse-images-released" && entry.ElapsedMs >= latest.ElapsedMs),
                "原生候选首帧和图片门控未正常放行");
            Require(!stages.Any(entry => entry.RunId == latest.RunId && entry.Stage == "browse-motion-start"),
                "None对照或原生候选重复启动自定义页面淡入");
            if (evidence is not null)
                await evidence.SaveAsync(Path.Combine(_options.Paths.LogDirectory, $"motion-{route}-{trial}-screen.png"));
            return samples;
        }
        finally
        {
            collecting = false;
            timer.Stop();
            CompositionTarget.Rendered -= OnRendered;
            frame.Navigating -= OnNavigating;
            frame.Navigated -= OnNavigated;
            try
            {
                await WritePerformanceEvidenceAsync($"motion-{route}-{trial}.json", new
                {
                    route,
                    trial,
                    frames = samples,
                    captureError = captureError?.GetType().Name,
                    captureMessage = captureError?.Message,
                    afterDisable,
                    finished = completed.Task.IsCompletedSuccessfully && completed.Task.Result,
                    nativeIntermediateScreenshot = evidence is not null,
                    readAfterRendered = true,
                    rootMarker = marker?.Parent is Grid,
                    baselineMotion = "Suppress + BrowseMotion.None (control isolating native transition cost)",
                    presentationEvents = sink.Snapshot(),
                    heartbeatIntervalMs = 16,
                    process = Environment.ProcessId
                });
            }
            finally
            {
                (frame.Content as IShellContent)?.Release();
                presentation.Stop();
                NavigationTiming.Stop();
                frame.Content = null;
                frame.BackStack.Clear();
                frame.ForwardStack.Clear();
                _root.Children.Remove(frame);
                _root.Children.Remove(heartbeat);
                _window.TopMost = topMost;
                HomeMotion.ApplyPreference(preference);
            }
        }

        void OnNavigating(object sender, NavigatingCancelEventArgs args) => presentation.Begin(
            !HomeMotion.AnimationsEnabled || args.NavigationMode == NavigationMode.Back || trial == "suppress"
                ? BrowseMotion.None : BrowseMotion.Native);

        void OnNavigated(object sender, NavigationEventArgs args)
        {
            if (args.Content is not Page page || page.GetType() != pageType) return;
            target = page;
            var root = Get<Grid>(page, pageType == typeof(LibraryPage) ? "PageRoot" : "Root");
            if (marker?.Parent is Grid oldRoot) oldRoot.Children.Remove(marker);
            marker = new Border
            {
                Width = 96,
                Height = 28,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(60, 80, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 255, 0)),
                IsHitTestVisible = false
            };
            Canvas.SetZIndex(marker, 999);
            root.Children.Add(marker);
            presentation.Attach();
        }

        void OnRendered(object? sender, RenderedEventArgs args)
        {
            if (!collecting || sampling || marker is null || clock.Elapsed.TotalMilliseconds < 0) return;
            sampling = true;
            try
            {
                // Rendered发生后才读取该次已提交的屏幕帧；全帧一次BitBlt，不用连续GetPixel跨帧。
                var pixels = CompositionPlaybackProbe.CaptureResizeFrame(_window.Handle, Stopwatch.GetTimestamp());
                var scale = _root.XamlRoot.RasterizationScale;
                var bounds = marker.TransformToVisual(_root).TransformBounds(new Rect(0, 0, marker.ActualWidth, marker.ActualHeight));
                var area = new MotionMarkerRegion(
                    Math.Max(0, (int)Math.Floor((bounds.Left - 16) * scale)),
                    Math.Max(0, (int)Math.Floor((bounds.Top - 96) * scale)),
                    Math.Min(pixels.Width - 1, (int)Math.Ceiling((bounds.Right + 16) * scale)),
                    Math.Min(pixels.Height - 1, (int)Math.Ceiling((bounds.Bottom + 96) * scale)));
                var read = MotionProbeEvidence.Read(pixels.Bytes, pixels.Width, pixels.Height, pixels.Stable,
                    clock.Elapsed.TotalMilliseconds, afterDisable, area);
                samples.Add(read);
                if (controlFinal is not null && read.Visible && !MotionProbeEvidence.SamePresentation(read, controlFinal))
                {
                    if (trial == "entrance" && evidence is null) evidence = pixels;
                    if (trial == "disable-in-flight" && !afterDisable)
                    {
                        HomeMotion.ApplyPreference(false);
                        frame.ContentTransitions.Clear();
                        afterDisable = true;
                    }
                }
                else if (trial == "disable-in-flight" && afterDisable && evidence is null) evidence = pixels;
                if (clock.Elapsed.TotalMilliseconds >= 800)
                {
                    collecting = false;
                    completed.TrySetResult(true);
                }
            }
            catch (Exception error)
            {
                captureError = error;
                completed.TrySetResult(false);
            }
            finally { sampling = false; }
        }
    }
}
