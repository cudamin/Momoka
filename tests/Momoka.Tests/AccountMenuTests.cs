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
            var toggle = menu.Elements().SingleOrDefault(element => element.Attribute(Xaml + "Name")?.Value == "AggregateModeItem");
            Assert.True(toggle is not null, "顶部账号菜单缺少聚合模式开关");
            Assert.Equal("ToggleMenuFlyoutItem", toggle!.Name.LocalName);
            Assert.Equal("聚合模式", toggle.Attribute("Text")?.Value);
            Assert.Equal("OnAggregateModeClicked", toggle.Attribute("Click")?.Value);
            Assert.True(menu.Elements().Any(element => element.Attribute(Xaml + "Name")?.Value == "OfflineMenu"),
                "顶部账号菜单缺少按账号下线入口");
        });

        TestHarness.Test("账号菜单：在线数量独立一行，长身份不会撑宽面板", () =>
        {
            var shell = Shell();
            var identity = shell.Descendants().Single(element => element.Attribute(Xaml + "Name")?.Value == "AccountIdentityItem");
            Assert.Equal("180", identity.Attribute("MaxWidth")?.Value);
            Assert.True(shell.Descendants().Any(element => element.Attribute(Xaml + "Name")?.Value == "AccountOnlineItem"),
                "在线账号数应独立呈现，不追加到身份行");
        });
    }

    private static XDocument Shell()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln")))
                return XDocument.Load(Path.Combine(directory.FullName, "src", "Momoka.Shell", "Views", "ShellPage.xaml"));
        throw new InvalidOperationException("本树源码缺失");
    }
}
