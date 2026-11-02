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
    }

    private static XDocument Shell()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln")))
                return XDocument.Load(Path.Combine(directory.FullName, "src", "Momoka.Shell", "Views", "ShellPage.xaml"));
        throw new InvalidOperationException("本树源码缺失");
    }
}
