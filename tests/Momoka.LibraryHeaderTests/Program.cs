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
                Assert.Equal("EgSectionTitleStyle", page.LibraryTitle.Style!.Key);
                Assert.Equal(HorizontalAlignment.Left, page.ToolbarSurface.HorizontalAlignment);
            });
        foreach (var kind in new[] { MediaServerKind.FnNas, MediaServerKind.Plex })
            Test($"{kind}继续观看恢复原来的页头规则", () =>
            {
                var page = new LibraryPage();
                page.ViewModel.SourceServerKind = kind;
                page.Apply(resume: true);
                Assert.Equal(kind == MediaServerKind.FnNas ? 20d : 0d, page.EmbyContent.Margin.Top);
                Assert.Equal(kind == MediaServerKind.FnNas ? "EgSectionTitleStyle" : "EgPageTitleStyle", page.LibraryTitle.Style!.Key);
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
            Assert.Equal("EgSectionTitleStyle", page.LibraryTitle.Style!.Key);
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
            Assert.Equal(Visibility.Collapsed, page.LibraryTitle.Visibility);
            Assert.Equal(900d, page.SearchSurface.Width);
            Assert.Equal(480d, page.SourceTabs.Width);
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
        Test("详情音频标签保留中文冒号", () =>
        {
            var document = XDocument.Load(Path.Combine(Root(), "src/Momoka.Shell/Strings/zh-Hans/Resources.resw"));
            var value = document.Root!.Elements("data")
                .Single(element => (string?)element.Attribute("name") == "DetailPage_TextBlock3.Text").Element("value")!.Value;
            Assert.Equal("音频：", value);
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
