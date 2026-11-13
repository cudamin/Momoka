using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task ScrollMusicWallAsync(ScrollView scroll, bool tail, string key)
    {
        await LayoutAsync(scroll);
        await WaitForDiagnosticsFrameAsync();
        var requested = tail ? scroll.ScrollableHeight : 0;
        var before = MusicScrollState(scroll);
        var completed = Pending<bool>();
        var seen = new HashSet<int>();
        var correlation = -1;
        var trace = new List<object>();
        void Record(string stage) => trace.Add(new { stage, current = MusicScrollState(scroll) });
        void OnCompleted(ScrollView sender, ScrollingScrollCompletedEventArgs args)
        {
            seen.Add(args.CorrelationId);
            Record("completed-" + args.CorrelationId);
            if (args.CorrelationId == correlation) completed.TrySetResult(true);
        }
        void OnChanged(ScrollView sender, object args)
        {
            if (trace.Count < 40) Record("changed");
        }
        scroll.ScrollCompleted += OnCompleted;
        scroll.ViewChanged += OnChanged;
        try
        {
            // 虚拟布局在滚尾时可重估extent，平台会把旧请求夹到新的最大offset。
            // 等真实correlation完成，随后核当前端点和首/末项；不改变共享静态offset helper。
            Record("before");
            correlation = scroll.ScrollTo(0, requested,
                new ScrollingScrollOptions(ScrollingAnimationMode.Disabled, ScrollingSnapPointsMode.Ignore));
            if (correlation != -1 && !seen.Contains(correlation))
                await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await UntilAsync(() => scroll.State == ScrollingInteractionState.Idle);
            await WaitForDiagnosticsFrameAsync();
            scroll.UpdateLayout();
            MusicArtworkProbeRules.EndOffset(requested, scroll.ScrollableHeight, scroll.VerticalOffset, tail);
        }
        finally
        {
            await WritePerformanceEvidenceAsync("music-scroll-" + key + ".json", new
            {
                requested,
                correlation,
                before,
                after = MusicScrollState(scroll),
                correlationCompleted = correlation == -1 || seen.Contains(correlation),
                trace
            });
            scroll.ScrollCompleted -= OnCompleted;
            scroll.ViewChanged -= OnChanged;
        }
    }

    private async Task ScrollMusicTracksAsync(ListView list, int index, string key)
    {
        var scroll = Descendants(list).OfType<ScrollViewer>().FirstOrDefault()
            ?? throw new InvalidOperationException("音乐曲目没有原生ScrollViewer");
        var trace = new List<object>();
        void Record(string stage)
        {
            var panel = list.ItemsPanelRoot as ItemsStackPanel;
            trace.Add(new
            {
                stage,
                offset = scroll.VerticalOffset,
                extent = scroll.ExtentHeight,
                viewport = scroll.ViewportHeight,
                cacheLength = panel?.CacheLength,
                firstCache = panel?.FirstCacheIndex,
                lastCache = panel?.LastCacheIndex,
                firstVisible = panel?.FirstVisibleIndex,
                lastVisible = panel?.LastVisibleIndex,
                rawContainers = Enumerable.Range(0, list.Items.Count).Count(value => list.ContainerFromIndex(value) is not null)
            });
        }
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? sender, ScrollViewerViewChangedEventArgs args)
        {
            if (trace.Count < 40) Record(args.IsIntermediate ? "intermediate" : "view-complete");
            if (!args.IsIntermediate) done.TrySetResult();
        }
        scroll.ViewChanged += OnChanged;
        try
        {
            Record("before");
            list.ScrollIntoView(list.Items[index]);
            await LayoutAsync(list);
            await WaitForDiagnosticsFrameAsync();
            if (!IsVisible()) await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await UntilAsync(IsVisible);
            await WaitForDiagnosticsFrameAsync();
            Record("after");
        }
        finally
        {
            await WritePerformanceEvidenceAsync("music-tracks-scroll-" + key + ".json", new { index, trace });
            scroll.ViewChanged -= OnChanged;
        }

        bool IsVisible()
        {
            if (list.ContainerFromIndex(index) is not FrameworkElement element) return false;
            var point = element.TransformToVisual(scroll).TransformPoint(new());
            return point.Y + element.ActualHeight > 0 && point.Y < scroll.ViewportHeight;
        }
    }

    private async Task<CardItem> FocusMusicItemAsync(ItemsView wall, int index, string expected, string key)
    {
        var scroll = wall.ScrollView ?? throw new InvalidOperationException("音乐墙没有原生ScrollView");
        var repeater = Descendants(wall).OfType<ItemsRepeater>().Single();
        await UntilAsync(() => repeater.TryGetElement(index) is not null);
        var element = (FrameworkElement)repeater.TryGetElement(index)!;
        var card = Descendants(element).OfType<Image>().Select(CardArtwork.GetItem).FirstOrDefault(value => value is not null)
            ?? throw new InvalidOperationException("音乐项没有实际x:Bind图片身份");
        var before = element.TransformToVisual(scroll).TransformPoint(new());
        var beforeScroll = MusicScrollState(scroll);
        var brings = new HashSet<int>();
        var completions = new HashSet<int>();
        void OnBringing(ScrollView sender, ScrollingBringingIntoViewEventArgs args) => brings.Add(args.CorrelationId);
        void OnCompleted(ScrollView sender, ScrollingScrollCompletedEventArgs args) => completions.Add(args.CorrelationId);
        scroll.BringingIntoView += OnBringing;
        scroll.ScrollCompleted += OnCompleted;
        var focused = false;
        try
        {
            focused = element is Control control && control.Focus(FocusState.Keyboard);
            // Focus可能另外发BringIntoView；等帧调度、关联完成和Idle后再读实际屏内bounds。
            await WaitForDiagnosticsFrameAsync();
            await UntilAsync(() => scroll.State == ScrollingInteractionState.Idle
                && brings.All(completions.Contains));
            await WaitForDiagnosticsFrameAsync();
            var point = element.TransformToVisual(scroll).TransformPoint(new());
            MusicArtworkProbeRules.Item(expected, card.Item.Id, focused, point.Y, element.ActualHeight, scroll.ViewportHeight);
            return card;
        }
        finally
        {
            var point = element.TransformToVisual(scroll).TransformPoint(new());
            await WritePerformanceEvidenceAsync("music-focus-" + key + ".json", new
            {
                index,
                expected,
                actual = card.Item.Id,
                focused,
                beforeY = before.Y,
                afterY = point.Y,
                height = element.ActualHeight,
                beforeScroll,
                afterScroll = MusicScrollState(scroll),
                focusBrings = brings.Order().ToArray(),
                completed = completions.Order().ToArray(),
                physicalKeys = false
            });
            scroll.BringingIntoView -= OnBringing;
            scroll.ScrollCompleted -= OnCompleted;
        }
    }

    private static object MusicScrollState(ScrollView scroll) => new
    {
        offset = scroll.VerticalOffset,
        presenterOffset = scroll.ScrollPresenter?.VerticalOffset,
        extent = scroll.ExtentHeight,
        viewport = scroll.ViewportHeight,
        scrollable = scroll.ScrollableHeight,
        state = scroll.State.ToString(),
        hostVisible = scroll.XamlRoot.IsHostVisible,
        contentHeight = (scroll.Content as FrameworkElement)?.ActualHeight
    };
}
