using System.Xml.Linq;

namespace Momoka.Tests;

internal static class AccountMenuTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static void Register()
    {
        TestHarness.Test("账号菜单：聚合开关与下线入口可稳定定位", () =>
        {
            var menu = Shell().Descendants().Single(element => element.Attribute(Xaml + "Name")?.Value == "AccountMenu");
            var toggle = menu.Descendants().SingleOrDefault(element => element.Attribute(Xaml + "Name")?.Value == "AggregateModeItem");
            Assert.True(toggle is not null, "顶部账号菜单缺少聚合模式开关");
            Assert.Equal("ToggleSwitch", toggle!.Name.LocalName);
            Assert.Equal("开", toggle.Attribute("OnContent")?.Value);
            Assert.Equal("关", toggle.Attribute("OffContent")?.Value);
            Assert.Equal("OnAggregateModeToggled", toggle.Attribute("Toggled")?.Value);
            Assert.False(menu.Descendants().Any(element => element.Attribute(Xaml + "Name")?.Value == "OfflineMenu"),
                "下线应放在账号行右侧，不单独建菜单");
            Assert.True(menu.Descendants().Any(element => element.Attribute(Xaml + "Name")?.Value == "AccountProfileList"),
                "服务器与账号应共用一张列表");
        });

        TestHarness.Test("账号菜单：在线数量独立一行，长身份不会撑宽面板", () =>
        {
            var shell = Shell();
            var identity = shell.Descendants().Single(element => element.Attribute(Xaml + "Name")?.Value == "AccountIdentityItem");
            Assert.Equal("CharacterEllipsis", identity.Attribute("TextTrimming")?.Value);
            Assert.True(shell.Descendants().Any(element => element.Attribute(Xaml + "Name")?.Value == "AccountOnlineItem"),
                "在线账号数应独立呈现，不追加到身份行");
        });

        TestHarness.Test("账号菜单：聚合选项可完整收起，面板使用紧凑字号与亚克力", () =>
        {
            var menu = Shell().Descendants().Single(element => element.Attribute(Xaml + "Name")?.Value == "AccountMenu");
            var toggle = menu.Descendants().Single(element => element.Attribute(Xaml + "Name")?.Value == "AggregateModeItem");
            Assert.Equal("AggregateModeRow", toggle.Parent?.Attribute(Xaml + "Name")?.Value);
            var body = menu.Descendants().Single(element => element.Attribute(Xaml + "Name")?.Value == "AccountMenuBody");
            Assert.True(double.Parse(body.Attribute("Width")!.Value, System.Globalization.CultureInfo.InvariantCulture) < 304,
                "账号面板未缩小");
            var style = body.Descendants().Single(element => element.Attribute(Xaml + "Key")?.Value == "AccountActionStyle");
            Assert.True(style.Elements().Any(setter => setter.Attribute("Property")?.Value == "FontSize"
                && setter.Attribute("Value")?.Value == "{StaticResource EgCaptionFontSize}"), "账号操作未使用较小字号");
            var presenter = menu.Elements().Single(element => element.Name.LocalName == "Flyout.FlyoutPresenterStyle");
            Assert.True(presenter.Descendants().Any(setter => setter.Attribute("Property")?.Value == "Background"
                && setter.Attribute("Value")?.Value == "{ThemeResource EgFrostBrush}"), "账号浮层未使用主题亚克力");
        });
        TestHarness.Test("账号菜单：清单与添加服务器入口已退役，分组线按顺序插在开关和添加账号之前", () =>
        {
            var body = Shell().Descendants().Single(element => element.Attribute(Xaml + "Name")?.Value == "AccountMenuBody");
            var children = body.Elements().ToArray();
            Assert.False(children.Any(element => element.Attribute(Xaml + "Name")?.Value is "ServerListsEntry" or "AddServerItem"),
                "账号菜单仍保留服务器清单或添加服务器入口");
            var aggregate = children.Single(element => element.Attribute(Xaml + "Name")?.Value == "AggregateModeRow");
            var aggregateDivider = children.Single(element => element.Attribute(Xaml + "Name")?.Value == "AggregateModeDivider");
            var profiles = children.Single(element => element.Attribute(Xaml + "Name")?.Value == "AccountProfileList");
            var actionsDivider = children.Single(element => element.Attribute(Xaml + "Name")?.Value == "AccountActionsDivider");
            var addAccount = children.Single(element => element.Attribute(Xaml + "Name")?.Value == "AddAccountItem");
            Assert.True(aggregate.ElementsBeforeSelf().Last().Attribute(Xaml + "Name")?.Value == "AccountOnlineItem",
                "聚合开关不在身份区末尾");
            Assert.True(aggregateDivider.ElementsBeforeSelf().Last() == aggregate
                && profiles.ElementsBeforeSelf().Last() == aggregateDivider,
                "聚合开关与账号列表之间没有分组线");
            Assert.True(actionsDivider.ElementsBeforeSelf().Last() == profiles
                && addAccount.ElementsBeforeSelf().Last() == actionsDivider,
                "添加账号上方没有分组线");
            foreach (var divider in new[] { aggregateDivider, actionsDivider })
            {
                Assert.Equal("Border", divider.Name.LocalName);
                Assert.Equal("1", divider.Attribute("Height")?.Value);
                Assert.Equal("{ThemeResource EgBorderBrush}", divider.Attribute("Background")?.Value);
            }
        });

        TestHarness.Test("标题栏账号按钮与系统三颗之间留出空隙", () =>
        {
            var cluster = Named(Shell(), "AccountCluster");
            Assert.Equal("0,4,160,0", cluster.Attribute("Margin")?.Value);
            Assert.Contains("CaptionRoom() + 16", Source("Views/ShellPage.Sidebar.cs"));
        });
    }

    private static XElement Named(XDocument document, string name) => document.Descendants().Single(element =>
        element.Attribute(Xaml + "Name")?.Value == name);

    private static string Source(string relativePath) =>
        File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", relativePath));

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new InvalidOperationException("本树源码缺失");
    }

    private static XDocument Shell() =>
        XDocument.Load(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "ShellPage.xaml"));
}
