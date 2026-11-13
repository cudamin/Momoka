using Microsoft.UI.Xaml.Input;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class PlaybackReturnScrollTests
{
    internal static void Register()
    {
        Test("正式返回保留刷新目标，临时extent夹取后恢复而不依赖第二次ViewChanged", () =>
        {
            var page = new DetailPage();
            page.Body.ScrollableHeight = 900;
            page.Body.VerticalOffset = 787;
            page.ViewModel.Busy = true;
            page.ViewModel.IsReady = false;
            page.KeepScrollThroughReveal(787);
            page.Body.ChangeOffset(442);
            page.Body.ChangeExtent(442);
            Assert.Equal(442d, page.Body.VerticalOffset);
            page.Body.ChangeExtent(900);
            Assert.Equal(787d, page.Body.VerticalOffset);
            page.ViewModel.Finish();
            Assert.Equal(787d, page.Body.VerticalOffset);
            page.EndRevealLayoutForFixture();
            Assert.Equal(0, page.Body.SubscriptionCount);
            page.Body.ChangeOffset(300);
            Assert.Equal(300d, page.Body.VerticalOffset, "布局期结束不再拖回用户滚动");
        });
        Test("正式返回加载超过旧600毫秒仍保留目标，Ready后才开始有限布局期", () =>
        {
            var page = new DetailPage();
            page.Body.ScrollableHeight = 456;
            page.Body.VerticalOffset = 456;
            page.ViewModel.Busy = true;
            page.ViewModel.IsReady = false;
            page.KeepScrollThroughReveal(801);
            page.SetRevealAgeForFixture(900);
            page.Body.ChangeOffset(456);
            page.ViewModel.Finish();
            page.Body.ChangeExtent(801);
            Assert.Equal(801d, page.Body.VerticalOffset);
            page.EndRevealLayoutForFixture();
            Assert.Equal(0, page.Body.SubscriptionCount);
        });
        Test("正式返回未完成滚动不重复发送同目标，完成后使用当前extent", () =>
        {
            var page = new DetailPage();
            page.Body.AutoComplete = false;
            page.Body.ScrollableHeight = 900;
            page.KeepScrollThroughReveal(787);
            for (var count = 0; count < 8; count++) page.Body.ChangeOffset(0);
            Assert.Equal(1, page.Body.Requests.Count);
            page.Body.ChangeExtent(442);
            Assert.Equal(2, page.Body.Requests.Count);
            page.Body.Complete(1);
            page.Body.Complete(2);
            Assert.Equal(442d, page.Body.VerticalOffset);
            page.Body.ChangeExtent(900);
            Assert.Equal(3, page.Body.Requests.Count);
            page.Body.Complete(3);
            Assert.Equal(787d, page.Body.VerticalOffset);
            page.StopRevealForFixture();
        });
        Test("正式返回发新滚动时同步到达的旧correlation不能重入发送", () =>
        {
            var page = new DetailPage();
            page.Body.AutoComplete = false;
            page.Body.ScrollableHeight = 900;
            page.KeepScrollThroughReveal(787);
            var callbackCount = 0;
            page.Body.BeforeScrollRequest = () =>
            {
                // Native ChangeOffsetsPrivate completes older delayed operations before publishing the new ID.
                if (++callbackCount == 1) page.Body.SignalCompleted(1);
            };
            page.Body.ChangeExtent(442);
            Assert.Equal(2, page.Body.Requests.Count, "新extent只应发送一次当前目标");
            page.Body.Complete(2);
            Assert.Equal(442d, page.Body.VerticalOffset);
            page.StopRevealForFixture();
        });
        Test("正式返回用户滚轮、导航键或原生Interaction立即解除", () =>
        {
            foreach (var kind in new[] { "wheel", "key", "interaction" })
            {
                var page = new DetailPage();
                page.Body.ScrollableHeight = 900;
                page.Body.VerticalOffset = 787;
                page.ViewModel.Busy = true;
                page.KeepScrollThroughReveal(787);
                if (kind == "interaction") page.Body.Interact();
                else page.Body.UserSignal(kind, kind == "key" ? new KeyRoutedEventArgs(Windows.System.VirtualKey.Tab) : new PointerRoutedEventArgs());
                Assert.Equal(0, page.Body.SubscriptionCount);
                page.Body.ChangeOffset(300);
                page.Body.ChangeExtent(1000);
                page.ViewModel.Finish();
                Assert.Equal(300d, page.Body.VerticalOffset);
            }
        });
        Test("正式返回重复登记仅一份，离页取消及无效目标不登记", () =>
        {
            var page = new DetailPage();
            page.Body.ScrollableHeight = 900;
            page.KeepScrollThroughReveal(787);
            var subscriptions = page.Body.SubscriptionCount;
            page.KeepScrollThroughReveal(600);
            Assert.Equal(subscriptions, page.Body.SubscriptionCount);
            page.StopRevealForFixture();
            Assert.Equal(0, page.Body.SubscriptionCount);
            page.Body.ChangeOffset(300);
            page.ViewModel.Finish();
            Assert.Equal(300d, page.Body.VerticalOffset);
            page.KeepScrollThroughReveal(double.NaN);
            Assert.Equal(0, page.Body.SubscriptionCount);
        });
        Test("正式返回释放后迟到correlation和Ready不能重新拖回", () =>
        {
            var page = new DetailPage();
            page.Body.AutoComplete = false;
            page.Body.ScrollableHeight = 900;
            page.KeepScrollThroughReveal(787);
            page.StopRevealForFixture();
            page.Body.ChangeOffset(300);
            page.Body.SignalCompleted(1);
            page.ViewModel.Finish();
            Assert.Equal(300d, page.Body.VerticalOffset);
            Assert.Equal(0, page.Body.SubscriptionCount);
            Assert.Equal(1, page.Body.Requests.Count);
        });
        Test("正式返回Release和Unloaded都在取消其他属主前解绑恢复", () =>
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) directory = directory.Parent;
            Assert.NotNull(directory);
            var source = File.ReadAllText(Path.Combine(directory!.FullName, "src/Momoka.Shell/Views/DetailPage.xaml.cs")).Replace("\r\n", "\n");
            var release = source.IndexOf("    public void Release()", StringComparison.Ordinal);
            var unload = source.IndexOf("        Unloaded += (_, _) =>", StringComparison.Ordinal);
            Assert.True(release >= 0 && unload >= 0);
            var releaseStop = source.IndexOf("StopScrollReveal();", release, StringComparison.Ordinal);
            var unloadStop = source.IndexOf("StopScrollReveal();", unload, StringComparison.Ordinal);
            Assert.True(releaseStop >= release && releaseStop < source.IndexOf("ViewModel.Cancel();", release, StringComparison.Ordinal));
            Assert.True(unloadStop >= unload && unloadStop < source.IndexOf("StopArtworkArrival();", unload, StringComparison.Ordinal));
        });
    }
}
