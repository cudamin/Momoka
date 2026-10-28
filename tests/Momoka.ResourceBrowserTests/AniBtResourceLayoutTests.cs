using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Momoka.AniBt;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class AniBtResourceLayoutTests
{
    internal static void Register(string sourceRoot)
    {
        Test("AniBT 小组头像有值、缺失及失败时保留组名和发布", () =>
        {
            var row = Row();
            var source = new AniBtReleaseGroup("org_one", "字幕组", "group", [1], 1, [row.Release],
                LogoUrl: "https://r2.anibt.net/group_logos/one.webp");
            var group = new AniBtResourceGroup(source.Name, [row], source, 12);
            Assert.Equal(source.LogoUrl, group.LogoUrl);
            Assert.Equal(Visibility.Visible, group.LogoVisibility);
            var changed = new List<string?>();
            group.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
            group.MarkLogoFailed();
            group.MarkLogoFailed();
            Assert.Equal(Visibility.Collapsed, group.LogoVisibility);
            Assert.Equal(1, changed.Count(name => name == nameof(group.LogoVisibility)));
            Assert.Equal("字幕组", group.Name);
            Assert.True(ReferenceEquals(row, group.VisibleRows.Single()));
            Assert.Equal(source.LogoUrl, group.LogoUrl, "图像失败不清除来源数据");
            foreach (var missing in new AniBtReleaseGroup?[] { null, source with { LogoUrl = null }, source with { LogoUrl = "" } })
            {
                var fallback = new AniBtResourceGroup("字幕组", [row], missing, 12);
                Assert.Equal(Visibility.Collapsed, fallback.LogoVisibility);
                Assert.Equal("字幕组", fallback.Name);
                Assert.Equal(1, fallback.VisibleRows.Count);
            }
        });

        Test("AniBT 无筛选列表按最新发布排序，切页签和展开保留头像与行状态", () =>
        {
            var first = Row();
            var newerItem = first.Item with { Release = first.Release with { ReleaseId = "rel_new", PublishedAt = DateTimeOffset.UtcNow } };
            var newer = new AniBtResourceRow(newerItem, _ => Task.CompletedTask);
            newer.State = MoviePilotDownloadState.Done;
            var source = new AniBtReleaseGroup("org_one", "字幕组", "group", [1], 2, [first.Release, newer.Release],
                LogoUrl: "https://r2.anibt.net/group_logos/one.webp");
            var browser = new AniBtBrowserViewModel();
            var rows = browser.Rows;
            var groups = browser.ResourceGroups;
            browser.SetResults([first, newer], "", [source], new AniBtAnime("anime", 123, "动画", EpisodeCount: 12));
            var group = browser.ResourceGroups.Single();
            Assert.True(ReferenceEquals(newer, browser.Rows.First()));
            foreach (var view in new[] { 1, 0, 1 }) browser.ViewIndex = view;
            Assert.True(ReferenceEquals(group, browser.ResourceGroups.Single()));
            Assert.Equal(source.LogoUrl, group.LogoUrl);
            Assert.Equal(MoviePilotDownloadState.Done, group.VisibleRows.First().State);
            browser.SetResults(browser.Rows, "", [source]);
            Assert.Equal(2, browser.Rows.Count, "刷新使用自身集合时不能先清空输入");
            Assert.True(ReferenceEquals(rows, browser.Rows));
            Assert.True(ReferenceEquals(groups, browser.ResourceGroups));
        });

        Test("AniBT 无资源的小组保留完整进度，清空后没有旧行或隐藏筛选", () =>
        {
            var browser = new AniBtBrowserViewModel();
            var source = new AniBtReleaseGroup("org_empty", "完整进度", "group", [1, 2], 8, []);
            browser.SetResults([], "没有可下载资源", [source], new AniBtAnime("anime", 123, "动画", EpisodeCount: 12));
            Assert.Equal(1, browser.ProgressGroups.Count);
            Assert.Equal(0, browser.ResourceGroups.Count);
            Assert.Equal("资源  8 条", browser.ResourcesTabText);
            Assert.Equal(Visibility.Visible, browser.EmptyVisibility);
            foreach (var retired in new[] { "SelectedEpisode", "SelectedGroup", "SelectedResolution", "SortIndex", "ResetFiltersCommand" })
                Assert.Null(typeof(AniBtBrowserViewModel).GetProperty(retired));
            browser.Clear();
            Assert.Equal(0, browser.Rows.Count);
            Assert.Equal(0, browser.ProgressGroups.Count);
            Assert.Equal(Visibility.Collapsed, browser.TabsVisibility);
            Assert.Equal(Visibility.Collapsed, browser.EmptyVisibility);
        });

        foreach (var (state, label, glyph, canDownload) in new[]
        {
            (MoviePilotDownloadState.Idle, "下载", "\uE896", true),
            (MoviePilotDownloadState.Working, "添加中…", "\uE895", false),
            (MoviePilotDownloadState.Done, "已加入下载", "\uE73E", false),
            (MoviePilotDownloadState.Failed, "重试下载", "\uE72C", true),
            (MoviePilotDownloadState.Uncertain, "请先核对", "\uE7BA", false)
        })
        {
            Test("AniBT 方形下载按钮保持状态提示和命令守卫：" + state, () =>
            {
                var row = Row();
                row.State = state == MoviePilotDownloadState.Idle ? MoviePilotDownloadState.Working : MoviePilotDownloadState.Idle;
                var changed = new List<string?>();
                row.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
                var commandChanges = 0;
                row.DownloadCommand.CanExecuteChanged += (_, _) => commandChanges++;
                row.State = state;
                Assert.Equal(label, row.DownloadLabel);
                Assert.Equal(glyph, row.DownloadGlyph);
                Assert.Equal(label + "：" + row.Title, row.DownloadAutomationName);
                Assert.Equal(canDownload, row.DownloadCommand.CanExecute(null));
                Assert.True(changed.Contains(nameof(row.DownloadGlyph)));
                Assert.True(changed.Contains(nameof(row.DownloadLabel)));
                Assert.True(changed.Contains(nameof(row.DownloadAutomationName)));
                Assert.True(commandChanges > 0);
            });
        }

        Test("AniBT 图标按钮保留无种子隐藏、身份拒绝和发布页动作", () =>
        {
            var opened = 0;
            var row = Row(open: _ => opened++);
            row.OpenDetailsCommand.Execute(null);
            Assert.Equal(1, opened);
            Assert.True(row.CanOpenDetails);
            var blocked = Row(problem: "需核对来源");
            Assert.False(blocked.DownloadCommand.CanExecute(null));
            Assert.Equal("\uE7BA", blocked.DownloadGlyph);
            Assert.Equal("身份待核对", blocked.DownloadLabel);
            Assert.Equal(Visibility.Collapsed, Row(torrent: false).DownloadVisibility);
        });

        Test("AniBT 发布模板把两个方钮放在独立行尾且保留辅助说明", () =>
        {
            var xml = XDocument.Load(Path.Combine(sourceRoot, "src/Momoka.Shell/Views/AniBtResourceBrowser.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var template = xml.Descendants().Single(node => node.Attribute(x + "Key")?.Value == "ReleaseTemplate");
            var root = template.Elements().Single();
            var columns = root.Elements().Single(node => node.Name.LocalName == "Grid.ColumnDefinitions").Elements().ToArray();
            Assert.Equal("*", columns[0].Attribute("Width")?.Value);
            Assert.Equal("Auto", columns[1].Attribute("Width")?.Value);
            var details = template.Descendants().Single(node => node.Attribute(x + "Name")?.Value == "OpenTorrentPage");
            var download = template.Descendants().Single(node => node.Attribute(x + "Name")?.Value == "DownloadResource");
            Assert.True(ReferenceEquals(details.Parent, download.Parent));
            Assert.Equal("1", details.Parent!.Attribute("Grid.Column")?.Value);
            Assert.Equal("Right", details.Parent.Attribute("HorizontalAlignment")?.Value);
            Assert.False(details.Ancestors().Any(node => node.Name.LocalName == "WrapRow"));
            Assert.Equal("{x:Bind OpenDetailsCommand}", details.Attribute("Command")?.Value);
            Assert.Equal("{x:Bind DownloadCommand}", download.Attribute("Command")?.Value);
            foreach (var button in new[] { details, download })
            {
                Assert.NotNull(button.Attribute("AutomationProperties.Name"));
                Assert.NotNull(button.Attribute("ToolTipService.ToolTip"));
                Assert.Equal("FontIcon", button.Elements().Single().Name.LocalName);
                Assert.Equal("{StaticResource ReleaseActionButtonStyle}", button.Attribute("Style")?.Value);
            }
            Assert.Equal("{x:Bind DownloadGlyph, Mode=OneWay}", download.Elements().Single().Attribute("Glyph")?.Value);
            Assert.Equal("{x:Bind DownloadLabel, Mode=OneWay}", download.Attribute("ToolTipService.ToolTip")?.Value);
            Assert.Equal("{x:Bind DownloadAutomationName, Mode=OneWay}", download.Attribute("AutomationProperties.Name")?.Value);
            var style = xml.Descendants().Single(node => node.Attribute(x + "Key")?.Value == "ReleaseActionButtonStyle");
            double Value(string name) => double.Parse(style.Elements().Single(node => node.Attribute("Property")?.Value == name)
                .Attribute("Value")!.Value, CultureInfo.InvariantCulture);
            Assert.Equal(Value("Width"), Value("Height"));
            Assert.True(Value("CornerRadius") > 0 && Value("CornerRadius") < Value("Width") / 2);
        });

        Test("AniBT 删除 RSS、二次文本筛选及四项资源筛选，保留分组列表", () =>
        {
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var browser = XDocument.Load(Path.Combine(sourceRoot, "src/Momoka.Shell/Views/AniBtResourceBrowser.xaml"));
            var panel = XDocument.Load(Path.Combine(sourceRoot, "src/Momoka.Shell/Views/AniBtSearchPanel.xaml"));
            foreach (var retired in new[] { "CopyAnimeRss", "CopyGroupRss", "CopyGroupProgressRss", "RssNotice", "ReleaseFilter", "ReleaseFilters", "EpisodeFilter", "GroupFilter", "ResolutionFilter", "SortOrder", "ResetFilters", "NoMatches" })
                Assert.False(browser.Descendants().Concat(panel.Descendants()).Any(node => node.Attribute(x + "Name")?.Value == retired));
            Assert.False(browser.Descendants().Any(node => node.Attribute("Text")?.Value == "{x:Bind ViewModel.CountText, Mode=OneWay}"));
            Assert.False(browser.Descendants().Any(node => node.Name.LocalName == "TextBox"));
            Assert.False(browser.Descendants().Any(node => node.Name.LocalName == "EgPicker"));
            Assert.NotNull(browser.Descendants().Single(node => node.Attribute(x + "Name")?.Value == "ReleaseList"));
            Assert.NotNull(browser.Descendants().Single(node => node.Attribute(x + "Name")?.Value == "ReleaseGroup"));
        });

        Test("AniBT 操作样式采用 MP 资源的角色色和紧凑尺寸", () =>
        {
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var styles = XDocument.Load(Path.Combine(sourceRoot, "src/Momoka.Shell/Theme/Styles.xaml"));
            var facet = XDocument.Load(Path.Combine(sourceRoot, "src/Momoka.Shell/Views/MoviePilotResourceFacetPicker.xaml"))
                .Descendants().Single(node => node.Attribute(x + "Name")?.Value == "OpenFacet");
            foreach (var key in new[] { "EgResourceButtonStyle" })
            {
                var style = styles.Descendants().Single(node => node.Attribute(x + "Key")?.Value == key);
                foreach (var property in new[] { "MinHeight", "Padding", "CornerRadius", "BorderThickness", "Background", "Foreground" })
                    Assert.Equal(facet.Attribute(property)?.Value, style.Elements().Single(node => node.Attribute("Property")?.Value == property).Attribute("Value")?.Value);
            }
            var browser = XDocument.Load(Path.Combine(sourceRoot, "src/Momoka.Shell/Views/AniBtResourceBrowser.xaml"));
            var action = browser.Descendants().Single(node => node.Attribute(x + "Key")?.Value == "ReleaseActionButtonStyle");
            Assert.Equal("{StaticResource EgResourceButtonStyle}", action.Attribute("BasedOn")?.Value);
            var resolution = browser.Descendants().Single(node => node.Attribute("Text")?.Value == "{x:Bind ResolutionText}");
            Assert.Equal("{ThemeResource EgInfoBrush}", resolution.Attribute("Foreground")?.Value);
            Assert.Equal("{ThemeResource EgSurfaceAltBrush}", resolution.Parent!.Attribute("Background")?.Value);
            var size = browser.Descendants().Single(node => node.Attribute("Text")?.Value == "{x:Bind SizeText}");
            Assert.Equal("{ThemeResource EgAccentBrush}", size.Attribute("Foreground")?.Value);
        });

        Test("AniBT 页签使用无框下划线样式并保留双向切换", () =>
        {
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var xml = XDocument.Load(Path.Combine(sourceRoot, "src/Momoka.Shell/Views/AniBtResourceBrowser.xaml"));
            var tabs = xml.Descendants().Single(node => node.Attribute(x + "Name")?.Value == "ReleaseTabs");
            Assert.Equal("{StaticResource PivotSegmentedStyle}", tabs.Attribute("Style")?.Value);
            Assert.NotNull(xml.Descendants().Single(node => node.Attribute("Source")?.Value ==
                "ms-appx:///CommunityToolkit.WinUI.Controls.Segmented/Themes/Generic.xaml"));
            Assert.Equal("{x:Bind ViewModel.ViewIndex, Mode=TwoWay}", tabs.Attribute("SelectedIndex")?.Value);
            Assert.Equal(2, tabs.Elements().Count(node => node.Name.LocalName == "SegmentedItem"));
            foreach (var item in tabs.Elements().Where(node => node.Name.LocalName == "SegmentedItem"))
                Assert.Equal("{StaticResource PivotSegmentedItemStyle}", item.Attribute("Style")?.Value);
            var underline = tabs.Descendants().Single(node => node.Attribute(x + "Key")?.Value == "PivotItemPillBackground");
            Assert.Equal("EgAccentBrush", underline.Attribute("ResourceKey")?.Value);
            Assert.False(tabs.Descendants().Any(node => node.Name.LocalName == "ControlTemplate"));
        });

        Test("打开 AniBT 位于返回番剧之前并复用当前身份的网页命令", () =>
        {
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var xml = XDocument.Load(Path.Combine(sourceRoot, "src/Momoka.Shell/Views/AniBtSearchPanel.xaml"));
            var open = xml.Descendants().Single(node => node.Attribute(x + "Name")?.Value == "OpenAniBtSite");
            var back = xml.Descendants().Single(node => node.Attribute(x + "Name")?.Value == "ReturnToAnime");
            Assert.Equal("打开 AniBT", open.Attribute("Content")?.Value);
            Assert.Equal("{StaticResource EgResourceButtonStyle}", open.Attribute("Style")?.Value);
            Assert.Equal("{x:Bind ViewModel.OpenAnimeSiteCommand}", open.Attribute("Command")?.Value);
            Assert.Equal("anibt", open.Attribute("CommandParameter")?.Value);
            Assert.True(ReferenceEquals(open.Parent, back.Parent));
            Assert.True(ReferenceEquals(back, open.ElementsAfterSelf().First()));
        });

        Test("AniBT 隐藏状态行没有固定行距，资源行保留两列且更紧凑", () =>
        {
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var panel = XDocument.Load(Path.Combine(sourceRoot, "src/Momoka.Shell/Views/AniBtSearchPanel.xaml"));
            var root = panel.Descendants().Single(node => node.Attribute(x + "Name")?.Value == "AnimeSearchRoot");
            Assert.Equal("0", root.Attribute("RowSpacing")?.Value);
            Assert.Equal("Collapsed", root.Elements().Single(node => node.Attribute(x + "Name")?.Value == "AnimeSearchInput").Attribute("Visibility")?.Value);
            foreach (var status in root.Elements().Where(node => node.Name.LocalName is "ProgressBar" or "InfoBar"))
            {
                Assert.NotNull(status.Attribute("Margin"));
                Assert.NotNull(status.Attribute("Visibility"));
            }
            var resources = panel.Descendants().Single(node => node.Attribute(x + "Name")?.Value == "AnimeResourcesView");
            Assert.Equal("8", resources.Attribute("Spacing")?.Value);
            var browser = XDocument.Load(Path.Combine(sourceRoot, "src/Momoka.Shell/Views/AniBtResourceBrowser.xaml"));
            var release = browser.Descendants().Single(node => node.Attribute(x + "Name")?.Value == "ReleaseRow");
            Assert.Equal("0,6", release.Attribute("Padding")?.Value);
            Assert.Equal("8", browser.Descendants().Single(node => node.Attribute(x + "Name")?.Value == "ReleaseContent").Attribute("Spacing")?.Value);
        });

        Test("搜索页移除外卡和标题，保留库页标题与搜索来源组件", () =>
        {
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var xml = XDocument.Load(Path.Combine(sourceRoot, "src/Momoka.Shell/Views/LibraryPage.xaml"));
            var surface = xml.Descendants().Single(node => node.Attribute(x + "Name")?.Value == "SearchSurface");
            Assert.Equal("0", surface.Attribute("Padding")?.Value);
            Assert.Equal("0", surface.Attribute("BorderThickness")?.Value);
            Assert.Null(surface.Attribute("Background"));
            Assert.Null(surface.Attribute("BorderBrush"));
            Assert.NotNull(surface.Descendants().Single(node => node.Attribute(x + "Name")?.Value == "SearchInput"));
            Assert.NotNull(surface.Descendants().Single(node => node.Attribute(x + "Name")?.Value == "SourceTabs"));
            var title = xml.Descendants().Single(node => node.Attribute(x + "Name")?.Value == "LibraryTitle");
            Assert.Equal("{x:Bind ViewModel.Heading, Mode=OneWay}", title.Attribute("Text")?.Value);
            Assert.Equal("{x:Bind local:LibraryPage.BrowseHintVisibility(ViewModel.SearchVisibility), Mode=OneWay}", title.Attribute("Visibility")?.Value);
        });

        Test("AniBT 页面说明与删除控件、筛选和紧凑样式一致", () =>
        {
            var guide = File.ReadAllText(Path.Combine(sourceRoot, "docs/MoviePilot订阅.md"));
            Assert.Contains("页面移除 RSS 复制按钮及资源二次文本筛选栏", guide);
            Assert.DoesNotContain("番剧与字幕组各有复制 RSS 链接", guide);
            Assert.Contains("移除集数、字幕组、清晰度及排序四项资源筛选", guide);
            Assert.Contains("打开 AniBT", guide);
            Assert.Contains("资源继续按字幕组列表展示", guide);
        });

        Test("AniBT 进度和资源标题的头像容器随失败完整收起", () =>
        {
            var xml = XDocument.Load(Path.Combine(sourceRoot, "src/Momoka.Shell/Views/AniBtResourceBrowser.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var logos = xml.Descendants().Where(node => node.Attribute(x + "Name")?.Value == "GroupLogoSurface").ToArray();
            Assert.Equal(2, logos.Length);
            foreach (var logo in logos)
            {
                Assert.Equal("{x:Bind LogoVisibility, Mode=OneWay}", logo.Attribute("Visibility")?.Value);
                var image = logo.Elements().Single();
                Assert.Equal("{x:Bind local:AniBtResourceBrowser.CreateGroupLogo(LogoUrl), Mode=OneWay}", image.Attribute("Source")?.Value);
                Assert.Equal("OnGroupLogoFailed", image.Attribute("ImageFailed")?.Value);
                Assert.Equal("{x:Bind Name}", logo.ElementsAfterSelf().First().Attribute("Text")?.Value);
            }
        });
    }

    private static AniBtResourceRow Row(bool torrent = true, string? problem = null, Action<AniBtResourceRow>? open = null)
    {
        var release = new AniBtRelease("rel_fixture", "字幕组 动画 - 01", new Uri("https://anibt.net/api/torrent/rel_fixture.torrent"),
            new Uri("https://anibt.net/release/rel_fixture"), GroupName: "字幕组", EpisodeKey: "1", HasTorrent: torrent);
        var resource = new MoviePilotResource
        {
            Title = release.Title,
            TorrentInfo = JsonSerializer.SerializeToElement(new { title = release.Title }),
            PageUrl = release.PageUri.AbsoluteUri,
            DownloadProblem = problem
        };
        return new AniBtResourceRow(new AniBtResource(release, resource), _ => Task.CompletedTask, open);
    }
}
