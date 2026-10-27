using System.Xml.Linq;

namespace Momoka.Tests;

/// <summary>设置页的可见结构契约；行为与实际几何另由离线 Shell 探针验证。</summary>
internal static class SettingsLayoutTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static void Register()
    {
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
            Assert.True(Named(document, "CompactCategoryNavigation").Ancestors().Contains(navigation));
        });
        TestHarness.Test("设置布局：正文没有重复分类标题、数量或分隔线", () =>
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
            Assert.Equal("{x:Bind HeadingVisibility, Mode=OneWay}", heading.Attribute("Visibility")?.Value);
            Assert.Equal("{x:Bind Title, Mode=OneWay}", heading.Attribute("Text")?.Value);
            var list = section.Descendants().Single(element => element.Attribute(Xaml + "Name")?.Value == "SettingsRowsList");
            var panel = list.Descendants().Single(element => element.Name.LocalName == "StackPanel");
            Assert.Equal("0", panel.Attribute("Spacing")?.Value);
            Assert.True(panel.Attribute("Background") is null, "不得用底色和 1px 缝重新制造选项分隔线");
            Assert.Equal("0", list.Parent?.Attribute("BorderThickness")?.Value);
        });
        TestHarness.Test("设置布局：卡片右侧保留独立滚动条槽位", () =>
        {
            var document = Page("SettingsPage.xaml");
            var scroll = Named(document, "SettingsScroll");
            Assert.Equal("ScrollViewer", scroll.Name.LocalName, "设置页不使用发布版位置钳制异常的合成滚动器");
            var cards = Named(document, "SettingsCardsContent");
            Assert.Equal("0,0,20,0", cards.Attribute("Margin")?.Value);
            Assert.True(cards.Ancestors().Contains(scroll));
            Assert.Equal("Auto", scroll.Attribute("VerticalScrollBarVisibility")?.Value);
            Assert.Equal("Disabled", scroll.Attribute("HorizontalScrollBarVisibility")?.Value,
                "横向滚动必须禁用并按视口约束内容宽度，Hidden仍会无限测量");
            Assert.Equal("Stretch", scroll.Attribute("HorizontalContentAlignment")?.Value);
            Assert.Equal("Left", Named(document, "SettingsSections").Attribute("HorizontalAlignment")?.Value);
        });
        TestHarness.Test("服务器账号：每个账号是独立表面，服务器只负责分组", () =>
        {
            var document = Page("ServersPage.xaml");
            var account = Template(document, "AccountTemplate");
            var card = account.Elements().Single();
            Assert.Equal("Border", card.Name.LocalName);
            Assert.Equal("{x:Bind}", card.Attribute("Tag")?.Value);
            Assert.Equal("{ThemeResource EgSurfaceBrush}", card.Attribute("Background")?.Value);
            Assert.True(card.Descendants().Any(element => element.Attribute("Text")?.Value == "{x:Bind ServerName}"));
            Assert.True(card.Descendants().Any(element => element.Attribute("Text")?.Value == "{x:Bind Username}"));
            var group = Template(document, "ServerTemplate").Elements().Single();
            Assert.Equal("Grid", group.Name.LocalName);
            Assert.True(group.Attribute("Background") is null, "服务器分组不再画外层卡片");
            var accounts = group.Elements().Single(element => element.Name.LocalName == "ItemsControl");
            Assert.Equal("{x:Bind Accounts}", accounts.Attribute("ItemsSource")?.Value);
            Assert.Equal("{StaticResource AccountTemplate}", accounts.Attribute("ItemTemplate")?.Value);
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
