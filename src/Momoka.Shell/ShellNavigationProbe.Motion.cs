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
        try
        {
            ResizeProbeClient(1000, 720);
            await MotionProbeEvidence.RunRoutesAsync(["video-library", "music-library"], async route =>
            {
                var measurements = new Dictionary<string, IReadOnlyList<MotionMarkerFrame>>();
                MotionCandidateDecision? decision = null;
                MotionDisableDispatchRead? disableDispatch = null;
                Exception? failure = null;
                var anonymousReadsVerified = false;
                try
                {
                    using var fixture = CreateFixture();
                    await fixture.SelectAsync();
                    var music = route == "music-library";
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
                    measurements.Add("suppress", control);
                    var native = await SampleMotionPageAsync(pageType, request, route, "entrance", control[^1]);
                    measurements.Add("entrance", native);
                    var disabled = await SampleMotionPageAsync(pageType, request, route, "disable-in-flight", control[^1],
                        dispatch => disableDispatch = dispatch);
                    measurements.Add("disable-in-flight", disabled);
                    var history = await SampleMotionPageAsync(pageType, request, route, "forward-disabled", control[^1]);
                    measurements.Add("forward-disabled", history);
                    decision = MotionProbeEvidence.Evaluate(control, native, disabled, history, disableDispatch);
                    Require(fixture.Actions.Plays == 0 && fixture.Transport.Requests.All(value => value.StartsWith("GET ", StringComparison.Ordinal)),
                        "动效候选发生真实播放或写入");
                    anonymousReadsVerified = true;
                    Require(decision.Outcome != "inconclusive", "原生候选缺少有效实帧证据：" + decision.Reason);
                }
                catch (Exception error)
                {
                    failure = error;
                    throw;
                }
                finally
                {
                    var result = new
                    {
                        route,
                        decision,
                        disableDispatch,
                        control = measurements.GetValueOrDefault("suppress"),
                        native = measurements.GetValueOrDefault("entrance"),
                        disabled = measurements.GetValueOrDefault("disable-in-flight"),
                        history = measurements.GetValueOrDefault("forward-disabled"),
                        failed = failure is not null,
                        failureType = failure?.GetType().Name,
                        failureMessage = failure?.Message,
                        nativeMigration = false,
                        systemPreferenceChangeTested = false,
                        sharedLibraryServices = "Emby anonymous fixture only",
                        markerOnlyContentTiming = true,
                        anonymousReadsVerified,
                        realNetwork = false,
                        playback = false
                    };
                    results.Add(result);
                    await WritePerformanceEvidenceAsync("motion-" + route + "-result.json", result);
                    if (failure is not null) Log.Warn("动效候选", route + "：失败证据已保存，继续下一路", failure);
                    else Log.Info("动效候选", route + "：" + decision!.Outcome + "；" + decision.Reason);
                }
            });
        }
        finally
        {
            HomeMotion.ApplyPreference(original);
            await WritePerformanceEvidenceAsync("motion-page-transitions.json", results);
        }
    }

    private async Task<IReadOnlyList<MotionMarkerFrame>> SampleMotionPageAsync(Type pageType, LibraryRequest request,
        string route, string trial, MotionMarkerFrame? controlFinal,
        Action<MotionDisableDispatchRead>? dispatchResult = null)
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
            Width = 2,
            Height = 2,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false,
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
        var captureTimings = new List<MotionCaptureTiming>();
        var uiFrames = new List<object>();
        var navigationEvents = new List<object>();
        Border? marker = null;
        Page? target = null;
        var clock = new Stopwatch();
        var collecting = false;
        var historyDisabled = false;
        var initialEntranceObserved = false;
        var forwardSuppressObserved = false;
        Exception? captureError = null;
        Exception? layoutError = null;
        Exception? trialFailure = null;
        CompositionPlaybackProbe.MotionScreenFrame? evidence = null;
        CompositionPlaybackProbe.MotionScreenFrame? lastCaptured = null;
        var capturedRegions = new List<object>();
        using var samplingStop = new CancellationTokenSource();
        MotionCaptureRegion? captureRegion = null;
        Task? sampler = null;
        var finished = false;
        var hwnd = _window!.Handle;
        var dispatcher = _root.DispatcherQueue;
        var disable = new MotionDisableDispatch(action => dispatcher.TryEnqueue(action.Invoke), () =>
        {
            HomeMotion.ApplyPreference(false);
            frame.ContentTransitions.Clear();
        }, () => clock.Elapsed.TotalMilliseconds);
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
                Require(initialEntranceObserved, "历史候选初次Navigating没有实际页面、原参数与Entrance；见navigationEvents");
                await Task.Delay(800);
                Require(frame.CanGoBack, "原生候选没有真实返回记录");
                // WinUI main 9402930972f25cfcad82abc9f82ecab29936f404:
                // dxaml/xcp/dxaml/lib/Frame_Partial.cpp GoBackWithTransitionInfoImpl ->
                // SetNavigationTransitionInfoOverrideImpl 先更新当前PageStackEntry，再由
                // NavigationHistory::CommitNavigation 把它压入ForwardStack。此源码用于解释；
                // 本次2.3.9的实际历史与Navigating字段才是运行证据，不直接写只读属性。
                frame.GoBack(new SuppressNavigationTransitionInfo());
                await LayoutAsync(frame);
                var entry = frame.ForwardStack.Count > 0 ? frame.ForwardStack[^1] : null;
                navigationEvents.Add(new
                {
                    stage = "history-after-back-suppress",
                    frame.CanGoForward,
                    forwardStackCount = frame.ForwardStack.Count,
                    sourcePageType = entry?.SourcePageType.FullName,
                    pageTypeMatches = entry?.SourcePageType == pageType,
                    parameterType = entry?.Parameter?.GetType().FullName,
                    sameRequest = ReferenceEquals(entry?.Parameter, request),
                    transitionType = entry?.NavigationTransitionInfo?.GetType().FullName
                });
                Require(frame.CanGoForward, "GoBack(Suppress)后CanGoForward为false；见navigationEvents");
                if (entry is null) throw new InvalidOperationException("GoBack(Suppress)后ForwardStack为空；不能伪造GoForward");
                Require(entry.SourcePageType == pageType, "历史前进页面类型变化：" + entry.SourcePageType?.FullName);
                Require(ReferenceEquals(entry.Parameter, request), "历史前进不再携带原request对象：" + entry.Parameter?.GetType().FullName);
                Require(entry.NavigationTransitionInfo is SuppressNavigationTransitionInfo,
                    "GoBack(Suppress)没有给实际前进条目留下Suppress：" + (entry.NavigationTransitionInfo?.GetType().FullName ?? "null"));
                HomeMotion.ApplyPreference(false);
                frame.ContentTransitions.Clear();
                historyDisabled = true;
            }
            CompositionTarget.Rendering += OnRendering;
            CompositionTarget.Rendered += OnRendered;
            collecting = true;
            NavigationTiming.Click("motion-" + route + "-" + trial);
            clock.Start();
            timer.Start();
            // 专用线程只捕获这个HWND、读取不可变区域与字节；UI线程只发布区域和事件时间。
            // 与Rendered解耦后，即使页面加载阻塞UI仍能观察系统合成器正在呈现的动效。
            sampler = MotionScreenSampler.RunAsync(() => CompositionPlaybackProbe.CaptureMotionFrame(hwnd,
                Stopwatch.GetTimestamp(), Volatile.Read(ref captureRegion)), OnCaptured,
                () => clock.Elapsed.TotalMilliseconds, 800, 16, samplingStop.Token);
            if (trial == "forward-disabled") frame.GoForward();
            else Require(frame.Navigate(pageType, request, trial == "suppress"
                ? new SuppressNavigationTransitionInfo() : new EntranceNavigationTransitionInfo()), "动效候选目标导航失败");
            await sampler.WaitAsync(TimeSpan.FromSeconds(4));
            if (layoutError is not null) throw new InvalidOperationException("动效实际标记区域测量失败", layoutError);
            finished = true;
            disable.ThrowIfFailed();
            Require(target is not null && target.IsLoaded && ReferenceEquals(frame.Content, target)
                && target.IsHitTestVisible, "原生候选末态页面未加载或不可命中");
            Require(trial != "forward-disabled" || forwardSuppressObserved,
                "真实GoForward的Navigating未携带原页面、原参数与Suppress；见navigationEvents");
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
        catch (Exception error)
        {
            trialFailure = error;
            throw;
        }
        finally
        {
            collecting = false;
            timer.Stop();
            CompositionTarget.Rendering -= OnRendering;
            CompositionTarget.Rendered -= OnRendered;
            frame.Navigating -= OnNavigating;
            frame.Navigated -= OnNavigated;
            disable.Stop();
            samplingStop.Cancel();
            // 不让迟到后台捕获读已释放窗口或写下一轮证据；捕获失败也继续保存这一轮日志。
            if (sampler is not null)
                try { await sampler; }
                catch (Exception error) { captureError = error; }
            var dispatchRead = disable.Snapshot;
            dispatchResult?.Invoke(dispatchRead);
            try
            {
                try
                {
                    // 失败图与最后一次记录来自同一份byte[]，不重抓已恢复的窗口来替代失败现场。
                    if (trialFailure is not null && lastCaptured is not null)
                        await lastCaptured.SaveAsync(Path.Combine(_options.Paths.LogDirectory, $"motion-{route}-{trial}-failed-screen.png"));
                }
                finally
                {
                    await WritePerformanceEvidenceAsync($"motion-{route}-{trial}.json", new
                    {
                        route,
                        trial,
                        frames = samples,
                        captureTimings,
                        capturedRegions,
                        uiFrames,
                        captureRegion = Volatile.Read(ref captureRegion),
                        disableDispatch = dispatchRead,
                        captureError = captureError?.GetType().Name,
                        captureMessage = captureError?.Message,
                        layoutError = layoutError?.GetType().Name,
                        layoutMessage = layoutError?.Message,
                        failureType = trialFailure?.GetType().Name,
                        failureMessage = trialFailure?.Message,
                        navigationEvents,
                        initialEntranceObserved,
                        forwardSuppressObserved,
                        afterDisable = historyDisabled || dispatchRead.AppliedAtMs is not null,
                        finished,
                        retainedFailureScreen = trialFailure is not null && lastCaptured is not null,
                        nativeIntermediateScreenshot = evidence is not null,
                        nativeIntermediateCrop = evidence?.Bounds,
                        retainedFailureCrop = trialFailure is not null ? lastCaptured?.Bounds : null,
                        actualScreenRoiCapture = true,
                        markerCoordinates = "original client pixels; PNG is actual captured crop",
                        readAfterRendered = false,
                        backgroundContinuousCapture = true,
                        oneToOnePresentationFrameProved = false,
                        rootMarker = marker?.Parent is Grid,
                        baselineMotion = "Suppress + BrowseMotion.None (control isolating native transition cost)",
                        presentationEvents = sink.Snapshot(),
                        heartbeatIntervalMs = 16,
                        process = Environment.ProcessId
                    });
                }
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

        void OnNavigating(object sender, NavigatingCancelEventArgs args)
        {
            navigationEvents.Add(new
            {
                stage = "navigating",
                mode = args.NavigationMode.ToString(),
                sourcePageType = args.SourcePageType.FullName,
                pageTypeMatches = args.SourcePageType == pageType,
                parameterType = args.Parameter?.GetType().FullName,
                sameRequest = ReferenceEquals(args.Parameter, request),
                transitionType = args.NavigationTransitionInfo?.GetType().FullName
            });
            if (trial == "forward-disabled" && args.SourcePageType == pageType && ReferenceEquals(args.Parameter, request))
            {
                if (args.NavigationMode == NavigationMode.New)
                    initialEntranceObserved = args.NavigationTransitionInfo is EntranceNavigationTransitionInfo;
                if (args.NavigationMode == NavigationMode.Forward)
                    forwardSuppressObserved = args.NavigationTransitionInfo is SuppressNavigationTransitionInfo;
            }
            presentation.Begin(!HomeMotion.AnimationsEnabled || args.NavigationMode == NavigationMode.Back || trial == "suppress"
                ? BrowseMotion.None : BrowseMotion.Native);
        }

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
            if (collecting) uiFrames.Add(new { stage = "Rendered", atMs = clock.Elapsed.TotalMilliseconds });
        }

        void OnRendering(object? sender, object args)
        {
            if (!collecting) return;
            uiFrames.Add(new { stage = "Rendering", atMs = clock.Elapsed.TotalMilliseconds });
            // 根标记在布局之后、系统提交之前量一次ROI；后台不读XAML，也不跟着动画位置移动搜索框。
            if (captureRegion is not null || marker is null || marker.ActualWidth <= 0 || marker.ActualHeight <= 0) return;
            try
            {
                var scale = _root.XamlRoot.RasterizationScale;
                var bounds = marker.TransformToVisual(_root).TransformBounds(new Rect(0, 0, marker.ActualWidth, marker.ActualHeight));
                var client = _window.ClientSize;
                Volatile.Write(ref captureRegion, new MotionCaptureRegion(new(
                    Math.Max(0, (int)Math.Floor((bounds.Left - 16) * scale)),
                    Math.Max(0, (int)Math.Floor((bounds.Top - 96) * scale)),
                    Math.Min(client.Width - 1, (int)Math.Ceiling((bounds.Right + 16) * scale)),
                    Math.Min(client.Height - 1, (int)Math.Ceiling((bounds.Bottom + 96) * scale))), clock.Elapsed.TotalMilliseconds));
            }
            catch (Exception error)
            {
                layoutError = error;
                samplingStop.Cancel();
            }
        }

        void OnCaptured(CompositionPlaybackProbe.MotionScreenFrame captured, MotionCaptureTiming timing)
        {
            captureTimings.Add(timing);
            lastCaptured = captured;
            capturedRegions.Add(new
            {
                timing.Index,
                captured.Bounds,
                captured.Pixels.Width,
                captured.Pixels.Height,
                captured.Pixels.Stable,
                regionMeasuredAtMs = captured.Region?.MeasuredAtMs
            });
            var region = captured.Region;
            if (region is null) return; // 未完成真实布局时不从整页绿色噪声猜标记。
            disable.ThrowIfFailed();
            var applied = disable.Snapshot.AppliedAtMs;
            // 捕获跨过关闭动作的一帧仍算关闭前；下一次真正开始的捕获才计AfterDisable。
            var afterDisable = historyDisabled || applied is { } at && at <= timing.StartMs;
            var pixels = captured.Pixels;
            var read = MotionProbeEvidence.ReadCrop(pixels.Bytes, pixels.Width, pixels.Height, pixels.Stable,
                timing.EndMs, afterDisable, captured.Bounds);
            samples.Add(read);
            if (controlFinal is not null && read.Visible && !MotionProbeEvidence.SamePresentation(read, controlFinal))
            {
                if (trial == "entrance" && evidence is null) evidence = captured;
                if (trial == "disable-in-flight") disable.Request();
            }
            else if (trial == "disable-in-flight" && afterDisable && evidence is null) evidence = captured;
        }
    }
}
