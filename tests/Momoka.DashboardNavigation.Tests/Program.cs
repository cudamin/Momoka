using System.Security.Cryptography;
using System.Text;
using Momoka.Emby;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("R-019 真实 Dashboard page 控制流；非真 WebView2/WinUI UI 验证，无窗口/HTTP/账号/凭据/App/native/播放");
        Console.WriteLine($"SOURCE: {DashboardSourceEvidence.Path}");
        Console.WriteLine($"SHA256: {DashboardSourceEvidence.Sha256}; 原样区域 {DashboardSourceEvidence.VerbatimRegions}");
        Test("源码证据对应当前本树 LF 页面", () =>
        {
            var source = File.ReadAllBytes(DashboardSourceEvidence.Path);
            Assert.False(source.Contains((byte)'\r'), "page 必须 LF");
            Assert.Equal(DashboardSourceEvidence.Sha256, Convert.ToHexStringLower(SHA256.HashData(source)));
        });
        Case("真实 StartAsync 同时接线 Starting/Completed", (_, f) =>
        {
            Assert.Equal(1, f.Page.FixtureWeb.StartingSubscribers);
            Assert.Equal(1, f.Page.FixtureWeb.CompletedSubscribers);
            Assert.Equal(1, f.Core.NewWindowSubscribers);
            Assert.True(f.Page.FixtureContentEnabled);
        });
        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
        {
            Case("首个整页当前成功 " + kind, (_, f) =>
            {
                var a = f.Route("dashboard");
                f.Core.Complete(a);
                Visible(f, "dashboard");
                Assert.True(f.Page.Summary.Navigated == true);
            }, kind);
            foreach (var oldError in new[] { CoreWebView2WebErrorStatus.OperationCanceled, CoreWebView2WebErrorStatus.Timeout })
            {
                Case("A旧失败先返回/B后成功 " + kind + "/" + oldError, (_, f) =>
                {
                    var a = f.Route("dashboard");
                    var b = f.Route("users");
                    f.Core.Complete(a, false, oldError);
                    Pending(f, "users");
                    f.Core.Complete(b);
                    Visible(f, "users");
                }, kind);
                Case("B成功先返回/A旧失败后返回 " + kind + "/" + oldError, (_, f) =>
                {
                    var a = f.Route("dashboard");
                    var b = f.Route("users");
                    f.Core.Complete(b);
                    var before = f.Page.Summary;
                    f.Core.Complete(a, false, oldError);
                    Visible(f, "users");
                    Assert.Equal(before, f.Page.Summary, "旧完成不能改当前 Summary");
                }, kind);
            }
        }
        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
        {
            Case("同URL刷新B前A未Starting/旧A取消不能遮B " + kind, (queue, f) =>
            {
                f.Core.AutoStart = false;
                var a = f.Route("users");
                f.Page.FixtureRefresh();
                Assert.Equal(a.Uri, f.Page.CurrentNavigationAddress);
                f.Core.Start(a);
                f.Core.Complete(a, false, CoreWebView2WebErrorStatus.OperationCanceled);
                queue.Drain();
                Pending(f, "users");
                Assert.Equal("", f.Page.FixtureNotice);
                Assert.Equal(2, f.Core.Navigations.Count, "B必须最终发出，不能因旧A挂住");
                var b = f.Core.Navigations[^1];
                Assert.Equal(a.Uri, b.Uri);
                Assert.False(a.Id == b.Id);
                f.Core.Start(b);
                f.Core.Complete(b);
                Visible(f, "users");
            }, kind);
        }
        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
        {
            Case("同URL等待Starting时只发一个请求/刷新不猜FIFO " + kind, (_, f) =>
            {
                f.Core.AutoStart = false;
                f.Route("users");
                f.Page.FixtureRefresh();
                Assert.Equal(1, f.Core.Navigations.Count, "B必须先串行，不能同URL重叠无ID请求");
                Pending(f, "users");
            }, kind);
            foreach (var success in new[] { false, true })
            {
                Case("旧A未回Starting只回Completed也能放行B " + kind + "/" + success, (queue, f) =>
                {
                    f.Core.AutoStart = false;
                    var a = f.Route("users");
                    f.Page.FixtureRefresh();
                    f.Core.Complete(a, success, CoreWebView2WebErrorStatus.OperationCanceled);
                    queue.Drain();
                    Pending(f, "users");
                    Assert.Equal("", f.Page.FixtureNotice);
                    Assert.Equal(2, f.Core.Navigations.Count, "没回Starting不能永久堵住最新B");
                    var b = f.Core.Navigations[^1];
                    f.Core.Start(b);
                    f.Core.Complete(b);
                    Visible(f, "users");
                }, kind);
            }
            Case("同URL旧A开始/B发出后旧A取消不能释放B未开始槽 " + kind, (queue, f) =>
            {
                f.Core.AutoStart = false;
                var a = f.Route("users");
                f.Page.FixtureRefresh();
                f.Core.Start(a);
                queue.Drain();
                var b = f.Core.Navigations[^1];
                f.Page.FixtureRefresh(); // C must wait for B, not for the old A cancellation.
                f.Core.Complete(a, false, CoreWebView2WebErrorStatus.OperationCanceled);
                queue.Drain();
                Assert.Equal(2, f.Core.Navigations.Count, "已见A的旧完成不能认作没Starting的B");
                Pending(f, "users");
                f.Core.Start(b);
                queue.Drain();
                Assert.Equal(3, f.Core.Navigations.Count);
                var c = f.Core.Navigations[^1];
                f.Core.Complete(b, false, CoreWebView2WebErrorStatus.OperationCanceled);
                Pending(f, "users");
                f.Core.Start(c);
                f.Core.Complete(c);
                Visible(f, "users");
            }, kind);
            Case("同URL当前B未回Starting的真实失败仍显示 " + kind, (queue, f) =>
            {
                f.Core.AutoStart = false;
                var a = f.Route("users");
                f.Page.FixtureRefresh();
                f.Core.Start(a);
                queue.Drain();
                var b = f.Core.Navigations[^1];
                f.Core.Complete(a, false, CoreWebView2WebErrorStatus.OperationCanceled);
                f.Core.Complete(b, false, CoreWebView2WebErrorStatus.Timeout);
                Failed(f, "users", CoreWebView2WebErrorStatus.Timeout);
            }, kind);
        }
        Case("无Starting完成过的A后来Starting也不能抢B", (queue, f) =>
        {
            f.Core.AutoStart = false;
            var a = f.Route("users");
            f.Page.FixtureRefresh();
            f.Core.Complete(a, false, CoreWebView2WebErrorStatus.OperationCanceled);
            queue.Drain();
            var b = f.Core.Navigations[^1];
            f.Core.Start(a);
            f.Core.Complete(a, false, CoreWebView2WebErrorStatus.OperationCanceled);
            Pending(f, "users");
            f.Core.Start(b);
            f.Core.Complete(b);
            Visible(f, "users");
        });
        Case("未开始刷新合并成最新三路由/同URL等待不堆Navigate", (queue, f) =>
        {
            f.Core.AutoStart = false;
            var a = f.Route("dashboard");
            f.Page.FixtureRefresh();
            f.Page.TryShowRoute("users");
            f.Page.TryShowRoute("dashboard/settings");
            Assert.Equal(1, f.Core.Navigations.Count);
            f.Core.Complete(a, false, CoreWebView2WebErrorStatus.OperationCanceled);
            queue.Drain();
            Assert.Equal(2, f.Core.Navigations.Count);
            var last = f.Core.Navigations[^1];
            Assert.Equal(f.Page.CurrentNavigationAddress, last.Uri);
            f.Core.Start(last);
            f.Core.Complete(last);
            Visible(f, "dashboard/settings");
        });
        Case("串行下一请求不依赖NavigationId递增", (queue, f) =>
        {
            f.Core.AutoStart = false;
            f.Core.NextId = 900;
            var a = f.Route("users");
            f.Page.FixtureRefresh();
            f.Core.NextId = 7;
            f.Core.Start(a);
            queue.Drain();
            var b = f.Core.Navigations[^1];
            Assert.Equal((ulong)7, b.Id);
            f.Core.Complete(a, false, CoreWebView2WebErrorStatus.OperationCanceled);
            Pending(f, "users");
            f.Core.Start(b);
            f.Core.Complete(b);
            Visible(f, "users");
        });
        Case("原已Started的更旧完成不能解开新未Started请求", (queue, f) =>
        {
            var old = f.Route("dashboard");
            f.Core.AutoStart = false;
            var a = f.Route("users");
            f.Page.FixtureRefresh();
            f.Core.Complete(old, false, CoreWebView2WebErrorStatus.OperationCanceled);
            queue.Drain();
            Assert.Equal(2, f.Core.Navigations.Count);
            Pending(f, "users");
            f.Core.Complete(a, false, CoreWebView2WebErrorStatus.OperationCanceled);
            queue.Drain();
            Assert.Equal(3, f.Core.Navigations.Count);
            var b = f.Core.Navigations[^1];
            f.Core.Start(b);
            f.Core.Complete(b);
            Visible(f, "users");
        });
        foreach (var revoke in new[] { false, true })
        {
            Case("已排队最新导航在Release/源撤销后不发出 " + revoke, (queue, f) =>
            {
                f.Core.AutoStart = false;
                var a = f.Route("users");
                f.Page.FixtureRefresh();
                f.Core.Start(a);
                Assert.Equal(1, f.Core.Navigations.Count);
                var before = f.Page.Summary;
                if (revoke) f.Source.Revoke();
                else f.Page.Release();
                queue.Drain();
                Assert.Equal(1, f.Core.Navigations.Count);
                Assert.Equal(before, f.Page.Summary);
            });
        }
        Case("排队待发前再选新路由只发送最新URL", (queue, f) =>
        {
            f.Core.AutoStart = false;
            var a = f.Route("dashboard");
            f.Page.FixtureRefresh();
            f.Core.Start(a);
            f.Page.TryShowRoute("users");
            queue.Drain();
            Assert.Equal(2, f.Core.Navigations.Count);
            var latest = f.Core.Navigations[^1];
            Assert.Equal(f.Page.CurrentNavigationAddress, latest.Uri);
            f.Core.Complete(a, false, CoreWebView2WebErrorStatus.OperationCanceled);
            f.Core.Start(latest);
            f.Core.Complete(latest);
            Visible(f, "users");
        });
        Case("UI队列拒绝待发导航如实失败不重入WebView", (_, f) =>
        {
            f.Core.AutoStart = false;
            var a = f.Route("users");
            f.Page.FixtureRefresh();
            f.Page.DispatcherQueue.Accept = false;
            f.Core.Start(a);
            Assert.Equal(1, f.Core.Navigations.Count);
            Assert.True(f.Page.Summary.Ready);
            Assert.True(f.Page.Summary.Navigated == false);
            Assert.Equal(Visibility.Visible, f.Page.FixtureNoticeVisibility);
            Assert.Equal(Visibility.Collapsed, f.Page.FixtureWeb.Visibility);
            Assert.True(f.Page.Summary.Error.Length > 0);
        });
        Case("UI排队后真正Navigate抛错保失败并可刷新恢复", (queue, f) =>
        {
            f.Core.AutoStart = false;
            var a = f.Route("users");
            f.Page.FixtureRefresh();
            f.Core.Start(a);
            f.Core.NavigateFailure = new InvalidOperationException("queued Navigate failed");
            queue.Drain();
            Assert.Equal(1, f.Core.Navigations.Count);
            Assert.True(f.Page.Summary.Ready);
            Assert.True(f.Page.Summary.Navigated == false);
            Assert.Equal("queued Navigate failed", f.Page.Summary.Error);
            Assert.Equal(Visibility.Visible, f.Page.FixtureNoticeVisibility);
            Assert.Equal(Visibility.Collapsed, f.Page.FixtureWeb.Visibility);
            f.Core.Complete(a, false, CoreWebView2WebErrorStatus.OperationCanceled);
            Assert.Equal("queued Navigate failed", f.Page.Summary.Error);
            f.Core.NavigateFailure = null;
            f.Page.FixtureRefresh();
            Assert.Equal(2, f.Core.Navigations.Count);
            var recovered = f.Core.Navigations[^1];
            f.Core.Start(recovered);
            f.Core.Complete(recovered);
            Visible(f, "users");
        });
        Case("旧成功不能替尚未完成的B宣布已载入", (_, f) =>
        {
            var a = f.Route("dashboard");
            f.Route("users");
            f.Core.Complete(a);
            Pending(f, "users");
            Assert.False(f.Page.FixtureReadout.Contains("已载入", StringComparison.Ordinal));
        });
        Case("真实B失败保留，不被A旧取消盖掉", (_, f) =>
        {
            var a = f.Route("dashboard");
            var b = f.Route("users");
            f.Core.Complete(b, false, CoreWebView2WebErrorStatus.HostNameNotResolved);
            var before = f.Page.Summary;
            var notice = f.Page.FixtureNotice;
            f.Core.Complete(a, false, CoreWebView2WebErrorStatus.OperationCanceled);
            Assert.Equal(before, f.Page.Summary);
            Assert.Equal(notice, f.Page.FixtureNotice);
            Failed(f, "users", CoreWebView2WebErrorStatus.HostNameNotResolved);
        });
        Case("真实B失败不被A旧成功改成成功", (_, f) =>
        {
            var a = f.Route("dashboard");
            var b = f.Route("users");
            f.Core.Complete(b, false, CoreWebView2WebErrorStatus.Timeout);
            f.Core.Complete(a);
            Failed(f, "users", CoreWebView2WebErrorStatus.Timeout);
        });
        Case("当前B自己的取消仍如实显示", (_, f) =>
        {
            f.Route("dashboard");
            var b = f.Route("users");
            f.Core.Complete(b, false, CoreWebView2WebErrorStatus.OperationCanceled);
            Failed(f, "users", CoreWebView2WebErrorStatus.OperationCanceled);
        });
        Case("连续三路由只接受C", (_, f) =>
        {
            var a = f.Route("dashboard");
            var b = f.Route("users");
            var c = f.Route("dashboard/settings");
            f.Core.Complete(c);
            var before = f.Page.Summary;
            f.Core.Complete(a, false, CoreWebView2WebErrorStatus.OperationCanceled);
            f.Core.Complete(b, false, CoreWebView2WebErrorStatus.Timeout);
            Visible(f, "dashboard/settings");
            Assert.Equal(before, f.Page.Summary);
        });
        Case("B已请求但Starting未回来的间隙也拒A完成", (_, f) =>
        {
            var a = f.Route("dashboard");
            f.Core.AutoStart = false;
            var b = f.Route("users");
            f.Core.Complete(a, false, CoreWebView2WebErrorStatus.OperationCanceled);
            Pending(f, "users");
            f.Core.Start(b);
            f.Core.Complete(b);
            Visible(f, "users");
        });
        Case("排队旧A Starting不能绑到B代次", (queue, f) =>
        {
            f.Core.AutoStart = false;
            var a = f.Route("dashboard");
            f.Page.TryShowRoute("users");
            f.Core.Start(a);
            f.Core.Complete(a, false, CoreWebView2WebErrorStatus.OperationCanceled);
            queue.Drain();
            Pending(f, "users");
            Assert.Equal(2, f.Core.Navigations.Count);
            var b = f.Core.Navigations[^1];
            f.Core.Start(b);
            f.Core.Complete(b);
            Visible(f, "users");
        });
        Case("未知NavigationId不能应用完成", (_, f) =>
        {
            f.Route("users");
            var before = f.Page.Summary;
            f.Core.EmitUnknownCompletion(ulong.MaxValue, false, CoreWebView2WebErrorStatus.Timeout);
            Assert.Equal(before, f.Page.Summary);
            Pending(f, "users");
        });
        Case("NavigationId按UInt64比较，不截到int", (_, f) =>
        {
            f.Core.NextId = 0x1_0000_0000;
            var a = f.Route("dashboard");
            var b = f.Route("users");
            f.Core.Complete(a, false, CoreWebView2WebErrorStatus.Timeout);
            f.Core.Complete(b);
            Visible(f, "users");
        });
        Case("当前成功显式清旧notice并恢复Web", (_, f) =>
        {
            var a = f.Route("dashboard");
            f.Page.FixtureShowOldNotice();
            f.Core.Complete(a);
            Visible(f, "dashboard");
        });
        Case("Refresh新整页拒旧A完成", (_, f) =>
        {
            var a = f.Route("dashboard");
            f.Page.FixtureRefresh();
            var b = f.Core.Navigations[^1];
            f.Core.Complete(a, false, CoreWebView2WebErrorStatus.OperationCanceled);
            Pending(f, "dashboard");
            f.Core.Complete(b);
            Visible(f, "dashboard");
        });
        Case("Refresh尚未Starting时切路由不能凭旧booted走inline", (queue, f) =>
        {
            var a = f.Route("dashboard");
            f.Core.Complete(a);
            f.Core.AutoStart = false;
            f.Page.FixtureRefresh();
            var b = f.Core.Navigations[^1];
            f.Page.TryShowRoute("users");
            Assert.Equal(2, f.Core.Navigations.Count, "新路由只等待B的导航身份，不再重复未开始请求");
            Assert.Equal(0, f.Core.Scripts.Count);
            f.Core.Start(b);
            queue.Drain();
            Assert.Equal(3, f.Core.Navigations.Count);
            var c = f.Core.Navigations[^1];
            f.Core.Complete(b, false, CoreWebView2WebErrorStatus.OperationCanceled);
            Pending(f, "users");
            f.Core.Start(c);
            f.Core.Complete(c);
            Visible(f, "users");
        });
        Case("当前重定向保同NavigationId/代次", (_, f) =>
        {
            var a = f.Route("users");
            var generation = f.Page.FixtureGeneration;
            f.Core.Start(a, redirected: true);
            Assert.Equal(generation, f.Page.FixtureGeneration);
            f.Core.Complete(a);
            Visible(f, "users");
        });
        Case("旧重定向不抢回B当前导航", (_, f) =>
        {
            var a = f.Route("dashboard");
            var b = f.Route("users");
            var generation = f.Page.FixtureGeneration;
            f.Core.Start(a, redirected: true);
            Assert.Equal(generation, f.Page.FixtureGeneration);
            f.Core.Complete(a, false, CoreWebView2WebErrorStatus.OperationCanceled);
            Pending(f, "users");
            f.Core.Complete(b);
            Visible(f, "users");
        });
        foreach (var success in new[] { false, true })
        {
            Case("来源撤销拒排队完成 " + success, (_, f) =>
            {
                var a = f.Route("users");
                var callback = f.Core.CaptureCompletion(a, success, CoreWebView2WebErrorStatus.Timeout);
                var before = f.Page.Summary;
                f.Source.Revoke();
                callback();
                Assert.Equal(before, f.Page.Summary);
                Assert.False(f.Page.TryShowRoute("dashboard"));
            });
        }
        Case("跟随当前源的页面在默认身份换走后拒完成", (_, f) =>
        {
            var a = f.Route("users");
            var before = f.Page.Summary;
            f.Source.SwitchDefault();
            f.Core.Complete(a);
            Assert.Equal(before, f.Page.Summary);
            Assert.False(f.Page.IsCurrentSession);
        }, pinned: false);
        Case("指定SourceKey保留仍在线源，不因默认身份换走失效", (_, f) =>
        {
            var a = f.Route("users");
            f.Source.SwitchDefault();
            Assert.True(f.Page.IsCurrentSession);
            f.Core.Complete(a);
            Visible(f, "users");
        });
        Case("Release拒已捕获队列回调并幂等退订/Close", (_, f) =>
        {
            var a = f.Route("users");
            var callback = f.Core.CaptureCompletion(a, false, CoreWebView2WebErrorStatus.Timeout);
            var before = f.Page.Summary;
            f.Page.Release();
            f.Page.Release();
            callback();
            Assert.Equal(before, f.Page.Summary);
            Assert.Equal(0, f.Page.FixtureWeb.StartingSubscribers);
            Assert.Equal(0, f.Page.FixtureWeb.CompletedSubscribers);
            Assert.Equal(0, f.Core.NewWindowSubscribers);
            Assert.Equal(1, f.Core.CloseCount);
            Assert.False(f.Page.TryShowRoute("dashboard"));
        });
        Case("Release后新page重开，重用数值ID不接旧回调", (_, f) =>
        {
            var a = f.Route("users");
            var callback = f.Core.CaptureCompletion(a, false, CoreWebView2WebErrorStatus.Timeout);
            f.Page.Release();
            using var reopened = new Fixture();
            var b = reopened.Route("dashboard");
            Assert.Equal(a.Id, b.Id);
            reopened.Core.Complete(b);
            callback();
            Visible(reopened, "dashboard");
        });
        Case("安全路由既有拒绝不改变页面状态", (_, f) =>
        {
            f.Route("users");
            var before = f.Page.Summary;
            Assert.False(f.Page.TryShowRoute("https://other.invalid/"));
            Assert.Equal(before, f.Page.Summary);
            Assert.Equal(1, f.Core.Navigations.Count);
        });
        RegisterInline();
        RegisterSamples();
        return Run();
    }

    private static void RegisterInline()
    {
        Case("已提交文档inline/hash不要求新的Starting事件", (queue, f) =>
        {
            f.Core.Complete(f.Route("dashboard"));
            Assert.True(f.Page.TryShowRoute("users"));
            queue.Until(() => f.Page.Summary.Ready, "inline/hash 未完成");
            Assert.Equal(1, f.Core.Navigations.Count);
            Assert.Equal(1, f.Core.Scripts.Count(script => script.Inline));
            Visible(f, "users");
        });
        foreach (var exception in new[] { false, true })
        {
            Case("inline不支持/异常回退整页 " + exception, (_, f) =>
            {
                f.Core.Complete(f.Route("dashboard"));
                f.Core.InlineResult = "false";
                if (exception) f.Core.InlineFailure = new InvalidOperationException("fixture inline failure");
                f.Page.TryShowRoute("users");
                Assert.Equal(2, f.Core.Navigations.Count);
                Pending(f, "users");
                f.Core.Complete(f.Core.Navigations[^1]);
                Visible(f, "users");
            });
        }
        Case("旧inline成功响应队列晚到不覆盖新路由", (queue, f) =>
        {
            f.Core.Complete(f.Route("dashboard"));
            f.Core.HoldInline = true;
            f.Page.TryShowRoute("users");
            var old = f.Core.Scripts[^1];
            old.ReturnRaw("true");
            queue.Until(() => queue.Pending > 0, "inline 响应未进入UI队列", pump: false);
            f.Page.TryShowRoute("dashboard/settings");
            var before = f.Page.Summary;
            queue.Drain();
            Assert.Equal(before, f.Page.Summary);
            Assert.Equal(1, f.Core.Navigations.Count);
        });
        Case("同路由网页脚本起整页后拒旧inline响应", (queue, f) =>
        {
            f.Core.Complete(f.Route("dashboard"));
            f.Core.HoldInline = true;
            f.Page.TryShowRoute("users");
            var old = f.Core.Scripts[^1];
            f.Core.NavigateFromScript(f.Page.CurrentNavigationAddress);
            old.ReturnRaw("true");
            queue.Until(() => queue.Pending > 0, "旧inline未排队", pump: false);
            queue.Drain();
            Pending(f, "users");
            f.Core.Complete(f.Core.Navigations[^1]);
            Visible(f, "users");
        });
        Case("同路由网页整页开始后拒旧inline异常回退", (queue, f) =>
        {
            f.Core.Complete(f.Route("dashboard"));
            f.Core.HoldInline = true;
            f.Page.TryShowRoute("users");
            var old = f.Core.Scripts[^1];
            f.Core.NavigateFromScript(f.Page.CurrentNavigationAddress);
            var count = f.Core.Navigations.Count;
            old.Fail(new InvalidOperationException("late inline error"));
            queue.Until(() => queue.Pending > 0, "旧inline异常未排队", pump: false);
            queue.Drain();
            Assert.Equal(count, f.Core.Navigations.Count, "旧异常不能再顶掉当前整页导航");
            Pending(f, "users");
        });
        Case("inline350ms渲染等待期间新整页不被宣告完成", (queue, f) =>
        {
            f.Core.Complete(f.Route("dashboard"));
            f.Page.TryShowRoute("users");
            f.Core.NavigateFromScript(f.Page.CurrentNavigationAddress);
            queue.Until(() => queue.Pending > 0, "渲染等待未排队", pump: false);
            queue.Drain();
            Pending(f, "users");
            f.Core.Complete(f.Core.Navigations[^1]);
            Visible(f, "users");
        });
    }

    private static void RegisterSamples()
    {
        Case("当前成功取样返回实际client读数", (queue, f) =>
        {
            f.Core.Complete(f.Route("users"));
            var script = WaitSample(queue, f);
            script.ReturnSample("fixture current sample");
            queue.Until(() => f.Page.Summary.Ready, "当前取样没有settle");
            Assert.Equal("fixture current sample", f.Page.Summary.Client);
            Visible(f, "users");
        });
        foreach (var exception in new[] { false, true })
        {
            Case("旧取样响应在UI队列/新路由先应用 " + exception, (queue, f) =>
            {
                f.Core.Complete(f.Route("dashboard"));
                var script = WaitSample(queue, f);
                if (exception) script.Fail(new InvalidOperationException("late sample error"));
                else script.ReturnSample("old sample must be ignored");
                queue.Until(() => queue.Pending > 0, "取样响应未排队", pump: false);
                f.Core.HoldInline = true;
                f.Page.TryShowRoute("users");
                var before = f.Page.Summary;
                queue.Drain();
                Assert.Equal(before, f.Page.Summary, "旧catch/finally也不能更新client/settled");
                Pending(f, "users");
            });
        }
        foreach (var exception in new[] { false, true })
        {
            Case("来源撤销后取样响应/catch/finally均不回写 " + exception, (queue, f) =>
            {
                f.Core.Complete(f.Route("users"));
                var script = WaitSample(queue, f);
                var before = f.Page.Summary;
                f.Source.Revoke();
                if (exception) script.Fail(new InvalidOperationException("revoked sample error"));
                else script.ReturnSample("revoked sample");
                queue.Until(() => queue.Pending > 0, "撤销后取样未排队", pump: false);
                queue.Drain();
                Assert.Equal(before, f.Page.Summary);
            });
        }
        Case("Release已排队取样异常不回写client或settled", (queue, f) =>
        {
            f.Core.Complete(f.Route("users"));
            var script = WaitSample(queue, f);
            script.Fail(new InvalidOperationException("release sample error"));
            queue.Until(() => queue.Pending > 0, "关闭前取样未排队", pump: false);
            var before = f.Page.Summary;
            f.Page.Release();
            queue.Drain();
            Assert.Equal(before, f.Page.Summary);
        });
        Case("同路由新整页使上一文档取样失效", (queue, f) =>
        {
            f.Core.Complete(f.Route("users"));
            var script = WaitSample(queue, f);
            f.Core.NavigateFromScript(f.Page.CurrentNavigationAddress);
            var before = f.Page.Summary;
            script.ReturnSample("previous document sample");
            queue.Until(() => queue.Pending > 0, "旧文档取样未排队", pump: false);
            queue.Drain();
            Assert.Equal(before, f.Page.Summary);
            Pending(f, "users");
        });
        Case("Watchdog只结算当前有效访问/代次，撤销关闭旧轮不回写", (queue, f) =>
        {
            using var revoked = new Fixture();
            using var closed = new Fixture();
            using var refreshed = new Fixture();
            f.Route("users");
            revoked.Route("users");
            closed.Route("users");
            refreshed.Route("users");
            var tasks = new[]
            {
                f.Page.FixtureWatchdog(f.Page.FixtureGeneration),
                revoked.Page.FixtureWatchdog(revoked.Page.FixtureGeneration),
                closed.Page.FixtureWatchdog(closed.Page.FixtureGeneration),
                refreshed.Page.FixtureWatchdog(refreshed.Page.FixtureGeneration)
            };
            revoked.Source.Revoke();
            closed.Page.Release();
            // Let all four real automatic and four explicit watchdog delays queue before
            // refreshing; the new watchdog cannot finish in this assertion window.
            queue.Until(() => queue.Pending >= 8, "真实watchdog等待未排队", pump: false, seconds: 17);
            refreshed.Page.FixtureRefresh();
            var beforeRevoked = revoked.Page.Summary;
            var beforeClosed = closed.Page.Summary;
            var beforeRefreshed = refreshed.Page.Summary;
            queue.Finish(Task.WhenAll(tasks));
            Assert.True(f.Page.Summary.Ready);
            Assert.True(f.Page.Summary.Client.Contains("15 秒", StringComparison.Ordinal));
            Assert.Equal(beforeRevoked, revoked.Page.Summary);
            Assert.Equal(beforeClosed, closed.Page.Summary);
            Assert.Equal(beforeRefreshed, refreshed.Page.Summary);
        });
    }

    private static ScriptCall WaitSample(FixtureUiQueue queue, Fixture fixture)
    {
        queue.Until(() => fixture.Core.Scripts.Any(script => !script.Inline), "真实SampleAsync未调用Web边界");
        return fixture.Core.Scripts.Last(script => !script.Inline);
    }

    private static void Case(string name, Action<FixtureUiQueue, Fixture> run,
        MediaServerKind kind = MediaServerKind.Emby, bool pinned = true) =>
        Test(name, () => FixtureUiQueue.Run(queue =>
        {
            using var fixture = new Fixture(kind, pinned);
            run(queue, fixture);
        }));

    private static void Visible(Fixture f, string route)
    {
        Assert.Equal(route, f.Page.CurrentRoute);
        Assert.Equal(EmbyWebConsole.RouteUrl(f.Source.Scope.Connection, route), f.Page.CurrentNavigationAddress);
        Assert.Equal(Visibility.Visible, f.Page.FixtureWeb.Visibility);
        Assert.Equal(Visibility.Collapsed, f.Page.FixtureNoticeVisibility);
        Assert.Equal("", f.Page.FixtureNotice);
        Assert.Equal("", f.Page.Summary.Error);
        Assert.True(f.Page.Summary.Navigated == true);
    }

    private static void Pending(Fixture f, string route)
    {
        Assert.Equal(route, f.Page.CurrentRoute);
        Assert.Equal(EmbyWebConsole.RouteUrl(f.Source.Scope.Connection, route), f.Page.CurrentNavigationAddress);
        Assert.Equal(Visibility.Visible, f.Page.FixtureWeb.Visibility);
        Assert.Equal(Visibility.Collapsed, f.Page.FixtureNoticeVisibility);
        Assert.Equal("", f.Page.Summary.Error);
        Assert.False(f.Page.Summary.Ready);
        Assert.True(f.Page.Summary.Navigated is null, "旧完成不能改当前加载结果");
    }

    private static void Failed(Fixture f, string route, CoreWebView2WebErrorStatus error)
    {
        Assert.Equal(route, f.Page.CurrentRoute);
        Assert.Equal(Visibility.Collapsed, f.Page.FixtureWeb.Visibility);
        Assert.Equal(Visibility.Visible, f.Page.FixtureNoticeVisibility);
        Assert.Equal(error.ToString(), f.Page.Summary.Error);
        Assert.True(f.Page.FixtureNotice.Contains(f.Page.CurrentNavigationAddress, StringComparison.Ordinal));
        Assert.True(f.Page.Summary.Navigated == false);
        Assert.True(f.Page.Summary.Ready);
    }

    private sealed class Fixture : IDisposable
    {
        internal AnonymousSource Source { get; }
        internal CoreWebView2 Core { get; } = new();
        internal DashboardPage Page { get; }
        internal Fixture(MediaServerKind kind = MediaServerKind.Emby, bool pinned = true)
        {
            Source = new(kind);
            Page = new(Core, Source.Scope, pinned ? Source.Scope.Key : null);
        }
        internal NavigationCall Route(string route)
        {
            Assert.True(Page.TryShowRoute(route));
            return Core.Navigations[^1];
        }
        public void Dispose() { Page.Release(); Source.Dispose(); }
    }
}
