using System.Text;
using Momoka.Emby;
using Momoka.Shell;

namespace Momoka.Tests;

internal static class Program
{
    private static readonly List<(string Name, Func<Task> Run)> Cases = [];

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length is < 2 or > 3)
        {
            Console.Error.WriteLine("用法：Momoka.HomeOrderProbeTests <本树当前 Release Momoka.dll> <本树根目录> [legacy-red]");
            return 2;
        }
        try
        {
            _ = new ShellRuntime(args[0], args[1]);
            Console.WriteLine("HomeOrder：真实 HomeCard/Rows/MP 可用性＋源链接页面/共享探针入口；无UI/登录/凭据/网络");
            if (args.Length == 3)
            {
                Assert.Equal("legacy-red", args[2]);
                Test("旧探针直接 WatchMoviePilot 后等待两个 MP 项（应复现 Timeout）", async () =>
                {
                    using var f = new HomeOrderFixture();
                    f.Page.WatchMoviePilot(f.Service);
                    Assert.True(await f.Connect());
                    f.Drain();
                    Console.WriteLine($"  旧夹具证据：Available={f.Service.Available}；Rows={f.Rows.Count}；MP={f.MoviePilotCount}；队列={f.Page.DispatcherQueue.Count}");
                    await ShellNavigationProbe.UntilForFixture(() => f.MoviePilotCount == 2);
                });
            }
            else Register(args[1]);
            var failed = 0;
            foreach (var (name, run) in Cases)
            {
                try { await run(); Console.WriteLine("  通过  " + name); }
                catch (Exception error) { failed++; Console.WriteLine("  失败  " + name + "\n" + error); }
            }
            Console.WriteLine($"共 {Cases.Count} 项，通过 {Cases.Count - failed}，失败 {failed}，跳过 0");
            return failed == 0 ? 0 : 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Test(string name, Func<Task> action) => Cases.Add((name, action));
    private static void Test(string name, Action action) => Test(name, () => { action(); return Task.CompletedTask; });

    private static void Register(string root)
    {
        Test("HomeOrder 原case断言、截图和轮播阈值全部保留（源码契约，非像素）", () => CoverageContract(root));
        Test("HomeOrder 实际排序行hook验证上下移、两端禁移和单次保存", () =>
        {
            var method = HomeOrderFixture.Runtime.Type("SettingHomeLayoutRow").GetMethod("Probe",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            var probe = ((bool Ok, string Detail))method.Invoke(null, null)!;
            Assert.True(probe.Ok, probe.Detail);
            Assert.Contains("写盘 2 次", probe.Detail);
        });
        Test("HomeOrder 未导航直接Watch不能借VM Attach打开页面活动期", async () =>
        {
            using var f = new HomeOrderFixture();
            f.Page.WatchMoviePilot(f.Service);
            Assert.True(await f.Connect());
            f.Drain();
            Assert.True(f.Service.Available);
            Assert.Equal(4, f.Rows.Count);
            Assert.Equal(0, f.MoviePilotCount);
            Assert.Equal(0, f.Page.DispatcherQueue.Count);
            f.Stable();
        });
        Test("HomeOrder 共享探针入口恢复活动期，未连接隐藏而GET成功原表补齐两项", async () =>
        {
            using var f = new HomeOrderFixture();
            await f.Open();
            Assert.Equal("媒体库顺序", HomeOrderFixture.Runtime.Get<string>(f.Table, "Label"));
            Assert.Equal(4, f.Rows.Count);
            f.Settings.Settings.MoviePilot.Enabled = true;
            f.Drain();
            Assert.False(f.Service.Available);
            Assert.Equal(0, f.MoviePilotCount);
            Assert.True(await f.Connect());
            Assert.Equal(4, f.Rows.Count, "后台通知只能排队，不能直接重填UI集合");
            f.Drain();
            await f.WaitRows(6);
            Assert.Equal(2, f.MoviePilotCount);
            Assert.Equal(1, f.Reads.Requests);
            Assert.Equal(248d, HomeOrderFixture.Runtime.Get<double>(f.Table, "ListHeight"));
            f.Stable();
        });
        Test("HomeOrder 断线/连接恢复原地4→6，非默认次序与两个取消勾选不丢失", async () =>
        {
            using var f = new HomeOrderFixture();
            await f.Open();
            Assert.True(await f.Connect());
            f.Drain();
            f.Reads.Offline = true;
            Assert.False(await f.Service.RefreshAvailabilityAsync());
            f.Drain();
            await f.WaitRows(4);
            Assert.Equal(168d, HomeOrderFixture.Runtime.Get<double>(f.Table, "ListHeight"));
            Assert.Equal(0, f.MoviePilotCount);
            f.Stable();
            f.Reads.Offline = false;
            Assert.True(await f.Connect());
            f.Drain();
            await f.WaitRows(6);
            Assert.Equal(HomeLayout.MoviePilotSeries, f.Keys[0]);
            foreach (var row in f.Rows.Cast<object>().Where(row => HomeLayout.IsMoviePilot(HomeOrderFixture.Runtime.Get<string>(row, "Key"))))
                Assert.False(HomeOrderFixture.Runtime.Get<bool>(row, "Visible"));
            f.Stable();
        });
        Test("HomeOrder Release后直接Watch仍拒绝，共享活动期重入才同步最新可用性", async () =>
        {
            using var f = new HomeOrderFixture();
            await f.Open();
            Assert.True(await f.Connect());
            f.Drain();
            f.Page.ReleaseHosted();
            f.Page.WatchMoviePilot(f.Service);
            f.Reads.Offline = true;
            Assert.False(await f.Service.RefreshAvailabilityAsync());
            Assert.Equal(0, f.Page.DispatcherQueue.Count);
            Assert.Equal(6, f.Rows.Count, "活动期结束不应让离树旧表偷偷更新");
            await f.Open();
            await f.WaitRows(4);
            f.Reads.Offline = false;
            Assert.True(await f.Connect());
            f.Drain();
            await f.WaitRows(6);
            f.Stable();
        });
        Test("HomeOrder MP关闭/恢复期间设置页离树；活动期重入4→6且Rows稳定", async () =>
        {
            using var f = new HomeOrderFixture();
            await f.Open();
            Assert.True(await f.Connect());
            f.Drain();
            f.Page.ReleaseHosted();
            f.Settings.Settings.MoviePilot.Enabled = false;
            Assert.Equal(6, f.Rows.Count);
            await f.Open();
            await f.WaitRows(4);
            f.Page.ReleaseHosted();
            Assert.True(await f.Connect());
            Assert.Equal(4, f.Rows.Count);
            await f.Open();
            await f.WaitRows(6);
            f.Stable();
        });
        Test("HomeOrder 旧活动期已排队连接回调不能穿过Release后重入", async () =>
        {
            using var f = new HomeOrderFixture();
            await f.Open();
            Assert.True(await f.Connect());
            Assert.True(f.Page.DispatcherQueue.Count > 0);
            f.Page.ReleaseHosted();
            f.Settings.Settings.MoviePilot.Enabled = false;
            await f.Open();
            await f.WaitRows(4);
            f.Drain();
            Assert.Equal(4, f.Rows.Count);
            Assert.False(f.Service.Available);
            f.Stable();
        });
        Test("HomeOrder 非主页分类不误等隐藏表；回主页才同步当前可用性", async () =>
        {
            using var f = new HomeOrderFixture();
            await f.Open();
            HomeOrderFixture.Runtime.Set(f.Vm, "SelectedCategory", "关于");
            Assert.True(await f.Connect());
            f.Drain();
            Assert.Equal(4, f.Rows.Count);
            HomeOrderFixture.Runtime.Set(f.Vm, "SelectedCategory", "主页");
            await f.WaitRows(6);
            f.Stable();
        });
        Test("HomeOrder 原样缓存重入调用先开共享活动期，保留6项与真实表身份", async () =>
        {
            using var f = new HomeOrderFixture();
            await f.Open();
            Assert.True(await f.Connect());
            f.Drain();
            await f.Reopen();
            Assert.Equal(6, f.Rows.Count);
            f.Reads.Offline = true;
            Assert.False(await f.Service.RefreshAvailabilityAsync());
            f.Drain();
            await f.WaitRows(4);
            f.Stable();
        });
        Test("HomeOrder 未开启活动期的错误等待仍实际UntilAsync超时，不延时洗绿", async () =>
        {
            using var f = new HomeOrderFixture();
            f.Page.WatchMoviePilot(f.Service);
            Assert.True(await f.Connect());
            f.Drain();
            try { await f.WaitRows(6); throw new AssertionException("错误夹具必须超时"); }
            catch (TimeoutException error) { Assert.Contains("未等到预期状态", error.Message); }
            Assert.Equal(4, f.Rows.Count);
            f.Stable();
        });
    }

    private static void CoverageContract(string root)
    {
        var source = File.ReadAllText(Path.Combine(root, "src", "Momoka.Shell", "ShellNavigationProbe.HomeOrder.cs"));
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(source,
            @"BeginSettingsFixtureVisit\(settings\);\s+settings\.WatchMoviePilot\(service\);").Count,
            "初入、缓存重入和MP关/开恢复统一复用共享入口");
        Assert.False(source.Contains("private static void BeginSettingsFixtureVisit", StringComparison.Ordinal), "不能重复定义共享入口");
        Assert.Contains("settings.ReleaseHosted();\n            _root.Children.Remove(settings);", source);
        foreach (var assertion in new[]
        {
            "关闭时排序表仍有 MP 或标题未改", "未连接时排序表显示 MP", "排序表未实现订阅勾选控件",
            "电影订阅勾选未保存", "假断线没有撤销可用性", "断线后排序表没有收起整行或仍有空白",
            "断线后有效列表项仍有订阅控件", "假连接未恢复", "恢复连接丢失原次序或勾选",
            "缓存设置页重入未恢复订阅排序", "主页未采用排序表次序", "混排订阅命中了隐藏的标题或条带",
            "订阅标题与封面之间缺少间距", "订阅混排的轮播折叠阈值漂移", "取消电视剧勾选影响了电影排",
            "关闭后连接未恢复", "关闭后恢复丢失电视剧勾选", "主页排序触发了订阅写操作"
        }) Assert.Contains(assertion, source);
        foreach (var evidence in new[]
        {
            "new[] { 1100, 760 }", "settings-home-order-{width}.png", "settings-home-order-offline.png",
            "settings-home-order-restored.png", "home-subscription-order-{width}.png",
            "home-subscription-order-overlay.png", "home-subscription-order-disabled.png",
            "movieCheck.IsChecked = false", "while (series.CanUp)", "choices[0].DownCommand.Execute(null)",
            "before.HasNext && after.HasNext && Math.Abs(before.Line - after.Line) < 2",
            "home.HomeRows.All(row => row is not MoviePilotHomeRow)", "await UntilAsync(() => choices.Count == 4)",
            "await UntilAsync(() => choices.Count == 6)", "server.Writes.Count == 0"
        }) Assert.Contains(evidence, source);
    }
}
