using System.Collections;
using System.Reflection;
using System.Net;
using CommunityToolkit.WinUI.Controls;
using Momoka.Diagnostics;
using Momoka.Configuration;
using Momoka.Services;
using Momoka.Updates;
using System.Diagnostics;
using Momoka.Danmaku;
using Momoka.Emby;
using Momoka.FnNas;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Mpv;
using Momoka.Playback;
using Momoka.Shell.Platform;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Momoka.Shell;

/// <summary>Actual WinUI frames/dialogs/dispatcher with anonymous data; no real library or playback.</summary>
internal sealed partial class ShellNavigationProbe
{
    private async Task PerformanceSearchVisitsAsync()
    {
        using var fixture = CreateFixture(new PerformanceSearchTransport());
        await fixture.SelectAsync();
        await ConnectMoviePilotAsync(fixture);
        var service = (MoviePilotService)fixture.Services.GetService(typeof(MoviePilotService))!;
        var baseline = AvailabilitySubscriptions(service);
        var frame = new Frame();
        _root.Children.Add(frame);
        var visits = new List<object>();
        try
        {
            // 普通库页：一次 Attach 只做一次视图/分区更新，五个懒面板一个都不建。
            Require(frame.Navigate(typeof(LibraryPage),
                LibraryRequest.ForRow(fixture.Services, HomeLayout.HomeRowTarget.Resume)), "真实 Frame 媒体库导航失败");
            var normal = (LibraryPage)frame.Content!;
            await LayoutAsync(normal);
            Require(normal.ViewApplies == 1 && normal.SectionBuilds == 1,
                $"一次 Attach 应只执行一次视图和分区更新：视图 {normal.ViewApplies}、分区 {normal.SectionBuilds}");
            Require(!normal.MoviePilotPanelCreated && !normal.MoviePilotResourcesCreated && !normal.AniBtPanelCreated,
                "普通媒体库创建了未使用的搜索面板");
            Require(!normal.FilterPanelCreated && !normal.SettingsPanelCreated,
                "普通媒体库创建了未使用的筛选或设置面板");
            normal.Release();
            frame.Content = null;
            frame.BackStack.Clear();

            for (var index = 0; index < 12; index++)
            {
                Require(frame.Navigate(typeof(LibraryPage), LibraryRequest.Search(fixture.Services)), "真实 Frame 搜索导航失败");
                var page = (LibraryPage)frame.Content!;
                await LayoutAsync(page);
                Require(page.MoviePilotPanel is null, "媒体库来源尚未使用时不应创建 MoviePilot 面板");
                var tabs = Get<Segmented>(page, "SourceTabs");
                tabs.SelectedIndex = 1;
                await LayoutAsync(page);
                Require(page.MoviePilotPanelCreated && !page.MoviePilotResourcesCreated && !page.AniBtPanelCreated,
                    "切到 MoviePilot 只应创建订阅面板");
                var panel = page.MoviePilotPanel
                    ?? throw new InvalidOperationException("切到 MoviePilot 后没有创建搜索面板");
                Require(Get<bool>(panel, "_active"), "首次切到 MoviePilot 后搜索面板不在有效访问");
                tabs.SelectedIndex = 0;
                tabs.SelectedIndex = 1;
                Require(Get<bool>(panel, "_active"), "同页来源往返把搜索面板永久 Release");
                await panel.SearchAsync("离线性能回归");
                Require(panel.ViewModel.Results.Count > 0, "来源往返后不能明确提交新搜索");
                var result = panel.ViewModel.Results[0];
                var callbacks = new List<Action>();
                Field(panel.ViewModel, "_dispatch", (Action<Action>)callbacks.Add);
                fixture.Settings.MoviePilot.Enabled = false;
                Require(callbacks.Count > 0, "没有产生实际单例可用性迟到通知");
                page.Release();
                PerformanceProbeRules.ReleasedSearch(AvailabilitySubscriptions(service) - baseline, panel.ViewModel.Results.Count);
                Require(result.Poster is null, "旧搜索结果保留图片对象");
                fixture.Settings.MoviePilot.Enabled = true;
                Require(await service.RefreshAvailabilityAsync(), "匿名服务恢复失败");
                Require(frame.Navigate(typeof(LibraryPage), LibraryRequest.Search(fixture.Services)), "下一真实页面导航失败");
                var next = (LibraryPage)frame.Content!;
                await LayoutAsync(next);
                Get<Segmented>(next, "SourceTabs").SelectedIndex = 1;
                await LayoutAsync(next);
                var current = next.MoviePilotPanel
                    ?? throw new InvalidOperationException("下一真实页面切到 MoviePilot 后没有创建搜索面板");
                await current.SearchAsync("新访问查询");
                var latest = current.ViewModel.Results.ToArray();
                foreach (var callback in callbacks) callback();
                Require(current.ViewModel.Results.SequenceEqual(latest), "旧队列通知覆盖了新访问结果");
                Require(panel.ViewModel.Results.Count == 0, "已释放搜索面板被迟到通知重新填入");
                if (index == 0)
                {
                    ResizeProbeClient(1040, 800);
                    await LayoutAsync(next);
                    await SaveProbeScreenAsync("performance-search-reentered-1040-screen.png");
                    ResizeProbeClient(520, 800);
                    await LayoutAsync(next);
                    await SaveProbeScreenAsync("performance-search-reentered-520-screen.png");
                    ResizeProbeClient(1040, 800);
                }
                next.Release();
                Require(frame.CanGoBack, "真实 Frame 没有形成返回记录");
                frame.GoBack();
                var returned = (LibraryPage)frame.Content!;
                await LayoutAsync(returned);
                var returnedPanel = returned.MoviePilotPanel
                    ?? throw new InvalidOperationException("真实返回后没有创建 MoviePilot 搜索面板");
                Require(Get<bool>(returnedPanel, "_active"), "真实返回后没有重新接线");
                returned.Release();
                PerformanceProbeRules.ReleasedSearch(AvailabilitySubscriptions(service) - baseline,
                    returnedPanel.ViewModel.Results.Count);
                visits.Add(new
                {
                    visit = index + 1,
                    subscriptions = AvailabilitySubscriptions(service) - baseline,
                    sourceSwitchSearch = true,
                    lateCallbacks = callbacks.Count,
                    returnedFrame = true
                });
            }
            Require(fixture.Actions.Plays == 0, "页面检查误触发播放");
            await WritePerformanceEvidenceAsync("performance-search-visits.json", visits);
        }
        finally
        {
            (frame.Content as IShellContent)?.Release();
            frame.Content = null;
            frame.BackStack.Clear();
            _root.Children.Remove(frame);
            ResizeInspect(1420, 980);
        }
    }

