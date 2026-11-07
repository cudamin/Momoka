using Microsoft.UI.Xaml.Controls;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task TriggerPerformancePageAsync(ScrollView scroll, Func<bool> pageArrived)
    {
        await WaitForDiagnosticsFrameAsync();
        var target = scroll.ScrollableHeight;
        var correlation = -1;
        var seen = new HashSet<int>();
        var completed = Pending<bool>();
        void OnCompleted(ScrollView sender, ScrollingScrollCompletedEventArgs args)
        {
            seen.Add(args.CorrelationId);
            if (args.CorrelationId == correlation) completed.TrySetResult(true);
        }
        scroll.ScrollCompleted += OnCompleted;
        try
        {
            correlation = scroll.ScrollTo(0, target, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
            if (correlation != -1 && !seen.Contains(correlation))
                await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // This scroll deliberately starts paging. The completed offset is not a frozen final
            // offset: appending rows can preserve an anchor and change it after this request ends.
            await UntilAsync(pageArrived);
            Require(pageArrived(), "分页滚动完成后没有接收新页");
        }
        finally { scroll.ScrollCompleted -= OnCompleted; }
    }

    private async Task SelectPerformanceSeasonAsync(DetailPage page, Momoka.Emby.EmbyItem season, ShelfStrip strip)
    {
        var scroll = Get<ScrollView>(strip, "Scroller");
        var completed = Pending<bool>();
        var trace = new List<string>();
        void Record(string stage)
        {
            if (trace.Count < 30) trace.Add($"{stage}: offset={scroll.HorizontalOffset:0.0}, target={Get<double>(strip, "_requested"):0.0}, "
                + $"leading={Get<int>(strip, "_leading")}, season={page.ViewModel.SelectedSeason?.Id}, state={scroll.State}");
        }
        void OnCompleted(ScrollView sender, ScrollingScrollCompletedEventArgs args)
        {
            Record("completed " + args.CorrelationId);
            if (page.ViewModel.SelectedSeason?.Id == season.Id
                && Math.Abs(scroll.HorizontalOffset - Get<double>(strip, "_requested")) < 1)
                completed.TrySetResult(true);
        }
        void OnChanged(ScrollView sender, object args) => Record("changed");
        scroll.ScrollCompleted += OnCompleted;
        scroll.ViewChanged += OnChanged;
        try
        {
            Record("before");
            page.SelectEpisodeSeason(season);
            Record("requested");
            await LayoutAsync(page);
            if (scroll.State != ScrollingInteractionState.Idle
                || Math.Abs(scroll.HorizontalOffset - Get<double>(strip, "_requested")) >= 1)
                await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await UntilAsync(() => scroll.State == ScrollingInteractionState.Idle
                && Math.Abs(scroll.HorizontalOffset - Get<double>(strip, "_requested")) < 1
                && page.ViewModel.SelectedSeason?.Id == season.Id);
            Record("settled");
        }
        catch (Exception error)
        {
            throw new InvalidOperationException("季菜单实际滚动未完成：" + season.Id + "\n" + string.Join("\n", trace), error);
        }
        finally
        {
            scroll.ScrollCompleted -= OnCompleted;
            scroll.ViewChanged -= OnChanged;
        }
    }
}
