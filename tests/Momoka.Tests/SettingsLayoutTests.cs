using System.Xml.Linq;

namespace Momoka.Tests;

/// <summary>设置页的可见结构契约；行为与实际几何另由离线 Shell 探针验证。</summary>
internal static class SettingsLayoutTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static void Register()
    {
        TestHarness.Test("详情外观探针：独立卡片设置、固定材质、默认20与隐藏恢复都有接线", () =>
        {
            var probe = Source("ShellNavigationProbe.DetailCustomization.cs");
            Assert.Contains("剧页面卡片", probe);
            Assert.Contains("单集页面卡片", probe);
            Assert.Contains("page.ViewModel.DetailCards.Count == 3", probe);
            Assert.Contains("backdropDefault.Value == 20", probe);
            Assert.Contains("EmbyItemType.Series => fixture.Settings.Ui.SeriesCards", probe);
            Assert.Contains("EmbyItemType.Episode => fixture.Settings.Ui.EpisodeCards", probe);
            Assert.Contains("恢复沉浸丢失背景不透明度偏好", probe);
            Assert.Contains("CheckFixedAcrylicBackdropAsync", probe);
            Assert.Contains("RequireFixedDetailForeground(page)", probe);
            Assert.Contains("current.Pixels.AsSpan().SequenceEqual(originalPixels.Pixels)", probe);
            Assert.Contains("requestCount() == baselineReads", probe);
            Assert.False(probe.Contains("ui.UseGaussianBackdrop =", StringComparison.Ordinal));
            Assert.False(probe.Contains("ui.UseAcrylicControls =", StringComparison.Ordinal));
            Assert.Contains("RequireEpisodeSubject(page, subject)", probe);
        });
        TestHarness.Test("背景亚克力探针：独立均色、错误Tint反例与实际回退像素覆盖保留", () =>
        {
            var probe = Source("ShellNavigationProbe.BackdropFade.cs");
            Assert.Contains("var ink = ExpectedBackdropBaseColor(page)", probe);
            Assert.Contains("acrylic.TintColor == ink && acrylic.FallbackColor == ink", probe);
            Assert.Contains("for (var row = 0; row < 5; row++)", probe);
            Assert.Contains("for (var column = 0; column < 8; column++)", probe);
            Assert.Contains("背景检查没有拒绝错误主图底色反例", probe);
            Assert.Contains("背景检查没有拒绝错误不透明度反例", probe);
            Assert.Contains("glass.AlwaysUseFallback = true", probe);
            Assert.Contains("glass.AlwaysUseFallback = previousFallback", probe);
            Assert.Contains("foreach (var theme in UiThemes.All)", probe);
            Assert.Contains("foreach (var width in new[] { 760, 1280 })", probe);
            Assert.Contains("new[] { 0d, 20, 100 }", probe);
            Assert.False(probe.Contains("fixture.Settings.Ui.UseGaussianBackdrop =", StringComparison.Ordinal));
        });
        TestHarness.Test("单集原布局探针：卡面与紧凑分档保留，导航和几何反例继续检查", () =>
        {
            var episode = Source("ShellNavigationProbe.EpisodeBand.cs");
            Assert.Contains("compact == (width < 1024)", episode);
            Assert.Contains("coverWidths[1023] == 0 && coverWidths[768] == 0", episode);
            Assert.Contains("RequireEpisodeSubject(page, episode)", episode);
            Assert.Contains("沉浸正文检查没有拒绝不透明卡面反例", episode);
            var responsive = Source("ShellNavigationProbe.DetailResponsive.cs");
            Assert.Contains("var oldEpisodeCompact = view.ItemType == EmbyItemType.Episode", responsive);
            Assert.Contains("view.IsCompact == oldEpisodeCompact", responsive);
        });
        TestHarness.Test("背景缓存探针：清理后原图稳定与旧票迟到拒绝仍覆盖", () =>
        {
            var probe = Source("ShellNavigationProbe.HeroCache.cs");
            Assert.Contains("index < 30", probe);
            Assert.Contains("ReferenceEquals(clearedPicture, changed.ViewModel.HeroImage)", probe);
            Assert.Contains("cache.RememberPixels(oldTicket, clearedPixels!)", probe);
            Assert.Contains("cache.RememberPicture(oldTicket, clearedPixels!, 0", probe);
            Assert.Contains("appearanceRetainedOriginal = true", probe);
            Assert.False(probe.Contains("_heroBlurGeneration", StringComparison.Ordinal));
        });
        TestHarness.Test("集页季度探针：正季边界、未知特典、投影与无请求换季接线", () =>
        {
            var series = Source("ShellNavigationProbe.DetailSeries.cs");
            Assert.Contains("secondSeason.PremiereDate?.AddTicks(-1)", series);
            Assert.Contains("equalSecond.PremiereDate = secondSeason.PremiereDate", series);
            Assert.Contains("unknown.PremiereDate = null", series);
            Assert.Contains("await pendingSeasonRequest.Task.WaitAsync", series);
            Assert.Contains("await UntilAsync(() => page.ViewModel.Episodes.Count == 9)", series);
            Assert.Contains("releaseSeasonRequest.TrySetResult(true)", series);
            Assert.Contains("RequireEpisodeSeasonRows(page, secondIds)", series);
            Assert.Contains("RequireEpisodeSeasonRows(page, firstIds)", series);
            Assert.Contains("fixture.Transport.Requests.Count == baselineRequests", series);
            Assert.Contains("SequenceEqual(subjects)", series);
            Assert.Contains("view.SeasonShelf is not { Cards.Count: > 0 }", series);
            Assert.Contains("rows.ItemsSourceView?.Count == expected.Count", series);
            var settings = Source("ShellNavigationProbe.DetailCustomization.cs");
            Assert.Contains("(\"单集页面卡片\", EmbyItemType.Episode, 4)", settings);
            Assert.Contains("单集设置仍显示全部剧季卡片选项", settings);
        });
        TestHarness.Test("长剧集页季度探针：全剧上下文与局部竖列表分开核验", () =>
        {
            var probe = Source("ShellNavigationProbe.LongSeries.cs");
            Assert.Contains("episodeView.Episodes.Count == 2000", probe);
            Assert.Contains("episodeView.EpisodeShelf is { Cards.Count: 100 }", probe);
            Assert.Contains("episodeView.EpisodeFocus == 0", probe);
            Assert.Contains("ReferenceEquals(episodeTarget, episodeView.PlayTarget)", probe);
            Assert.Contains("fixture.Transport.Requests.Count == episodeRequestCount", probe);
        });
        TestHarness.Test("亚克力透明度探针：控件画刷透明度实际跟随读数，Tint固定", () =>
        {
            var probe = Source("ShellNavigationProbe.DetailCustomization.cs");
            Assert.Contains("Math.Abs(glass.Opacity - expected) < 0.001", probe);
            Assert.Contains("glass.TintOpacity == 0.55 && glass.TintLuminosityOpacity == 0.55", probe);
            Assert.Contains("Math.Abs(glass.Opacity - strength / 100) < 0.001", probe);
            var materials = Source("ShellNavigationProbe.Materials.cs");
            Assert.Contains("AcrylicBrush { AlwaysUseFallback: false } actual", materials);
            Assert.Contains("fill.TintOpacity == 0.55 && fill.TintLuminosityOpacity == 0.55", materials);
        });
        TestHarness.Test("详情卡片排序表：弹簧拖拽有完整开始结束与卸载处理", () =>
        {
            var document = Page("SettingsPage.xaml");
            var list = Named(document, "CardOrderList");
            Assert.Equal("True", list.Attribute("CanReorderItems")?.Value);
            Assert.Equal("OnHomeRowDragStarting", list.Attribute("DragItemsStarting")?.Value);
            Assert.Equal("OnPriorityDragCompleted", list.Attribute("DragItemsCompleted")?.Value);
            Assert.Equal("OnHomeLayoutListLoaded", list.Attribute("Loaded")?.Value);
            Assert.Equal("OnPriorityListUnloaded", list.Attribute("Unloaded")?.Value);
            Assert.Equal("{x:Bind}", list.Attribute("Tag")?.Value);
            Assert.True(Named(document, "CardOrderRoot").Ancestors().Contains(list));
            Assert.True(Named(document, "CardOrderRoot").Elements().Any(element =>
                element.Name.LocalName == "Grid.RenderTransform" && element.Elements().Any(transform =>
                    transform.Name.LocalName == "TranslateTransform")), "弹簧需要模板根自身的位移变换");
        });
        TestHarness.Test("设置布局：左上搜索与原分类共用无卡片侧栏", () =>
        {
            var document = Page("SettingsPage.xaml");
            var navigation = Named(document, "SettingsNavigation");
            var search = Named(document, "SettingsSearchBox");
            var list = Named(document, "CategoryNavigation");
            Assert.True(search.Ancestors().Contains(navigation) && list.Ancestors().Contains(navigation));
            Assert.Equal("Transparent", list.Attribute("Background")?.Value);
            Assert.True(list.Attribute("CornerRadius") is null);
            var header = document.Descendants().Single(element => element.Attribute(Xaml + "Key")?.Value == "SettingsGroupHeader");
            Assert.False(header.Descendants().Any(element => element.Name.LocalName == "Rectangle"));
            Assert.True(header.Descendants().Any(element => element.Name.LocalName == "ControlTemplate"), "框架默认分组头仍会画一条细线");
            Assert.Equal("{x:Bind CategoryGroupsSource.View, Mode=OneWay}", list.Attribute("ItemsSource")?.Value);
            Assert.Equal("{x:Bind ViewModel.SettingsSearchResults, Mode=OneWay}", search.Attribute("ItemsSource")?.Value);
            Assert.Equal("{x:Bind ViewModel.SettingsSearchQuery, Mode=TwoWay}", search.Attribute("Text")?.Value);
            Assert.Equal("False", search.Attribute("UpdateTextOnSelect")?.Value);
            Assert.Equal("OnSettingsSearchSubmitted", search.Attribute("QuerySubmitted")?.Value);
            Assert.True(search.Attribute("QueryIcon") is null, "搜索图标已移到左侧，右侧不再重复显示");
            Assert.Equal("1", search.Attribute("BorderThickness")?.Value);
            Assert.True(search.Descendants().Any(element => element.Attribute(Xaml + "Key")?.Value == "TextControlBorderThemeThicknessFocused"
                && element.Value == "1"), "焦点边框四边应等粗");
            Assert.True(search.Parent?.Attribute("CornerRadius") is null, "搜索框圆角只绘制一层，不叠加外层裁剪");
            var searchButton = Named(document, "SettingsSearchButton");
            Assert.Equal("OnSettingsSearchClick", searchButton.Attribute("Click")?.Value);
            Assert.Equal("Left", searchButton.Attribute("HorizontalAlignment")?.Value);
            Assert.Equal(search.Parent, searchButton.Parent);
            Assert.True(Named(document, "CompactCategoryNavigation").Ancestors().Contains(navigation));
        });
        TestHarness.Test("设置布局：正文卡片标题、数量与分隔线规则", () =>
        {
            var document = Page("SettingsPage.xaml");
            var content = Named(document, "SettingsContent");
            Assert.False(content.Elements().Any(element => element.Name.LocalName == "TextBlock"), "正文顶部不再显示设置标题");
            Assert.False(content.Elements().Any(element => element.Name.LocalName == "Grid.RowDefinitions"), "标题移除后不保留空白行");
            foreach (var name in new[] { "SettingsScroll", "HostedFrame" })
            {
                var body = Named(document, name);
                Assert.True(body.Parent == content && body.Attribute("Grid.Row") is null, "卡片与内嵌页面均占满正文区域");
            }
            var section = Named(document, "SettingsSection");
            Assert.False(section.Descendants().Any(element => element.Name.LocalName == "ShelfHead"));
            Assert.False(section.Descendants().Any(element => element.Attribute("Text")?.Value == "{x:Bind Count, Mode=OneWay}"));
            var heading = Named(Page("SettingsPage.xaml"), "SettingsSectionTitle");
            Assert.True(heading.Attribute("Visibility") is null, "每张卡片都显示自己的标题，不再按分类名折叠");
            Assert.Equal("{x:Bind Title, Mode=OneWay}", heading.Attribute("Text")?.Value);
            Assert.Equal("{StaticResource EgSettingsSectionTitle}", heading.Attribute("Style")?.Value);
            var list = section.Descendants().Single(element => element.Attribute(Xaml + "Name")?.Value == "SettingsRowsList");
            var panel = list.Descendants().Single(element => element.Name.LocalName == "StackPanel");
            Assert.Equal("0", panel.Attribute("Spacing")?.Value);
            Assert.True(panel.Attribute("Background") is null, "不得用底色和 1px 缝重新制造选项分隔线");
            Assert.Equal("0", list.Parent?.Attribute("BorderThickness")?.Value);
        });
        TestHarness.Test("设置布局：说明收起时行顶画细线，首行不画", () =>
        {
            var document = Page("SettingsPage.xaml");

            // 线色由行样式一处给出，模板只管画不画；写进模板就会漂成十几种灰。
            foreach (var key in new[] { "EgSettingRowStyle", "EgSettingBlockStyle" })
            {
                var style = document.Descendants().Single(element => element.Attribute(Xaml + "Key")?.Value == key);
                Assert.True(style.Elements().Any(setter => setter.Attribute("Property")?.Value == "BorderBrush"
                        && setter.Attribute("Value")?.Value == "{ThemeResource EgBorderBrush}"),
                    $"{key} 没给出分隔线颜色");
            }

            var roots = document.Descendants()
                .Where(element => element.Attribute("Style")?.Value
                    is "{StaticResource EgSettingRowStyle}" or "{StaticResource EgSettingBlockStyle}")
                .ToArray();
            // 模板增减时同步这个数：这里是复核提醒，不是禁止重构 —— 少一条就是某一种行在精简模式下没有分隔线。
            Assert.Equal(18, roots.Length);
            Assert.True(roots.All(element => element.Attribute("BorderThickness")?.Value == "{x:Bind DividerThickness, Mode=OneWay}"),
                "行模板根要 OneWay 绑 DividerThickness：行会在原地增删，首行身份跟着换人");
        });
        TestHarness.Test("设置布局：卡片右侧保留独立滚动条槽位", () =>
        {
            var document = Page("SettingsPage.xaml");
            var scroll = Named(document, "SettingsScroll");
            Assert.Equal("16,8,0,16", Named(document, "SettingsLayout").Attribute("Margin")?.Value,
                "滚动器所在正文应贴右边，卡片自身留内侧间距");
            Assert.True(scroll.Descendants().Any(element => element.Attribute(Xaml + "Key")?.Value == "ScrollViewerScrollBarMargin"
                && element.Value == "0"), "滚动条框架模板不再额外缩进");
            Assert.Equal("ScrollViewer", scroll.Name.LocalName, "设置页不使用发布版位置钳制异常的合成滚动器");
            var cards = Named(document, "SettingsCardsContent");
            Assert.Equal("0,0,20,0", cards.Attribute("Margin")?.Value);
            Assert.True(cards.Ancestors().Contains(scroll));
            Assert.Equal("Auto", scroll.Attribute("VerticalScrollBarVisibility")?.Value);
            Assert.Equal("Disabled", scroll.Attribute("HorizontalScrollBarVisibility")?.Value,
                "横向滚动必须禁用并按视口约束内容宽度，Hidden仍会无限测量");
            Assert.Equal("Stretch", scroll.Attribute("HorizontalContentAlignment")?.Value);
            var sections = Named(document, "SettingsSections");
            Assert.Equal("Stretch", sections.Attribute("HorizontalAlignment")?.Value);
            Assert.Equal("Stretch", sections.Attribute("HorizontalContentAlignment")?.Value);
            Assert.True(sections.Attribute("MaxWidth") is null, "放大窗口时卡片应跟随可用宽度展开");
        });
        TestHarness.Test("服务器账号：每个账号是独立表面，服务器只负责分组", () =>
        {
            var document = Page("ServersPage.xaml");
            var account = Template(document, "AccountTemplate");
            var card = account.Elements().Single();
            Assert.Equal("Border", card.Name.LocalName);
            Assert.Equal("{x:Bind}", card.Attribute("Tag")?.Value);
            Assert.Equal("{ThemeResource CardBackgroundFillColorDefaultBrush}", card.Attribute("Background")?.Value);
            Assert.True(card.Descendants().Any(element => element.Attribute("Text")?.Value == "{x:Bind ServerName}"));
            Assert.True(card.Descendants().Any(element => element.Attribute("Text")?.Value == "{x:Bind Username}"));
            var group = Template(document, "ServerTemplate").Elements().Single();
            Assert.Equal("Grid", group.Name.LocalName);
            Assert.True(group.Attribute("Background") is null, "服务器分组不再画外层卡片");
            var accounts = group.Elements().Single(element => element.Name.LocalName == "ListView");
            Assert.Equal("{x:Bind Accounts}", accounts.Attribute("ItemsSource")?.Value);
            Assert.Equal("{StaticResource AccountTemplate}", accounts.Attribute("ItemTemplate")?.Value);
            Assert.Equal("True", accounts.Attribute("CanReorderItems")?.Value, "组内账号要能拖拽换位");
            Assert.Equal("OnAccountDragStarting", accounts.Attribute("DragItemsStarting")?.Value);
            Assert.Equal("OnAccountDragCompleted", accounts.Attribute("DragItemsCompleted")?.Value);
            Assert.False(document.Descendants().Any(element => element.Attribute("ItemsSource")?.Value == "{x:Bind Others}"));
        });
        TestHarness.Test("服务器账号：主操作明确绑定账号，整服删除只能通过右键菜单", () =>
        {
            var card = Template(Page("ServersPage.xaml"), "AccountTemplate").Elements().Single();
            foreach (var command in new[] { "LoginAccount", "TakeOffline", "DeleteAccount" })
            {
                var button = card.Descendants().Single(element => element.Name.LocalName == "Button"
                    && element.Attribute("Command")?.Value == $"{{x:Bind Owner.{command}Command}}");
                Assert.Equal("{x:Bind}", button.Attribute("CommandParameter")?.Value, "每个动作认卡片自己的账号");
            }
            Assert.False(card.Descendants().Any(element => element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == "{x:Bind Owner.DeleteServerCommand}"));
            var deleteServer = card.Descendants().Single(element => element.Name.LocalName == "MenuFlyoutItem"
                && element.Attribute("Command")?.Value == "{x:Bind Owner.DeleteServerCommand}");
            Assert.Equal("{x:Bind ServerGroup}", deleteServer.Attribute("CommandParameter")?.Value);
            Assert.Equal("删除此服务器及全部账号", deleteServer.Attribute("Text")?.Value);
        });
        TestHarness.Test("服务器账号：没有保存账号时保留登录和删除服务器的入口", () =>
        {
            var group = Template(Page("ServersPage.xaml"), "ServerTemplate").Elements().Single();
            var empty = group.Elements().Single(element => element.Name.LocalName == "Border");
            Assert.Equal("{x:Bind EmptyVisibility}", empty.Attribute("Visibility")?.Value);
            Assert.True(empty.Descendants().Any(element => element.Attribute("Command")?.Value == "{x:Bind Owner.LoginServerCommand}"));
            Assert.True(empty.Descendants().Any(element => element.Attribute("Command")?.Value == "{x:Bind Owner.DeleteServerCommand}"));
        });
        TestHarness.Test("运行状态：标题与操作共行，状态摘要不再独立滚动", () =>
        {
            var document = Page("DiagnosticsPage.xaml");
            var toolbar = Named(document, "DiagnosticsToolbar");
            Assert.Equal("CommandBar", toolbar.Name.LocalName);
            Assert.True(toolbar.Elements().Any(element => element.Name.LocalName == "CommandBar.Content"));
            Assert.Equal(3, toolbar.Elements().Count(element => element.Name.LocalName == "AppBarButton"));
            var summaries = Named(document, "StatusCards");
            Assert.Equal("Grid", summaries.Name.LocalName);
            Assert.Equal(3, summaries.Elements().Count(element => element.Name.LocalName == "StackPanel"));
            Assert.False(summaries.Ancestors().Any(element => element.Name.LocalName is "ScrollView" or "ScrollViewer"));
            Assert.True(Named(document, "LogPanel").Attribute("Grid.Column") is null, "日志占完整宽度，不再放在侧栏旁边");
        });
        TestHarness.Test("运行状态：路径及启动参数移入详情，但功能和自动化入口保留", () =>
        {
            var document = Page("DiagnosticsPage.xaml");
            var details = Named(document, "DiagnosticsDetailsButton");
            Assert.True(details.Elements().Any(element => element.Name.LocalName == "Button.Flyout"));
            var launch = document.Descendants().Single(element =>
                element.Attribute("AutomationProperties.AutomationId")?.Value == "DiagnosticsLaunchOptionsBox");
            Assert.True(launch.Ancestors().Any(element => element.Name.LocalName == "Flyout"));
            Assert.Equal("{x:Bind ViewModel.LaunchVisibility, Mode=OneWay}", launch.Parent?.Attribute("Visibility")?.Value);
            Assert.Equal("True", launch.Attribute("IsReadOnly")?.Value);
            Assert.True(launch.Ancestors().Any(element => element.Name.LocalName == "ScrollView"), "详情内容过长时仍能滚动查看");
            var filters = Named(document, "DiagnosticsFilterBar");
            var follow = Named(document, "DiagnosticsFollowSwitch");
            Assert.True(follow.Ancestors().Contains(filters));
            Assert.Equal("{x:Bind ViewModel.Following, Mode=TwoWay}", follow.Attribute("IsOn")?.Value);
            Assert.Equal(2, filters.Descendants().Count(element => element.Name.LocalName == "ComboBox"));
        });
        TestHarness.Test("运行详情：换行按显示内容比较，兼容 TextBox 的 CR 归一化", () =>
        {
            const string expected = "vo = gpu-next\nao = wasapi";
            foreach (var actual in new[] { expected, "vo = gpu-next\rao = wasapi", "vo = gpu-next\r\nao = wasapi" })
                Assert.Equal(expected, actual.ReplaceLineEndings("\n"));
            Assert.False("vo = gpu-next\rao = null".ReplaceLineEndings("\n") == expected,
                "换行兼容不能掩盖参数内容不同");
        });
    }

    private static string Source(string file)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln")))
                return File.ReadAllText(Path.Combine(directory.FullName, "src", "Momoka.Shell", file));
        throw new DirectoryNotFoundException("未找到当前 Momoka 工作树");
    }

    private static XDocument Page(string file)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln")))
                return XDocument.Load(Path.Combine(directory.FullName, "src", "Momoka.Shell", "Views", file));
        throw new DirectoryNotFoundException("未找到当前 Momoka 工作树");
    }

    private static XElement Template(XDocument document, string key) => document.Descendants().Single(element =>
        element.Name.LocalName == "DataTemplate" && element.Attribute(Xaml + "Key")?.Value == key);

    private static XElement Named(XDocument document, string name) => document.Descendants().Single(element =>
        element.Attribute(Xaml + "Name")?.Value == name);
}