    private static int AvailabilitySubscriptions(MoviePilotService service) =>
        (service.GetType().GetField("AvailabilityChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service) as Delegate)
            ?.GetInvocationList().Length ?? 0;

    private async Task PerformanceMatchCloseAsync()
    {
        var searches = new List<(CancellationToken Token, TaskCompletionSource<IReadOnlyList<FnNasScrapResult>> Reply)>();
        var dialog = new FnNasMatchDialog("匿名匹配候选", (_, _, token) =>
        {
            var reply = Pending<IReadOnlyList<FnNasScrapResult>>();
            searches.Add((token, reply));
            return reply.Task; // Deliberately ignore cancellation so late-response ownership is also exercised.
        })
        { XamlRoot = _root.XamlRoot };
        Task? readA = null;
        Task? readB = null;
        Task? shown = null;
        try
        {
            var opened = Pending<bool>();
            dialog.Opened += (_, _) => opened.TrySetResult(true);
            shown = ShowPerformanceDialogAsync(dialog);
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(3));
            readA = (Task)Call(dialog, "SearchAsync")!;
            Require(searches.Count == 1 && !searches[0].Token.IsCancellationRequested, "第一次搜索没有当前只读令牌");
            dialog.Hide();
            await shown.WaitAsync(TimeSpan.FromSeconds(3));
            Require(searches[0].Token.IsCancellationRequested, "真实关闭没有取消只读请求");
            Require(Get<ProgressBar>(dialog, "Busy").Visibility == Visibility.Collapsed, "关闭后旧读取圈仍可见");

            var reopened = Pending<bool>();
            dialog.Opened += (_, _) => reopened.TrySetResult(true);
            shown = ShowPerformanceDialogAsync(dialog);
            await reopened.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Get<TextBox>(dialog, "KeywordBox").Text = "新访问候选";
            readB = (Task)Call(dialog, "SearchAsync")!;
            Require(searches.Count == 2, "重开没有允许用户明确提交新搜索");
            searches[0].Reply.TrySetResult([new("旧候选", "tmdb", "1", "Movie", "2024-01-01")]);
            await readA.WaitAsync(TimeSpan.FromSeconds(3));
            PerformanceProbeRules.MatchingReadCanceled(searches[0].Token.IsCancellationRequested,
                Get<bool>(dialog, "_busy"), CountDialogRows(dialog));
            Require(Get<ProgressBar>(dialog, "Busy").Visibility == Visibility.Visible, "旧 finally 收起了新访问加载状态");
            searches[1].Reply.TrySetResult([new("新候选", "tmdb", "2", "Movie", "2024-01-02")]);
            await readB.WaitAsync(TimeSpan.FromSeconds(3));
            Require(CountDialogRows(dialog) == 1 && !Get<bool>(dialog, "_busy"), "新访问没有收到自己的结果");
            var row = ((IEnumerable)Get<ListView>(dialog, "Results").ItemsSource).Cast<FnNasScrapCandidate>().Single();
            Require(row.Title == "新候选", "旧访问借用了新候选表");
            await LayoutAsync(dialog);
            await WaitForDiagnosticsFrameAsync();
            await SaveProbeScreenAsync("performance-match-new-visit-screen.png");
            dialog.Hide();
            await shown.WaitAsync(TimeSpan.FromSeconds(3));
            await WritePerformanceEvidenceAsync("performance-match-close.json", new
            {
                requests = searches.Count,
                firstCanceled = searches[0].Token.IsCancellationRequested,
                secondCanceled = searches[1].Token.IsCancellationRequested,
                lateResponseRejected = true,
                reopenedResult = row.Title,
                writes = 0,
                realNetwork = false
            });
        }
        finally
        {
            dialog.Hide();
            foreach (var read in searches) read.Reply.TrySetResult([]);
            if (shown is not null) await shown.WaitAsync(TimeSpan.FromSeconds(3));
            if (readA is not null) await readA.WaitAsync(TimeSpan.FromSeconds(3));
            if (readB is not null) await readB.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    private static int CountDialogRows(FnNasMatchDialog dialog) =>
        (Get<ListView>(dialog, "Results").ItemsSource as IEnumerable)?.Cast<object>().Count() ?? 0;
    private static async Task ShowPerformanceDialogAsync(ContentDialog dialog) => await dialog.ShowAsync();

    private async Task PerformanceDiagnosticsAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, "diagnostics-performance"));
        paths.EnsureCreated();
        var playback = new PlaybackService(fixture.Session, fixture.Settings,
            () => throw new InvalidOperationException("性能探针禁止播放后端"),
            new PlaybackPlanner(fixture.Settings, new ShaderGroupResolver(fixture.Settings.Shaders),
                paths.ShaderCacheDirectory, paths.ScreenshotDirectory), allowPlayback: false);
        var queue = new PerformanceDispatcher(_root.DispatcherQueue);
        var sink = new RingBufferLogSink(80);
        var page = new DiagnosticsPage();
        _root.Children.Add(page);
        try
        {
            AttachDiagnostics(page.ViewModel);
            await page.ViewModel.ReloadAsync();
            var posts = queue.Posted;
            var callbacks = queue.Executed;
            // Writing on this UI stack prevents callbacks running until we return; Post must still hop.
            for (var index = 0; index < 10000; index++)
                sink.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Info, "性能夹具", "burst-" + index, null));
            Require(queue.Posted - posts == 1, "每条日志仍各排一个UI回调");
            await queue.IdleAsync();
            PerformanceProbeRules.OneLogWake(queue.Posted - posts, queue.Executed - callbacks,
                page.ViewModel.Rows.Count, 80, page.ViewModel.Rows.Any(row => row.Message == "burst-9999"));
            ResizeProbeClient(1100, 800);
            await LayoutAsync(page);
            await SaveProbeScreenAsync("performance-diagnostics-burst-1100-screen.png");
            ResizeProbeClient(560, 800);
            await LayoutAsync(page);
            await SaveProbeScreenAsync("performance-diagnostics-burst-560-screen.png");

