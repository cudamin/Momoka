using System.Xml.Linq;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>本地媒体库的页面接线契约；不替代实际 WinUI 布局与 Explorer 验证。</summary>
internal static class LocalLibraryHomeTests
{
    public static void Register()
    {
        Test("本地媒体库：首页与 MP 卡片仅同步自己的导航钥匙", () =>
        {
            var home = Read("ViewModels/HomeViewModel.LocalLibraries.cs");
            var moviePilot = Read("ViewModels/HomeViewModel.MoviePilotLibrary.cs");
            Assert.False(moviePilot.Contains("var wantedKeys", StringComparison.Ordinal),
                "首页不再追加 MP 汇总卡，只创建电影和电视剧分类卡");
            Assert.True(moviePilot.Contains("var current = rowsByKey[key];", StringComparison.Ordinal));
            Assert.True(home.Contains("LocalLibraryRequest.Prefix", StringComparison.Ordinal));
            Assert.True(home.Contains("entry.IsInLibrary", StringComparison.Ordinal));
            Assert.True(home.Contains("localPosterPath:", StringComparison.Ordinal));
            Assert.True(moviePilot.Contains("!IsSubscriptionLibrary(cards[index].Item.Id)", StringComparison.Ordinal),
                "MP 更新不能摘掉其他来源的导航卡片");
            Assert.True(moviePilot.Contains("key is HomeLayout.MoviePilotMovies or HomeLayout.MoviePilotSeries or MoviePilotSubscriptionsRequest.LibraryKey", StringComparison.Ordinal),
                "MP 同步只认自己的三把导航钥匙（电影／电视剧／旧汇总），别的来源一律不碰");
            Assert.True(Read("ViewModels/HomeViewModel.cs").Contains("SyncLocalLibraries();", StringComparison.Ordinal));
        });
        Test("本地媒体库：首页通知带访问归属并在离页退订", () =>
        {
            var page = Read("Views/HomePage.xaml.cs");
            Assert.True(page.Contains("Changed += OnLocalLibrariesChanged", StringComparison.Ordinal));
            Assert.True(page.Contains("Changed -= OnLocalLibrariesChanged", StringComparison.Ordinal));
            Assert.True(page.Contains("navigation != _navigation", StringComparison.Ordinal));
            Assert.True(page.Contains("ReferenceEquals(service, _localLibraries)", StringComparison.Ordinal));
            var start = page.IndexOf("public void Release()", StringComparison.Ordinal);
            Assert.True(start >= 0, "HomePage.Release 必须存在");
            // Release 提到关方法签名即可；第一个四空格缩进的右花括号就是它的结尾（方法内只允许八空格起的花括号）。
            var release = page[start..page.IndexOf("\n    }", start, StringComparison.Ordinal)];
            Assert.True(release.Contains("Changed -= OnLocalLibrariesChanged", StringComparison.Ordinal));
        });
        Test("本地媒体库：列表独立滚动，路径与状态可读，播放未接时隐藏", () =>
        {
            var xml = XDocument.Parse(Read("Views/LocalLibraryPage.xaml"));
            XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var list = xml.Descendants(xaml + "ListView").Single();
            Assert.False(list.Ancestors(xaml + "ScrollViewer").Any(), "ListView 自身滚动，不能嵌套整页滚动器");
            var source = Read("ViewModels/LocalLibraryViewModel.cs");
            Assert.True(source.Contains("entry.IsInLibrary", StringComparison.Ordinal));
            Assert.False(source.Contains("Momoka.Emby", StringComparison.Ordinal));
            Assert.True(source.Contains("ReferenceEquals(service, _service)", StringComparison.Ordinal));
            Assert.True(source.Contains("_visit", StringComparison.Ordinal));
            Assert.True(source.Contains("_play is null", StringComparison.Ordinal));
            var row = Read("ViewModels/LocalLibraryRow.cs");
            Assert.True(row.Contains("LocalMediaOutputKind.Poster", StringComparison.Ordinal));
            Assert.True(row.Contains("output.Success", StringComparison.Ordinal));
            Assert.False(row.Contains("PosterUrl", StringComparison.Ordinal), "不能自动回退到互联网海报");
            Assert.True(row.Contains("entry.TargetPath", StringComparison.Ordinal));
            Assert.True(row.Contains("entry.Complete", StringComparison.Ordinal));
        });
        Test("本地媒体库：导航在服务器库查找之前解析本地钥匙", () =>
        {
            var shell = Read("Views/ShellPage.xaml.cs");
            var local = shell.IndexOf("tag.StartsWith(LocalLibraryRequest.Prefix", StringComparison.Ordinal);
            Assert.True(local >= 0 && local < shell.IndexOf("_libraries.TryGetValue(tag", StringComparison.Ordinal));
            Assert.True(shell.Contains("OpenLocalLibrary = id => GoTo(LocalLibraryRequest.Prefix + id)", StringComparison.Ordinal));
        });
    }

    private static string Read(string path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln")))
                return File.ReadAllText(Path.Combine(directory.FullName, "src", "Momoka.Shell", path));
            directory = directory.Parent;
        }
        throw new InvalidOperationException("未找到当前测试工作树。");
    }
}
