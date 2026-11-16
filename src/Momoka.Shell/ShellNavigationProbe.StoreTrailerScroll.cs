using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task StoreTrailerWebScrollAsync(DetailPage page, StoreYouTubePreview preview,
        Func<Task<StoreTrailerWebRead>> readWeb, Func<bool, Task> requireNotification)
    {
        var scroll = Get<ScrollView>(page, "Body");
        var header = Get<Border>(page, "StickyHeader");
        var rows = new List<StoreTrailerScrollRead>();
        var intervals = new List<double>();
        var offsets = new List<double>();
        var viewChanges = 0;
        var stageStart = 0;
        var threshold = 0d;
        var deep = 0d;
        var idleMs = 0d;
        var collapseMs = 0d;
        var stickyShown = false;
        var negatives = 0;
        var failure = "";
        var passed = false;
        void OnChanged(ScrollView sender, object args)
        {
            viewChanges++;
            offsets.Add(sender.VerticalOffset);
        }
        scroll.ViewChanged += OnChanged;
        try
        {
            await ScrollProbeAsync(scroll, 0);
            await UntilAsync(() => scroll.State == ScrollingInteractionState.Idle && header.Visibility == Visibility.Collapsed);
            await UntilAsync(() => page.ViewModel.CastShelf is { Cards.Count: 14 }
                && page.ViewModel.SimilarShelf is { Cards.Count: 14 });
            page.UpdateLayout();
            threshold = Get<Grid>(page, "StoreTrailerContent").ActualHeight * 0.5 + Get<Grid>(page, "StoreTrailerHost").Margin.Top;
            var anchor = Get<Button>(page, "ColumnPlayButton");
            var anchorBottom = anchor.TransformToVisual(scroll).TransformPoint(default).Y + anchor.ActualHeight;
            deep = Math.Min(scroll.ScrollableHeight - 4, Math.Max(threshold + 120, anchorBottom + 40));
            Require(threshold > 24 && deep > threshold + 60 && deep > anchorBottom && preview.IsPlaying,
                "匿名实际演职人员/类似作品不足以越过预告和悬浮卡阈值");
            BeginStage();
            var initial = await CaptureAsync("top-playing");
            StoreTrailerScrollProbeEvidence.RequireBaseline(initial);

            BeginStage();
            await SweepAsync(4, threshold - 12);
            var below = await CaptureAsync("below-threshold-continuous");
            StoreTrailerScrollProbeEvidence.RequireStep(initial, below, false, 0, 0, false, false, 0, threshold - 6, 2);

            BeginStage();
            await ScrollProbeAsync(scroll, threshold + 12);
            await requireNotification(true);
            var crossed = await CaptureAsync("first-threshold-crossing");
            StoreTrailerScrollProbeEvidence.RequireStep(below, crossed, true, 0, 1, true, false, threshold + 6, deep);

            BeginStage();
            await SweepAsync(threshold + 24, deep);
            await UntilAsync(() => header.Visibility == Visibility.Visible);
            stickyShown = true;
            var sameSide = await CaptureAsync("paused-side-continuous");
            StoreTrailerScrollProbeEvidence.RequireStep(crossed, sameSide, true, 0, 0, true, false, threshold + 6, deep + 1, 2);

            BeginStage();
            var topClock = Stopwatch.StartNew();
            await ScrollProbeAsync(scroll, 0);
            await UntilAsync(() => scroll.State == ScrollingInteractionState.Idle);
            idleMs = topClock.Elapsed.TotalMilliseconds;
            await UntilAsync(() => header.Visibility == Visibility.Collapsed);
            collapseMs = topClock.Elapsed.TotalMilliseconds;
            await requireNotification(false);
            var returned = await CaptureAsync("top-resumed");
            StoreTrailerScrollProbeEvidence.RequireStep(sameSide, returned, false, 1, 0, false, false, 0, 0.5);

            BeginStage();
            Call(page, "OnStoreTrailerPause", Get<AppBarButton>(page, "StoreTrailerPause"), new RoutedEventArgs());
            await requireNotification(true);
            var userPaused = await CaptureAsync("user-paused-at-top");
            StoreTrailerScrollProbeEvidence.RequireStep(returned, userPaused, true, 0, 1, false, true, 0, 0.5, 0);
            BeginStage();
            await ScrollProbeAsync(scroll, deep);
            var userDeep = await CaptureAsync("user-paused-below-fold");
            StoreTrailerScrollProbeEvidence.RequireStep(userPaused, userDeep, true, 0, 1, true, true, threshold + 6, deep + 1);
            BeginStage();
            await ScrollProbeAsync(scroll, deep - 24);
            var userSameSide = await CaptureAsync("user-paused-same-side");
            StoreTrailerScrollProbeEvidence.RequireStep(userDeep, userSameSide, true, 0, 0, true, true, threshold + 6, deep + 1);
            BeginStage();
            await ScrollProbeAsync(scroll, 0);
            await UntilAsync(() => scroll.State == ScrollingInteractionState.Idle && header.Visibility == Visibility.Collapsed);
            var userReturned = await CaptureAsync("user-paused-returned-top");
            StoreTrailerScrollProbeEvidence.RequireStep(userSameSide, userReturned, true, 0, 0, false, true, 0, 0.5);
            BeginStage();
            Call(page, "OnStoreTrailerPause", Get<AppBarButton>(page, "StoreTrailerPause"), new RoutedEventArgs());
            await requireNotification(false);
            var restored = await CaptureAsync("restored-top-playing");
            StoreTrailerScrollProbeEvidence.RequireStep(userReturned, restored, false, 1, 0, false, false, 0, 0.5, 0);

            var measured = intervals.Skip(5).Order().ToArray();
            var p95 = measured.Length == 0 ? 0 : measured[(int)Math.Min(measured.Length - 1, Math.Floor(measured.Length * 0.95))];
            StoreTrailerScrollProbeEvidence.RequirePerformance(intervals.Count, p95, stickyShown,
                header.Visibility == Visibility.Collapsed, scroll.State == ScrollingInteractionState.Idle, scroll.VerticalOffset);
            // 失败路径使用本轮真实回读的副本，重复Pause及用户暂停回顶多发Play都必须被同一判据拒绝。
            Reject(() => StoreTrailerScrollProbeEvidence.RequireStep(crossed,
                sameSide with { Web = sameSide.Web with { PauseCalls = sameSide.Web.PauseCalls + 1 } },
                true, 0, 0, true, false, threshold + 6, deep + 1, 2));
            Reject(() => StoreTrailerScrollProbeEvidence.RequireStep(userSameSide,
                userReturned with { Web = userReturned.Web with { PlayCalls = userReturned.Web.PlayCalls + 1, Paused = false } },
                true, 0, 0, false, true, 0, 0.5));
            passed = true;
        }
        catch (Exception error) { failure = error.Message; throw; }
        finally
        {
            scroll.ViewChanged -= OnChanged;
            await WriteAsync();
        }

        void BeginStage() { stageStart = viewChanges; offsets.Clear(); }
        async Task<StoreTrailerScrollRead> CaptureAsync(string stage)
        {
            var web = await readWeb();
            var offset = scroll.VerticalOffset;
            var observed = offsets.Append(offset).ToArray();
            var row = new StoreTrailerScrollRead(stage, offset, observed.Min(), observed.Max(), viewChanges - stageStart,
                Get<bool>(page, "_storeTrailerScrollPaused"), Get<bool>(page, "_storeTrailerUserPaused"), web);
            rows.Add(row);
            await WriteAsync();
            return row;
        }

        async Task SweepAsync(double lower, double upper)
        {
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var clock = Stopwatch.StartNew();
            var frames = 0;
            var last = 0L;
            void OnRendering(object? sender, object args)
            {
                var now = Stopwatch.GetTimestamp();
                if (last != 0) intervals.Add(Stopwatch.GetElapsedTime(last, now).TotalMilliseconds);
                last = now;
                frames++;
                scroll.ScrollTo(0, lower + (upper - lower) * Math.Min(frames, 36) / 36,
                    new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
                if (frames >= 36 || clock.Elapsed > TimeSpan.FromSeconds(8)) completed.TrySetResult();
            }
            CompositionTarget.Rendering += OnRendering;
            try { await completed.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
            finally { CompositionTarget.Rendering -= OnRendering; }
            await ScrollProbeAsync(scroll, upper);
        }

        void Reject(Action check)
        {
            var rejected = false;
            try { check(); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "网页滚动补验没有拒绝本轮实际回读构造的错误API增量");
            negatives++;
        }

        Task WriteAsync()
        {
            var measured = intervals.Skip(5).Order().ToArray();
            var evidence = new
            {
                passed,
                failure,
                threshold,
                deep,
                scrollable = scroll.ScrollableHeight,
                frames = intervals.Count,
                frameP95Ms = measured.Length == 0 ? 0 : measured[(int)Math.Min(measured.Length - 1, Math.Floor(measured.Length * 0.95))],
                frameMaxMs = measured.Length == 0 ? 0 : measured[^1],
                stickyShown,
                topIdleMs = idleMs,
                topCollapseMs = collapseMs,
                topCollapsed = header.Visibility == Visibility.Collapsed,
                idle = scroll.State == ScrollingInteractionState.Idle,
                negativeControls = negatives,
                rows,
                realNetwork = false,
                mainPlayback = false,
                physicalWheel = false,
                input = "ScrollView.ScrollTo; production Body.ViewChanged -> ScrollStoreTrailer"
            };
            return File.WriteAllTextAsync(Path.Combine(_options.Paths.LogDirectory, "store-trailer-web-scroll.json"),
                JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
