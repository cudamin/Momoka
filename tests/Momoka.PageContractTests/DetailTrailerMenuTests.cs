using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 详情页的预告片入口（2026-11-19 用户令「把沉浸模式中的预告片按钮收进更多按钮中」）。这里钉住三件接线：
/// 两套操作排不再单独给预告片留一颗键、更多菜单里留一行且判据仍是 TrailerButtonVisibility、按下去仍回到
/// 原来那份预告菜单。真正弹出、读取与失败重试由 Shell 原生探针的详情预告分区核对。
/// </summary>
internal static class DetailTrailerMenuTests
{
    internal static void Register()
    {
        Test("详情预告片：宽窗与紧凑的操作排都不再单独占一颗键", () =>
        {
            var xaml = Source("Views/DetailPage.xaml");
            Assert.DoesNotContain("ColumnTrailerButton", xaml);
            Assert.DoesNotContain("CompactTrailerButton", xaml);
            // 预告片那颗从前挂在 OnTrailersClick 上；入口收进菜单后这个处理器只该留在历史里。
            Assert.DoesNotContain("OnTrailersClick", xaml);
        });

        Test("详情更多：菜单里按同一判据摆一行预告片，按下回到原来的预告菜单", () =>
        {
            var page = Source("Views/DetailPage.xaml.cs");
            Assert.Contains("trailer: ViewModel.TrailerButtonVisibility == Visibility.Visible", page);
            Assert.Contains("DispatcherQueue.TryEnqueue(() => _ = ShowTrailersAsync(anchor))", page);
            // 菜单实例留给自检读行；关闭时只清自己那一张。
            Assert.Contains("private MenuFlyout? _moreMenu;", page);
            Assert.Contains("if (ReferenceEquals(_moreMenu, menu)) _moreMenu = null;", page);

            var commands = Source("Views/ItemCommands.cs");
            Assert.Contains("Action? trailer = null", commands);
            Assert.Contains("Text = \"预告片\",", commands);
            Assert.Contains("AutomationProperties.SetAutomationId(entry, \"DetailTrailerMenuItem\");", commands);
            // 菜单是自己搭的，自检抓不到实例就只能翻浮层模板；Show 交回一张。
            Assert.Contains("public static MenuFlyout Show(", commands);
        });

        Test("详情预告片：入口只剩菜单一行，旧的点击处理器退役", () =>
        {
            var trailers = Source("Views/DetailPage.Trailers.cs");
            Assert.DoesNotContain("OnTrailersClick", trailers);
            Assert.Contains("internal async Task ShowTrailersAsync(Button anchor)", trailers);
        });
    }

    private static string Source(string relative) => File.ReadAllText(Path.Combine(
        FindRoot(), "src", "Momoka.Shell", relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new InvalidOperationException("找不到当前工作树");
    }
}
