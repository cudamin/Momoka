using System.Xml.Linq;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 外壳那两条「框架」的源码契约（2026-11-13 用户六条令的第 1、2、4、5、6 条）：标题栏 48 高、侧边栏 56 宽，
/// 栏里那些按钮统一成 40×40 的方格，选中的那一项铺一块实心底，栏底那颗设置回到页脚项，标题栏左上不再写应用名。
/// <para>
/// 这几件都改错了也不报错：少写一格度量，屏上只是「按钮比昨天小一点」「选中那一项看不出选中」。真正的观感
/// 由实屏截图与 <c>--probe-shell --probe-section shell</c> 的几何断言核对，这一组只钉住那几个数与接线。
/// </para>
/// </summary>
internal static class ShellChromeTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static void Register()
    {
        Test("外壳尺寸：标题栏 48 高、侧边栏 56 宽，页面与栏都让开 48", () =>
        {
            var document = Shell();

            // 2026-11-13 用户令「参考 codex 窗口边框的宽度和高度」：那一条 40→48、那条栏 48→56。
            Assert.Equal("48", Named(document, "AppTitleBar").Attribute("Height")?.Value,
                "标题栏不是 48 高了");
            Assert.Equal("0,48,0,0", Named(document, "ContentHost").Attribute("Margin")?.Value,
                "默认商店模式没有让画布避开标题栏");
            Assert.True(Named(document, "ContentHost").Attribute("Padding") is null,
                "默认商店模式额外推开了页面内容");
            Assert.Equal("0,48,0,0", Named(document, "SidebarHost").Attribute("Margin")?.Value,
                "侧边栏宿主没有从 48 起画（标题栏下沿）");
            Assert.True(Named(document, "Sidebar").Attribute("Margin") is null,
                "侧边栏在宿主里又推了一次标题栏");

            // 2026-11-14 用户令「标题栏的颜色要和侧边栏一样」：整条标题栏铺栏那一支角色色（EgSidebarBrush），
            // 不接管输入；默认收着，由 SyncSidebar 随栏一起露出。
            var titleBackground = Named(document, "TitleBarBackground");
            Assert.Equal("48", titleBackground.Attribute("Height")?.Value, "标题栏那一条不是 48 高");
            Assert.Equal("{ThemeResource EgSidebarBrush}", titleBackground.Attribute("Background")?.Value,
                "标题栏那一条不是侧边栏的颜色");
            Assert.Equal("False", titleBackground.Attribute("IsHitTestVisible")?.Value,
                "标题栏那一条接管了标题栏输入");
            Assert.Equal("Collapsed", titleBackground.Attribute("Visibility")?.Value,
                "标题栏那一条默认没有收着");

            var sidebar = Named(document, "Sidebar");
            Assert.Equal("256", sidebar.Attribute("OpenPaneLength")?.Value, "展开那一档不是 256");
            Assert.Equal("56", sidebar.Attribute("CompactPaneLength")?.Value, "图标栏那一档不是 56");

            // 标题栏正中那颗搜索框与右边那颗账号都落在同一条中线上（中心 24）。
            Assert.Equal("34", Named(document, "TitleSearchBox").Attribute("Height")?.Value);
            Assert.Equal("0,7,0,0", Named(document, "TitleSearchBox").Attribute("Margin")?.Value,
                "搜索框没有落在 48 的中线上");
        });

        Test("标题栏那一排：40×40 的托盘，返回与折叠 20、工具字形 28", () =>
        {
            var style = Named(Shell(), "AppTitleBar").Descendants()
                .First(element => element.Name.LocalName == "Style"
                    && element.Attribute("TargetType")?.Value == "Button");
            var height = style.Elements().Single(element => element.Attribute("Property")?.Value == "Height");
            var width = style.Elements().Single(element => element.Attribute("Property")?.Value == "Width");
            Assert.Equal("40", height.Attribute("Value")?.Value, "托盘不是 40 高");
            Assert.Equal("40", width.Attribute("Value")?.Value, "托盘不是 40 宽");

            // 四个按 24×24 画的路径图标外面那层放大框：26 → 28（PathIcon 不缩放几何）。
            foreach (var name in new[] { "BackButton", "ForwardButton", "NavToggleButton", "HomeButton", "SearchButton", "SettingsButton" })
            {
                var box = Named(Shell(), name).Descendants().Single(element => element.Name.LocalName == "Viewbox");
                var expected = name is "BackButton" or "ForwardButton" or "NavToggleButton" ? "20" : "28";
                Assert.Equal(expected, box.Attribute("Width")?.Value, $"{name} 的字形尺寸不对");
                Assert.Equal(expected, box.Attribute("Height")?.Value, $"{name} 的字形尺寸不对");
            }
        });

        Test("侧边栏度量：条目 40 高、图标盒 20、按钮左右各留 8，选中项铺实心底", () =>
        {
            var document = Shell();
            var presentation = document.Root!.Name.Namespace;
            var resources = Named(document, "Sidebar").Elements()
                .Single(element => element.Name.LocalName == "NavigationView.Resources")
                .Element(presentation + "ResourceDictionary")!;
            var dark = resources.Element(presentation + "ResourceDictionary.ThemeDictionaries")!
                .Elements(presentation + "ResourceDictionary")
                .Single(element => (string?)element.Attribute(Xaml + "Key") == "Dark");

            string? Value(string key) => resources.Elements()
                .FirstOrDefault(element => element.Attribute(Xaml + "Key")?.Value == key)
                ?.Value;

            Assert.Equal("40", Value("NavigationViewItemOnLeftMinHeight"), "条目不是 40 高");
            Assert.Equal("20", Value("NavigationViewItemOnLeftIconBoxHeight"), "图标盒不是 20");
            Assert.Equal("4,2", Value("NavigationViewItemButtonMargin"), "按钮不是左右各留 4");
            // 实心底取的是角色色而不是写死的色值：换主题时由 ThemeHost 顺手涂过去。
            foreach (var key in new[]
            {
                "NavigationViewItemBackgroundSelected",
                "NavigationViewItemBackgroundSelectedPointerOver",
                "NavigationViewItemBackgroundSelectedPressed"
            })
            {
                var entry = dark.Elements().Single(element => element.Attribute(Xaml + "Key")?.Value == key);
                Assert.Equal("EgSurfaceHoverBrush", entry.Attribute("ResourceKey")?.Value,
                    $"{key} 不是实心的角色色");
            }
        });

        Test("侧边栏页脚：设置回到 FooterMenuItems，账号不在栏里", () =>
        {
            var document = Shell();
            var footer = Named(document, "Sidebar").Elements()
                .Single(element => element.Name.LocalName == "NavigationView.FooterMenuItems");
            var item = footer.Elements().Single(element => element.Name.LocalName == "NavigationViewItem");
            Assert.Equal("settings", item.Attribute("Tag")?.Value);
            Assert.Equal("False", item.Attribute("SelectsOnInvoked")?.Value,
                "栏底的设置会点亮自己：它开的是另一个窗口");
            Assert.Equal("ShellPage_SidebarSettingsButton", item.Attribute(Xaml + "Uid")?.Value,
                "栏底的设置丢了本地化名字");

            // 账号那一颗只剩标题栏右上角那一处（用户令「人像图标移到最小化左边」）：栏里不该再有第二份入口。
            var cluster = Named(document, "AccountCluster");
            var children = cluster.Elements().Where(element => element.Name.LocalName == "Button").ToArray();
            Assert.Equal(1, children.Length);
            Assert.Equal("AccountButton", children[0].Attribute(Xaml + "Name")?.Value);
        });

        Test("标题栏按钮态：悬停按下使用圆角半透明底板，禁用态与高对比度独立着墨", () =>
        {
            var code = File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "ShellPage.xaml.cs"));
            Assert.Contains("Chrome.Resources[\"ButtonBackgroundPointerOver\"] = _titleOverlayHover;", code);
            Assert.Contains("Chrome.Resources[\"ButtonBackgroundPressed\"] = _titleOverlayPressed;", code);
            Assert.Contains("AccountCluster.Resources[\"ButtonBackgroundPointerOver\"] = _titleOverlayHover;", code);
            Assert.Contains("AccountCluster.Resources[\"ButtonBackgroundPressed\"] = _titleOverlayPressed;", code);
            Assert.Contains("AppTitleBar.Resources[\"ButtonForegroundDisabled\"] = _titleInkDisabled;", code);
            Assert.Contains("Ink(\"SystemColorHighlightTextColor\")", code);
            Assert.Contains("Ink(\"SystemColorGrayTextColor\")", code);
            Assert.Contains("Ink(\"SystemColorHighlightColor\")", code);
            Assert.Contains("Ink(\"EgTextFaintBrush\")", code);
            Assert.Contains("Ink(\"EgOverlayHoverBrush\")", code);
            Assert.Contains("Ink(\"EgOverlayPressedBrush\")", code);
        });

        Test("窄窗侧栏：低于 641 DIP 折叠为覆盖抽屉，不挤占内容宽且支持遮罩与 Esc 收起", () =>
        {
            var document = Shell();
            Assert.Equal("0,48,0,0", Named(document, "SidebarDismissScrim").Attribute("Margin")?.Value);
            Assert.Equal("OnSidebarDismissPointerPressed", Named(document, "SidebarDismissScrim").Attribute("PointerPressed")?.Value);
            Assert.Equal("{ThemeResource EgFrostBrush}", Named(document, "SidebarOverlayBackdrop").Attribute("Background")?.Value);
            Assert.Equal("OnSidebarHostKeyDown", Named(document, "SidebarHost").Attribute("KeyDown")?.Value);

            var sidebarCode = File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "ShellPage.Sidebar.cs"));
            Assert.Contains("SidebarMinimalThreshold = 641;", sidebarCode);
            Assert.Contains("NavigationViewPaneDisplayMode.LeftMinimal", sidebarCode);
            Assert.Contains("var contentLeft = minimal ? 0 : width;", sidebarCode);
            Assert.Contains("SidebarDismissScrim.Visibility =", sidebarCode);
            Assert.Contains("SidebarOverlayBackdrop.Visibility =", sidebarCode);
        });
    }

    private static XDocument Shell() =>
        XDocument.Load(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "ShellPage.xaml"));

    private static XElement Named(XDocument document, string name) => document.Descendants().Single(element =>
        element.Attribute(Xaml + "Name")?.Value == name);

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new InvalidOperationException("本树源码缺失");
    }
}
