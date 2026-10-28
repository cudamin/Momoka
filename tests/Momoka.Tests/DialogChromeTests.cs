using System.Text.RegularExpressions;
using System.Xml.Linq;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>Source contracts for separate library and MP/default surfaces; rendering is covered by native probes.</summary>
internal static class DialogChromeTests
{
    private const string SurfaceStyle = "EgDialogSurfaceStyle";
    private const string LibraryStyle = "EgLibraryDialogSurfaceStyle";
    private static readonly HashSet<string> LibraryDialogs = new(StringComparer.Ordinal)
    {
        "AniBtScrapeDialog", "ArtworkViewer", "CollectionDialog", "CoverDialog", "CoverPickerDialog", "EmbyMetadataDialog",
        "FnNasMatchDialog", "FnNasMetadataDialog", "FnNasVersionsDialog", "IdentifyDialog", "MetadataDialog",
        "MissingEpisodesDialog", "RefreshMetadataDialog", "SubtitleDialog"
    };
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

        Test("媒体库弹窗：与设置侧栏同一底色角色并继承圆角", () =>
        {
            var style = Styles().Single(element => element.Attribute(Xaml + "Key")?.Value == LibraryStyle);
            Assert.Equal("{StaticResource " + SurfaceStyle + "}", style.Attribute("BasedOn")?.Value);
            Assert.Equal("{ThemeResource LayerFillColorDefaultBrush}", Setters(style).GetValueOrDefault("Background"));
            var settings = XDocument.Load(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "SettingsPage.xaml")).Root!;
            Assert.Equal(settings.Attribute("Background")?.Value, Setters(style).GetValueOrDefault("Background"));
        });

        Test("弹窗表面：媒体库与MP及普通窗口按明确范围接各自样式", () =>
        {
            var styles = Styles();
            var rooted = styles.Where(element => element.Name.LocalName == "Style" && element.Attribute(Xaml + "Key") is null)
                .ToDictionary(element => element.Attribute("TargetType")?.Value ?? "", element => element.Attribute("BasedOn")?.Value);
            Assert.Equal("{StaticResource " + SurfaceStyle + "}", rooted.GetValueOrDefault("ContentDialog"),
                "普通/MP动态窗口不得改成媒体库底色");
            var folder = Path.Combine(Root(), "src", "Momoka.Shell");
            var subclasses = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories)
                .SelectMany(file => Subclass.Matches(File.ReadAllText(file)).Select(match => match.Groups[1].Value))
                .Distinct(StringComparer.Ordinal).ToArray();
            Assert.True(subclasses.Length >= 15);
            foreach (var type in subclasses)
            {
                if (type == "MoviePilotDownloadDialog") continue;
                var expected = LibraryDialogs.Contains(type) ? LibraryStyle : SurfaceStyle;
                Assert.Equal("{StaticResource " + expected + "}", rooted.GetValueOrDefault("local:" + type),
                    $"{type} 的弹窗表面越过媒体库/MP边界");
            }
        });

        Test("媒体库弹窗：正文叠色局部绑定侧栏颜色而MP保持框架原样", () =>
        {
            var folder = Path.Combine(Root(), "src", "Momoka.Shell", "Views");
            foreach (var type in LibraryDialogs)
            {
                var alias = XDocument.Load(Path.Combine(folder, type + ".xaml")).Descendants().Single(element =>
                    element.Attribute(Xaml + "Key")?.Value == "ContentDialogTopOverlay");
                Assert.Equal("LayerFillColorDefaultBrush", alias.Attribute("ResourceKey")?.Value);
            }
            foreach (var file in Directory.GetFiles(folder, "MoviePilot*.xaml"))
                Assert.False(File.ReadAllText(file).Contains("ContentDialogTopOverlay", StringComparison.Ordinal), "MP叠色被修改");
            Assert.False(Styles().Any(element => element.Attribute(Xaml + "Key")?.Value == "ContentDialogTopOverlay"),
                "不能全局覆盖正文叠色");
        });

        Test("媒体库弹窗：筛选和库设置也用相同主题底色与圆角刻度", () =>
        {
            foreach (var file in new[] { "FilterPanel.xaml", "LibrarySettingsPanel.xaml" })
            {
                var surface = XDocument.Load(Path.Combine(Root(), "src", "Momoka.Shell", "Views", file)).Descendants()
                    .Single(element => element.Attribute(Xaml + "Name")?.Value == "LibraryDialogSurface");
                Assert.Equal("{ThemeResource LayerFillColorDefaultBrush}", surface.Attribute("Background")?.Value);
                Assert.Equal("{StaticResource EgCardCornerRadius}", surface.Attribute("CornerRadius")?.Value);
            }
        });

        Test("媒体库弹窗：编辑子浮层同色圆角但不污染全局Flyout", () =>
        {
            var style = Styles().Single(element => element.Attribute(Xaml + "Key")?.Value == "EgLibraryFlyoutSurfaceStyle");
            Assert.Equal("FlyoutPresenter", style.Attribute("TargetType")?.Value);
            Assert.Equal("{ThemeResource LayerFillColorDefaultBrush}", Setters(style).GetValueOrDefault("Background"));
            Assert.Equal("{StaticResource EgCardCornerRadius}", Setters(style).GetValueOrDefault("CornerRadius"));
            foreach (var file in new[] { "EmbyMetadataDialog.xaml", "MetadataListEditor.xaml" })
                foreach (var flyout in XDocument.Load(Path.Combine(Root(), "src", "Momoka.Shell", "Views", file)).Descendants()
                    .Where(element => element.Name.LocalName == "Flyout"))
                    Assert.Equal("{StaticResource EgLibraryFlyoutSurfaceStyle}", flyout.Attribute("FlyoutPresenterStyle")?.Value);
        });

        Test("媒体库弹窗：删除等确认只从库操作使用库样式入口", () =>
        {
            var folder = Path.Combine(Root(), "src", "Momoka.Shell", "Views");
            foreach (var file in new[] { "ItemCommands.Server.cs", "ItemCommands.FnNas.cs" })
            {
                var text = File.ReadAllText(Path.Combine(folder, file));
                Assert.Contains("ConfirmDialog.ForLibrary(owner)", text);
                Assert.False(text.Contains("ConfirmDialog.For(owner)", StringComparison.Ordinal));
            }
            foreach (var file in Directory.GetFiles(folder, "MoviePilot*.cs"))
                Assert.False(File.ReadAllText(file).Contains("EgLibrary", StringComparison.Ordinal), "MP窗口被库样式污染");
            var confirm = File.ReadAllText(Path.Combine(folder, "ConfirmDialog.cs"));
            Assert.Contains("if (library)", confirm);
            Assert.Contains("""dialog.Style = (Style)Application.Current.Resources["EgLibraryDialogSurfaceStyle"]""", confirm);
            Assert.Contains("""dialog.Resources["ContentDialogTopOverlay"]""", confirm);
        });

        Test("弹窗表面：MP下载样板保持本地四值，其余表面集中在样式", () =>
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
