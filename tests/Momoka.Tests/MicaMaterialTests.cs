using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 主窗口材质（2026-11-12 用户令「改为Mica」）的源码契约：窗口画布是压在 Mica 上的一层半透明窗口色，
/// 全树只在 <c>ShellPage.ContentHost</c> 画一次；各页面的根背景必须留空，否则同一浓度叠两道，Mica 就白开了。
/// <para>
/// 屏上的观感由实屏截图核对，这一组只钉住「哪一层在哪儿画、画多浓、换主题之后还记不记得这个浓度」——
/// 三件都是编译器看不见、跑起来也不报错的事。
/// </para>
/// </summary>
internal static class MicaMaterialTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static void Register()
    {
        Test("卡片主题角色：别名与运行涂色登记一致，媒体副行保留压图层级", () =>
        {
            var host = File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "Theme", "ThemeHost.cs"));
            var palette = Palette();
            foreach (var dictionary in new[] { "Dark", "Light" })
            {
                var entries = Dictionaries(palette)[dictionary];
                foreach (var key in new[] { "EgCardCaptionForegroundBrush", "EgCardCaptionSecondaryBrush" })
                {
                    Assert.Equal("EgTextBrush", Entry(entries, key).Attribute("ResourceKey")?.Value);
                    Assert.Contains("(\"" + key + "\", theme => theme.Colors.Text)", host);
                }
                Assert.Equal("EgOnScrimBrush", Entry(entries, "EgMediaCaptionForegroundBrush").Attribute("ResourceKey")?.Value);
                Assert.Equal("EgOnScrimDimBrush", Entry(entries, "EgMediaCaptionSecondaryBrush").Attribute("ResourceKey")?.Value);
            }
            Assert.Contains("(\"EgMediaCaptionForegroundBrush\", _ => White)", host);
            Assert.Contains("(\"EgMediaCaptionSecondaryBrush\", _ => ThemeColor.Rgb(0xC9, 0xD0, 0xD9))", host);
            var contrast = Dictionaries(palette)["HighContrast"];
            foreach (var key in new[] { "EgCardCaptionForegroundBrush", "EgCardCaptionSecondaryBrush", "EgMediaCaptionForegroundBrush", "EgMediaCaptionSecondaryBrush" })
                Assert.Equal("{ThemeResource SystemColorWindowTextColor}", Entry(contrast, key).Attribute("Color")?.Value);
        });

        Test("窗口画布：Mica 上那一层是半透明窗口色，弹窗表面仍不透明", () =>
        {
            var palette = Palette();
            foreach (var key in new[] { "Dark", "Light" })
            {
                var dictionary = Dictionaries(palette)[key];
                var canvas = Entry(dictionary, "EgCanvasBrush");
                var window = Entry(dictionary, "EgWindowColor");
                var color = canvas.Attribute("Color")?.Value ?? string.Empty;

                Assert.Equal(0x99, Alpha(color), $"{key}：EgCanvasBrush 的浓度变了（{color}）");
                Assert.Equal(LastSix(window.Value), LastSix(color),
                    $"{key}：画布不是窗口色，壁纸透上来会带一层别的色相");

                // 十几张弹窗、筛选面板与 ContentDialogTopOverlay 都靠这一支当实心表面。
                Assert.Equal(0xFF, Alpha(Entry(dictionary, "LayerFillColorDefaultBrush").Attribute("Color")?.Value ?? string.Empty),
                    $"{key}：浮层表面被做成了半透明，弹窗会跟着变成玻璃");
            }

            var contrast = Dictionaries(palette)["HighContrast"];
            Assert.NotNull(Entry(contrast, "EgCanvasBrush"), "高对比度字典里没有画布角色，会一直停在别的字典的字面值上");
        });

        Test("窗口画布：只有外壳画它，页面根背景都是透明的", () =>
        {
            var shell = XDocument.Load(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "ShellPage.xaml"));
            Assert.Equal("{ThemeResource EgCanvasBrush}", Named(shell, "ContentHost").Attribute("Background")?.Value,
                "外壳那块画布不是 EgCanvasBrush");
            // 2026-11-12 晚用户令「侧边栏和标题栏采用云母 Alt」：栏自己不再铺那层半透明面色，直接露出窗口材质。
            Assert.Equal("Transparent", Named(shell, "SidebarHost").Attribute("Background")?.Value,
                "侧边栏又铺了自己的一块底，云母 Alt 被盖住");
            // 画布必须让开标题栏那一整条：它原本靠内边距让位，而内边距只推孩子、画布仍铺满整条 —— 那一档
            // 标题栏永远是画布色，材质一点也露不出来。2026-11-12 晚改成外边距，2026-11-13 标题栏 40→48。
            Assert.Equal("0,48,0,0", Named(shell, "ContentHost").Attribute("Margin")?.Value,
                "画布又盖住了标题栏那一层材质");

            // 页面自己再画一层，屏上就是两道 60% 叠成 84% —— 顶上一条比正文暗两档。
            var offenders = new List<string>();
            foreach (var file in Directory.EnumerateFiles(Path.Combine(Root(), "src", "Momoka.Shell", "Views"), "*.xaml"))
            {
                var root = XDocument.Load(file).Root!;
                if (root.Name.LocalName != "Page") continue;

                var background = root.Attribute("Background")?.Value;
                if (background is null || background == "Transparent") continue;

                offenders.Add($"{Path.GetFileName(file)}={background}");
            }

            Assert.True(offenders.Count == 0, $"这些页面的根背景又铺了一层：{string.Join("、", offenders)}");

            // 登录页挂在 <see cref="Grid"/> 上，不在 Page 那一条里。
            var signIn = XDocument.Load(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "SignInPage.xaml"));
            Assert.Equal("Transparent", Named(signIn, "SignInRoot").Attribute("Background")?.Value,
                "登录页根上那层还在，Mica 透不上来");
        });

        Test("窗口画布：岛底材是 Mica Alt，「在前台」由宿主窗口说了算", () =>
        {
            // 2026-11-12 改判据：原来钉的是 HostWindow 里那句 `new MicaBackdrop { Kind = MicaKind.Base }`。
            // 实屏逐像素量出那句话是错的 —— 框架的 MicaBackdrop 拿默认配置，而默认配置的 IsInputActive
            // 跟 XamlRoot 走，岛是子窗口、拿不到激活，于是云母按「不在前台」渲染成实心 #202020，壁纸一点
            // 没采（同一台机器上设置窗口的云母是带蓝的 45,57,70）。现在改由 IslandMicaBackdrop 自己钉这一项。
            // 档位（2026-11-12 晚起是 Alt，用户令「侧边栏和标题栏采用云母 Alt」）与「不许回到框架默认配置」
            // 两件事仍要守着，所以判据搬在新文件上。
            var host = File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "Windowing", "HostWindow.cs"));
            var hostBackdrop = File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "Windowing", "HostWindow.Backdrop.cs"));
            Assert.Contains("_backdrop ??= new IslandMicaBackdrop();", hostBackdrop, "主窗口没有挂自己那份云母");
            Assert.DoesNotContain("new MicaBackdrop", host, "又回到框架默认配置了：岛上的云母会渲染成实心灰");

            // 「在前台」这条线要接上：断了云母就退回实心，而屏上不报错，只有像素量得出来。
            Assert.Contains("FocusChanged += OnBackdropFocusChanged;", host, "宿主窗口的激活没有转给云母");
            Assert.Contains("_backdrop?.SetInputActive(active)", hostBackdrop, "激活转给了别的东西");
            Assert.Contains("_backdrop?.ApplyTheme()", host, "换主题时没有重钉云母的深浅");

            var backdrop = File.ReadAllText(
                Path.Combine(Root(), "src", "Momoka.Shell", "Windowing", "IslandMicaBackdrop.cs"));
            Assert.Contains("controller = new MicaController();", backdrop);
            Assert.Contains("controller.Kind = MicaKind.BaseAlt;", backdrop,
                "窗口底材不是 Mica Alt 了：侧边栏与标题栏露出来的就是另一种材质");
            Assert.Contains("configuration.IsInputActive = _inputActive;", backdrop,
                "又把「在前台」交回框架默认值了，岛上报的永远是假");
            Assert.Contains("OnDefaultSystemBackdropConfigurationChanged", backdrop,
                "框架重算配置之后没人钉回来，IsInputActive 会被写回假");
            Assert.Contains("MicaController.IsSupported()", backdrop, "没问本机支不支持就挂控制器");
        });

        Test("窗口画布：换主题时重画这一层仍带着浓度", () =>
        {
            var palette = Palette();
            var dark = Dictionaries(palette)["Dark"];
            var source = File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "Theme", "ThemeHost.cs"));

            // 首帧的字面值和换主题时算出来的数必须是同一个 —— 两处漂开，切一次主题 Mica 就没了。
            Assert.Equal(Alpha(Entry(dark, "EgCanvasBrush").Attribute("Color")?.Value ?? string.Empty), Constant(source, "CanvasAlpha"));
            Assert.Equal(Alpha(Entry(dark, "LayerFillColorAltBrush").Attribute("Color")?.Value ?? string.Empty), Constant(source, "SidebarLayerAlpha"));

            Assert.Contains("(\"EgCanvasBrush\", theme => theme.Colors.Window.WithAlpha(CanvasAlpha))", source,
                "ThemeHost 没有把画布按窗口角色加重画");
        });

        Test("设置页材质：整窗一块 Mica ＋ 一层画布，标题栏与分类栏都不另铺底", () =>
        {
            var window = File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "SettingsWindow.cs"));

            // 窗口级那一层：普通窗口用 Base，和主窗口同一个档位。
            Assert.Contains("new MicaBackdrop { Kind = MicaKind.Base }", window, "设置窗口没有挂 Mica（Base）");
            // 画布浓度与主窗口成对；回退路必须落回不透明，否则没材质时整窗是透明的。
            Assert.Contains("private const byte CanvasAlpha = 0x99", window, "设置窗口的画布浓度和主窗口不是一档");
            Assert.Contains("_canvas.Color = SelectCanvasColor(systemAppearance, WindowColor(theme), _mica)", window,
                "换主题时画布没有按系统状态重新算");
            Assert.Contains("created.InvalidateSystemAppearance();", window,
                "材质赋值后仍绕过外观缓存失效，失败重画后相同系统状态无法恢复");
            Assert.DoesNotContain("TryGetValue(\"EgWindowBrush\"", window,
                "又回去按资源键取窗口色了：EgWindowBrush 在 ThemeDictionaries 里，代码查找取不到");

            // 整窗只有一块底（2026-11-12 用户令「不是，你为什么一定要给标题和侧边栏加卡片」）：标题栏与
            // 三颗按钮的四种底色全透明，底下就是根 Grid 那支画布，窗口从上沿到下沿不断开。
            Assert.Contains("titleBar.BackgroundColor = Colors.Transparent", window, "标题栏又铺了自己的一块底");
            Assert.Contains("titleBar.ButtonInactiveBackgroundColor = Colors.Transparent", window,
                "失焦时按钮底下会露出第二块默认底");
            Assert.DoesNotContain("SystemBackdropElement", window, "设置窗口里又挂了局部材质");

            // 页面里不许再出现分类栏那块自己的底：那是第二块「卡片」，窗口就分成两截了。
            var page = File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "SettingsPage.xaml"));
            Assert.DoesNotContain("SystemBackdropElement", page, "设置页里又挂了局部材质");
            Assert.DoesNotContain("SettingsNavigationLayer", page, "分类栏又铺了一块自己的底");
        });

        Test("商店模式底材：详情页与订阅详情页不再挂页面级材质", () =>
        {
            // 2026-11-12 用户令「把媒体详情页的商店模式也改成跟主页一样的」：这两页的底从此就是外壳那块画布
            // （Mica Base ＋ 60% 窗口色，只在 ShellPage.ContentHost 画一次）。从前它们各自铺一块 Mica Alt 把
            // 画布盖掉，同一格设置在主页与详情页是两种底 —— 屏上不报错，只有截图与探针读得出来。
            foreach (var (file, label, showcase) in new[]
            {
                ("DetailPage.xaml", "媒体详情页", "DetailPage.Showcase.cs"),
                ("MoviePilotSubscriptionPage.xaml", "MP 订阅详情页", "MoviePilotSubscriptionPage.Showcase.cs"),
            })
            {
                var document = XDocument.Load(Path.Combine(Root(), "src", "Momoka.Shell", "Views", file));
                // Page.Content 那一层就是页面底；卡面自己的 SystemBackdropElement 在更深的子树里，不算。
                var surface = document.Root!.Elements().FirstOrDefault(element => element.Name.LocalName == "Grid")
                    ?? throw new InvalidOperationException($"{label} 的根不是 Grid");
                var backdrop = surface.Elements().FirstOrDefault(element => element.Name.LocalName == "SystemBackdropElement");
                Assert.True(backdrop is null,
                    $"{label} 又给页面挂了局部材质：{backdrop?.Attribute(Xaml + "Name")?.Value}");

                var source = File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "Views", showcase));
                Assert.DoesNotContain("DetailMica.Apply(PlainBackdrop", source, $"{label} 的简洁档又去挂页面级材质了");
            }
        });

        Test("商店详情内容卡：共享窗口底材，内容层色与高对比度使用系统角色", () =>
        {
            var page = XDocument.Load(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "DetailPage.xaml"));
            Assert.False(page.Descendants().Any(element => element.Name.LocalName == "SystemBackdropElement"),
                "顶部内容卡又创建了独立系统材质，没有共享窗口基底");
            Assert.Equal("{ThemeResource EgDetailContentLayerBrush}",
                Named(page, "HeroContentLayerSource").Attribute("Background")?.Value);
            foreach (var name in new[] { "HeroCardTop", "HeroCardBottom" })
                Assert.False(Named(page, name).Elements().Any(), "卡片背景仍有多余的子材质层");

            var dictionaries = Dictionaries(Palette());
            foreach (var key in new[] { "Dark", "Light" })
            {
                var brush = Entry(dictionaries[key], "EgDetailContentLayerBrush");
                Assert.Equal("SolidColorBrush", brush.Name.LocalName);
                Assert.Equal("{ThemeResource LayerFillColorDefault}", brush.Attribute("Color")?.Value,
                    "内容卡必须使用系统内容层角色，而非命令层或项目亚克力卡面");
            }
            Assert.Equal("{ThemeResource SystemColorWindowColor}",
                Entry(dictionaries["HighContrast"], "EgDetailContentLayerBrush").Attribute("Color")?.Value,
                "高对比度不能使用系统层色的占位 Color，必须使用系统窗口色");
        });
    }

    private static XDocument Palette() =>
        XDocument.Load(Path.Combine(Root(), "src", "Momoka.Shell", "Theme", "Palette.xaml"));

    private static Dictionary<string, XElement> Dictionaries(XDocument palette) =>
        palette.Descendants()
            .Where(element => element.Name.LocalName == "ResourceDictionary" && element.Attribute(Xaml + "Key") is not null)
            .ToDictionary(element => element.Attribute(Xaml + "Key")!.Value, element => element);

    private static XElement Entry(XElement dictionary, string key) =>
        dictionary.Elements().SingleOrDefault(element => element.Attribute(Xaml + "Key")?.Value == key)
        ?? throw new InvalidOperationException($"{dictionary.Attribute(Xaml + "Key")?.Value} 里没有 {key}");

    private static XElement Named(XDocument document, string name) =>
        document.Descendants().Single(element => element.Attribute(Xaml + "Name")?.Value == name);

    private static string LastSix(string? color) =>
        color is { Length: 9 } ? color[3..] : throw new InvalidOperationException($"颜色不是 #AARRGGBB：{color}");

    private static int Alpha(string color) =>
        color is { Length: 9 } && int.TryParse(color.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var alpha)
            ? alpha
            : throw new InvalidOperationException($"颜色不是 #AARRGGBB：{color}");

    private static int Constant(string source, string name)
    {
        var match = Regex.Match(source, $@"const\s+byte\s+{name}\s*=\s*0x([0-9A-Fa-f]{{2}})", RegexOptions.CultureInvariant);
        if (!match.Success) throw new InvalidOperationException($"找不到 {name} 的声明");
        return int.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new InvalidOperationException("本树源码缺失");
    }
}
