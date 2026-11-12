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
            shelf.Repeater.ThrowOnCreate = true;
            shelf.FocusAt(8);
            shelf.DispatcherQueue.Drain();
            Assert.Equal(2, shelf.Repeater.CreateCount);
            Assert.Equal(8, shelf.Pending);
            Assert.Equal(0, shelf.DispatcherQueue.PendingCount);
            shelf.Repeater.ThrowOnCreate = false;
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
    }

    private static (ShelfFocusFixture Shelf, FrameworkElement Target) Create()
    {
        var shelf = new ShelfFocusFixture();
        shelf.ReplaceItems(10);
        shelf.Load();
        var target = new FrameworkElement();
        shelf.Repeater.Prepare(2, target);
        return (shelf, target);
    }
}
