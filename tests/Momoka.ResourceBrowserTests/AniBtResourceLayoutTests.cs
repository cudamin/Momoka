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

        Test("AniBT 小组筛选与收起保留头像和行状态", () =>
        {
            var first = Row();
            var second = Row();
            second.State = MoviePilotDownloadState.Done;
            var source = new AniBtReleaseGroup("org_one", "字幕组", "group", [1], 2, [first.Release, second.Release],
                LogoUrl: "https://r2.anibt.net/group_logos/one.webp");
            var browser = new AniBtBrowserViewModel();
            browser.SetResults([first, second], "", [source], new AniBtAnime("anime", 123, "动画", EpisodeCount: 12));
            var group = browser.ResourceGroups.Single();
            browser.Text = "不存在";
            Assert.Equal(0, browser.ResourceGroups.Count);
            browser.ResetFiltersCommand.Execute(null);
            Assert.True(ReferenceEquals(group, browser.ResourceGroups.Single()));
            Assert.Equal(source.LogoUrl, group.LogoUrl);
            Assert.Equal(MoviePilotDownloadState.Done, group.VisibleRows.Last().State);
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
