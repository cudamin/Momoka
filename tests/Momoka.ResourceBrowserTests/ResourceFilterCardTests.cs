using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Momoka.Configuration;
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
        Test("资源计数徽标：完整、筛选、零匹配和恢复时更新计数，支持中英文", () =>
        {
            var previousLanguage = UiText.Language;
            try
            {
                UiText.Language = UiLanguages.Chinese;
                var vm = Browser();
                Assert.Equal("4 个资源", vm.CountText);
                var updates = new List<string>();
                vm.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(vm.CountText)) updates.Add(vm.CountText);
                };

                vm.Text = "Chinese";
                vm.Text = "absent";
                vm.ResetCommand.Execute(null);
                Assert.Equal("1 / 4 个资源|0 / 4 个资源|4 个资源", string.Join('|', updates));

                UiText.Language = UiLanguages.English;
                Assert.Equal("4 resources", vm.CountText);
                vm.Text = "Chinese";
                Assert.Equal("1 / 4 resources", vm.CountText);
                vm.SetResults([vm.Rows.Single()]);
                Assert.Equal("1 resource", vm.CountText);
            }
            finally { UiText.Language = previousLanguage; }
        });
        Test("资源筛选卡片：坏季集字段不阻断整批铺设，筛选与清除保留集合", () =>
        {
            var vm = new MoviePilotResourceBrowserViewModel();
            var rows = vm.Rows;
            vm.SetResults(new[] { Season("正常", "S02", 2), Season("超长", "S2147483648", 1),
                Season("Unicode", "S０１E０２", 1), Season("范围", "S01-S99999999999999999", 1) }.Select(Row));
            Assert.Equal(4, vm.Rows.Count);
            Choice(vm.SeasonFacet.Choices, "S02").Selected = true;
            Assert.Equal("正常", Titles(vm));
            vm.ResetCommand.Execute(null);
            Assert.Equal(4, vm.Rows.Count);
            Assert.True(ReferenceEquals(rows, vm.Rows));
        });
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
            Choice(vm.SeasonFacet.Choices, "S01").Selected = true;
            Assert.False(vm.Episodes.Contains("E03"));
            Assert.Equal(MoviePilotResourceBrowserViewModel.AllEpisodes, vm.SelectedEpisode);
            Assert.True(ReferenceEquals(e01, Choice(vm.EpisodeCards, "E01")));
            e03.Selected = false;
            e03.Selected = true;
            Assert.Equal(MoviePilotResourceBrowserViewModel.AllEpisodes, vm.SelectedEpisode, "已退役的旧卡不能再改本批条件");
        });
        Test("季筛选：网页端 season_episode 原值含集，整季优先、同类按季集降序", () =>
        {
            var vm = new MoviePilotResourceBrowserViewModel();
            var rows = new[]
            {
                Row(Season("Whole.S10", "S10", 10)),
                Row(Season("Whole.S02", "S02", 2)),
                Row(Season("Whole.S01", "S01", 1)),
                Row(Season("Episode.S10E01E03", "S10E01-E03", 10)),
                Row(Season("Episode.S02E12", "S02E12", 2)),
                Row(Season("Episode.S01E05", "S01E05", 1))
            };
            vm.SetResults(rows);
            Assert.Equal("S10,S02,S01,S10E01-E03,S02E12,S01E05",
                string.Join(',', vm.SeasonFacet.Choices.Select(choice => choice.Value)));
            Choice(vm.SeasonFacet.Choices, "S01E05").Selected = true;
            Assert.Equal("Episode.S01E05", Titles(vm));
            Choice(vm.SeasonFacet.Choices, "S01E05").Selected = false;
            Choice(vm.SeasonFacet.Choices, "S01").Selected = true;
            Assert.Equal("Whole.S01", Titles(vm), "整季选项按原值精确匹配，不吞掉同一季的单集");
            Assert.Equal(MoviePilotResourceBrowserViewModel.AllEpisodes, vm.SelectedEpisode);
            Assert.Equal("季 · S01", Choice(vm.SeasonFacet.Choices, "S01").FilterLabel);
        });
        Test("季筛选：缺 season_episode 的旧回包退回季号仍可选", () =>
        {
            var vm = Browser();
            Assert.Equal("S02,S01", string.Join(',', vm.SeasonFacet.Choices.Select(choice => choice.Value)));
            Choice(vm.SeasonFacet.Choices, "S02").Selected = true;
            Assert.Equal("Voice", Titles(vm));
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
            Assert.False(Choice(vm.LabelCards, "中字").Selected, "标签单选保持独立，字幕分组是另一维多选");
            Assert.Equal(1, vm.LabelCards.Count(card => card.Selected));
            Choice(vm.LabelCards, "特效").RemoveCommand.Execute(null);
            Assert.Equal(MoviePilotResourceBrowserViewModel.AllLabels, vm.SelectedLabel);
            vm.SelectedLabel = "国语";
            Assert.Equal("Voice", Titles(vm));
            Assert.True(Choice(vm.LabelCards, "国语").Selected);
        });
        Test("综合筛选分区：网页端九组齐全，源/字幕/促销可多选，全选与清除只动本组", () =>
        {
            var vm = Browser();
            Assert.Equal("站点,季,源,字幕,促销状态,视频编码,质量,分辨率,制作组",
                string.Join(',', vm.DialogFacets.Select(facet => facet.Label)));
            var source = vm.DialogFacets.Single(facet => facet.Label == "源");
            Assert.Equal("AMZN,Netflix", string.Join(',', source.Choices.Select(choice => choice.Value)));
            Assert.Equal(Visibility.Collapsed, source.SelectionVisibility);
            Choice(source.Choices, "Netflix").Selected = true;
            Assert.Equal("Chinese,Dual", Titles(vm));
            Assert.Equal(Visibility.Visible, source.SelectionVisibility);
            var subtitle = vm.DialogFacets.Single(facet => facet.Label == "字幕");
            Assert.Equal("中字,特效", string.Join(',', subtitle.Choices.Select(choice => choice.Value)));
            subtitle.SelectAllCommand.Execute(null);
            Assert.Equal("Chinese,Dual", Titles(vm), "字幕全选后仍受已选源的约束");
            Assert.Equal("Netflix", string.Join(',', source.Selected), "全选字幕不应改动其它分组");
            subtitle.ClearCommand.Execute(null);
            Assert.Equal("Chinese,Dual", Titles(vm));
            var promotion = vm.DialogFacets.Single(facet => facet.Label == "促销状态");
            Choice(promotion.Choices, "50%").Selected = true;
            Assert.Equal(0, vm.Rows.Count, "源和促销是不同维度的交集");
            Assert.Equal(Visibility.Visible, promotion.SelectionVisibility);
            source.ClearCommand.Execute(null);
            Assert.Equal("Effects", Titles(vm), "清除源只放开源，不影响已选促销");
            promotion.ClearCommand.Execute(null);
            Assert.Equal(4, vm.Rows.Count);
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
            var dialog = Load(sourceRoot, "MoviePilotResourceFilterDialog.xaml");
            Assert.False(picker.Descendants().Any(node => node.Name.LocalName == "CheckBox"));
            Assert.False(browser.Descendants().Any(node => node.Name.LocalName == "CheckBox"));
            Assert.False(dialog.Descendants().Any(node => node.Name.LocalName == "CheckBox"));
            Assert.False(styles.Descendants().Any(node => node.Name.LocalName == "ControlTemplate"), "不替换原生模板");
            var option = Named(picker, "SelectChoice");
            Assert.True(XNode.DeepEquals(Keyed(picker, "ResourceFilterOptionTemplate"), Keyed(dialog, "ResourceFilterOptionTemplate")),
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
                (dialog, "EpisodeFilter", "ViewModel.EpisodeCards"), (dialog, "LabelFilter", "ViewModel.LabelCards"), (dialog, "DateFilter", "ViewModel.DateCards") })
            {
                var items = Named(xml, name);
                Assert.Equal("ItemsControl", items.Name.LocalName);
                Assert.Equal("{x:Bind " + source + ", Mode=OneWay}", items.Attribute("ItemsSource")?.Value);
                Assert.Equal("{StaticResource ResourceFilterOptionTemplate}", items.Attribute("ItemTemplate")?.Value);
                Assert.True(items.Descendants().Any(node => node.Name.LocalName == "WrapRow"));
            }
            foreach (var xml in new[] { picker, dialog })
                Assert.True(xml.Descendants().Where(node => node.Name.LocalName == "ScrollViewer").All(node =>
                    node.Attribute("HorizontalScrollBarVisibility")?.Value == "Disabled" && node.Attribute("HorizontalScrollMode")?.Value == "Disabled"));
            foreach (var name in new[] { "SeededFilter", "HitAndRunFilter" })
            {
                var toggle = Named(dialog, name);
                Assert.Equal("ToggleButton", toggle.Name.LocalName);
                Assert.NotNull(toggle.Attribute("AutomationProperties.Name"));
                Assert.Contains("Mode=TwoWay", toggle.Attribute("IsChecked")?.Value);
            }
        });
        Test("综合筛选弹窗：网页端弹窗结构与下方展开、图标字色不再被框架悬停槽改掉", () =>
        {
            var browser = Load(sourceRoot, "MoviePilotResourceBrowser.xaml");
            var picker = Load(sourceRoot, "MoviePilotResourceFacetPicker.xaml");
            var dialog = Load(sourceRoot, "MoviePilotResourceFilterDialog.xaml");
            Assert.Equal("ContentDialog", dialog.Root!.Name.LocalName);
            Assert.Equal("BottomEdgeAlignedLeft", Named(picker, "ChoicesFlyout").Attribute("Placement")?.Value,
                "筛选项又展开到按钮上方了");
            var expander = Named(browser, "FilterExpander");
            Assert.Equal("Button", expander.Name.LocalName, "综合筛选应打开弹窗而不是下拉");
            Assert.Equal("OnAdvancedFilters", expander.Attribute("Click")?.Value);
            var icon = expander.Descendants().First(node => node.Name.LocalName == "FontIcon");
            Assert.Equal("{ThemeResource EgAccentBrush}", icon.Attribute("Foreground")?.Value,
                "折叠栏图标继承模板前景色，悬停时会丢失强调色");
            var facetIcon = Named(picker, "OpenFacet").Descendants().First(node => node.Name.LocalName == "FontIcon");
            Assert.Equal("{ThemeResource EgAccentBrush}", facetIcon.Attribute("Foreground")?.Value);
            foreach (var (xml, element) in new[] { (browser, expander), (picker, Named(picker, "OpenFacet")) })
            {
                var resources = element.Elements().Single(node => node.Name.LocalName == "Button.Resources" || node.Name.LocalName == "DropDownButton.Resources");
                foreach (var theme in new[] { "Dark", "Light" })
                {
                    var dict = resources.Descendants().Single(node => node.Name.LocalName == "ResourceDictionary" && (string?)node.Attribute(X + "Key") == theme);
                    Assert.True(dict.Elements().Any(node => node.Attribute(X + "Key")?.Value == "ButtonBackgroundPointerOver"
                        && node.Attribute("ResourceKey")?.Value == "EgAccentMutedBrush"), "悬停背景没钉在强调色柔和底上");
                    Assert.True(dict.Elements().Any(node => node.Attribute(X + "Key")?.Value == "ButtonBackgroundPressed"
                        && node.Attribute("ResourceKey")?.Value == "EgAccentSoftBrush"), "按下背景没钉住");
                }
                Assert.NotNull(resources.Descendants().SingleOrDefault(node => node.Name.LocalName == "ResourceDictionary" && (string?)node.Attribute(X + "Key") == "HighContrast"));
            }
            foreach (var (name, source) in new[] { ("WebSourceFilter", "ViewModel.WebSourceFacet"), ("SubtitleFilter", "ViewModel.SubtitleFacet") })
            {
                var facet = Named(browser, name);
                Assert.Equal("MoviePilotResourceFacetPicker", facet.Name.LocalName);
                Assert.Equal("{x:Bind " + source + ", Mode=OneWay}", facet.Attribute("Facet")?.Value);
            }
            var groups = Named(dialog, "DialogFacetGroups");
            Assert.Equal("ItemsRepeater", groups.Name.LocalName);
            Assert.Equal("{x:Bind ViewModel.DialogFacets, Mode=OneWay}", groups.Attribute("ItemsSource")?.Value);
            Assert.Equal("{StaticResource ResourceFilterGroupTemplate}", groups.Attribute("ItemTemplate")?.Value);
            Assert.True(groups.Descendants().Any(node => node.Name.LocalName == "UniformGridLayout"
                && node.Attribute("ItemsStretch")?.Value == "Fill"), "网页端分组卡按可用宽度换行成两列");
            var group = Keyed(dialog, "ResourceFilterGroupTemplate");
            Assert.Equal("{x:Bind Choices}", group.Descendants().Single(node => node.Name.LocalName == "ItemsControl").Attribute("ItemsSource")?.Value);
            Assert.Equal("{x:Bind SelectAllCommand}", Named(group, "SelectAllGroup").Attribute("Command")?.Value);
            Assert.Equal("{x:Bind ClearCommand}", Named(group, "ClearGroup").Attribute("Command")?.Value);
            Assert.Equal("{x:Bind SelectionVisibility, Mode=OneWay}", Named(group, "ClearGroup").Attribute("Visibility")?.Value,
                "分组清除只在这一组有选中时出现");
        });
        Test("资源筛选卡片：已有句柄保留原文件归属，不靠移动句柄或修改基线洗绿", () =>
        {
            foreach (var file in new[] { "MoviePilotResourceBrowser.xaml", "MoviePilotResourceFacetPicker.xaml", "MoviePilotResourceFilterDialog.xaml" })
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
            Assert.Equal("EgAccentSoftBrush", light["ToggleButtonBackground"]);
            Assert.Equal("EgBorderBrush", light["ToggleButtonBorderBrush"]);
            Assert.Equal("EgAccentBrush", light["ToggleButtonBackgroundChecked"]);
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
            var document = new XDocument(xml);
            var structure = Named(document, "ResourceList").Elements().Single(node => node.Name.LocalName == "ItemsView.ItemTemplate");
            // Only the three reviewed localization identifiers are neutral to this structural
            // baseline. Assert their targets and both resources before removing them from the clone.
            foreach (var (uid, property, chinese, english) in new[]
            {
                ("MoviePilotResourceBrowser_Localization8", "[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip", "做种人数", "Seeders"),
                ("MoviePilotResourceBrowser_Localization9", "[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip", "下载人数", "Leechers"),
                ("MoviePilotResourceBrowser_Localization10", "Text", "身份待核对", "Identity needs verification")
            })
            {
                var target = structure.Descendants().Single(node => node.Attribute(X + "Uid")?.Value == uid);
                Assert.Equal(chinese, target.Attribute(property == "Text" ? "Text" : "ToolTipService.ToolTip")?.Value);
                foreach (var (language, value) in new[] { ("zh-Hans", chinese), ("en-US", english) })
                {
                    var resources = XDocument.Load(Path.Combine(sourceRoot, "src/Momoka.Shell/Strings", language, "Resources.resw"));
                    Assert.Equal(value, resources.Root!.Elements("data").Single(entry => entry.Attribute("name")?.Value == uid + "." + property).Element("value")?.Value);
                }
                target.Attribute(X + "Uid")!.Remove();
            }
            var canonical = string.Concat(structure.ToString(SaveOptions.DisableFormatting).Where(character => !char.IsWhiteSpace(character)));
            Assert.Equal("{ThemeResource CardBackgroundFillColorDefaultBrush}", Named(xml, "ResourceCard").Attribute("Background")?.Value);
            Assert.Equal("6E659467B05107E6CAB68B3950CDC498C44DE263107DF19FDD4EDAB29E1ADEB0", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
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
    private static XElement Named(XContainer xml, string name) => xml.Descendants().Single(node => node.Attribute(X + "Name")?.Value == name);
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
    private static MoviePilotResource Season(string title, string seasonEpisode, int season) => new()
    {
        Title = title,
        SeasonEpisode = seasonEpisode,
        Seasons = [season],
        Episodes = [1],
        Size = 10,
        TorrentInfo = JsonSerializer.SerializeToElement(new { title = "anonymous" })
    };
    private static MoviePilotResource[] Resources()
    {
        var now = DateTimeOffset.Now;
        var first = new MoviePilotResource
        {
            Title = "Chinese",
            Description = "1080p",
            SiteName = "A",
            Resolution = "1080p",
            VideoCodec = "H264",
            Edition = "WEB-DL",
            ReleaseGroup = "GROUP",
            WebSource = "Netflix",
            Promotion = "普通",
            Labels = ["中字"],
            Seasons = [1],
            Episodes = [1],
            Size = 10,
            Seeders = 10,
            PublishedAt = now.AddHours(-1),
            TorrentInfo = JsonSerializer.SerializeToElement(new { title = "anonymous" })
        };
        return [first, first with { Title = "Effects", SiteName = "B", Labels = ["特效"], Episodes = [2], Size = 20, Seeders = 0, PublishedAt = now.AddDays(-2),
                VideoCodec = "H265", Edition = "BluRay", ReleaseGroup = "OTHER", WebSource = "AMZN", Promotion = "50%" },
            first with { Title = "Dual", SiteName = "A", Description = "2160p", Resolution = "2160p", Labels = ["中字", "特效"], Episodes = [2],
                Size = 30, PublishedAt = now.AddDays(-100) },
            first with { Title = "Voice", Description = "片名含中字不代表字幕 1080p", SiteName = "C", Labels = ["国语"], Seasons = [2], Episodes = [3],
                Size = 40, HitAndRun = true, WebSource = "AMZN" }];
    }
}
