using Microsoft.UI.Xaml;
using Momoka.Shell.Windowing;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        Test("研究窗口Show Hide重开保留原内容与草稿", () =>
        {
            var draft = new object();
            var window = new Window { Content = draft };
            using var research = new FrameworkWindowResearch(window);
            research.Show();
            Assert.True(research.IsOpen && window.AppWindow.Visible);
            research.Hide();
            research.Show();
            Assert.True(ReferenceEquals(draft, window.Content));
            Assert.Equal(2, window.ActivateCalls);
        });
        Test("用户Close取消并隐藏，只有Dispose真正关闭", () =>
        {
            var window = new Window { Content = new object() };
            var research = new FrameworkWindowResearch(window);
            var hidden = 0;
            research.Hidden += () => hidden++;
            research.Show();
            window.Close();
            Assert.False(research.IsOpen);
            Assert.False(research.IsClosed);
            Assert.Equal(1, hidden);
            Assert.Equal(0, window.CloseCalls);
            research.Show();
            research.Dispose();
            Assert.True(research.IsClosed);
            Assert.Equal(1, window.CloseCalls);
            Assert.True(research.ContentReleased);
            Assert.Throws<InvalidOperationException>(() => _ = window.Content);
        });
        Test("重复隐藏与释放幂等，窗口关闭解除所有订阅", () =>
        {
            var window = new Window();
            var research = new FrameworkWindowResearch(window);
            var hidden = 0;
            research.Hidden += () => hidden++;
            research.Show();
            research.Hide();
            research.Hide();
            Assert.Equal(1, hidden);
            research.Dispose();
            research.Dispose();
            Assert.Equal(0, window.AppWindow.ClosingSubscriptions);
            Assert.Equal(0, window.ClosedSubscriptions);
            Assert.Equal(1, window.CloseCalls);
            Assert.Throws<ObjectDisposedException>(research.Show);
        });
        Test("Activate和Hide失败不冒充已完成状态", () =>
        {
            var window = new Window();
            using var research = new FrameworkWindowResearch(window);
            window.ThrowOnActivate = true;
            Assert.Throws<InvalidOperationException>(research.Show);
            Assert.False(research.IsOpen);
            window.ThrowOnActivate = false;
            research.Show();
            window.AppWindow.ThrowOnHide = true;
            Assert.Throws<InvalidOperationException>(research.Hide);
            Assert.True(research.IsOpen);
            window.AppWindow.ThrowOnHide = false;
            research.Hide();
            Assert.False(research.IsOpen);
        });
        Test("真正Close失败保留用户Close取消语义并可重试收尾", () =>
        {
            var window = new Window();
            var research = new FrameworkWindowResearch(window);
            research.Show();
            window.ThrowOnClose = true;
            Assert.Throws<InvalidOperationException>(research.Dispose);
            Assert.False(research.IsClosed);
            window.ThrowOnClose = false;
            window.Close();
            Assert.False(research.IsClosed);
            Assert.False(research.IsOpen);
            research.Dispose();
            Assert.True(research.IsClosed);
        });
        Test("延迟Closed不提前释放内容，事件到达后清理", () =>
        {
            var draft = new object();
            var window = new Window { Content = draft, DeferClosed = true };
            var research = new FrameworkWindowResearch(window);
            research.Show();
            research.Dispose();
            research.Dispose();
            Assert.Equal(1, window.CloseCalls);
            Assert.False(research.IsClosed);
            Assert.True(ReferenceEquals(draft, window.Content));
            window.DeliverClosed();
            Assert.True(research.IsClosed);
            Assert.True(research.ContentReleased);
            Assert.Throws<InvalidOperationException>(() => _ = window.Content);
            Assert.Equal(0, window.ClosedSubscriptions);
        });
        return Run();
    }
}
