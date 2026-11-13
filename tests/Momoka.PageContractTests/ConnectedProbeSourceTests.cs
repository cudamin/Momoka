using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class ConnectedProbeSourceTests
{
    internal static void Register()
    {
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
            Assert.False(source.Contains("CardDetailConnectedSourcePage", StringComparison.Ordinal), "不能用未具XAML元数据的自定义Page激活");
            Assert.False(source.Contains("frame.Content = sourcePage", StringComparison.Ordinal), "直接替换Content不能证明GoBack");
        });
    }

    private static string RepositoryRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "Momoka.sln"))) return folder.FullName;
        throw new AssertionException("未找到当前测试工作树");
    }
}
