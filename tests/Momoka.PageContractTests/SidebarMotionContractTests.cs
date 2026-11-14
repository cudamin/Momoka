using System.Xml.Linq;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>侧边栏设置图标的模板与偏好门控契约；实际状态由运行时探针检查。</summary>
internal static class SidebarMotionContractTests
{
    internal static void Register()
    {
        Test("侧边栏设置项保留 AnimatedIcon 与静态回退", () =>
        {
            var page = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "ShellPage.xaml"));
            XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
            var item = page.Descendants().Single(element => (string?)element.Attribute(xaml + "Name") == "SidebarSettings");
            var icon = item.Descendants().Single(element => element.Name.LocalName == "AnimatedIcon");
            Assert.Equal("SidebarSettingsIcon", (string?)icon.Attribute(xaml + "Name"));
            Assert.True(icon.Descendants().Any(element => element.Name.LocalName == "AnimatedSettingsVisualSource"));
            var fallback = icon.Descendants().Single(element => element.Name.LocalName == "FontIconSource");
            Assert.Equal("\uE713", (string?)fallback.Attribute("Glyph"));
        });
        Test("侧边栏设置图标跟随应用与系统动画开关", () =>
        {
            var source = File.ReadAllText(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "ShellPage.SidebarMotion.cs"));
            Assert.Contains("HomeMotion.AnimationsEnabled", source);
            Assert.Contains("SidebarSettingsIcon.Source", source);
            Assert.Contains("AnimationsEnabledChanged", source);
            Assert.Contains("SyncSidebarSettingsIcon", source);
            var hookup = File.ReadAllText(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "ShellPage.Sidebar.cs"));
            Assert.Contains("AttachSidebarMotion();", hookup);
            Assert.Contains("DetachSidebarMotion();", hookup);
        });
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new AssertionException("找不到本次编译所属的工作树");
    }
}
