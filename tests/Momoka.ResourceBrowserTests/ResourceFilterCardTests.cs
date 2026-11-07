using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>执行真实筛选 VM/命令，并核对本树原生控件和主题槽；不冒充运行中的布局/UIA。</summary>
internal static class ResourceFilterCardTests
{
    public static void Register(string sourceRoot)
    {
        Test("资源筛选卡片：同维度多选并集、跨维度交集、X只移除本项", () =>
        {
            var vm = Browser();
            var rows = vm.Rows;
            var active = vm.ActiveChoices;
            var siteA = Choice(vm.SiteFacet.Choices, "A");
            var siteB = Choice(vm.SiteFacet.Choices, "B");
            var hd = Choice(vm.ResolutionFacet.Choices, "1080p");
            siteA.Selected = siteB.Selected = true;
            Assert.Equal(3, vm.Rows.Count);
            hd.Selected = true;
            Assert.Equal("Chinese,Effects", Titles(vm));
            Assert.Equal(3, vm.ActiveChoices.Count);
            Assert.True(ReferenceEquals(rows, vm.Rows) && ReferenceEquals(active, vm.ActiveChoices));
            siteB.RemoveCommand.Execute(null);
            Assert.Equal("Chinese", Titles(vm));
            Assert.False(siteB.Selected);
            Assert.True(siteA.Selected && hd.Selected);
            Assert.Equal("站点 · A", siteA.FilterLabel);
            Assert.Equal("移除筛选：站点 · A", siteA.RemoveName);
            Assert.DoesNotContain("×", siteA.FilterLabel, "X属于独立原生按钮而非标签字符串");
        });
        Test("资源筛选卡片：原生选中提示通知、取消和重选", () =>
        {
            var calls = 0;
            var card = new MoviePilotResourceChoice("v", "选项", "质量", () => calls++);
            var notifications = new List<string>();
            card.PropertyChanged += (_, e) => notifications.Add(e.PropertyName ?? "");
            Assert.Equal(Visibility.Collapsed, card.CheckVisibility);
            card.Selected = true;
            Assert.Equal(Visibility.Visible, card.CheckVisibility);
            Assert.True(notifications.Contains(nameof(card.CheckVisibility)));
            card.RemoveCommand.Execute(null);
            Assert.Equal(Visibility.Collapsed, card.CheckVisibility);
            Assert.Equal(2, calls);
            card.Selected = true;
            Assert.Equal(3, calls);
        });
        Test("资源筛选卡片：无命中不清空多选、取消恢复、清除保留排序和下载反馈", () =>
        {
            var vm = Browser();
            var first = vm.Rows[0];
            first.State = MoviePilotDownloadState.Done;
            vm.SortIndex = (int)MoviePilotResourceSort.Size;
            vm.SortDescending = false;
            Choice(vm.SiteFacet.Choices, "C").Selected = true;
            Choice(vm.ResolutionFacet.Choices, "2160p").Selected = true;
            Assert.Equal(0, vm.Rows.Count);
            Assert.Equal(Visibility.Visible, vm.NoMatchesVisibility);
            Assert.Equal(2, vm.ActiveChoices.Count);
            Choice(vm.ResolutionFacet.Choices, "2160p").RemoveCommand.Execute(null);
            Assert.Equal("Voice", Titles(vm));
            vm.ResetCommand.Execute(null);
            Assert.Equal(4, vm.Rows.Count);
            Assert.Equal(0, vm.ActiveChoices.Count);
            Assert.Equal((int)MoviePilotResourceSort.Size, vm.SortIndex);
            Assert.False(vm.SortDescending);
            Assert.True(first.State == MoviePilotDownloadState.Done && vm.Rows.Contains(first));
        });
        foreach (var value in new[] { "E02", "E03" })
        {
            Test("综合筛选卡片：集单选与原标量双向、移除返回全部 " + value, () =>
            {
                var vm = Browser();
                var cards = vm.EpisodeCards;
                Choice(cards, value).Selected = true;
                Assert.Equal(value, vm.SelectedEpisode);
                Assert.Equal(1, cards.Count(card => card.Selected));
                var selected = Choice(cards, value);
                Assert.True(vm.ActiveChoices.Contains(selected));
                vm.SelectedEpisode = "E01";
                Assert.True(Choice(cards, "E01").Selected && !selected.Selected);
                Assert.True(ReferenceEquals(cards, vm.EpisodeCards));
                Choice(cards, "E01").RemoveCommand.Execute(null);
                Assert.Equal(MoviePilotResourceBrowserViewModel.AllEpisodes, vm.SelectedEpisode);
                Assert.True(Choice(cards, MoviePilotResourceBrowserViewModel.AllEpisodes).Selected);
                Assert.Equal(0, vm.ActiveChoices.Count);
                var all = Choice(cards, MoviePilotResourceBrowserViewModel.AllEpisodes);
                all.Selected = false;
                Assert.True(all.Selected, "不限项保持唯一选中态，不允许没有选中状态");
            });
        }
        Test("综合筛选卡片：季联动原地保留仍可选卡片、撤销旧选择", () =>
        {
            var vm = Browser();
            var e01 = Choice(vm.EpisodeCards, "E01");
            var e03 = Choice(vm.EpisodeCards, "E03");
            e03.Selected = true;
            Choice(vm.SeasonFacet.Choices, "1").Selected = true;
            Assert.False(vm.Episodes.Contains("E03"));
            Assert.Equal(MoviePilotResourceBrowserViewModel.AllEpisodes, vm.SelectedEpisode);
            Assert.True(ReferenceEquals(e01, Choice(vm.EpisodeCards, "E01")));
            e03.Selected = false;
            e03.Selected = true;
            Assert.Equal(MoviePilotResourceBrowserViewModel.AllEpisodes, vm.SelectedEpisode, "已退役的旧卡不能再改本批条件");
        });
        Test("综合筛选卡片：meta缺集号的目标集可清除并选回，新结果撤销旧卡", () =>
        {
            var vm = new MoviePilotResourceBrowserViewModel();
            var resource = new MoviePilotResource { Title = "Target.S01E05.1080p", TorrentInfo = JsonSerializer.SerializeToElement(new { title = "anonymous" }) };
            vm.SetResults([Row(resource)], 5);
            Assert.True(Choice(vm.EpisodeCards, "E05").Selected);
            Assert.Equal(1, vm.Rows.Count);
            var seeded = Choice(vm.EpisodeCards, "E05");
            seeded.RemoveCommand.Execute(null);
            Assert.Equal(MoviePilotResourceBrowserViewModel.AllEpisodes, vm.SelectedEpisode);
            seeded.Selected = true;
            Assert.Equal("E05", vm.SelectedEpisode);
            vm.SetResults([Row(resource with { Title = "Target.S01E06.1080p" })], 6);
            Assert.True(Choice(vm.EpisodeCards, "E06").Selected);
            Assert.False(vm.EpisodeCards.Contains(seeded));
            seeded.Selected = false;
            seeded.Selected = true;
            Assert.Equal("E06", vm.SelectedEpisode);
        });
        Test("综合筛选卡片：上游中字与特效只取显式标签，不由片名或国语猜测", () =>
        {
            var vm = Browser();
            Choice(vm.LabelCards, "中字").Selected = true;
            Assert.Equal("Chinese,Dual", Titles(vm));
            Choice(vm.LabelCards, "特效").Selected = true;
            Assert.Equal("Effects,Dual", Titles(vm));
            Assert.False(Choice(vm.LabelCards, "中字").Selected, "保留原标签单选，不顺带增加独立字幕业务维度");
            Assert.Equal(1, vm.LabelCards.Count(card => card.Selected));
            Choice(vm.LabelCards, "特效").RemoveCommand.Execute(null);
            Assert.Equal(MoviePilotResourceBrowserViewModel.AllLabels, vm.SelectedLabel);
            vm.SelectedLabel = "国语";
            Assert.Equal("Voice", Titles(vm));
            Assert.True(Choice(vm.LabelCards, "国语").Selected);
        });
        foreach (var (index, count) in new[] { (0, 4), (1, 2), (2, 3), (3, 3), (4, 3) })
        {
            Test("综合筛选卡片：时间选项保持原天数口径与移除 index=" + index, () =>
            {
                var vm = Browser();
                var key = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Choice(vm.DateCards, key).Selected = true;
                Assert.Equal(index, vm.DateIndex);
                Assert.Equal(count, vm.Rows.Count);
                Assert.Equal(1, vm.DateCards.Count(card => card.Selected));
                Choice(vm.DateCards, key).RemoveCommand.Execute(null);
                Assert.Equal(0, vm.DateIndex);
                Assert.Equal(4, vm.Rows.Count);
                Assert.True(Choice(vm.DateCards, "0").Selected);
            });
        }
        Test("综合筛选卡片：文本/做种/HR组合与重置、新搜索默认排序", () =>
        {
            var vm = Browser();
            vm.Text = "1080p";
            vm.SeededOnly = true;
            vm.ExcludeHitAndRun = true;
            Assert.Equal("Chinese", Titles(vm));
            Assert.Equal(Visibility.Visible, vm.ResetVisibility);
            vm.ResetCommand.Execute(null);
            Assert.Equal(4, vm.Rows.Count);
            Assert.Equal(Visibility.Collapsed, vm.ResetVisibility);
            Assert.True(vm.EpisodeCards[0].Selected && vm.LabelCards[0].Selected && vm.DateCards[0].Selected);
            vm.SortIndex = 2;
            vm.SortDescending = false;
            vm.SetResults(Resources().Select(Row));
            Assert.Equal(0, vm.SortIndex);
            Assert.True(vm.SortDescending);
        });
        Test("资源筛选卡片：新空结果收起完整组但保留清除后的不限投影", () =>
        {
            var vm = Browser();
            vm.SetResults([]);
            Assert.Equal(Visibility.Collapsed, vm.FiltersVisibility);
            Assert.Equal(Visibility.Collapsed, vm.EpisodeVisibility);
            Assert.Equal(Visibility.Collapsed, vm.LabelVisibility);
            Assert.Equal(0, vm.ActiveChoices.Count);
            Assert.Equal(1, vm.EpisodeCards.Count);
            Assert.Equal(1, vm.LabelCards.Count);
            Assert.Equal(5, vm.DateCards.Count);
        });
        Test("资源筛选卡片：模板原生Toggle/系统焦点/勾号/换行而非纵向Checkbox", () =>
        {
            var picker = Load(sourceRoot, "MoviePilotResourceFacetPicker.xaml");
            var styles = Load(sourceRoot, "MoviePilotResourceFilterStyles.xaml");
            var browser = Load(sourceRoot, "MoviePilotResourceBrowser.xaml");
            Assert.False(picker.Descendants().Any(node => node.Name.LocalName == "CheckBox"));
            Assert.False(browser.Descendants().Any(node => node.Name.LocalName == "CheckBox"));
            Assert.False(styles.Descendants().Any(node => node.Name.LocalName == "ControlTemplate"), "不替换原生模板");
            var option = Named(picker, "SelectChoice");
            Assert.True(XNode.DeepEquals(Keyed(picker, "ResourceFilterOptionTemplate"), Keyed(browser, "ResourceFilterOptionTemplate")),
                "两处轻量内容模板一致，保持旧SelectChoice的文件定位契约，不引入额外控件生命周期");
            Assert.Equal("ToggleButton", option.Name.LocalName);
            Assert.Equal("{x:Bind Selected, Mode=TwoWay}", option.Attribute("IsChecked")?.Value);
            Assert.Equal("{x:Bind FilterLabel}", option.Attribute("AutomationProperties.Name")?.Value);
            Assert.True(option.Descendants().Any(node => node.Attribute("Visibility")?.Value == "{x:Bind CheckVisibility, Mode=OneWay}"));
            Assert.False(option.Descendants().Any(node => node.Attribute("Foreground") is not null), "文字/图标继承原生选中态字色");
            var style = Keyed(styles, "ResourceFilterOptionStyle");
            Assert.Equal("{StaticResource DefaultToggleButtonStyle}", style.Attribute("BasedOn")?.Value);
            Assert.True(style.Elements().Any(node => node.Attribute("Property")?.Value == "UseSystemFocusVisuals" && node.Attribute("Value")?.Value == "True"));
            Assert.True(style.Elements().Any(node => node.Attribute("Property")?.Value == "IsThreeState" && node.Attribute("Value")?.Value == "False"));
            foreach (var (xml, name, source) in new[] { (picker, "FacetChoices", "Facet.Choices"),
                (browser, "EpisodeFilter", "ViewModel.EpisodeCards"), (browser, "LabelFilter", "ViewModel.LabelCards"), (browser, "DateFilter", "ViewModel.DateCards") })
            {
                var items = Named(xml, name);
                Assert.Equal("ItemsControl", items.Name.LocalName);
                Assert.Equal("{x:Bind " + source + ", Mode=OneWay}", items.Attribute("ItemsSource")?.Value);
                Assert.Equal("{StaticResource ResourceFilterOptionTemplate}", items.Attribute("ItemTemplate")?.Value);
                Assert.True(items.Descendants().Any(node => node.Name.LocalName == "WrapRow"));
            }
            foreach (var xml in new[] { picker, browser })
                Assert.True(xml.Descendants().Where(node => node.Name.LocalName == "ScrollViewer").All(node =>
                    node.Attribute("HorizontalScrollBarVisibility")?.Value == "Disabled" && node.Attribute("HorizontalScrollMode")?.Value == "Disabled"));
            foreach (var name in new[] { "SeededFilter", "HitAndRunFilter" })
            {
                var toggle = Named(browser, name);
                Assert.Equal("ToggleButton", toggle.Name.LocalName);
                Assert.NotNull(toggle.Attribute("AutomationProperties.Name"));
                Assert.Contains("Mode=TwoWay", toggle.Attribute("IsChecked")?.Value);
            }
        });
        Test("资源筛选卡片：已有句柄保留原文件归属，不靠移动句柄或修改基线洗绿", () =>
        {
            foreach (var file in new[] { "MoviePilotResourceBrowser.xaml", "MoviePilotResourceFacetPicker.xaml" })
            {
                var names = Load(sourceRoot, file).Descendants().Attributes(X + "Name").Select(attribute => attribute.Value).ToHashSet();
                var prefix = "handle  Views/" + file + "  ";
                foreach (var line in File.ReadLines(Path.Combine(sourceRoot, "tests/Momoka.Tests/Agreements.txt")).Where(line => line.StartsWith(prefix, StringComparison.Ordinal)))
                    Assert.True(names.Contains(line[prefix.Length..]), "旧句柄消失：" + line);
            }
        });
        Test("资源筛选卡片：Light/Dark主题槽对齐实际SDK，HighContrast交由原生", () =>
        {
            var styles = Load(sourceRoot, "MoviePilotResourceFilterStyles.xaml");
            var dictionaries = styles.Descendants().Where(node => node.Name.LocalName == "ResourceDictionary" && node.Attribute(X + "Key") is not null).ToArray();
            Assert.Equal("Light,Dark,HighContrast", string.Join(',', dictionaries.Select(node => node.Attribute(X + "Key")!.Value)));
            Assert.Equal(0, dictionaries[2].Elements().Count());
            var light = dictionaries[0].Elements().ToDictionary(node => node.Attribute(X + "Key")!.Value, node => node.Attribute("ResourceKey")!.Value);
            var dark = dictionaries[1].Elements().ToDictionary(node => node.Attribute(X + "Key")!.Value, node => node.Attribute("ResourceKey")!.Value);
            Assert.Equal(18, light.Count);
            foreach (var (key, role) in light)
            {
                Assert.Equal(role, dark[key]);
                Assert.True(role.StartsWith("Eg", StringComparison.Ordinal));
            }
            Assert.Equal("EgActionGlassBrush", light["ToggleButtonBackground"]);
            Assert.Equal("EgBorderBrush", light["ToggleButtonBorderBrush"]);
            Assert.Equal("EgActionGlassBrush", light["ToggleButtonBackgroundChecked"]);
            Assert.Equal("EgTextOnAccentBrush", light["ToggleButtonForegroundChecked"]);
            Assert.False(styles.Descendants().Any(node => node.Name.LocalName == "SolidColorBrush"));
            Assert.False(styles.Descendants().Attributes().Any(attribute => attribute.Name.LocalName == "HighContrastAdjustment"));
        });
        Test("资源已选标签：柔和主题边线、独立X/UIA/Command，扩展单选标签", () =>
        {
            var xml = Load(sourceRoot, "MoviePilotResourceBrowser.xaml");
            var active = Named(xml, "ActiveFilters");
            Assert.Equal("{x:Bind ViewModel.ActiveChoices, Mode=OneWay}", active.Attribute("ItemsSource")?.Value);
            var tag = Named(xml, "ActiveFilterTag");
            Assert.Equal("{ThemeResource EgAccentSoftBrush}", tag.Attribute("Background")?.Value);
            Assert.Equal("{ThemeResource EgAccentMutedBrush}", tag.Attribute("BorderBrush")?.Value);
            Assert.Equal("1", tag.Attribute("BorderThickness")?.Value);
            var remove = Named(xml, "RemoveFilter");
            Assert.Equal("Button", remove.Name.LocalName);
            Assert.Equal("{x:Bind RemoveCommand}", remove.Attribute("Command")?.Value);
            Assert.Equal("{x:Bind RemoveName}", remove.Attribute("AutomationProperties.Name")?.Value);
            Assert.Null(remove.Attribute("Content"), "标签不是可误触的整颗移除按钮");
            Assert.True(remove.Descendants().Any(node => node.Name.LocalName == "FontIcon" && node.Attribute("Glyph")?.Value == "\uE711"));
            Assert.Equal("True", remove.Attribute("UseSystemFocusVisuals")?.Value);
        });
        Test("资源筛选卡片：已审核亚克力模板与交互结构保持", () =>
        {
            var xml = Load(sourceRoot, "MoviePilotResourceBrowser.xaml");
            var template = Named(xml, "ResourceList").Elements().Single(node => node.Name.LocalName == "ItemsView.ItemTemplate");
            var canonical = string.Concat(template.ToString(SaveOptions.DisableFormatting).Where(character => !char.IsWhiteSpace(character)));
            Assert.Equal("{ThemeResource CardBackgroundFillColorDefaultBrush}", Named(xml, "ResourceCard").Attribute("Background")?.Value);
            Assert.Equal("D51477DBB1D37698A365639A4E3B759A5FE349CF3EA5AA4C36E63753CC1BB82E", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
        });
        foreach (var width in new[] { 240d, 280d })
        {
            Test("资源筛选卡片：复用WrapLayout按有限宽度分行 width=" + width, () =>
            {
                var boxes = new[] { (Width: 76d, Height: 32d), (Width: 108d, Height: 32d), (Width: 92d, Height: 32d) };
                var plan = WrapLayout.Place(boxes, width, 8, 8);
                Assert.True(plan.Height > 32);
                for (var index = 0; index < boxes.Length; index++)
                    Assert.True(plan.Spots[index].Left >= 0 && plan.Spots[index].Left + boxes[index].Width <= width);
            });
        }
    }

    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static XDocument Load(string sourceRoot, string file) => XDocument.Load(Path.Combine(sourceRoot, "src/Momoka.Shell/Views", file));
    private static XElement Named(XDocument xml, string name) => xml.Descendants().Single(node => node.Attribute(X + "Name")?.Value == name);
    private static XElement Keyed(XDocument xml, string key) => xml.Descendants().Single(node => node.Attribute(X + "Key")?.Value == key);
    private static MoviePilotResourceChoice Choice(IEnumerable<MoviePilotResourceChoice> cards, string value) => cards.Single(card => card.Value == value);
    private static string Titles(MoviePilotResourceBrowserViewModel vm) => string.Join(',', vm.Rows.Select(row => row.Title));
    private static MoviePilotResourceBrowserViewModel Browser()
    {
        var vm = new MoviePilotResourceBrowserViewModel();
        vm.SetResults(Resources().Select(Row));
        return vm;
    }
    private static MoviePilotResourceRow Row(MoviePilotResource resource) => new(resource, _ => Task.CompletedTask);
    private static MoviePilotResource[] Resources()
    {
        var now = DateTimeOffset.Now;
        var first = new MoviePilotResource
        {
            Title = "Chinese",
            Description = "1080p",
            SiteName = "A",
            Resolution = "1080p",
            Labels = ["中字"],
            Seasons = [1],
            Episodes = [1],
            Size = 10,
            Seeders = 10,
            PublishedAt = now.AddHours(-1),
            TorrentInfo = JsonSerializer.SerializeToElement(new { title = "anonymous" })
        };
        return [first, first with { Title = "Effects", SiteName = "B", Labels = ["特效"], Episodes = [2], Size = 20, Seeders = 0, PublishedAt = now.AddDays(-2) },
            first with { Title = "Dual", SiteName = "A", Description = "2160p", Resolution = "2160p", Labels = ["中字", "特效"], Episodes = [2],
                Size = 30, PublishedAt = now.AddDays(-100) },
            first with { Title = "Voice", Description = "片名含中字不代表字幕 1080p", SiteName = "C", Labels = ["国语"], Seasons = [2], Episodes = [3],
                Size = 40, HitAndRun = true }];
    }
}