            queue.Reject = true;
            sink.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Info, "性能夹具", "rejected", null));
            queue.Reject = false;
            sink.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Info, "性能夹具", "after-reject", null));
            await queue.IdleAsync();
            Require(page.ViewModel.Rows.Any(row => row.Message == "after-reject"), "队列拒绝后唤醒槽没有复位");
            var rows = page.ViewModel.Rows.Count;
            sink.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Info, "性能夹具", "old-visit", null));
            page.Release();
            await queue.IdleAsync();
            Require(page.ViewModel.Rows.Count == rows, "旧访问队列在离页后仍更新VM");
            AttachDiagnostics(page.ViewModel);
            await page.ViewModel.ReloadAsync();
            sink.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Info, "性能夹具", "new-visit", null));
            await queue.IdleAsync();
            Require(page.ViewModel.Rows.Any(row => row.Message == "new-visit"), "诊断重入没有恢复唤醒");
            Require(fixture.Actions.Plays == 0 && !playback.IsPlaying, "性能探针触发了真实播放");
            await WritePerformanceEvidenceAsync("performance-diagnostics-queue.json", new
            {
                writes = 10000,
                burstPosts = 1,
                burstCallbacks = 1,
                retainedRows = 80,
                rejectedPostRecovered = true,
                oldVisitRejected = true,
                reentered = true,
                realPlayback = false
            });
        }
        finally
        {
            page.Release();
            _root.Children.Remove(page);
            ResizeInspect(1420, 980);
        }
        void AttachDiagnostics(DiagnosticsViewModel vm) => vm.Attach(fixture.Actions, fixture.SettingsService,
            fixture.Session, fixture.Capabilities, playback, paths, queue, new DiagnosticsProbeClipboard(), fixture.Launcher, sink);
    }

    private async Task PerformanceCoverAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, "cover-performance"));
        paths.EnsureCreated();
        var shaders = new ShaderGroupResolver(fixture.Settings.Shaders);
        var playback = new PlaybackService(fixture.Session, fixture.Settings,
            () => throw new InvalidOperationException("封面性能探针禁止播放后端"),
            new PlaybackPlanner(fixture.Settings, shaders, paths.ShaderCacheDirectory, paths.ScreenshotDirectory), allowPlayback: false);
        using var danmaku = new DandanPlayClient(fixture.Settings, Path.Combine(paths.Root, "danmaku-cache"), new RejectNetwork());
        var vm = new PlayerViewModel(playback, fixture.SettingsService, fixture.Session, fixture.Images,
            shaders, new UiDispatcher(_root.DispatcherQueue), danmaku);
        var image = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(24) };
        _root.Children.Add(image);
        var original = await FixtureImageAsync(3840, 2160);
        var smaller = await FixtureImageAsync(640, 360);
        var oldRead = Pending<HttpResponseMessage>();
        var entered = Pending<bool>();
        fixture.Transport.Reply = (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (!path.Contains("/Images/", StringComparison.Ordinal)) return Task.FromResult(FakeTransport.Standard(request));
            if (path.Contains("/cover-old/", StringComparison.Ordinal))
            {
                entered.TrySetResult(true);
                return oldRead.Task.WaitAsync(token);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(path.Contains("/cover-next/", StringComparison.Ordinal) ? smaller : original) });
        };
        var scope = fixture.Session.Capture();
        EmbyItem Item(string id) => scope.Bind(new EmbyItem
        { Id = id, Name = id, Type = EmbyItemType.Movie, BackdropImageTags = ["image"] });
        Task Load(EmbyItem? item) => (Task)Call(vm, "LoadCoverBackdropAsync", item)!;
        try
        {
            var ready = Pending<bool>();
            Require(_root.DispatcherQueue.TryEnqueue(() => ready.TrySetResult(true)), "队列拒绝封面响应标记");
            var loading = Load(Item("cover-large"));
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await loading.WaitAsync(TimeSpan.FromSeconds(8));
            var frame = vm.CoverBackdropFrame ?? throw new InvalidOperationException("真实封面caller未发布BGRA帧");
            var bitmap = vm.CoverBackdrop ?? throw new InvalidOperationException("真实封面caller未发布XAML图片");
            Require(frame.Width <= CoverBackdropSize.MaxWidth && frame.Height <= CoverBackdropSize.MaxHeight
                && (long)frame.Width * frame.Height <= CoverBackdropSize.MaxPixels, "真实caller交出的帧超预算");
            Require(bitmap.DecodePixelWidth == frame.Width && bitmap.DecodePixelHeight == frame.Height,
                "XAML图片解码预算没有跟实际BGRA帧一致");
            image.Source = bitmap;
            await LayoutAsync(image);
            await UntilAsync(() => bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0);
            // BitmapSource.PixelWidth/Height expose original image metadata, not the decoded surface.
            // WinUI CImageSource::GetPixelWidth/Height return ImageMetadata width/height; explicit
            // DecodePixelWidth/Height are a separate physical-size request. Do not call metadata a peak.
            Require(bitmap.PixelWidth == 3840 && bitmap.PixelHeight == 2160,
                "XAML图片原图元数据与匿名夹具不一致");
            ResizeProbeClient(1100, 800);
            await LayoutAsync(image);
            await WaitForDiagnosticsFrameAsync();
            await SaveProbeScreenAsync("performance-cover-bounded-1100-screen.png");
            ResizeProbeClient(560, 800);
            await LayoutAsync(image);
            await SaveProbeScreenAsync("performance-cover-bounded-560-screen.png");

            var stale = Load(Item("cover-old"));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Load(Item("cover-next")).WaitAsync(TimeSpan.FromSeconds(8));
            oldRead.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(original) });
            await stale.WaitAsync(TimeSpan.FromSeconds(3));
            Require(Get<string>(vm, "_coverBackdropItemId") == Item("cover-next").IdentityKey
                && vm.CoverBackdropFrame?.Width == 640, "旧封面请求晚到覆盖新条目图");
            await Load(null);
            Require(vm.CoverBackdrop is null && vm.CoverBackdropFrame is null, "清空条目没有撤销并释放两层图片");
            await WritePerformanceEvidenceAsync("performance-cover-runtime.json", new
            {
                originalWidth = 3840,
                originalHeight = 2160,
                frameWidth = frame.Width,
                frameHeight = frame.Height,
                retainedFrameBytes = frame.Pixels.Length,
                intrinsicBitmapWidth = bitmap.PixelWidth,
                intrinsicBitmapHeight = bitmap.PixelHeight,
                requestedDecodeWidth = bitmap.DecodePixelWidth,
                requestedDecodeHeight = bitmap.DecodePixelHeight,
                xamlInternalSurfacePeakMeasured = false,
                rasterizationScale = _root.XamlRoot.RasterizationScale,
                queuedMarkerReturned = true,
                staleRejected = true,
                clearReleasesBoth = true,
                realNetwork = false,
                mediaLoaded = false
            });
        }
        finally
        {
            oldRead.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(original) });
            await Load(null);
            await vm.ShutdownAsync();
            image.Source = null;
            _root.Children.Remove(image);
            ResizeInspect(1420, 980);
        }
    }

    private async Task PerformanceSettingsAsync()
    {
        using var fixture = CreateFixture();
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, "settings-save-performance"));
        paths.EnsureCreated();
        var settings = new AppSettings();
        var store = new SettingsStore(paths, PassthroughSecretProtector.Instance);
        using var service = new SettingsService(store, settings);
        var captures = 0;
        service.ConfigureDeferredSave(work => _root.DispatcherQueue.TryEnqueue(() => { captures++; work(); }));
        using var client = new MoviePilotClient(new RejectNetwork());
        using var updates = new UpdateClient(new RejectNetwork());
        var fonts = new FontLibrary();
        Field(fonts, "_scan", Task.FromResult(FontCatalogue.Empty));
        var page = new SettingsPage();
        page.ViewModel.Attach(service, new ShaderStaging(settings), fonts, paths,
            fixture.Launcher, fixture.Startup, new AudioDeviceCatalogue(() => null), () => Task.CompletedTask,
            _ => Task.CompletedTask, () => Task.CompletedTask,
            new MoviePilotService(client, new MoviePilotCredentials(PassthroughSecretProtector.Instance), settings),
            new MoviePilotCredentials(PassthroughSecretProtector.Instance), updates, fixture.Capabilities, fixture.Images);
        _root.Children.Add(page);
        var successfulWrites = 0;
        void Saved(object? sender, EventArgs args)
        {
            if (!store.HasUnsavedChanges) Interlocked.Increment(ref successfulWrites);
        }
        store.SaveStateChanged += Saved;
        try
        {
            BeginSettingsFixtureVisit(page);
            await page.ViewModel.ReloadAsync();
            page.SelectedCategory = "字幕";
            ResizeProbeClient(1100, 800);
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
            var row = page.ViewModel.Sections.SelectMany(section => section.Rows).OfType<SettingSliderRow>()
                .Single(item => item.Label == "字号");
            var control = Descendants(page).OfType<Slider>()
                .Single(slider => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(slider) == "字号");
            var oldCapture = captures;
            var oldSuccess = Volatile.Read(ref successfulWrites);
            var watch = Stopwatch.StartNew();
            // Programmatic WinUI RangeValue updates exercise the actual TwoWay generated binding.
            // They are not pointer drags or FPS measurements; all values differ in one synchronous burst.
            var low = Math.Max(control.Minimum, 20);
            var high = Math.Min(control.Maximum, 80);
            for (var index = 0; index < 150; index++) control.Value = index % 2 == 0 ? low : high;
            watch.Stop();
            Require(row.Value == high && settings.Playback.SubtitleFontSize == (int)high,
                "正式Slider TwoWay绑定没有实时更新内存");
            Require(await service.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5)), "合并滑块最后值未排空");
            Require(captures - oldCapture == 1 && Volatile.Read(ref successfulWrites) - oldSuccess == 1,
                "150个真实控件值仍触发多次捕获或成功保存");
            Require(store.LoadStrict().Playback.SubtitleFontSize == (int)high, "实际临时文件没有最后字号");
            await page.ViewModel.FlushSettingsAsync();
            Require(!page.ViewModel.HasUnsavedChanges, "成功排空后仍显示保存欠项");
            await LayoutAsync(page);
            await SaveProbeScreenAsync("performance-settings-slider-1100-screen.png");

            // Lock only this fixture's file: failure must be visible and retry must save the live value.
            using (var locked = new FileStream(paths.SettingsFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                control.Value = low;
                Require(!await service.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5)), "真实文件锁失败被报为成功");
                page.ViewModel.SyncSaveState();
                Require(service.HasUnsavedChanges && page.ViewModel.HasUnsavedChanges,
                    "保存失败没有保留共享欠项和页面提示");
                await SaveProbeScreenAsync("performance-settings-save-failure-screen.png");
            }
            await page.ViewModel.FlushSettingsAsync();
            Require(!service.HasUnsavedChanges && store.LoadStrict().Playback.SubtitleFontSize == (int)low,
                "解锁后明确重试没有落下内存最后值");
            control.Value = high;
            page.Release();
            Require(await service.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5)), "离页仍丢了已登记的最后滑块值");
            Require(store.LoadStrict().Playback.SubtitleFontSize == (int)high, "页面释放没有排空共享保存");
            await WritePerformanceEvidenceAsync("performance-settings-runtime.json", new
            {
                updates = 150,
                captures = 1,
                successfulWrites = 1,
                synchronousControlUpdatesMs = watch.Elapsed.TotalMilliseconds,
                liveValue = high,
                diskValue = store.LoadStrict().Playback.SubtitleFontSize,
                actualXamlTwoWay = true,
                failedSaveRetainsDebt = true,
                explicitRetrySaved = true,
                releasedPageFlushed = true,
                source = "anonymous temporary SettingsStore",
                realUserSettings = false
            });
        }
        finally
        {
            store.SaveStateChanged -= Saved;
            page.Release();
            await service.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
            _root.Children.Remove(page);
            ResizeInspect(1420, 980);
        }
    }

    private Task WritePerformanceEvidenceAsync<T>(string name, T evidence) => File.WriteAllTextAsync(
        Path.Combine(_options.Paths.LogDirectory, name), System.Text.Json.JsonSerializer.Serialize(evidence,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");

    private sealed class PerformanceSearchTransport : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Require(request.RequestUri?.Host == "moviepilot.invalid", "性能搜索探针拒绝真实服务器");
            token.ThrowIfCancellationRequested();
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("login/access-token", StringComparison.Ordinal))
                return Task.FromResult(Json(new { access_token = "anonymous-fixture-token" }));
            Require(request.Method == HttpMethod.Get, "性能搜索探针不允许订阅/下载写请求");
            if (path.EndsWith("dashboard/system", StringComparison.Ordinal)) return Task.FromResult(Json(new { version = "fixture" }));
            if (path.Contains("media/source", StringComparison.Ordinal)) return Task.FromResult(SearchProbeServer.Sources());
            if (path.Contains("media/search", StringComparison.Ordinal)) return Task.FromResult(Json(new[]
            {
                new { title = "匿名测试电影", type = "电影", media_source = "themoviedb", media_id = "123", tmdb_id = 123 }
            }));
            if (path.Contains("subscribe", StringComparison.Ordinal) || path.Contains("site", StringComparison.Ordinal))
                return Task.FromResult(Json(Array.Empty<object>()));
            throw new InvalidOperationException("未登记的性能搜索只读路径");
        }
    }

    private sealed class PerformanceDispatcher(DispatcherQueue queue) : IUiDispatcher
    {
        internal int Posted { get; private set; }
        internal int Executed { get; private set; }
        internal bool Reject { get; set; }
        public bool Post(Action work)
        {
            Posted++;
            return !Reject && queue.TryEnqueue(() => { Executed++; work(); });
        }
        public void Run(Action work)
        {
            if (queue.HasThreadAccess) work();
            else Post(work);
        }
        internal async Task IdleAsync()
        {
            var done = Pending<bool>();
            Require(queue.TryEnqueue(() => done.TrySetResult(true)), "真实UI队列拒绝完成标记");
            await done.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }
}
