using Momoka.Diagnostics;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>Anonymous event collector for the actual source-loaded browsing VMs.</summary>
internal sealed class BrowseTimingCapture : INavigationTimingSink, IDisposable
{
    private readonly List<NavigationTimingEvent> _events = [];
    private readonly NavigationTimingRun _run;

    internal BrowseTimingCapture()
    {
        NavigationTiming.Start(new NavigationTimingRecorder(this));
        _run = NavigationTiming.Click("browse-fixture")!;
    }

    public void Write(NavigationTimingEvent entry) => _events.Add(entry);

    internal void RequireSpan(string name, int count)
    {
        var begin = _events.SingleOrDefault(entry => entry.Stage == name + "-begin");
        var end = _events.SingleOrDefault(entry => entry.Stage == name + "-end");
        Assert.Equal(_run.RunId, begin.RunId, name + " 缺少属于本次恢复的开始标记");
        Assert.Equal(_run.RunId, end.RunId, name + " 缺少属于本次恢复的结束标记");
        Assert.Equal(count, end.Count, name + " 的数量应来自本次实际集合");
        Assert.True(end.ElapsedMs >= begin.ElapsedMs, name + " 的顺序不正确");
    }

    public void Dispose() => NavigationTiming.Stop();
}
