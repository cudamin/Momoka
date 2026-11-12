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
        try
        {
            // 共用既有helper：等待ScrollTo返回的真实correlation完成，再验证最终offset。
            await ScrollProbeAsync(scroll, requested);
            await UntilAsync(() => scroll.State == ScrollingInteractionState.Idle);
            await WaitForDiagnosticsFrameAsync();
        }
        finally
        {
            await WritePerformanceEvidenceAsync("music-scroll-" + key + ".json", new
            {
                requested,
                before,
                after = MusicScrollState(scroll)
            });
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
