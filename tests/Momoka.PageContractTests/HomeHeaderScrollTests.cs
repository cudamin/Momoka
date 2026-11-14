using System.Xml.Linq;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class HomeHeaderScrollTests
{
    internal static void Register()
    {
        Test("沉浸页眉：顶端透明、下滚隐藏、停住仍隐藏、反向上滚恢复", () =>
        {
            var state = new HomeHeaderScrollState();
            state.Reset(true, 0);
            Assert.Equal(HomeHeaderMode.Top, state.Mode);
            state.Update(true, 150);
            Assert.Equal(HomeHeaderMode.Hidden, state.Mode);
            state.Update(true, 150);
            Assert.Equal(HomeHeaderMode.Hidden, state.Mode);
            state.Update(true, 134);
            Assert.Equal(HomeHeaderMode.Revealed, state.Mode);
            state.Update(true, 0);
            Assert.Equal(HomeHeaderMode.Top, state.Mode);
        });

        Test("沉浸页眉：同向细小滚动累计，反向抖动不会闪现", () =>
        {
            var state = new HomeHeaderScrollState();
            state.Reset(true, 0);
            for (var offset = 1; offset <= 24; offset++) state.Update(true, offset);
            Assert.Equal(HomeHeaderMode.Hidden, state.Mode);
            foreach (var offset in new[] { 23, 22, 24, 21, 23 }) state.Update(true, offset);
            Assert.Equal(HomeHeaderMode.Hidden, state.Mode);
            for (var offset = 23; offset >= 12; offset--) state.Update(true, offset);
            Assert.Equal(HomeHeaderMode.Revealed, state.Mode);
            foreach (var offset in new[] { 13, 14, 12, 15, 13 }) state.Update(true, offset);
            Assert.Equal(HomeHeaderMode.Revealed, state.Mode);
            state.Update(true, 24);
            Assert.Equal(HomeHeaderMode.Hidden, state.Mode);
        });

        Test("沉浸页眉：上滚中停住或横向滚动不改变显现状态", () =>
        {
            var state = new HomeHeaderScrollState();
            state.Reset(true, 200);
            for (var index = 0; index < 20; index++) state.Update(true, 200);
            Assert.Equal(HomeHeaderMode.Revealed, state.Mode);
            state.Update(true, 190);
            state.Update(true, 199);
            Assert.Equal(HomeHeaderMode.Revealed, state.Mode);
            state.Update(true, 203);
            Assert.Equal(HomeHeaderMode.Hidden, state.Mode);
        });

        Test("沉浸页眉：返回深处显示操作与背景，恢复不是向下手势", () =>
        {
            var state = new HomeHeaderScrollState();
            state.Reset(true, 0);
            state.Reset(true, 600);
            Assert.Equal(HomeHeaderMode.Revealed, state.Mode);
            state.Update(true, 600);
            Assert.Equal(HomeHeaderMode.Revealed, state.Mode);
            state.Update(true, 620);
            Assert.Equal(HomeHeaderMode.Hidden, state.Mode);
            state.Reset(true, 620);
            Assert.Equal(HomeHeaderMode.Revealed, state.Mode);
        });

        Test("沉浸页眉：离开沉浸后立即撤销隐藏，商店滚动不隐藏", () =>
        {
            var state = new HomeHeaderScrollState();
            state.Reset(true, 0);
            state.Update(true, 300);
            state.Update(false, 300);
            Assert.Equal(HomeHeaderMode.Top, state.Mode);
            state.Update(false, 600);
            state.Update(false, 400);
            Assert.Equal(HomeHeaderMode.Top, state.Mode);
            state.Update(true, 400);
            Assert.Equal(HomeHeaderMode.Revealed, state.Mode);
        });

        Test("沉浸页眉：触顶立即恢复、容忍浮点误差和越界，非法读数不污染方向", () =>
        {
            var state = new HomeHeaderScrollState();
            state.Reset(true, 0);
            state.Update(true, 300);
            state.Update(true, double.NaN);
            state.Update(true, double.PositiveInfinity);
            Assert.Equal(HomeHeaderMode.Hidden, state.Mode);
            state.Update(true, 0.25);
            Assert.Equal(HomeHeaderMode.Top, state.Mode);
            state.Update(true, -8);
            Assert.Equal(HomeHeaderMode.Top, state.Mode);
            state.Update(true, 1);
            Assert.Equal(HomeHeaderMode.Revealed, state.Mode);
            state.Reset(false, 0);
            Assert.Equal(HomeHeaderMode.Top, state.Mode);
        });

        Test("沉浸页眉：亚克力是全宽独立背景、默认收起且不接管输入", () =>
        {
            var xaml = XDocument.Load(Path.Combine(Root(), "src/Momoka.Shell/Views/ShellPage.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var background = xaml.Descendants().Single(node => node.Attribute(x + "Name")?.Value == "HomeHeaderBackground");
            Assert.Equal("Chrome", background.Parent?.Attribute(x + "Name")?.Value);
            Assert.Equal("48", background.Attribute("Height")?.Value);
            Assert.Equal("{ThemeResource EgFrostBrush}", background.Attribute("Background")?.Value);
            Assert.Equal("False", background.Attribute("IsHitTestVisible")?.Value);
            Assert.Equal("Collapsed", background.Attribute("Visibility")?.Value);
            Assert.True(background.Attribute("Margin") is null && background.Attribute("Width") is null);
            Assert.True(background.ElementsBeforeSelf().All(node => node.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal)));
        });

        Test("沉浸页眉：订阅、恢复完成和离页复位都接到当前页面", () =>
        {
            var page = Source("Views/HomePage.xaml.cs");
            Assert.Contains("Scroller.ViewChanged += OnHeaderViewChanged", page);
            Assert.Contains("Scroller.ScrollCompleted += OnHeaderScrollCompleted", page);
            var release = page[page.IndexOf("public void Release()", StringComparison.Ordinal)..];
            Assert.Contains("_headerScroll.Reset(false, 0)", release);
            Assert.Contains("_actions?.RefreshHomeHeader()", release);
            var header = Source("Views/HomePage.Header.cs");
            Assert.Contains("if (_released) return", header);
            Assert.Contains("args.CorrelationId != _headerRestoreCorrelation", header);
            Assert.Contains("_reflowingHeader || _restoringHeaderScroll || _restoredScroll is not null", header);
            var shell = Source("Views/ShellPage.HomeHeader.cs");
            Assert.Contains("ContentFrame.Content is HomePage home", shell);
            Assert.Contains("AppTitleBar.Visibility = hidden ? Visibility.Collapsed", shell);
            Assert.Contains("AccountCluster.Visibility = !hidden", shell);
            Assert.Contains("AccountMenu.Hide()", shell);
            Assert.Contains("FocusHeaderScroll()", shell);
            Assert.Contains("Scroller.IsTabStop = true", header);
            Assert.Contains("finally { Scroller.IsTabStop = tabStop; }", header);
            Assert.Contains("_window.BrowseTitleBarHidden = hidden", shell);
        });

        Test("沉浸页眉：系统三键与播放各自持有状态，隐藏仍保留整条拖动区", () =>
        {
            var window = Source("Windowing/HostWindow.cs");
            Assert.Contains("titleBar.PreferredHeightOption = _playbackTitleBar || _browseTitleBarHidden", window);
            Assert.Contains("var dragRight = _browseTitleBarHidden ? width", window);
            Assert.Contains("var holes = _playbackTitleBar || _browseTitleBarHidden ? []", window);
            Assert.Contains("var (minimise, maximise, close) = _playbackTitleBar || _browseTitleBarHidden", window);
            Assert.Contains("_playbackTitleBar ? [Empty] : CaptionAround(holes, dragRight, height)", window);
            var shell = Source("Views/ShellPage.xaml.cs");
            Assert.Contains("if (_homeHeaderHidden)", shell);
            Assert.Contains("_window.SetTitleBarHoles()", shell);
        });
    }

    private static string Source(string file) => File.ReadAllText(Path.Combine(Root(), "src/Momoka.Shell", file));

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new InvalidOperationException("本树源码缺失");
    }
}
