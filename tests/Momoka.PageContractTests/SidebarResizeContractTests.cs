using System.Xml.Linq;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 侧边栏拖动调宽的源码契约（2026-11-14 用户令「拖动侧边栏边缘调整宽度」「只有展开时才支持调整」）：
/// 拖动条挂在哪儿、指针链路怎么接、宽度在哪儿夹取、松手写回哪一格。实际拖动与几何由运行时探针检查。
/// </summary>
internal static class SidebarResizeContractTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static void Register()
    {
        Test("侧边栏拖动条：透明命中带压在栏右沿，初始收着", () =>
        {
            var page = XDocument.Load(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "ShellPage.xaml"));
            var grip = Named(page, "SidebarResizeGrip");

            // 初始收着：登录前与沉浸模式都没有可拖的栏；展开后由 ApplySidebarLayout 露出来。
            Assert.Equal("Collapsed", (string?)grip.Attribute("Visibility"));
            // 透明但可命中 —— null 背景的 Grid 收不到指针事件。
            Assert.Equal("Transparent", (string?)grip.Attribute("Background"));
            // 站在内容区一侧的栏右沿，宽 7 从标题栏下沿到底（初始 256 与出厂宽度成对，随后由代码重摆）。
            Assert.Equal("7", (string?)grip.Attribute("Width"));
            Assert.Equal("256,48,0,0", (string?)grip.Attribute("Margin"));
            Assert.Equal("调整侧边栏宽度", (string?)grip.Attribute("AutomationProperties.Name"));

            // 必须画在侧栏（ZIndex 1）之上，否则那 7 像素的命中带会被栏自己的子树吃掉。
            Assert.Equal("2", (string?)grip.Attribute("Canvas.ZIndex"));
            Assert.Equal("1", (string?)Named(page, "SidebarHost").Attribute("Canvas.ZIndex"));
        });

        Test("侧边栏拖动：只在展开档接线，宽度夹取后写回设置", () =>
        {
            var source = File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "ShellPage.Sidebar.cs"));
            Assert.Contains("SidebarResizeGrip.PointerPressed", source);
            Assert.Contains("SidebarResizeGrip.CapturePointer", source);
            Assert.Contains("InputSystemCursorShape.SizeWestEast", source, "拖动时光标不是左右箭头");
            // 只有展开档才露出拖动条；展开与收起的门在 ApplySidebarLayout 里。
            Assert.Contains("var resizable = shown && Sidebar.IsPaneOpen;", source);
            Assert.Contains("SidebarResizeGrip.Visibility = resizable", source);
            // 上下限与拖动那一头共用 Core 的常量；夹取不写回，松手才写。
            Assert.Contains("UiSettings.MinSidebarWidth", source);
            Assert.Contains("UiSettings.MaxSidebarWidth", source);
            Assert.Contains("Settings.Ui.SidebarWidth = width;", source);
            Assert.Contains("_settings.Save();", source);

            var settings = File.ReadAllText(
                Path.Combine(Root(), "src", "Momoka.Core", "Configuration", "SettingsMigration.cs"));
            Assert.Contains("UiSettings.ClampSidebarWidth(settings.Ui.SidebarWidth)", settings,
                "规整没有把存下来的侧栏宽度夹回拖动范围");
        });

        Test("侧边栏拖动条：ProtectedCursor 只能从派生类型拿到", () =>
        {
            var source = File.ReadAllText(
                Path.Combine(Root(), "src", "Momoka.Shell", "Views", "SidebarResizeGrip.cs"));
            Assert.Contains("class SidebarResizeGrip : Grid", source);
            Assert.Contains("ProtectedCursor", source);
        });
    }

    private static XElement Named(XDocument document, string name) =>
        document.Descendants().Single(element => (string?)element.Attribute(Xaml + "Name") == name);

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new AssertionException("找不到本次编译所属的工作树");
    }
}
