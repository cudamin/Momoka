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
        Test("标题输入一次导航，当前页输入与回车不重建来源", () =>
        {
            var shell = new ShellPage();
            var title = new Microsoft.UI.Xaml.Controls.AutoSuggestBox();
            shell.FocusTitle();
            var page = shell.Page!;
            title.Text = "搜索词";
            shell.TypeTitle(title, new() { Reason = Microsoft.UI.Xaml.Controls.AutoSuggestionBoxTextChangeReason.UserInput });
            Assert.Equal(1, page.TypingCalls);
            shell.QueryTitle(title, title.Text);
            Assert.Equal(1, page.Submissions);
            Assert.Equal(1, shell.Navigations);
            Assert.True(ReferenceEquals(page, shell.Page));
        });
        Test("回车后的同词延迟通知及程序同步不重新清空查询", () =>
        {
            var shell = new ShellPage();
            shell.FocusTitle();
            var title = new Microsoft.UI.Xaml.Controls.AutoSuggestBox { Text = "立即回车" };
            shell.QueryTitle(title, title.Text);
            shell.TypeTitle(title, new() { Reason = Microsoft.UI.Xaml.Controls.AutoSuggestionBoxTextChangeReason.UserInput });
            title.Text = "程序同步词";
            shell.TypeTitle(title, new() { Reason = Microsoft.UI.Xaml.Controls.AutoSuggestionBoxTextChangeReason.ProgrammaticChange });
            shell.TypeTitle(title, new() { Reason = Microsoft.UI.Xaml.Controls.AutoSuggestionBoxTextChangeReason.UserInput, Current = false });
            Assert.Equal(0, shell.Page!.TypingCalls);
            Assert.Equal(1, shell.Page.Submissions);
            Assert.Equal("立即回车", shell.Page.ViewModel.SearchText);
        });
        Test("离页后迟到文本事件不重新打开搜索页", () =>
        {
            var shell = new ShellPage();
            shell.FocusTitle();
            shell.Page = null;
            shell.TypeTitle(new() { Text = "旧页草稿" }, new() { Reason = Microsoft.UI.Xaml.Controls.AutoSuggestionBoxTextChangeReason.UserInput });
            Assert.True(shell.Page is null);
            Assert.Equal(1, shell.Navigations);
        });
        Test("标题输入绑定同步原词，重复接管不重复订阅，释放彻底解绑", () =>
        {
            var page = new LibraryPage();
            page.ViewModel.SearchText = "中文关键词";
            page.SearchInput.Text = "中文关键词";
            page.SearchInput.PlaceholderText = "资源搜索";
            var title = new Microsoft.UI.Xaml.Controls.AutoSuggestBox();
            page.AttachTitle(title);
            Assert.True(ReferenceEquals(title, page.ActiveSearchInput));
            Assert.Equal("中文关键词", title.Text);
            Assert.Equal("资源搜索", title.PlaceholderText);
            Assert.Equal(2, title.HandlerCount);
            page.AttachTitle(title);
            Assert.Equal(2, title.HandlerCount);
            var cancellations = page.ImeCancelCount;
            title.LoseFocus();
            Assert.Equal(cancellations + 1, page.ImeCancelCount);
            page.AttachTitle(null);
            Assert.Equal(0, title.HandlerCount);
            Assert.True(ReferenceEquals(page.SearchInput, page.ActiveSearchInput));
            cancellations = page.ImeCancelCount;
            title.LoseFocus();
            Assert.Equal(cancellations, page.ImeCancelCount);
            page.AttachTitle(title, released: true);
            Assert.Equal(0, title.HandlerCount);
        });
        Test("标题尚未同步的草稿在宽窄往返保留", () =>
        {
            var page = new LibraryPage();
            var title = new Microsoft.UI.Xaml.Controls.AutoSuggestBox();
            page.AttachTitle(title);
            title.Text = "未触发延迟事件的新词";
            page.AttachTitle(null);
            Assert.Equal(title.Text, page.ViewModel.SearchText);
            Assert.Equal(1, page.TypingCalls);
            Assert.Equal(title.Text, page.SearchInput.Text);
            page.AttachTitle(title);
            Assert.Equal("未触发延迟事件的新词", page.ActiveSearchInput.Text);
        });
        Test("原生内部输入焦点在宽窄切换后转交，离页取消迟到转交", () =>
        {
            var page = new LibraryPage();
            var title = new Microsoft.UI.Xaml.Controls.AutoSuggestBox { XamlRoot = new object() };
            page.AttachTitle(title);
            Microsoft.UI.Xaml.Input.FocusManager.Focused = new DependencyObject { VisualParent = title };
            page.AttachTitle(null);
            Assert.Equal(1, page.DispatcherQueue.Pending.Count);
            page.DispatcherQueue.Run();
            Assert.Equal(FocusState.Programmatic, page.SearchInput.FocusState);
            page.SearchInput.XamlRoot = new object();
            Microsoft.UI.Xaml.Input.FocusManager.Focused = new DependencyObject { VisualParent = page.SearchInput };
            page.AttachTitle(title);
            page.AttachTitle(null, released: true);
            page.DispatcherQueue.Run();
            Assert.Equal(FocusState.Unfocused, title.FocusState);
            Microsoft.UI.Xaml.Input.FocusManager.Focused = null;
        });
        Test("标题输入接管后收起整行并上移来源，窄窗恢复原布局", () =>
        {
            var page = new LibraryPage();
            page.ViewModel.MoviePilotTabsVisibility = Visibility.Visible;
            page.TitleInputShown = true;
            page.Apply(search: true);
            Assert.Equal(Visibility.Collapsed, page.SearchEntry.Visibility);
            Assert.Equal(0d, page.SearchForm.RowSpacing);
            Assert.Equal(12d, page.PageRoot.Margin.Top);
            page.TitleInputShown = false;
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
