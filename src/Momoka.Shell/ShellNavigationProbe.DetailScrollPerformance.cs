using System.Diagnostics;
using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>
    /// 详情页竖滚的真实帧节奏：匿名详情页、真实 ScrollView，逐帧滚一小段再折返，记录帧间隔、
    /// 回顶后横幅收起耗时与悬浮卡片是否真的收放。阅读色以前挂在每个 ViewChanged 上重画整页视觉树，
    /// 一个事件能占二十多毫秒；这条检查守住「滚动这一路只做随偏移变的事」。
    /// 只读匿名假数据，不启动播放、不连真实服务器。
    /// </summary>
    private async Task PerformanceDetailScrollAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var poster = await FixtureImageAsync(260, 375);
        var item = new EmbyItem
        {
            Id = "scroll-movie",
            Name = "滚动性能 · 长页面与演职人员",
            Type = EmbyItemType.Movie,
            Genres = ["剧情", "冒险"],
            Overview = string.Join("\n", Enumerable.Repeat("离线剧情说明，用于检验详情页滚动帧节奏与悬浮卡片。", 14)),
            ImageTags = new() { ["Primary"] = "poster", ["Backdrop"] = "backdrop" },
            BackdropImageTags = ["backdrop"],
            People = Enumerable.Range(0, 14).Select(index => new EmbyPerson
            {
                Id = "person-" + index,
                Name = "演职人员 " + index,
                Role = "角色 " + index,
                Type = "Actor",
                PrimaryImageTag = "face-" + index
            }).ToList()
        };
        fixture.Transport.Reply = (request, _) =>
        {
            Require(request.Method == HttpMethod.Get, "详情滚动探针不得写入服务器");
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(poster) });
            if (path.EndsWith("/Items/" + item.Id, StringComparison.Ordinal)) return Task.FromResult(Json(item));
            if (path.EndsWith("/Similar", StringComparison.Ordinal)) return Task.FromResult(Items(14, 14, "similar-"));
            return Task.FromResult(FakeTransport.Standard(request));
        };
        var host = new Grid { Margin = new Thickness(0, 68, 0, 0) };
        var frame = new Frame();
        host.Children.Add(frame);
        _root.Children.Add(host);
        var preference = HomeMotion.PreferenceEnabled;
        DetailPage? page = null;
        try
        {
            HomeMotion.ApplyPreference(true);
            frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, item));
            page = (DetailPage)frame.Content;
            page.AttachWindow(_window!);
            await UntilAsync(() => page.IsReady && page.ViewModel.StillImage is not null);
            await ResizeResponsiveAsync(page, 1240, 800);
            await UntilAsync(() =>
            {
                page.UpdateLayout();
                return Math.Abs(page.ActualWidth - 1240) < 1;
            });
            await LayoutAsync(page);

            var scroll = Get<ScrollView>(page, "Body");
            var header = Get<Border>(page, "StickyHeader");
            // 悬浮卡片初始是收起的（ActualWidth 为 0），不能拿它当滚动先决条件；这一页够长就能开始。
            await UntilAsync(() => scroll.ScrollableHeight > 400);
            // 图片晚到会在真正的滚动期里改变布局；先等这一页的卡片和图片都落定。
            await Task.Delay(1200);
            page.UpdateLayout();

            var intervals = new List<double>();
            var frames = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var clock = Stopwatch.StartNew();
            var last = 0L;
            var direction = 1d;
            var cycles = 0;
            var headerFlips = 0;
            var headerShown = header.Visibility == Visibility.Visible;
            var topHoldFrames = 0;
            var topHoldSince = 0L;
            var topHoldMaxMs = 0d;
            var offsetAtStart = scroll.VerticalOffset;

            void OnRendering(object? sender, object args)
            {
                var now = Stopwatch.GetTimestamp();
                if (last != 0) intervals.Add(Stopwatch.GetElapsedTime(last, now).TotalMilliseconds);
                last = now;

                var offset = scroll.VerticalOffset;
                var bottom = scroll.ScrollableHeight;
                if (offset <= 0.5) direction = 1;
                else if (offset >= bottom - 0.5) direction = -1;
                scroll.ScrollBy(0, direction * 18, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));

                // 回到页顶时横幅是按设计先播完按钮移回的关联动画再收起，所以这里只量「在同一位置停了多久」。
                // UI 线程被占住时这段会被拉长，正常应该只有动画本身的百来毫秒。
                var shown = header.Visibility == Visibility.Visible;
                if (shown != headerShown) { headerShown = shown; headerFlips++; }
                if (scroll.VerticalOffset <= 0.5 && shown)
                {
                    if (topHoldSince == 0) topHoldSince = now;
                    topHoldFrames++;
                    topHoldMaxMs = Math.Max(topHoldMaxMs, Stopwatch.GetElapsedTime(topHoldSince, now).TotalMilliseconds);
                }
                else topHoldSince = 0;

                if (scroll.VerticalOffset <= 0.5 && direction == 1) cycles++;
                if (intervals.Count >= 300 || clock.Elapsed > TimeSpan.FromSeconds(8)) frames.TrySetResult();
            }

            CompositionTarget.Rendering += OnRendering;
            try { await frames.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
            finally { CompositionTarget.Rendering -= OnRendering; }

            var collapseClock = Stopwatch.StartNew();
            await ScrollProbeAsync(scroll, 0);
            await UntilAsync(() => header.Visibility == Visibility.Collapsed);
            var topCollapseMs = collapseClock.Elapsed.TotalMilliseconds;

            var measured = intervals.Skip(5).Order().ToArray();
            double Percentile(double ratio) => measured.Length == 0 ? 0 : measured[(int)Math.Min(measured.Length - 1, Math.Floor(measured.Length * ratio))];
            var evidence = new
            {
                frames = intervals.Count,
                cycles,
                scrollable = scroll.ScrollableHeight,
                offsetAtStart,
                offsetAtEnd = scroll.VerticalOffset,
                frameP50Ms = Percentile(0.5),
                frameP95Ms = Percentile(0.95),
                frameMaxMs = measured.Length == 0 ? 0 : measured[^1],
                framesOver16Ms = measured.Count(value => value > 16.7),
                framesOver33Ms = measured.Count(value => value > 33.4),
                framesOver100Ms = measured.Count(value => value > 100),
                stickyFlips = headerFlips,
                topHoldFrames,
                topHoldMaxMs,
                topCollapseMs,
                realNetwork = false,
                playback = false
            };
            await WritePerformanceEvidenceAsync("performance-detail-scroll.json", evidence);
            Log.Info("详情滚动性能", $"帧 {evidence.frames}，p50 {evidence.frameP50Ms:0.0}ms、"
                + $"p95 {evidence.frameP95Ms:0.0}ms、最大 {evidence.frameMaxMs:0.0}ms；"
                + $"超过 33ms {evidence.framesOver33Ms} 帧；回顶后收起 {topCollapseMs:0}ms");

            Require(evidence.frames >= 60, $"滚动帧数过少：{evidence.frames}");
            Require(evidence.stickyFlips > 0, "整个滚动过程中悬浮卡片一次都没有收放");
            Require(evidence.frameP95Ms < 40,
                $"详情竖滚帧间隔 p95 为 {evidence.frameP95Ms:0.0}ms（应在 40ms 内）");
            Require(fixture.Actions.Plays == 0, "详情滚动探针发生起播");
        }
        finally
        {
            page?.Release();
            _root.Children.Remove(host);
            HomeMotion.ApplyPreference(preference);
            ResizeInspect(1420, 980);
        }
    }
}
