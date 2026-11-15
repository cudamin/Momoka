using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class ConnectedProbeSourceTests
{
    internal static void Register()
    {
        Test("悬停标题原生补验：真实预览点击与生产媒体库交接，不绕Prepare或Frame导航", () =>
        {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Momoka.Shell", "ShellNavigationProbe.PreviewHeading.cs"));
            Assert.Contains("frame.Navigate(typeof(LibraryPage)", source);
            Assert.Contains("source.ShowPreviewForShot()", source);
            Assert.Contains("CaptureHeading(popup.HorizontalOffset, popup.VerticalOffset", source);
            Assert.Contains("source!.TakePreviewHeading() is null", source);
            Assert.Contains("fixture.Actions.CaptureOpen = opened =>", source);
            Assert.Contains("frame.Navigate(typeof(DetailPage)", source);
            Assert.Contains("new ButtonAutomationPeer(Get<Button>(preview, \"DetailsButton\"))", source);
            Assert.DoesNotContain("PreviewHeadingTransition.Prepare(", source);
            Assert.DoesNotContain("BeginPreviewHeadingMotion(", source);
            Assert.Contains("board.SeekAlignedToLastTick(TimeSpan.Zero)", source);
            Assert.Contains("TimeSpan.FromMilliseconds(160)", source);
            Assert.Contains("RequireHeadingSettled(title, plate)", source);
            Assert.Contains("HomeMotion.ApplyPreference(false)", source);
            Assert.Contains("fixture.Actions.Plays == 0", source);
            var inspect = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Momoka.Shell", "ShellNavigationProbe.Inspect.cs"));
            Assert.Contains("\"InspectPreviewHeadingMotion\"", inspect);
        });
        Test("性能检查台：定向入口运行原详情滚动与长剧用例并保留截止", () =>
        {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Momoka.Shell", "ShellNavigationProbe.Inspect.cs"));
            Assert.Contains("AddCheck(\"InspectDetailScrollPerformance\", \"详情竖滚帧节奏\", PerformanceDetailScrollAsync, ShellProbeSections.Performance, timeoutSeconds: 60);", source);
            Assert.Contains("AddCheck(\"InspectLongSeries\", \"长剧季跳与首尾焦点\", PerformanceLongSeriesAsync, ShellProbeSections.Performance, timeoutSeconds: 90);", source);
        });
        Test("关联原生探针：现有XAML媒体库页正式导航并恢复原Page和原海报", () =>
        {
            var path = Path.Combine(RepositoryRoot(), "src", "Momoka.Shell", "ShellNavigationProbe.ConnectedAnimation.cs");
            var source = File.ReadAllText(path);
            Assert.Contains("frame.Navigate(typeof(LibraryPage), request", source);
            Assert.Contains("Descendants(wall).OfType<PosterCard>()", source);
            Assert.Contains("ReferenceEquals(frame.Content, sourcePage)", source);
            Assert.Contains("ReferenceEquals(poster, source)", source);
            Assert.Contains("frame.GoBack(new SuppressNavigationTransitionInfo())", source);
            Assert.Contains("receipt.Started && receipt.Completed", source);
            Assert.Contains("forward-artwork-changed", source);
            Assert.Contains("missing-return-source", source);
            Assert.Contains("fixture.Actions.Plays == 0", source);
            var evidence = source.IndexOf("await WritePerformanceEvidenceAsync(\"card-detail-connected.json\"", StringComparison.Ordinal);
            var screen = source.IndexOf("await SaveProbeScreenAsync(\"card-detail-connected-forward-screen.png\")", StringComparison.Ordinal);
            Assert.True(evidence >= 0 && screen > evidence, "原生回执应先落盘，再用临时置顶且还原的实屏截图路径");
            Assert.False(source.Contains("CompositionPlaybackProbe.SaveScreenAsync", StringComparison.Ordinal), "不能绕过统一截图路径的遮挡准备和还原");
            Assert.False(source.Contains("CardDetailConnectedSourcePage", StringComparison.Ordinal), "不能用未具XAML元数据的自定义Page激活");
            Assert.False(source.Contains("frame.Content = sourcePage", StringComparison.Ordinal), "直接替换Content不能证明GoBack");
        });
        Test("悬浮卡片原生探针：核对真实客户区与页面宽，拒绝VM目标宽冒充视口", () =>
        {
            var path = Path.Combine(RepositoryRoot(), "src", "Momoka.Shell", "ShellNavigationProbe.DetailStickyHeader.cs");
            var source = File.ReadAllText(path);
            var resize = source.IndexOf("await ResizeResponsiveAsync(page, width, 800);", StringComparison.Ordinal);
            var actual = source.IndexOf("Math.Abs(page.ActualWidth - width) < 1", resize, StringComparison.Ordinal);
            var capture = source.IndexOf("await SaveProbeScreenAsync", actual, StringComparison.Ordinal);
            Assert.True(resize >= 0 && actual > resize && capture > actual);
            Assert.Contains("_window!.ClientSize.Width / _root.XamlRoot.RasterizationScale - width", source);
            Assert.Contains("ClientWidth = _window!.ClientSize.Width / _root.XamlRoot.RasterizationScale", source);
            Assert.Contains("PageWidth = page.ActualWidth", source);
        });
    }

    private static string RepositoryRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "Momoka.sln"))) return folder.FullName;
        throw new AssertionException("未找到当前测试工作树");
    }
}
