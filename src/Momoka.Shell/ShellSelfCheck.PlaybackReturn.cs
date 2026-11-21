using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Momoka.Shell.Views;
using Windows.Foundation;

namespace Momoka.Shell;

internal static partial class ShellSelfCheck
{
    private static Action<string>? _writePlaybackReturnTrace;
    private static PlaybackReturnTrace? _playbackReturnTrace;
    private static Action? _stopPlaybackReturnTrace;

    private static void BeginPlaybackReturnTrace(DetailPage page)
    {
        _stopPlaybackReturnTrace?.Invoke();
        if (_writePlaybackReturnTrace is not { } write) return;
        var body = page.FindName("Body") as ScrollView
            ?? throw new InvalidOperationException("详情返回读数找不到正式正文滚动视图");
        var trace = new PlaybackReturnTrace(page.PlaybackReturnReading, write, () => Environment.TickCount64);
        _playbackReturnTrace = trace;
        TypedEventHandler<ScrollView, object> view = (_, _) => trace.Record("view-changed");
        TypedEventHandler<ScrollView, object> extent = (_, _) => trace.Record("extent-changed");
        TypedEventHandler<ScrollView, object> state = (_, _) => trace.Record("state-changed");
        TypedEventHandler<ScrollView, ScrollingScrollCompletedEventArgs> completed = (_, args) =>
            trace.Record("scroll-completed", args.CorrelationId);
        PropertyChangedEventHandler ready = (_, args) =>
        {
            if (args.PropertyName is "Busy" or "IsReady") trace.Record("view-model-" + args.PropertyName);
        };
        body.ViewChanged += view;
        body.ExtentChanged += extent;
        body.StateChanged += state;
        body.ScrollCompleted += completed;
        page.ViewModel.PropertyChanged += ready;
        _stopPlaybackReturnTrace = () =>
        {
            body.ViewChanged -= view;
            body.ExtentChanged -= extent;
            body.StateChanged -= state;
            body.ScrollCompleted -= completed;
            page.ViewModel.PropertyChanged -= ready;
            _stopPlaybackReturnTrace = null;
        };
        trace.Record("before");
    }

    private static void EndPlaybackReturnTrace()
    {
        _playbackReturnTrace?.Record("final-read");
        _stopPlaybackReturnTrace?.Invoke();
        _playbackReturnTrace = null;
    }
}
