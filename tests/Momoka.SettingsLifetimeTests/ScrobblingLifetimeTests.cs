using System.Reflection;
using System.Text.RegularExpressions;
using Momoka.Scrobbling;
using Momoka.Shell.Views;

namespace Momoka.Tests;

internal static class ScrobblingLifetimeTests
{
    public static void Register(string sourceRoot)
    {
        TestHarness.Test("打格子进入设置自动检查，离开取消且重入重新检查", () =>
        {
            using var fixture = new LifetimeFixture();
            fixture.Settings.Settings.Scrobbling.Douban.Enabled = true;
            fixture.Scrobbling.ConfigurationChanged();
            var calls = 0;
            fixture.Douban.LoginCheck = _ => { calls++; return Task.FromResult(true); };
            var page = fixture.NewCachedPage("主页");
            page.BeginScrobblingVisitForFixture();
            page.AttachScrobblingForFixture(fixture.Scrobbling);
            Assert.Equal(1, calls);
            Assert.Equal(ScrobblingConnectionState.Connected, fixture.Scrobbling.DoubanConnection);
            fixture.Douban.LoginCheck = async token => { calls++; await Task.Delay(Timeout.Infinite, token); return true; };
            var check = page.VerifyScrobblingForFixture();
            Assert.Equal(ScrobblingConnectionState.Checking, fixture.Scrobbling.DoubanConnection);
            page.ReleaseHosted();
            check.GetAwaiter().GetResult();
            Assert.Equal(ScrobblingConnectionState.Unchecked, fixture.Scrobbling.DoubanConnection);
            fixture.Douban.LoginCheck = _ => { calls++; return Task.FromResult(false); };
            page.BeginScrobblingVisitForFixture();
            page.AttachScrobblingForFixture(fixture.Scrobbling);
            Assert.Equal(3, calls);
            Assert.Equal(ScrobblingConnectionState.SignedOut, fixture.Scrobbling.DoubanConnection);
            page.ReleaseHosted();
        });
        TestHarness.Test("豆瓣登录窗口关闭后真实页面立即检查，关闭窗口不冒充登录成功", () =>
        {
            using var fixture = new LifetimeFixture();
            fixture.Settings.Settings.Scrobbling.Douban.Enabled = true;
            fixture.Scrobbling.ConfigurationChanged();
            fixture.Douban.LoginCheck = _ => Task.FromResult(false);
            var page = fixture.NewCachedPage("主页");
            page.BeginScrobblingVisitForFixture();
            page.AttachScrobblingForFixture(fixture.Scrobbling);
            page.LoginDoubanForFixture().GetAwaiter().GetResult();
            Assert.Equal(ScrobblingConnectionState.SignedOut, fixture.Scrobbling.DoubanConnection);
            fixture.Douban.LoginCheck = _ => Task.FromResult(true);
            page.LoginDoubanForFixture().GetAwaiter().GetResult();
            Assert.Equal(ScrobblingConnectionState.Connected, fixture.Scrobbling.DoubanConnection);
            page.ReleaseHosted();
        });
        TestHarness.Test("打格子页面活动期只订阅一次，ReleaseHosted撤销监听并拒绝迟到回调，重入恢复", () =>
        {
            using var fixture = new LifetimeFixture();
            var page = fixture.NewCachedPage("主页");
            page.BeginScrobblingVisitForFixture();
            page.AttachScrobblingForFixture(fixture.Scrobbling);

            Assert.Equal(1, page.ViewModel.ScrobblingUseCalls);
            Assert.True(ReferenceEquals(fixture.Scrobbling, page.ViewModel.LastScrobblingService));
            fixture.Scrobbling.SetStatus("第一趟状态");
            Assert.Equal(1, page.DispatcherQueue.Count, "服务状态事件应只排一个页面刷新");
            page.DispatcherQueue.Drain();
            Assert.Equal(1, page.ViewModel.ScrobblingRefreshCalls);

            fixture.Scrobbling.SetStatus("释放前排队的状态");
            Assert.Equal(1, page.DispatcherQueue.Count);
            page.ReleaseHosted();
            page.BeginScrobblingVisitForFixture();
            page.AttachScrobblingForFixture(fixture.Scrobbling);
            Assert.Equal(2, page.ViewModel.ScrobblingUseCalls);
            Assert.Equal(1, page.DispatcherQueue.Count, "重入后旧回调仍在队列中，尚未执行");
            page.DispatcherQueue.Drain();
            Assert.Equal(1, page.ViewModel.ScrobblingRefreshCalls,
                "旧活动期排队回调不得刷新重入后的页面");

            fixture.Scrobbling.SetStatus("重入状态");
            Assert.Equal(1, page.DispatcherQueue.Count, "重入不能累积旧订阅或重复订阅");
            page.DispatcherQueue.Drain();
            Assert.Equal(2, page.ViewModel.ScrobblingRefreshCalls);

            page.ReleaseHosted();
            fixture.Scrobbling.SetStatus("释放后状态");
            Assert.Equal(0, page.DispatcherQueue.Count, "ReleaseHosted 必须移除真实服务事件订阅");
        });
        TestHarness.Test("释放设置页时关闭打格子记录对话框", () =>
        {
            using var fixture = new LifetimeFixture();
            var page = fixture.NewCachedPage("主页");
            var dismissed = false;
            var callback = typeof(SettingsPage).GetField("_dismissScrobblingRecords",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.True(callback is not null, "页面必须保留活动记录对话框的关闭回调");
            callback!.SetValue(page, (Action)(() => dismissed = true));

            page.ReleaseHosted();

            Assert.True(dismissed, "释放活动期应关闭记录对话框");
            Assert.Null(callback.GetValue(page), "关闭回调应在释放时清空");
        });

        TestHarness.Test("打格子导航在设置请求切换后只调用一次真实接线助手", () =>
        {
            var page = File.ReadAllText(Path.Combine(sourceRoot, "src", "Momoka.Shell", "Views", "SettingsPage.xaml.cs"));
            var assignment = page.IndexOf("_request = request;", StringComparison.Ordinal);
            const string attach = "AttachScrobblingForVisit(request.Services.GetRequiredService<Momoka.Scrobbling.ScrobblingService>()";
            var calls = Regex.Matches(page, Regex.Escape(attach));
            var call = page.IndexOf(attach, StringComparison.Ordinal);
            Assert.Equal(1, calls.Count, "导航中必须只有一处打格子接线，避免双订阅");
            Assert.True(assignment >= 0 && call > assignment, "必须先保存当前请求，再绑定服务与活动期监听");

            var scrobbling = File.ReadAllText(Path.Combine(sourceRoot, "src", "Momoka.Shell", "Views", "SettingsPage.Scrobbling.cs"));
            var helperStart = scrobbling.IndexOf("private void AttachScrobblingForVisit(", StringComparison.Ordinal);
            var helperEnd = scrobbling.IndexOf("private void WatchScrobbling()", helperStart, StringComparison.Ordinal);
            Assert.True(helperStart >= 0 && helperEnd > helperStart, "缺少活动期接线助手或监听入口");
            var helper = scrobbling[helperStart..helperEnd];
            Assert.Equal(1, Regex.Matches(helper, @"ViewModel\.UseScrobbling\(").Count);
            Assert.Equal(1, Regex.Matches(helper, @"WatchScrobbling\(\)").Count);
            Assert.True(helper.IndexOf("ViewModel.UseScrobbling(", StringComparison.Ordinal)
                < helper.IndexOf("WatchScrobbling()", StringComparison.Ordinal), "必须先向真实 VM 绑定服务，再挂 UI 状态监听");
        });
    }
}
