using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Xaml.Controls
{
    public enum ScrollingAnimationMode { Disabled }
    public enum ScrollingInteractionState { Idle, Animation }
    public sealed record ScrollingScrollOptions(ScrollingAnimationMode Mode);
    public sealed class ScrollingScrollCompletedEventArgs(int correlation) : EventArgs { public int CorrelationId => correlation; }
    public sealed class ScrollView
    {
        public double ScrollableHeight { get; set; } = 5608;
        public double HorizontalOffset { get; set; }
        public ScrollingInteractionState State { get; set; }
        internal bool PageArrived;
        internal Action? CompleteSeason;
        public event Action<ScrollView, ScrollingScrollCompletedEventArgs>? ScrollCompleted;
        public event Action<ScrollView, object>? ViewChanged;
        public int ScrollTo(double x, double y, ScrollingScrollOptions options)
        {
            Emit(7);
            PageArrived = true;
            HorizontalOffset = x + 736; // Completed request can subsequently coexist with append anchoring.
            ViewChanged?.Invoke(this, new());
            return 7;
        }
        internal void Emit(int correlation) => ScrollCompleted?.Invoke(this, new(correlation));
    }
}

namespace Momoka.Shell.Views
{
    public sealed class ShelfStrip
    {
        internal ScrollView Scroll { get; } = new();
        internal double Requested;
        internal int Leading;
    }
    public sealed class DetailPage : Microsoft.UI.Xaml.FrameworkElement
    {
        internal CompletionVm ViewModel { get; } = new();
        internal ShelfStrip Strip { get; } = new();
        internal bool WrongSeason;
        internal void SelectEpisodeSeason(Momoka.Emby.EmbyItem season)
        {
            ViewModel.SelectedSeason = season;
            Strip.Requested = 19000;
            Strip.Scroll.State = ScrollingInteractionState.Animation;
            Strip.Scroll.CompleteSeason = () =>
            {
                Strip.Scroll.HorizontalOffset = Strip.Requested;
                Strip.Scroll.State = ScrollingInteractionState.Idle;
                Strip.Leading = 1900;
                if (WrongSeason) ViewModel.SelectedSeason = new() { Id = "old-season" };
                Strip.Scroll.Emit(8);
            };
        }
    }
    public sealed class CompletionVm { internal Momoka.Emby.EmbyItem? SelectedSeason { get; set; } }
}

namespace Momoka.Shell
{
    internal sealed partial class ShellNavigationProbe
    {
        private static TaskCompletionSource<T> Pending<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task PageCompletionForFixture(ScrollView scroll, Func<bool> arrived) => TriggerPerformancePageAsync(scroll, arrived);
        internal Task SeasonCompletionForFixture(Views.DetailPage page, Momoka.Emby.EmbyItem season) => SelectPerformanceSeasonAsync(page, season, page.Strip);
        private static Task WaitForDiagnosticsFrameAsync() => Task.CompletedTask;
        private static Task UntilAsync(Func<bool> condition)
        {
            if (!condition()) throw new InvalidOperationException("fixture condition was not fulfilled");
            return Task.CompletedTask;
        }
        private Task LayoutAsync(Views.DetailPage page)
        {
            page.Strip.Scroll.CompleteSeason?.Invoke();
            return Task.CompletedTask;
        }
        private T Get<T>(Views.ShelfStrip strip, string name) => (T)(name switch
        {
            "Scroller" => (object)strip.Scroll,
            "_requested" => strip.Requested,
            "_leading" => strip.Leading,
            _ => throw new InvalidOperationException("unexpected completion boundary")
        });
    }
}
