using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Shell;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>Actual production diagnostic predicates; malformed readings cannot self-approve.</summary>
internal static class SelfCheckLayoutRulesTests
{
    public static void Register()
    {
        Test("自检布局：九种真实视图与原选择恢复", () =>
        {
            Assert.True(SelfCheckLayoutRules.LibraryViews(Views(), true, CardSize.Chrome));
            Assert.False(SelfCheckLayoutRules.LibraryViews(Views(), false, CardSize.Chrome));
        });
        Test("自检布局：概要行统一保留且不能各视图漂移", () =>
        {
            Assert.True(SelfCheckLayoutRules.LibraryViews(Views(18), true, CardSize.Chrome + 18));
            var readings = Views();
            readings[(int)LibraryView.Thumb] = (readings[(int)LibraryView.Thumb].Label, readings[(int)LibraryView.Thumb].Layout, readings[(int)LibraryView.Thumb].Cell, readings[(int)LibraryView.Thumb].Row + 18);
            Assert.False(SelfCheckLayoutRules.LibraryViews(readings, true, CardSize.Chrome));
        });
        foreach (var view in Enum.GetValues<LibraryView>())
        {
            Test($"自检布局：缺少或错序的 {view} 不能通过", () =>
            {
                var readings = Views();
                Assert.False(SelfCheckLayoutRules.LibraryViews(readings.Where((_, index) => index != (int)view).ToArray(), true, CardSize.Chrome));
                readings[(int)view] = ("错误视图", readings[(int)view].Layout, readings[(int)view].Cell, readings[(int)view].Row);
                Assert.False(SelfCheckLayoutRules.LibraryViews(readings, true, CardSize.Chrome));
            });
            Test($"自检布局：{view} 布局类型或行高损坏不能通过", () =>
            {
                var readings = Views();
                var original = readings[(int)view];
                readings[(int)view] = (original.Label, "错误布局", original.Cell, original.Row);
                Assert.False(SelfCheckLayoutRules.LibraryViews(readings, true, CardSize.Chrome));
                readings[(int)view] = (original.Label, original.Layout, original.Cell, original.Row + 4);
                Assert.False(SelfCheckLayoutRules.LibraryViews(readings, true, CardSize.Chrome));
            });
        }
        Test("自检布局：多余视图和无效格宽不能通过", () =>
        {
            var readings = Views();
            Assert.False(SelfCheckLayoutRules.LibraryViews([.. readings, readings[0]], true, CardSize.Chrome));
            foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, 0, -1, 170.5 })
            {
                var changed = Views();
                changed[0] = (changed[0].Label, changed[0].Layout, invalid, changed[0].Row);
                Assert.False(SelfCheckLayoutRules.LibraryViews(changed, true, CardSize.Chrome));
            }
        });
        Test("自检布局：两种卡片视图必须保留16像素底板空间", () =>
        {
            foreach (var view in new[] { LibraryView.PosterCard, LibraryView.ThumbCard })
            {
                var readings = Views();
                readings[(int)view] = (readings[(int)view].Label, readings[(int)view].Layout, readings[(int)view].Cell, readings[(int)view].Row - LibraryCardMetrics.CardFrameChrome);
                Assert.False(SelfCheckLayoutRules.LibraryViews(readings, true, CardSize.Chrome));
            }
        });
        Test("自检布局：模型正确但实际MinItemHeight仍旧时拒绝", () =>
        {
            var readings = Views(18);
            Assert.False(SelfCheckLayoutRules.LibraryViews(readings, true, CardSize.Chrome));
            Assert.True(SelfCheckLayoutRules.LibraryViews(readings, true, CardSize.Chrome + 18));
            foreach (var view in new[] { LibraryView.PosterCard, LibraryView.ThumbCard })
            {
                var stale = Views();
                var current = stale[(int)view];
                stale[(int)view] = (current.Label, current.Layout, current.Cell,
                    current.Row - LibraryCardMetrics.CardFrameChrome);
                Assert.False(SelfCheckLayoutRules.LibraryViews(stale, true, CardSize.Chrome));
            }
        });
        Test("自检HDR：SDR映射精确四项，HDR和自动精确五项", () =>
        {
            Assert.True(SelfCheckLayoutRules.HdrNumbers(Hdr(false), true));
            Assert.True(SelfCheckLayoutRules.HdrNumbers(Hdr(true), false));
            Assert.False(SelfCheckLayoutRules.HdrNumbers(Hdr(true), true));
            Assert.False(SelfCheckLayoutRules.HdrNumbers(Hdr(false), false));
        });
        foreach (var label in Hdr(true))
        {
            Test($"自检HDR：缺少 {label} 或重复替代不能通过", () =>
            {
                var labels = Hdr(true);
                Assert.False(SelfCheckLayoutRules.HdrNumbers(labels.Where(value => value != label).ToArray(), false));
                Assert.False(SelfCheckLayoutRules.HdrNumbers([.. labels.Where(value => value != label), labels.First(value => value != label)], false));
            });
        }
        Test("自检底板：当前无描边仍有底板，旧1px框和缺边均拒绝", () =>
        {
            Assert.True(SelfCheckLayoutRules.PanelBorder(0, 0, 0, 0, 0, 0, 0, 0));
            Assert.False(SelfCheckLayoutRules.PanelBorder(1, 1, 1, 1, 0, 0, 0, 0));
            Assert.True(SelfCheckLayoutRules.PanelBorder(1, 1, 1, 1, 1, 1, 1, 1));
            Assert.False(SelfCheckLayoutRules.PanelBorder(1, 0, 1, 1, 1, 1, 1, 1));
            Assert.False(SelfCheckLayoutRules.PanelBorder(double.NaN, 0, 0, 0, 0, 0, 0, 0));
        });
        Test("表格探针：先等待真实窄视口，再保留画布与横向偏移断言", () =>
        {
            var source = File.ReadAllText(Path.Combine(Root(), "src/Momoka.Shell/ShellNavigationProbe.LibraryBrowse.cs"));
            var marker = source.IndexOf("// 表格视图的横向滚动需要", StringComparison.Ordinal);
            var apply = source.IndexOf("page.ViewModel.ApplyDisplay(new LibraryDisplay", marker, StringComparison.Ordinal);
            Assert.True(marker >= 0 && apply > marker);
            var preparation = source[marker..apply];
            Assert.Contains("ResizeProbeClient(900, 700);", preparation);
            Assert.Contains("await UntilAsync(() => _window is not null", preparation);
            Assert.Contains("_window.ClientSize.Width / _root.XamlRoot.RasterizationScale - 900", preparation);
            Assert.Contains("Math.Abs(page.ActualWidth - 900) < 1", preparation);
            Assert.Contains("page.TableCanvas.ActualWidth > page.TableScroll.ActualWidth + 1", source);
            Assert.Contains("page.TableScroll.HorizontalOffset > 1", source);
            var fields = new[] { LibraryField.Title, LibraryField.Genres, LibraryField.Overview,
                LibraryField.Director, LibraryField.Tagline, LibraryField.Studios };
            var width = LibraryTableColumns.TotalWidth(fields);
            Assert.True(width > 900 && width < 1560,
                "同一表格在真实900视口应溢出，在旧1560视口不溢出是合法的，不能放宽原断言");
        });
        Test("探针收尾：飞牛库释放页面并移除本次Frame，不污染后续服务器页", () =>
        {
            var source = File.ReadAllText(Path.Combine(Root(), "src/Momoka.Shell/ShellNavigationProbe.FnNasLibrary.cs"));
            var begin = source.IndexOf("    private async Task FnNasLibrarySpacingAsync()", StringComparison.Ordinal);
            var end = source.IndexOf("    private static double PosterTop", begin, StringComparison.Ordinal);
            Assert.True(begin >= 0 && end > begin);
            var method = source[begin..end];
            Assert.Contains("try { (frame.Content as IShellContent)?.Release(); }", method);
            Assert.Contains("frame.Content = null;", method);
            Assert.Contains("_root.Children.Remove(frame);", method);
            Assert.True(method.IndexOf("_root.Children.Remove(frame);", StringComparison.Ordinal)
                > method.IndexOf("finally", StringComparison.Ordinal));
        });
        Test("自检接线：真实调用者使用当前判据且基线检查名未退役", () =>
        {
            var root = Root();
            var pages = File.ReadAllText(Path.Combine(root, "src/Momoka.Shell/ShellSelfCheck.Pages.cs"));
            var settings = File.ReadAllText(Path.Combine(root, "src/Momoka.Shell/ShellSelfCheck.Settings.cs"));
            var reads = File.ReadAllText(Path.Combine(root, "src/Momoka.Shell/ShellSelfCheck.Reads.cs"));
            var library = File.ReadAllText(Path.Combine(root, "src/Momoka.Shell/Views/LibraryPage.xaml.cs"));
            var detail = File.ReadAllText(Path.Combine(root, "src/Momoka.Shell/Views/DetailPage.xaml.cs"));
            var style = File.ReadAllText(Path.Combine(root, "src/Momoka.Shell/Theme/Styles.xaml"));
            var baseline = File.ReadAllText(Path.Combine(root, "docs/selfcheck-baseline.txt"));
            Assert.Contains("SelfCheckLayoutRules.LibraryViews(walk, library.ViewRestored, library.GridTextHeight)", pages);
            Assert.Contains("ViewModel.CellWidth, grid.MinItemHeight)", library);
            Assert.Contains("library.ViewModel.Fields.Contains(LibraryField.Tagline)", reads);
            Assert.Contains("library.ViewModel.Fields.Contains(LibraryField.Overview)", reads);
            Assert.Contains("SelfCheckLayoutRules.HdrNumbers(", settings);
            // 2026-10-25 移除了页面级卡片描边，集带那条自检从「有边框」改成「无静止描边」，判据换成 Rings；
            // 次序与紧凑首屏仍查，所以这条接线检查还在，只是钉的源码换成了现在这一份。
            Assert.Contains("var bareFrame = !Rings(EpisodePanel);", detail);
            Assert.Contains("panel.Background is SolidColorBrush { Color.A: > 0 }", detail);
            Assert.Contains("ok &= order && below && bareFrame;", detail);
            Assert.Contains("<Thickness x:Key=\"EgCardBorderThickness\">0</Thickness>", style);
            foreach (var name in new[] { "媒体库七种视图的布局", "视频输出包含 HDR 与着色器", "集页首屏（文件页）" })
                Assert.Contains("通过  " + name, baseline);
        });
    }

    private static (string Label, string Layout, double Cell, double Row)[] Views(int noteHeight = 0) =>
        Enum.GetValues<LibraryView>().Select(view =>
        {
            if (view is LibraryView.List or LibraryView.Table)
                return (LibraryDisplayCatalog.LabelOf(view), "StackLayout", 0d, 0d);
            var width = view is LibraryView.Poster or LibraryView.PosterCard or LibraryView.Disc ? 192 : 330;
            var frame = view is LibraryView.PosterCard or LibraryView.ThumbCard ? LibraryCardMetrics.CardFrameChrome : 0;
            var height = LibraryCardMetrics.Height(view, width) + CardSize.Chrome + noteHeight + frame;
            return (LibraryDisplayCatalog.LabelOf(view), "UniformGridLayout", (double)width, height);
        }).ToArray();

    private static string[] Hdr(bool peak) => peak
        ? ["HDR 输出峰值亮度", "HDR 参考白亮度", "文字字幕与 OSD 亮度", "图形字幕亮度", "HDR 对比度恢复"]
        : ["HDR 参考白亮度", "文字字幕与 OSD 亮度", "图形字幕亮度", "HDR 对比度恢复"];

    private static string Root()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Momoka.sln"))) folder = folder.Parent;
        return folder?.FullName ?? throw new InvalidOperationException("本树源码缺失");
    }
}
