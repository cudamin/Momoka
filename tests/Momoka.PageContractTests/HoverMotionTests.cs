using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 悬停放大（2026-11-13）：卡片整卡抬一档、预览面板换弹簧撑开。两处都参考 WinUI Gallery 的
/// <c>Samples/XamlCompInterop</c>（固定提交 6de91fe）。这里钉住参数与实现路径，防止被悄悄改回
/// 「整卡不放大」或退回一段 keyframe；实际手感、遮挡和减弱动效下的落定仍靠运行截图与探针。
/// </summary>
internal static class HoverMotionTests
{
    internal static void Register()
    {
        Test("悬停放大：卡片整卡抬到 1.03，抬起用 BackEase 过冲", () =>
        {
            var source = Read("src/Momoka.Shell/Views/PosterCard.xaml.cs");
            Assert.Contains("private const double HoverScale = 1.03;", source);
            Assert.Contains("var lifted = _hovered || _previewOpen;", source);
            Assert.Contains("new BackEase", source);
            Assert.Contains("XamlCompInterop", source);
            Assert.Contains("6de91fe", source);
        });

        Test("悬停预览：Popup独立于缩放卡片，窗口坐标不再次乘祖先缩放", () =>
        {
            var source = Read("src/Momoka.Shell/Views/PosterCard.xaml.cs");
            Assert.DoesNotContain("Root.Children.Add(_previewHost);", source);
            Assert.Contains("width, rootWidth, PreviewMargin) + art.Left", source);
            Assert.Contains("height, rootHeight, PreviewMargin) + art.Top", source);
        });

        Test("主页命中探针：显式打开分隔线后量边界，保留原主页无通栏线检查", () =>
        {
            var source = Read("src/Momoka.Shell/ShellNavigationProbe.HomeHeader.cs");
            Assert.Contains("!initialRule && rule.Visibility == Visibility.Collapsed", source);
            Assert.True(source.IndexOf("head.ShowRule = true;", StringComparison.Ordinal)
                < source.IndexOf("!HitsScope(Center(rule))", StringComparison.Ordinal));
            Assert.Contains("head.ShowRule = initialRule;", source);
            Assert.Contains("负向检查没有检测到整行点击回归", source);
            Assert.Contains("Get<Grid>(card, \"Root\").IsHitTestVisible = false;", source);
            Assert.Contains("Call(card, \"ResetMotion\");", source);
            Assert.Contains("对齐检查未检出旧的居中布局", source);
        });
        Test("外壳队列：初始空ViewModel安全隐藏，独占Detach后保留队列", () =>
        {
            var source = Read("src/Momoka.Shell/Views/ShellPage.xaml.cs");
            var start = source.IndexOf("bool IShellActions.CanQueueVideo(", StringComparison.Ordinal);
            var end = source.IndexOf("/// <summary>进播放前", start, StringComparison.Ordinal);
            var queue = source[start..end];
            Assert.DoesNotContain("Player.Attached", queue);
            Assert.Contains("Player.ViewModel?.CanQueue(item) == true", queue);
            Assert.Contains("Player.ViewModel?.Queue(item, next) == true", queue);
            Assert.Contains("Player.ViewModel?.HasPendingQueue == true", queue);
            Assert.Contains("Player.ViewModel?.CanResumeQueue == true", queue);
            Assert.Contains("Player.ViewModel is not { CanResumeQueue: true } player", queue);
            Assert.Contains("Player.ViewModel?.ClearQueue()", queue);
            Assert.Contains("Player.ViewModel is { } player", queue);
            var page = Read("src/Momoka.Shell/Views/PlayerPage.xaml.cs");
            var detach = page[page.IndexOf("internal void Detach()", StringComparison.Ordinal)..];
            detach = detach[..detach.IndexOf("// ---- 输出尺寸", StringComparison.Ordinal)];
            Assert.DoesNotContain("ViewModel = null", detach);
        });
        Test("整理菜单探针：保留真实菜单验收并记录激活结果，不强求前台", () =>
        {
            var source = Read("src/Momoka.Shell/ShellNavigationProbe.SeriesResourceBrowser.cs");
            Assert.DoesNotContain("UntilAsync(() => Native.GetForegroundWindow() == handle)", source);
            Assert.Contains("mp-series-menu-activation.txt", source);
            Assert.Contains("UntilAsync(() => menu.IsOpen)", source);
            Assert.Contains("整理清单菜单超出窗口可用边界", source);
            Assert.Contains("菜单缺少运行时UIA句柄或名称", source);
        });

        Test("标题栏自检：切换前后取同一真实拖动点，避开按钮洞", () =>
        {
            var run = Read("src/Momoka.Shell/ShellSelfCheck.Run.cs");
            var frame = Read("src/Momoka.Shell/ShellSelfCheck.Frame.cs");
            Assert.DoesNotContain("HitAt(window, 8, 16)", run);
            Assert.Contains("CaptionDragPoint(window)", run);
            Assert.Contains("window.TitleBarMetrics.Height / scale - 2", frame);
            Assert.Contains("captionBefore.Hit == Native.HitCaption", frame);
            Assert.Contains("HitAt(window, captionBefore.Point.X, captionBefore.Point.Y) == Native.HitCaption", frame);
            Assert.Contains("centre == Native.HitClient", frame);
            Assert.Contains("beyond == Native.HitCaption", frame);
        });
        Test("悬停放大：预览面板用弹簧驱动 Progress，不再用关键帧", () =>
        {
            var source = Read("src/Momoka.Shell/Views/CardPreview.xaml.cs");
            Assert.Contains("CreateSpringScalarAnimation()", source);
            Assert.Contains("spring.Target = \"Progress\";", source);
            Assert.Contains("spring.DampingRatio", source);
            Assert.Contains("spring.Period", source);
            Assert.Contains("6de91fe", source);
            Assert.DoesNotContain("CreateScalarKeyFrameAnimation", source);
        });
    }

    private static string Read(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Momoka.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory, "找不到方案根目录");
        return File.ReadAllText(Path.Combine(directory!.FullName, relativePath));
    }
}
