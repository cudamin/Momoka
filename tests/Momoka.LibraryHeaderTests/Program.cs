using Microsoft.UI.Xaml;
using Momoka.Emby;
using Momoka.Shell.Views;
using System.Xml.Linq;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        Test("点标题框只导航一次并备好关联动画", () =>
        {
            TitleSearchTransition.Prepared = 0;
            var shell = new ShellPage();
            shell.FocusTitle();
            Assert.Equal(1, shell.Navigations);
            Assert.Equal(1, TitleSearchTransition.Prepared);
            shell.FocusTitle();
            Assert.Equal(1, shell.Navigations);
        });
        Test("搜索页输入框与来源栏常驻，页头恢复40DIP安全区", () =>
        {
            var page = new LibraryPage();
            page.ViewModel.MoviePilotTabsVisibility = Visibility.Visible;
            page.Apply(search: true);
            Assert.Equal(Visibility.Visible, page.SearchEntry.Visibility);
            Assert.Equal(0d, page.SearchForm.RowSpacing);
            Assert.Equal(8d, page.SourceTabs.Margin.Top);
            Assert.Equal(40d, page.PageRoot.Margin.Top);
        });
        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
            Test($"{kind}继续观看保留小标题和28DIP间距", () =>
            {
                var page = new LibraryPage();
                page.ViewModel.SourceServerKind = kind;
                page.Apply(resume: true);
                Assert.Equal(0d, page.PageRoot.RowSpacing);
                Assert.Equal(0d, page.HeaderGrid.RowSpacing);
                Assert.Equal(0d, page.HeaderLayout.RowSpacing);
                Assert.Equal(0d, ((Microsoft.UI.Xaml.Controls.Grid)page.UpcomingScroll.Parent!).RowSpacing);
                Assert.Equal(28d, page.EmbyContent.Margin.Top);
                Assert.Equal(12d, page.ToolbarSurface.Margin.Top);
                Assert.Equal(12d, page.LibraryNoticeBar.Margin.Top);
                Assert.Equal("EgPageTitleStyle", page.LibraryTitle.Style!.Key);
                Assert.Equal(HorizontalAlignment.Left, page.ToolbarSurface.HorizontalAlignment);
            });
        foreach (var kind in new[] { MediaServerKind.FnNas, MediaServerKind.Plex })
            Test($"{kind}继续观看恢复原来的页头规则", () =>
            {
                var page = new LibraryPage();
                page.ViewModel.SourceServerKind = kind;
                page.Apply(resume: true);
                Assert.Equal(kind == MediaServerKind.FnNas ? 20d : 0d, page.EmbyContent.Margin.Top);
                Assert.Equal("EgPageTitleStyle", page.LibraryTitle.Style!.Key);
                Assert.Equal(kind == MediaServerKind.FnNas ? 0d : 20d, page.PageRoot.RowSpacing);
                Assert.Equal(HorizontalAlignment.Left, page.ToolbarSurface.HorizontalAlignment);
            });
        Test("Emby和Jellyfin普通库恢复20DIP间距并保持居中", () =>
        {
            foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
            {
                var page = new LibraryPage();
                page.ViewModel.SourceServerKind = kind;
                page.ViewModel.IsSectionLibrary = true;
                page.Apply();
                Assert.Equal(20d, page.EmbyContent.Margin.Top);
                Assert.Equal(HorizontalAlignment.Center, page.LibrarySectionsSurface.HorizontalAlignment);
                Assert.Equal(HorizontalAlignment.Center, page.ToolbarSurface.HorizontalAlignment);
                Assert.True(page.Toolbar.CenterRows);
            }
        });
        Test("收藏与预告恢复原有4DIP外层行距", () =>
        {
            foreach (var upcoming in new[] { false, true })
            {
                var page = new LibraryPage();
                page.ViewModel.IsSectionLibrary = true;
                page.ViewModel.IsFavoritesPage = !upcoming;
                page.ViewModel.IsUpcomingPage = upcoming;
                page.Apply();
                Assert.Equal(4d, page.PageRoot.RowSpacing);
                Assert.Equal(12d, page.HeaderLayout.RowSpacing);
                Assert.Equal(0d, page.EmbyContent.Margin.Top);
            }
        });
        Test("普通浏览与继续观看来回切换不会遗留小标题和新间距", () =>
        {
            var page = new LibraryPage();
            page.Apply(resume: true);
            page.Apply();
            Assert.Equal(20d, page.PageRoot.RowSpacing);
            Assert.Equal(16d, page.HeaderGrid.RowSpacing);
            Assert.Equal(0d, page.EmbyContent.Margin.Top);
            Assert.Equal("EgPageTitleStyle", page.LibraryTitle.Style!.Key);
            page.Apply(resume: true);
            Assert.Equal(28d, page.EmbyContent.Margin.Top);
            Assert.Equal("EgPageTitleStyle", page.LibraryTitle.Style!.Key);
        });
        // 2026-11-12 用户令：参考 emby 媒体库左上角标题的大小，统一其他库左上角标题的大小。
        // 从前分类库、飞牛库根、Jellyfin list、继续观看各落到 20 那一档；现在库页只认 36 这一档。
        Test("所有库页标题同一档（Emby 媒体库那一档）", () =>
        {
            foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin, MediaServerKind.FnNas, MediaServerKind.Plex })
            {
                foreach (var section in new[] { false, true })
                {
                    var page = new LibraryPage();
                    page.ViewModel.SourceServerKind = kind;
                    page.ViewModel.IsSectionLibrary = section;
                    page.Apply();
                    Assert.Equal("EgPageTitleStyle", page.LibraryTitle.Style!.Key);

                    var resume = new LibraryPage();
                    resume.ViewModel.SourceServerKind = kind;
                    resume.Apply(resume: true);
                    Assert.Equal("EgPageTitleStyle", resume.LibraryTitle.Style!.Key);
                }
            }
        });
        Test("文件夹恢复原有面包屑和居中工具条", () =>
        {
            var page = new LibraryPage();
            page.ViewModel.IsFolderDrillDown = true;
            page.Apply();
            Assert.Equal(Visibility.Collapsed, page.LibraryTitle.Visibility);
            Assert.Equal(12d, page.ToolbarSurface.Margin.Top);
            Assert.Equal(0, page.ToolbarSurface.Row);
            Assert.Equal(20d, page.EmbyContent.Margin.Top);
            Assert.Equal(HorizontalAlignment.Center, page.ToolbarSurface.HorizontalAlignment);
        });
        Test("搜索保留40DIP安全区和8DIP内容间距", () =>
        {
            var page = new LibraryPage();
            page.Apply(search: true);
            Assert.Equal(40d, page.PageRoot.Margin.Top);
            Assert.Equal(8d, page.EmbyContent.Margin.Top);
            Assert.Equal(-page.PageRoot.Margin.Right, page.EmbyContent.Margin.Right);
            Assert.Equal(Visibility.Collapsed, page.LibraryTitle.Visibility);
            Assert.Equal(900d, page.SearchSurface.Width);
            // 五个来源平分这一条；上限从四个时的 480 提到 600，单格宽度不变。
            Assert.Equal(600d, page.SourceTabs.Width);
        });
        Test("Attach未结束或布局未测量时不提前改动页头", () =>
        {
            var page = new LibraryPage();
            page.Apply(attaching: true);
            Assert.Equal(0, page.HeaderApplies);
            page.HeaderLayout.ActualWidth = 0;
            page.Apply();
            Assert.Equal(0, page.HeaderApplies);
        });
        Test("详情音频与字幕标签保留中文冒号", () =>
        {
            var document = XDocument.Load(Path.Combine(Root(), "src/Momoka.Shell/Strings/zh-Hans/Resources.resw"));
            string Value(string name) => document.Root!.Elements("data")
                .Single(element => (string?)element.Attribute("name") == name).Element("value")!.Value;
            Assert.Equal("音频：", Value("DetailPage_TextBlock3.Text"));
            Assert.Equal("字幕：", Value("DetailPage_TextBlock4.Text"));
        });
        AgreementsTests.Register();
        return Run();
    }

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new InvalidOperationException("找不到当前工作树");
    }
}
