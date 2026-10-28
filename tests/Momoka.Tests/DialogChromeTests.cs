using System.Text.RegularExpressions;
using System.Xml.Linq;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 弹窗的表面只由 Theme/Styles.xaml 的 <c>EgDialogSurfaceStyle</c> 决定（2026-10-28 用户令）。
/// <para>
/// 起因是「请选择订阅季」那张表还是框架默认的灰底、8 圆角、1 像素边框，而下载确认弹窗已经是
/// EgSurfaceBrush 底、EgCardCornerRadius 16 圆角、无边框；用户把后者点名当样板，要求其余弹窗照它统一。
/// 隐式样式按类型精确匹配，派生弹窗不吃 ContentDialog 那一条，所以每个具体类型各接一行 —— 这份合同
/// 盯三件事：基础样式在、每个派生弹窗都接了那一行、下载弹窗保留自己的本地四值不被波及。
/// </para>
/// <para>
/// 这里只读源码文本：证明不了样式的实际应用、渲染圆角或遮罩颜色，那些由真实弹窗探针的屏幕截图覆盖。
/// </para>
/// </summary>
internal static class DialogChromeTests
{
    private const string SurfaceStyle = "EgDialogSurfaceStyle";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly Regex Subclass = new(@"class\s+(\w+)\s*:\s*ContentDialog\b", RegexOptions.CultureInvariant);

    public static void Register()
    {
        Test("弹窗表面：共享基础样式给 EgSurface 底、无边框、EgCard 圆角", () =>
        {
            var style = Styles().Single(element => element.Attribute(Xaml + "Key")?.Value == SurfaceStyle);
            Assert.Equal("ContentDialog", style.Attribute("TargetType")?.Value);
            Assert.Equal("{StaticResource DefaultContentDialogStyle}", style.Attribute("BasedOn")?.Value,
                "基础样式不接框架那一条就没有 ControlTemplate");

            var setters = Setters(style);
            Assert.Equal("{ThemeResource EgSurfaceBrush}", setters.GetValueOrDefault("Background"));
            Assert.Equal("{ThemeResource EgTextBrush}", setters.GetValueOrDefault("Foreground"));
            Assert.Equal("0", setters.GetValueOrDefault("BorderThickness"));
            Assert.Equal("{StaticResource EgCardCornerRadius}", setters.GetValueOrDefault("CornerRadius"));
        });

        Test("弹窗表面：每个 ContentDialog 派生类型都接了那一行，下载确认弹窗除外", () =>
        {
            var styles = Styles();
            var rooted = styles
                .Where(element => element.Name.LocalName == "Style" && element.Attribute(Xaml + "Key") is null)
                .Where(element => element.Attribute("BasedOn")?.Value == "{StaticResource " + SurfaceStyle + "}")
                .Select(element => element.Attribute("TargetType")?.Value ?? "")
                .ToHashSet(StringComparer.Ordinal);

            Assert.True(rooted.Contains("ContentDialog"), "代码里 new 的普通 ContentDialog 没有接基础样式");

            var folder = Path.Combine(Root(), "src", "Momoka.Shell");
            var subclasses = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories)
                .SelectMany(file => Subclass.Matches(File.ReadAllText(file)).Select(match => match.Groups[1].Value))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.True(subclasses.Length >= 15, $"只找到 {subclasses.Length} 个 ContentDialog 派生类，清单不完整");
            foreach (var type in subclasses)
            {
                if (type == "MoviePilotDownloadDialog") continue;
                Assert.True(rooted.Contains("local:" + type),
                    $"{type} 没有 EgDialogSurfaceStyle 那行 —— 隐式样式按类型精确匹配，它会停在框架默认灰底方角");
            }
        });

        Test("弹窗表面：只有下载确认弹窗保留本地四值，其余 ContentDialog 不另写", () =>
        {
            var folder = Path.Combine(Root(), "src", "Momoka.Shell", "Views");
            var dialogs = Directory.GetFiles(folder, "*.xaml")
                .Select(file => (File: file, Root: XDocument.Load(file).Root))
                .Where(entry => entry.Root?.Name.LocalName == "ContentDialog")
                .ToArray();

            Assert.True(dialogs.Length >= 15, $"只找到 {dialogs.Length} 张 ContentDialog，弹窗清单不完整");

            foreach (var (file, root) in dialogs)
            {
                var values = new[]
                {
                    root!.Attribute("Background")?.Value,
                    root.Attribute("Foreground")?.Value,
                    root.Attribute("BorderThickness")?.Value,
                    root.Attribute("CornerRadius")?.Value
                };

                if (Path.GetFileName(file) == "MoviePilotDownloadDialog.xaml")
                {
                    Assert.Equal("{ThemeResource EgSurfaceBrush}", values[0], "下载弹窗的样板底色被改了");
                    Assert.Equal("{ThemeResource EgTextBrush}", values[1], "下载弹窗的样板前景色被改了");
                    Assert.Equal("0", values[2], "下载弹窗的样板边框被改了");
                    Assert.Equal("{StaticResource EgCardCornerRadius}", values[3], "下载弹窗的样板圆角被改了");
                    continue;
                }

                Assert.True(values.All(value => value is null),
                    $"{Path.GetFileName(file)} 自己写了弹窗表面属性，样式会漂开：{string.Join(" / ", values)}");
            }
        });
    }

    private static XElement[] Styles() =>
        XDocument.Load(Path.Combine(Root(), "src", "Momoka.Shell", "Theme", "Styles.xaml"))
            .Descendants().Where(element => element.Name.LocalName == "Style").ToArray();

    private static Dictionary<string, string?> Setters(XElement style) =>
        style.Elements().Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(element => element.Attribute("Property")?.Value ?? "", element => element.Attribute("Value")?.Value);

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new InvalidOperationException("本树源码缺失");
    }
}
