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
            Assert.Equal("{ThemeResource LayerFillColorAltBrush}", Named(shell, "SidebarHost").Attribute("Background")?.Value,
                "侧边栏没有走半透明那一层");

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

        Test("窗口画布：岛底材是 Mica（Base），不是 Mica Alt", () =>
        {
            var source = File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "Windowing", "HostWindow.cs"));
            Assert.Contains("new MicaBackdrop { Kind = MicaKind.Base }", source);
            Assert.DoesNotContain("MicaKind.BaseAlt", source);
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
