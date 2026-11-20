using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Momoka.Diagnostics;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>真实XAML岛、原生ConnectedAnimation、当前详情页面；仅匿名读请求，无播放。</summary>
    private async Task CardDetailConnectedAnimationAsync()
    {
        Log.Info("关联动画探针", "阶段：创建匿名夹具");
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var item = new EmbyItem
        {
            Id = "connected-movie",
            Name = "匿名关联动画",
            Type = EmbyItemType.Movie,
            ImageTags = new() { ["Primary"] = "poster" },
            BackdropImageTags = ["backdrop"]
        };
        var bytes = await FixtureImageAsync(260, 375);
        fixture.Transport.Reply = (request, _) =>
        {
            Require(request.Method == HttpMethod.Get, "关联动画探针只允许匿名读取");
            if (request.RequestUri!.AbsolutePath.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
            if (request.RequestUri.AbsolutePath.EndsWith("/Items/" + item.Id, StringComparison.Ordinal))
                return Task.FromResult(Json(item));
            if (request.RequestUri.AbsolutePath.EndsWith("/Items", StringComparison.Ordinal))
                return Task.FromResult(Json(new ItemsResult { Items = [item], TotalRecordCount = 1 }));
            return Task.FromResult(FakeTransport.Standard(request));
        };
        var frame = new Frame { CacheSize = 2 };
        LibraryPage? sourcePage = null;
        PosterCard? source = null;
        _root.Children.Add(frame);
        var motion = HomeMotion.PreferenceEnabled;
        var samples = new List<object>();
        var receipts = new List<CardDetailTransitionReceipt>();
        var observations = new List<Action>();
        DetailPage? target = null;
        try
        {
            ResizeProbeClient(1280, 850);
            HomeMotion.ApplyPreference(true);
            var request = new LibraryRequest
            {
                Services = fixture.Services,
                Title = "匿名关联动画库",
                ParentId = "connected-library",
                CollectionType = "movies",
                Source = fixture.Session.Capture()
            };
            Log.Info("关联动画探针", "阶段：正式导航现有LibraryPage源");
            Require(frame.Navigate(typeof(LibraryPage), request, new SuppressNavigationTransitionInfo()), "现有媒体库源Page正式导航失败");
            sourcePage = (LibraryPage)frame.Content;
            await UntilAsync(() => sourcePage.IsReady && !sourcePage.ViewModel.Busy && sourcePage.ViewModel.Cards.Count == 1);
            await LayoutAsync(sourcePage);
            var wall = Get<ItemsView>(sourcePage, "CardWall");
            Require(wall.ScrollView is not null, "真实库图片源没有滚动视口");
            await ScrollProbeAsync(wall.ScrollView!, 0);
            await UntilAsync(() =>
            {
                source = Descendants(wall).OfType<PosterCard>().FirstOrDefault(poster => poster.Card?.Item.Id == item.Id
                    && poster.ConnectedPoster.IsLoaded && poster.ConnectedPoster.ActualWidth > 0);
                return source is not null;
            });
            item = source!.Card!.Item;
            await LayoutAsync(source);
            await source.Card!.EnsurePosterAsync();
            await UntilAsync(() => source.ConnectedPoster.Source is BitmapSource { PixelWidth: > 0 } && !source.PosterArrivalRunning);
            Log.Info("关联动画探针", "阶段：真实媒体库海报已呈现，准备前进");
            Require(CardDetailTransition.Prepare(frame, source), "XAML岛不能准备当前可见视频海报关联动画");
            if (CardDetailTransition.Observe(frame, receipts.Add) is { } detach) observations.Add(detach);
            Require(frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, item), new SuppressNavigationTransitionInfo()), "匿名详情导航失败");
            target = (DetailPage)frame.Content;
            await UntilAsync(() => target.IsReady && CardDetailTransition.Started(frame));
            samples.Add(new { State = "forward", Started = true, Identity = target.ViewModel.CurrentItem?.IdentityKey, NoPlayback = fixture.Actions.Plays == 0 });
            await UntilAsync(() => receipts.Any(receipt => receipt.Direction == "forward" && receipt.Completed));
            Require(receipts.Count(receipt => receipt.Direction == "forward" && receipt.Terminal) == 1,
                "海报前进未得到唯一终态");
            Require(receipts.Where(receipt => receipt.Direction == "forward").All(receipt => receipt.ElapsedMs >= 0 && receipt.PrepareToStartMs is >= 0),
                "海报前进缺少单调准备到启动计时");
            Log.Info("关联动画探针", "阶段：原生前进完成，准备正式GoBack");
            // 原生回执先落盘；截图沿用探针的临时置顶/还原路径，仍由实屏捕获核对遮挡。
            await WritePerformanceEvidenceAsync("card-detail-connected.json", new { samples, receipts, RealNetwork = false, MediaLoaded = false });
            await SaveProbeScreenAsync("card-detail-connected-forward-screen.png");

            // 原始源已离树：仅尝试返回，不能为了动画强制恢复或创建源容器。
            var returnPrepared = CardDetailTransition.BeginReturn(target);
            Require(returnPrepared && frame.CanGoBack, "同图关联动画不能准备正式返回");
            frame.GoBack(new SuppressNavigationTransitionInfo());
            await LayoutAsync(sourcePage);
            await UntilAsync(() => !sourcePage.ViewModel.Busy);
            Require(Descendants(Get<ItemsView>(sourcePage, "CardWall")).OfType<PosterCard>().Any(poster => ReferenceEquals(poster, source)),
                "库缓存返回没有恢复原实际PosterCard");
            if (returnPrepared) CardDetailTransition.CompleteReturn(frame);
            await UntilAsync(() => receipts.Any(receipt => receipt.Direction == "return" && (receipt.Completed || receipt.TimedOut || !receipt.Started)));
            Require(ReferenceEquals(frame.Content, sourcePage), "Frame缓存返回没有恢复原源Page");
            Require(receipts.Any(receipt => receipt.Direction == "return" && receipt.Started && receipt.Completed), "正式GoBack没有完成原生返回关联动画");
            Require(receipts.Count(receipt => receipt.Direction == "return" && receipt.Terminal) == 1,
                "海报返回未得到唯一终态");
            Require(receipts.Where(receipt => receipt.Direction == "return").All(receipt => receipt.ElapsedMs >= 0 && receipt.PrepareToStartMs is >= 0),
                "海报返回缺少独立准备到启动计时");
            samples.Add(new
            {
                State = "return",
                Prepared = returnPrepared,
                FrameRestored = true,
                FrameGoBack = true,
                TryStart = receipts.Any(receipt => receipt.Direction == "return" && receipt.Started),
                Completed = receipts.Any(receipt => receipt.Direction == "return" && receipt.Completed),
                TimeoutFallback = receipts.Any(receipt => receipt.Direction == "return" && receipt.TimedOut)
            });
            Log.Info("关联动画探针", "阶段：原生正式返回完成，核对普通回退");
            CardDetailTransition.Cancel(frame);

            HomeMotion.ApplyPreference(false);
            Require(!CardDetailTransition.Prepare(frame, source), "关动画仍准备关联动画");
            samples.Add(new { State = "motion-off", Prepared = false });
            HomeMotion.ApplyPreference(true);
            var image = source.ConnectedPoster.Source;
            source.ConnectedPoster.Source = null;
            Require(!CardDetailTransition.Prepare(frame, source), "缺源图片仍准备关联动画");
            source.ConnectedPoster.Source = image;
            samples.Add(new { State = "missing-source", Prepared = false });

            // 原生前进可选动效失败：仍正式导航到详情，700ms普通回退，随后正常GoBack。
            Require(CardDetailTransition.Prepare(frame, source), "前进失败回退测试不能准备");
            if (CardDetailTransition.Observe(frame, receipts.Add) is { } detachForwardFailure) observations.Add(detachForwardFailure);
            item.ImageTags["Primary"] = "replacement"; // 同项主体图版本已改变，旧Primary票据必须拒绝。
            var beforeFailure = receipts.Count;
            Require(frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, item), new SuppressNavigationTransitionInfo()), "旧图票据阻止普通详情导航");
            target = (DetailPage)frame.Content;
            await UntilAsync(() => target.IsReady && receipts.Skip(beforeFailure).Any(receipt => receipt.Direction == "forward" && !receipt.Started));
            Require(!CardDetailTransition.Started(frame), "旧图前进仍报告原生成功");
            Require(receipts.Skip(beforeFailure).Any(receipt => receipt.Terminal && receipt.Reason == ConnectedMotionReason.ArtworkChanged),
                "旧图前进回退没有报告图版本变化原因");
            frame.GoBack(new SuppressNavigationTransitionInfo());
            await LayoutAsync(sourcePage);
            Require(ReferenceEquals(frame.Content, sourcePage), "前进动效回退破坏正常返回");
            item.ImageTags["Primary"] = "poster";
            samples.Add(new { State = "forward-artwork-changed", FrameNavigated = true, TryStart = false, Fallback = true, FrameGoBack = true });

            Require(CardDetailTransition.Prepare(frame, source), "取消测试不能准备");
            var beforeCancel = receipts.Count;
            if (CardDetailTransition.Observe(frame, receipts.Add) is { } detachCancel) observations.Add(detachCancel);
            CardDetailTransition.Cancel(frame);
            Require(!CardDetailTransition.IsPrepared(frame, DetailRequest.For(fixture.Services, item)), "离页取消留下旧准备");
            Require(receipts.Skip(beforeCancel).Count(receipt => receipt.Terminal) == 1
                && receipts.Skip(beforeCancel).Any(receipt => receipt.Reason == ConnectedMotionReason.NavigationCancelled),
                "离页取消没有唯一的取消原因回执");
            samples.Add(new { State = "cancelled", Prepared = false });

            // 第二次真实详情往返让已存在的源图失效：正常GoBack仍完成，关联动画必须给出普通回退回执。
            Require(CardDetailTransition.Prepare(frame, source), "失效源返回测试不能准备");
            if (CardDetailTransition.Observe(frame, receipts.Add) is { } detachFallback) observations.Add(detachFallback);
            Require(frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, item), new SuppressNavigationTransitionInfo()), "失效源测试详情导航失败");
            target = (DetailPage)frame.Content;
            await UntilAsync(() => target.IsReady && CardDetailTransition.Started(frame));
            // 该用例只验证返回缺图；先结束前进，避免把中途反向取消混进这一条失败场景。
            await UntilAsync(() => receipts.Skip(beforeCancel).Any(receipt => receipt.Direction == "forward" && receipt.Completed));
            Require(CardDetailTransition.BeginReturn(target), "失效源回退测试不能准备返回");
            source.ConnectedPoster.Source = null;
            var previousReceipts = receipts.Count;
            frame.GoBack(new SuppressNavigationTransitionInfo());
            await LayoutAsync(sourcePage);
            source.ConnectedPoster.Source = null; // Loaded缓存恢复之后显式模拟源图片已不可用。
            CardDetailTransition.CompleteReturn(frame);
            await UntilAsync(() => receipts.Skip(previousReceipts).Any(receipt => receipt.Direction == "return" && !receipt.Started));
            Require(receipts.Skip(previousReceipts).Any(receipt => receipt.Terminal && receipt.Reason == ConnectedMotionReason.SourceUnavailable),
                "返回缺图没有报告源不可用原因");
            Require(ReferenceEquals(frame.Content, sourcePage), "源图片不可用阻止了正式返回");
            samples.Add(new { State = "missing-return-source", FrameGoBack = true, FrameRestored = true, TryStart = false, Fallback = true });
            Require(fixture.Actions.Plays == 0 && fixture.Transport.Requests.All(request => request.StartsWith("GET ", StringComparison.Ordinal)),
                "关联动画研究产生播放或服务器写入");
            await WritePerformanceEvidenceAsync("card-detail-connected.json", new { samples, receipts, RealNetwork = false, MediaLoaded = false });
            Log.Info("关联动画探针", "阶段：成功与回退回执已保存");
        }
        finally
        {
            CardDetailTransition.Cancel(frame);
            foreach (var detach in observations) detach();
            target?.Release();
            sourcePage?.Release();
            source?.Card?.AbandonPoster();
            frame.Content = null;
            _root.Children.Remove(frame);
            HomeMotion.ApplyPreference(motion);
        }
        await HomeCardDetailConnectedAnimationAsync();
    }

    /// <summary>同一项关联检查补主页真实 Click → Shell 导航，不能用直接 Prepare 代替入口接线。</summary>
    private async Task HomeCardDetailConnectedAnimationAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        fixture.Settings.Ui.ShowHomeBanner = false;
        fixture.Settings.Ui.AnimationsEnabled = true;
        var library = fixture.Session.Capture().Bind(new EmbyItem
        {
            Id = "connected-home-library",
            Name = "匿名主页库",
            Type = EmbyItemType.CollectionFolder,
            CollectionType = "movies"
        });
        // 现场失败来自聚合主页的非默认在线来源：保留甲账号，再让乙成为默认。
        await fixture.SelectAsync("connected-default.invalid", "default");
        Require(library.Source is { IsAvailable: true, IsCurrent: false }, "主页夹具没有建立非默认在线来源");
        var item = new EmbyItem
        {
            Id = "connected-home-movie",
            Name = "匿名主页竖海报",
            Type = EmbyItemType.Movie,
            ImageTags = new() { ["Primary"] = "poster" }
        };
        var bytes = await CoverMarkedImageAsync(260, 375);
        fixture.Transport.Reply = (request, _) =>
        {
            Require(request.Method == HttpMethod.Get, "主页关联检查拒绝服务器写入");
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
            if (path.EndsWith("/Items/Latest", StringComparison.Ordinal)) return Task.FromResult(Json(new[] { item }));
            if (path.EndsWith("/Items/Resume", StringComparison.Ordinal) || path.EndsWith("/Shows/NextUp", StringComparison.Ordinal))
                return Task.FromResult(Json(new ItemsResult { Items = [], TotalRecordCount = 0 }));
            if (path.EndsWith("/Items/" + item.Id, StringComparison.Ordinal)) return Task.FromResult(Json(item));
            return Task.FromResult(FakeTransport.Standard(request));
        };
        var shell = DiscoveryShell(fixture);
        fixture.Actions.CaptureOpen = shell.OpenItem;
        var frame = shell.Pages;
        var receipts = new List<CardDetailTransitionReceipt>();
        var navigations = new List<object>();
        var observations = new List<Action>();
        var framePixels = new List<(double Milliseconds, CompositionPlaybackProbe.ResizeScreenFrame Frame)>();
        var pixelReadings = new List<object>();
        var markerBounds = new List<(int Count, int Top, int Bottom, bool Valid)>();
        using var captureCancellation = new CancellationTokenSource();
        Task? captureFlight = null;
        var motion = HomeMotion.PreferenceEnabled;
        var captureWindow = _window!;
        var captureHandle = captureWindow.Handle;
        var topMost = captureWindow.TopMost;
        PosterCard? source = null;
        _root.Children.Add(shell);
        void Navigating(object sender, Microsoft.UI.Xaml.Navigation.NavigatingCancelEventArgs args)
        {
            if (source is null) return;
            navigations.Add(new
            {
                direction = args.NavigationMode.ToString(),
                prepared = CardDetailTransition.IsPrepared(frame, args.Parameter),
                returning = CardDetailTransition.Returning(frame),
                enabled = HomeMotion.AnimationsEnabled,
                sourceLoaded = source.ConnectedPoster.IsLoaded,
                sourceRoot = source.ConnectedPoster.XamlRoot is not null,
                sourceWidth = source.ConnectedPoster.ActualWidth,
                sourceHeight = source.ConnectedPoster.ActualHeight,
                primaryMatches = source.Card?.LoadedPosterArtwork == CardDetailTransition.PrimaryArtwork(source.Card!.Item),
                sourceAvailable = source.Card?.Item.Source?.IsAvailable,
                sourceCurrent = source.Card?.Item.Source?.IsCurrent
            });
            if (observations.Count == 0 && CardDetailTransition.Observe(frame, receipts.Add) is { } detach) observations.Add(detach);
        }
        frame.Navigating += Navigating;
        try
        {
            ResizeProbeClient(1280, 850);
            HomeMotion.ApplyPreference(true);
            Require(frame.Navigate(typeof(HomePage), new HomeRequest(fixture.Services, [library], null),
                new SuppressNavigationTransitionInfo()), "匿名主页不能正式导航");
            var home = (HomePage)frame.Content;
            await UntilAsync(() => home.IsReady && home.ViewModel.Shelves.Any(shelf => shelf.Cards.Any(card => card.Item.Id == item.Id)));
            await LayoutAsync(home);
            await UntilAsync(() =>
            {
                source = Descendants(home).OfType<PosterCard>().FirstOrDefault(card => card.Card?.Item.Id == item.Id
                    && CardDetailTransition.ArrangedImage(card.ConnectedPoster));
                return source is not null;
            });
            await source!.Card!.EnsurePosterAsync();
            Require(source.Card.Item.Source is { IsAvailable: true, IsCurrent: false }, "主页没有呈现预定的非默认来源海报");
            await UntilAsync(() => !source.PosterArrivalRunning);
            Button? button = null;
            for (var parent = VisualTreeHelper.GetParent(source); parent is not null; parent = VisualTreeHelper.GetParent(parent))
                if (parent is Button found) { button = found; break; }
            Require(button is not null, "主页竖海报没有生产点击按钮");
            captureWindow.TopMost = true;
            var sourceBounds = source.ConnectedPosterSurface.TransformToVisual(_root)
                .TransformBounds(new Windows.Foundation.Rect(0, 0, source.ConnectedPosterSurface.ActualWidth, source.ConnectedPosterSurface.ActualHeight));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            await WaitForDiagnosticsFrameAsync();
            framePixels.Add((clock.Elapsed.TotalMilliseconds, CompositionPlaybackProbe.CaptureResizeFrame(captureHandle, 0)));
            async Task CaptureFlightAsync()
            {
                for (var index = 0; index < 15; index++)
                {
                    await Task.Delay(35, captureCancellation.Token);
                    captureCancellation.Token.ThrowIfCancellationRequested();
                    framePixels.Add((clock.Elapsed.TotalMilliseconds,
                        CompositionPlaybackProbe.CaptureResizeFrame(captureHandle, System.Diagnostics.Stopwatch.GetTimestamp())));
                }
            }
            captureFlight = CaptureFlightAsync();
            ((IInvokeProvider)new ButtonAutomationPeer(button!).GetPattern(PatternInterface.Invoke)).Invoke();
            await UntilAsync(() => frame.Content is DetailPage { IsReady: true });
            await UntilAsync(() => CardDetailTransition.Started(frame) || receipts.Any(receipt => receipt.Terminal));
            Require(navigations.Count > 0 && CardDetailTransition.Started(frame), "主页点击没有启动关联转场");
            await UntilAsync(() => receipts.Any(receipt => receipt.Direction == "forward" && receipt.Completed));
            await captureFlight;
            await WaitForDiagnosticsFrameAsync();
            framePixels.Add((clock.Elapsed.TotalMilliseconds,
                CompositionPlaybackProbe.CaptureResizeFrame(captureHandle, System.Diagnostics.Stopwatch.GetTimestamp())));
            var target = (DetailPage)frame.Content;
            var targetBounds = target.ConnectedPosterSurface.TransformToVisual(_root)
                .TransformBounds(new Windows.Foundation.Rect(0, 0, target.ConnectedPosterSurface.ActualWidth, target.ConnectedPosterSurface.ActualHeight));
            foreach (var (milliseconds, pixels) in framePixels)
            {
                var valid = pixels.Stable && pixels.Width > 0 && pixels.Height > 0
                    && pixels.Bytes.LongLength == (long)pixels.Width * pixels.Height * 4;
                var count = 0;
                long sumX = 0, sumY = 0;
                var left = pixels.Width;
                var top = pixels.Height;
                var right = -1;
                var bottom = -1;
                for (var y = 0; valid && y < pixels.Height; y++)
                    for (var x = 0; x < pixels.Width; x++)
                    {
                        var offset = (y * pixels.Width + x) * 4;
                        if (pixels.Bytes[offset + 2] < 170 || pixels.Bytes[offset + 1] > 65 || pixels.Bytes[offset] > 65) continue;
                        count++; sumX += x; sumY += y;
                        left = Math.Min(left, x); top = Math.Min(top, y);
                        right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                    }
                pixelReadings.Add(new
                {
                    milliseconds,
                    count,
                    centerX = count > 0 ? (double)sumX / count : -1,
                    centerY = count > 0 ? (double)sumY / count : -1,
                    left,
                    top,
                    right,
                    bottom,
                    pixels.Stable,
                    valid
                });
                markerBounds.Add((count, top, bottom, valid));
            }
            await WritePerformanceEvidenceAsync("home-card-flight-pixels.json", new { sourceBounds, targetBounds, frames = pixelReadings });
            Require(markerBounds.Count == 17 && markerBounds.All(bounds => bounds.Valid), "封面采屏帧缺失或客户区不稳定");
            foreach (var index in new[] { 0, 2, 4, 6, 8, 12, 15 })
                await framePixels[index].Frame.SaveAsync(Path.Combine(_options.Paths.LogDirectory, $"home-card-flight-{index:00}-screen.png"));
            await framePixels[^1].Frame.SaveAsync(Path.Combine(_options.Paths.LogDirectory, "home-card-flight-final-screen.png"));
            var first = markerBounds[0];
            var last = markerBounds[^1];
            Require(first.Count > 100 && last.Count > 100, "封面首尾帧没有足够标记像素");
            Require(Math.Abs(first.Top - last.Top) >= 24 && Math.Abs(first.Bottom - last.Bottom) >= 24,
                "封面首尾位置不足以判断飞行");
            static bool Between(int value, int a, int b) => value >= Math.Min(a, b) + 12 && value <= Math.Max(a, b) - 12;
            Require(markerBounds.Skip(1).SkipLast(1).Any(bounds => bounds.Count > 100
                && Between(bounds.Top, first.Top, last.Top) && Between(bounds.Bottom, first.Bottom, last.Bottom)),
                "封面没有同时离开首尾位置的中间帧，不能只凭原生回执证明飞行");
            Require(fixture.Actions.Opened == 1 && fixture.Actions.Plays == 0, "主页点击没有只打开一次详情");
            await SaveProbeScreenAsync("home-card-detail-connected-forward-screen.png");
            Require(frame.CanGoBack, "主页转场没有真实返回栈");
            frame.GoBack(new SuppressNavigationTransitionInfo());
            await UntilAsync(() => ReferenceEquals(frame.Content, home) && receipts.Any(receipt => receipt.Direction == "return" && receipt.Terminal));
            Require(receipts.Any(receipt => receipt.Direction == "return" && receipt.Completed), "主页正式返回没有完成封面转场");
            Require(fixture.Actions.Plays == 0, "主页关联检查发生播放");
        }
        finally
        {
            captureCancellation.Cancel();
            try
            {
                if (captureFlight is not null)
                    try { await captureFlight; }
                    catch (OperationCanceledException) when (captureCancellation.IsCancellationRequested) { }
            }
            finally
            {
                try
                {
                    await WritePerformanceEvidenceAsync("home-card-detail-connected.json", new { navigations, receipts, realNetwork = false, mediaLoaded = false });
                }
                finally
                {
                    try
                    {
                        frame.Navigating -= Navigating;
                        CardDetailTransition.Cancel(frame);
                        foreach (var detach in observations) detach();
                        Call(shell, "ReleaseContent");
                        frame.Content = null;
                        fixture.Actions.CaptureOpen = null;
                        Get<DispatcherTimer>(shell, "_toast").Stop();
                        _root.Children.Remove(shell);
                    }
                    finally
                    {
                        try { captureWindow.TopMost = topMost; }
                        finally { HomeMotion.ApplyPreference(motion); }
                    }
                }
            }
        }
    }
}
