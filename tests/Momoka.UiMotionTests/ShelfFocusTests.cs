using Microsoft.UI.Xaml;
using Momoka.Shell.Views;
using Windows.System;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class ShelfFocusTests
{
    internal static void Register()
    {
        Test("持续拒绝焦点只投递一次，等待下一次布局恢复", () =>
        {
            var (shelf, target) = Create();
            target.FocusAccepted = false;
            shelf.FocusAt(2);
            Assert.Equal(1, shelf.DispatcherQueue.PendingCount);
            shelf.DispatcherQueue.Drain();
            Assert.Equal(2, target.FocusCalls);
            Assert.Equal(0, shelf.DispatcherQueue.PendingCount);
            Assert.Equal(2, shelf.Pending);
            target.FocusAccepted = true;
            shelf.Layout();
            Assert.Equal(FocusState.Keyboard, target.FocusState);
            Assert.Equal(-1, shelf.Pending);
        });

        Test("容器实现失败有限重试，后来布局实现并聚焦", () =>
        {
            var shelf = new ShelfFocusFixture();
            shelf.ReplaceItems(10);
            shelf.Load();
            shelf.AssumeScrollSettled();
            shelf.Repeater.ThrowOnCreate = true;
            shelf.FocusAt(8);
            shelf.DispatcherQueue.Drain();
            Assert.Equal(2, shelf.Repeater.CreateCount);
            Assert.Equal(8, shelf.Pending);
            Assert.Equal(0, shelf.DispatcherQueue.PendingCount);
            shelf.Repeater.ThrowOnCreate = false;
            shelf.Layout();
            shelf.DispatcherQueue.Drain();
            shelf.Layout();
            Assert.Equal(-1, shelf.Pending);
            Assert.Equal(FocusState.Keyboard, shelf.Repeater.TryGetElement(8)!.FocusState);
        });

        Test("队列拒绝不锁住重试标志，尺寸更新仍能恢复", () =>
        {
            var (shelf, target) = Create();
            target.FocusAccepted = false;
            shelf.DispatcherQueue.RejectEnqueue = true;
            shelf.FocusAt(2);
            Assert.False(shelf.Queued);
            Assert.Equal(2, shelf.Pending);
            target.FocusAccepted = true;
            shelf.Layout();
            Assert.Equal(FocusState.Keyboard, target.FocusState);
        });

        Test("换来源与离页后的旧焦点回调不再触碰原容器", () =>
        {
            foreach (var replace in new[] { false, true })
            {
                var (shelf, target) = Create();
                target.FocusAccepted = false;
                shelf.FocusAt(2);
                if (replace) shelf.ReplaceItems(10);
                else shelf.Unload();
                shelf.DispatcherQueue.Drain();
                Assert.Equal(1, target.FocusCalls);
                Assert.Equal(-1, shelf.Pending);
                Assert.False(shelf.Queued);
            }
        });

        Test("连续请求合并为最后目标；旧来源回调不清新队列", () =>
        {
            var (shelf, old) = Create();
            old.FocusAccepted = false;
            shelf.FocusAt(2);
            shelf.ReplaceItems(10);
            var current = new FrameworkElement { FocusAccepted = false };
            shelf.Repeater.Prepare(4, current);
            shelf.FocusAt(4);
            Assert.Equal(2, shelf.DispatcherQueue.PendingCount);
            shelf.DispatcherQueue.DrainOne();
            Assert.True(shelf.Queued);
            shelf.DispatcherQueue.Drain();
            Assert.Equal(4, shelf.Pending);
            current.FocusAccepted = true;
            shelf.Layout();
            Assert.Equal(FocusState.Keyboard, current.FocusState);
        });

        Test("货架Home End整页与方向键保持边界，普通键不拦截", () =>
        {
            var (shelf, _) = Create();
            shelf.FocusAt(2);
            Assert.True(shelf.Key(VirtualKey.Home));
            Assert.Equal(0, shelf.Focused);
            Assert.False(shelf.Direction(FocusNavigationDirection.Left));
            Assert.True(shelf.Direction(FocusNavigationDirection.Right));
            Assert.Equal(1, shelf.Focused);
            Assert.True(shelf.Key(VirtualKey.End));
            Assert.Equal(9, shelf.Focused);
            Assert.False(shelf.Direction(FocusNavigationDirection.Right));
            Assert.True(shelf.Key(VirtualKey.PageUp));
            Assert.True(shelf.Focused < 9);
            Assert.False(shelf.Key(VirtualKey.Enter));
            Assert.False(shelf.Key(VirtualKey.Escape));
        });
        Test("同尺寸卡片滚动完成后恢复失败焦点，不等待SizeChanged", () =>
        {
            var (shelf, target) = Create();
            shelf.Scroller.ScrollSettled = false;
            target.FocusAccepted = false;
            shelf.FocusAt(2);
            shelf.DispatcherQueue.Drain();
            Assert.Equal(0, target.FocusCalls);
            Assert.Equal(2, shelf.Pending);
            target.FocusAccepted = true;
            shelf.CompleteScroll(shelf.ScrollCorrelation);
            shelf.DispatcherQueue.Drain();
            Assert.Equal(FocusState.Keyboard, target.FocusState);
            Assert.Equal(-1, shelf.Pending);
            Assert.Equal(-1, shelf.ScrollCorrelation);
        });
        Test("旧滚动完成不重试当前目标，当前完成持续拒绝也不自排队", () =>
        {
            var (shelf, target) = Create();
            shelf.Scroller.ScrollSettled = false;
            target.FocusAccepted = false;
            shelf.FocusAt(2);
            var previous = shelf.ScrollCorrelation;
            shelf.DispatcherQueue.Drain();
            var current = shelf.ScrollCorrelation;
            shelf.CompleteScroll(previous);
            Assert.Equal(0, shelf.DispatcherQueue.PendingCount);
            shelf.CompleteScroll(current);
            shelf.DispatcherQueue.Drain();
            Assert.Equal(1, target.FocusCalls);
            Assert.Equal(0, shelf.DispatcherQueue.PendingCount);
            Assert.Equal(2, shelf.Pending);
        });
        Test("已Loaded但零尺寸目标布局完成一次重试并撤掉监听", () =>
        {
            var (shelf, target) = Create();
            target.ActualWidth = 0;
            target.FocusAccepted = false;
            shelf.FocusAt(2);
            shelf.DispatcherQueue.Drain();
            Assert.Equal(1, target.LayoutSubscriptionCount);
            target.ActualWidth = 600;
            target.FocusAccepted = true;
            target.RaiseLayout();
            shelf.DispatcherQueue.Drain();
            Assert.Equal(FocusState.Keyboard, target.FocusState);
            Assert.Equal(0, target.LayoutSubscriptionCount);
            Assert.Equal(0, target.LoadedSubscriptionCount);
            target.RaiseLayout();
            Assert.Equal(0, shelf.DispatcherQueue.PendingCount);
        });
        Test("迟到Loaded恢复目标；离页换来源或新焦点取消目标监听与滚动", () =>
        {
            foreach (var cancel in new[] { "none", "unload", "replace", "focus" })
            {
                var (shelf, target) = Create();
                target.IsLoaded = false;
                target.FocusAccepted = false;
                shelf.FocusAt(2);
                shelf.DispatcherQueue.Drain();
                var correlation = shelf.ScrollCorrelation;
                if (cancel == "unload") shelf.Unload();
                if (cancel == "replace") shelf.ReplaceItems(10);
                if (cancel == "focus") shelf.TakeOtherFocus(4);
                target.FocusAccepted = true;
                target.RaiseLoaded();
                shelf.CompleteScroll(correlation);
                shelf.DispatcherQueue.Drain();
                Assert.Equal(cancel == "none" ? 1 : 0, target.FocusCalls);
                Assert.Equal(0, target.LayoutSubscriptionCount);
                Assert.Equal(0, target.LoadedSubscriptionCount);
            }
        });
        Test("新目标取消旧布局和滚动回调，重排时新请求不被旧调用覆盖", () =>
        {
            var (shelf, target) = Create();
            target.IsLoaded = false;
            target.FocusAccepted = false;
            shelf.FocusAt(2);
            var previous = shelf.ScrollCorrelation;
            var current = new FrameworkElement { FocusAccepted = false };
            shelf.Repeater.Prepare(4, current);
            shelf.FocusAt(4);
            Assert.Equal(0, target.LayoutSubscriptionCount);
            target.FocusAccepted = true;
            target.RaiseLoaded();
            shelf.CompleteScroll(previous);
            shelf.DispatcherQueue.Drain();
            Assert.Equal(0, target.FocusCalls);
            Assert.Equal(4, shelf.Pending);
            current.FocusAccepted = true;
            shelf.CompleteScroll(shelf.ScrollCorrelation);
            shelf.DispatcherQueue.Drain();
            Assert.Equal(FocusState.Keyboard, current.FocusState);
        });
        Test("新建容器先取得实际布局再聚焦，布局重入新焦点使旧请求失效", () =>
        {
            foreach (var reentrant in new[] { false, true })
            {
                var (shelf, target) = Create();
                target.FocusAccepted = false;
                target.OnUpdateLayout = () =>
                {
                    target.FocusAccepted = true;
                    if (reentrant) shelf.TakeOtherFocus(4);
                };
                shelf.FocusAt(2);
                Assert.Equal(reentrant ? 0 : 1, target.FocusCalls);
                Assert.Equal(reentrant ? 4 : 2, shelf.Focused);
                Assert.Equal(-1, shelf.Pending);
            }
        });
        Test("单次滚动完成持续拒绝且没有新滚动时不无限重试", () =>
        {
            var (shelf, target) = Create();
            target.FocusAccepted = false;
            shelf.Scroller.ReuseCurrentRequest = true;
            shelf.FocusAt(2);
            shelf.DispatcherQueue.Drain();
            shelf.CompleteScroll(shelf.ScrollCorrelation);
            shelf.DispatcherQueue.Drain();
            Assert.Equal(3, target.FocusCalls);
            Assert.Equal(0, shelf.DispatcherQueue.PendingCount);
        });
        Test("待重试目标监听实际滚动完成且不会在每个ViewChanged抢焦点", () =>
        {
            var root = AppContext.BaseDirectory;
            for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln")))
                {
                    var source = File.ReadAllText(Path.Combine(directory.FullName, "src", "Momoka.Shell", "Views", "ShelfStrip.xaml.cs"));
                    Assert.Contains("Scroller.ScrollCompleted += OnFocusScrollCompleted;", source);
                    var first = source.IndexOf("    private void OnViewChanged(", StringComparison.Ordinal);
                    var last = source.IndexOf("    private void RaiseLeading(", first, StringComparison.Ordinal);
                    var view = source[first..last];
                    Assert.False(view.Contains("FocusItem", StringComparison.Ordinal));
                    Assert.False(view.Contains("ApplyPendingFocusedItem", StringComparison.Ordinal));
                    return;
                }
            throw new AssertionException("找不到本树Shelf源码");
        });
        Test("未Loaded虚拟目标即使可Focus也先完成bring，防止临时焦点被回收", () =>
        {
            var shelf = new ShelfFocusFixture();
            shelf.ReplaceItems(2000);
            shelf.Load();
            var target = new FrameworkElement { IsLoaded = false, FocusAccepted = true };
            target.OnStartBring = () => shelf.BringFromElement(42);
            shelf.Repeater.CreateElement = _ => target;
            shelf.FocusAt(1999);
            shelf.DispatcherQueue.Drain();
            Assert.Equal(0, target.FocusCalls);
            Assert.Equal(1, target.BringRequests);
            Assert.True(shelf.BringHandled, "虚拟目标bring没有在横带边界截住外层纵滚");
            target.RaiseLoaded();
            shelf.CompleteScroll(shelf.ScrollCorrelation);
            shelf.DispatcherQueue.Drain();
            Assert.Equal(FocusState.Keyboard, target.FocusState);
            Assert.Equal(-1, shelf.Pending);
        });
        Test("Focus同步回调发起新目标时旧成功不能清掉新请求", () =>
        {
            var (shelf, target) = Create();
            var next = new FrameworkElement { IsLoaded = false };
            shelf.Repeater.Prepare(4, next);
            target.OnFocus = () => shelf.FocusAt(4);
            shelf.FocusAt(2);
            Assert.Equal(4, shelf.Pending);
            Assert.Equal(1, next.LoadedSubscriptionCount);
            next.RaiseLoaded();
            shelf.DispatcherQueue.Drain();
            Assert.Equal(FocusState.Keyboard, next.FocusState);
            Assert.Equal(-1, shelf.Pending);
        });
        Test("虚拟目标bring异步到达边界时仍记录本次完成并阻断外层纵滚", () =>
        {
            var shelf = new ShelfFocusFixture();
            shelf.ReplaceItems(2000);
            shelf.Load();
            var target = new FrameworkElement { IsLoaded = false };
            shelf.Repeater.CreateElement = _ => target;
            shelf.FocusAt(1999);
            shelf.BringFromElement(91);
            Assert.Equal(91, shelf.ScrollCorrelation);
            Assert.True(shelf.BringHandled);
            target.RaiseLoaded();
            shelf.CompleteScroll(91);
            shelf.DispatcherQueue.Drain();
            Assert.Equal(FocusState.Keyboard, target.FocusState);
        });
    }

    private static (ShelfFocusFixture Shelf, FrameworkElement Target) Create()
    {
        var shelf = new ShelfFocusFixture();
        shelf.ReplaceItems(10);
        shelf.Load();
        shelf.AssumeScrollSettled();
        var target = new FrameworkElement();
        shelf.Repeater.Prepare(2, target);
        return (shelf, target);
    }
}
