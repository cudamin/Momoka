using System.Text.RegularExpressions;
using System.Xml.Linq;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>Source contracts for separate library and MP/default surfaces; rendering is covered by native probes.</summary>
internal static class DialogChromeTests
{
    private const string SurfaceStyle = "EgDialogSurfaceStyle";
    private const string LibraryStyle = "EgLibraryDialogSurfaceStyle";
    private const string OutsideClickProperty = "local:DialogDismiss.ClosesOnOutsideClick";
    private static readonly HashSet<string> LibraryDialogs = new(StringComparer.Ordinal)
    {
        "AniBtScrapeDialog", "ArtworkViewer", "CollectionDialog", "CoverDialog", "CoverPickerDialog", "EmbyMetadataDialog",
        "FnNasMatchDialog", "FnNasMetadataDialog", "FnNasVersionsDialog", "IdentifyDialog", "MetadataDialog",
        "MissingEpisodesDialog", "RefreshMetadataDialog", "SubtitleDialog"
    };
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly Regex Subclass = new(@"class\s+(\w+)(?:\s*<[^>]+>)?(?:\s*\([^)]*\))?\s*:\s*([\w.]+)", RegexOptions.CultureInvariant);

    public static void Register()
    {
        Test("弹窗表面：共享基础样式给实心面色、无边框、EgCard 圆角", () =>
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

        Test("菜单与浮出层：深浅字典都是实心面色和主题边线，弹窗不再上亚克力", () =>
        {
            var palette = XDocument.Load(Path.Combine(Root(), "src", "Momoka.Shell", "Theme", "Palette.xaml"));
            foreach (var key in new[] { "Dark", "Light" })
            {
                var entries = palette.Descendants().Single(element => element.Name.LocalName == "ResourceDictionary"
                    && element.Attribute(Xaml + "Key")?.Value == key);
                Assert.False(entries.Elements().Any(element => element.Attribute(Xaml + "Key")?.Value == "EgDialogGlassBrush"),
                    "弹窗表面又用上了亚克力");
                Assert.False(entries.Elements().Any(element => element.Attribute(Xaml + "Key")?.Value == "ContentDialogTopOverlay"),
                    "弹窗顶盖又被打成了全局覆盖");
                foreach (var menuKey in new[] { "MenuFlyoutPresenterBackground", "FlyoutPresenterBackground" })
                    Assert.Equal("EgSurfaceBrush", entries.Elements().Single(element =>
                        element.Attribute(Xaml + "Key")?.Value == menuKey).Attribute("ResourceKey")?.Value);
                foreach (var borderKey in new[] { "MenuFlyoutPresenterBorderBrush", "FlyoutPresenterBorderBrush" })
                    Assert.Equal("EgBorderBrush", entries.Elements().Single(element =>
                        element.Attribute(Xaml + "Key")?.Value == borderKey).Attribute("ResourceKey")?.Value);
            }
            var contrast = palette.Descendants().Single(element => element.Name.LocalName == "ResourceDictionary"
                && element.Attribute(Xaml + "Key")?.Value == "HighContrast");
            Assert.False(contrast.Elements().Any(element => element.Attribute(Xaml + "Key")?.Value == "EgDialogGlassBrush"),
                "高对比度里还留着一张弹窗玻璃");
        });

        Test("媒体库弹窗：同一块侧栏底色并继承圆角", () =>
        {
            var style = Styles().Single(element => element.Attribute(Xaml + "Key")?.Value == LibraryStyle);
            Assert.Equal("{StaticResource " + SurfaceStyle + "}", style.Attribute("BasedOn")?.Value);
            Assert.Equal("{ThemeResource LayerFillColorDefaultBrush}", Setters(style).GetValueOrDefault("Background"));
        });

        Test("弹窗表面：媒体库与MP及普通窗口按明确范围接各自样式", () =>
        {
            var styles = Styles();
            var rooted = styles.Where(element => element.Name.LocalName == "Style" && element.Attribute(Xaml + "Key") is null)
                .ToDictionary(element => element.Attribute("TargetType")?.Value ?? "", element => element.Attribute("BasedOn")?.Value);
            Assert.Equal("{StaticResource " + SurfaceStyle + "}", rooted.GetValueOrDefault("ContentDialog"),
                "普通/MP动态窗口不得改成媒体库底色");
            var folder = Path.Combine(Root(), "src", "Momoka.Shell");
            var subclasses = DialogTypes(folder);
            Assert.True(subclasses.Length >= 15);
            foreach (var type in subclasses)
            {
                if (type == "MoviePilotDownloadDialog") continue;
                var expected = LibraryDialogs.Contains(type) ? LibraryStyle : SurfaceStyle;
                Assert.Equal("{StaticResource " + expected + "}", rooted.GetValueOrDefault("local:" + type),
                    $"{type} 的弹窗表面越过媒体库/MP边界");
            }
        });

        Test("弹窗表面：库弹窗顶盖与侧栏同色，MP不另写", () =>
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
            // 2026-10-30：弹窗表面回到实心之后，不再需要全局透明盖；普通/MP 弹窗回到框架自己的顶盖。
            var palette = XDocument.Load(Path.Combine(Root(), "src", "Momoka.Shell", "Theme", "Palette.xaml"));
            Assert.False(palette.Descendants().Any(element => element.Attribute(Xaml + "Key")?.Value == "ContentDialogTopOverlay"),
                "框架顶盖又被打成了全局覆盖");
        });

        Test("媒体库弹窗：筛选和库设置也用侧栏底色与圆角刻度", () =>
        {
            foreach (var file in new[] { "FilterPanel.xaml", "LibrarySettingsPanel.xaml" })
            {
                var surface = XDocument.Load(Path.Combine(Root(), "src", "Momoka.Shell", "Views", file)).Descendants()
                    .Single(element => element.Attribute(Xaml + "Name")?.Value == "LibraryDialogSurface");
                Assert.Equal("{ThemeResource LayerFillColorDefaultBrush}", surface.Attribute("Background")?.Value);
                Assert.Equal("{StaticResource EgCardCornerRadius}", surface.Attribute("CornerRadius")?.Value);
            }
        });

        Test("Emby元数据图标按钮：锁定和日期小格不继承文字按钮内边距", () =>
        {
            var folder = Path.Combine(Root(), "src", "Momoka.Shell", "Views");
            var count = 0;
            foreach (var file in new[] { "EmbyMetadataDialog.xaml", "MetadataListEditor.xaml" })
            {
                var buttons = XDocument.Load(Path.Combine(folder, file)).Descendants()
                    .Where(element => element.Name.LocalName is "ToggleButton" or "Button"
                        && element.Attribute("Width")?.Value == "36").ToArray();
                foreach (var button in buttons)
                {
                    Assert.Equal("0", button.Attribute("Padding")?.Value, "小图标按钮不能用文字按钮的11 DIP内边距");
                    Assert.Equal("32", button.Attribute("Height")?.Value, "去掉文字内边距后仍须保持正常按钮高度");
                    Assert.Equal("Center", button.Attribute("HorizontalContentAlignment")?.Value);
                    Assert.Equal("Center", button.Attribute("VerticalContentAlignment")?.Value);
                    Assert.True(button.Elements().Any(element => element.Name.LocalName == "FontIcon"));
                    count++;
                }
            }
            Assert.Equal(9, count, "七个字段锁、列表锁和加入日期按钮应全部覆盖");
        });

        Test("媒体库弹窗：编辑子浮层同底色圆角但不污染全局Flyout", () =>
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

        Test("弹窗表面：每张表都接上「点外面关闭」，新增的也不例外", () =>
        {
            var styles = Styles();
            var byKey = styles.Where(style => style.Attribute(Xaml + "Key") is not null)
                .ToDictionary(style => style.Attribute(Xaml + "Key")!.Value, style => style);
            var rooted = styles.Where(style => style.Attribute(Xaml + "Key") is null)
                .ToDictionary(style => style.Attribute("TargetType")?.Value ?? "", style => style);

            Assert.Equal("True", Setters(byKey[SurfaceStyle]).GetValueOrDefault("local:DialogDismiss.ClosesOnOutsideClick"),
                "共享基础样式没有打开点外面关闭，派生出来的表一个都不会有");

            // 隐式样式按类型精确匹配，所以「所有弹窗」这句话只能靠每张表各接一行来落实；
            // 这里按源码里的派生类清单逐个数，新增一张表却忘了接样式就在这儿红。
            var folder = Path.Combine(Root(), "src", "Momoka.Shell");
            var subclasses = DialogTypes(folder);
            Assert.True(subclasses.Length >= 15, $"只找到 {subclasses.Length} 张 ContentDialog，弹窗清单不完整");
            Assert.True(CarriesOutsideClick(rooted["ContentDialog"], byKey), "代码创建的普通弹窗不能漏掉");
            foreach (var type in subclasses)
            {
                Assert.True(rooted.TryGetValue("local:" + type, out var style), $"{type} 没有自己的弹窗样式行");
                Assert.True(CarriesOutsideClick(style!, byKey), $"{type} 的弹窗样式没有接上「点外面关闭」");
            }
            foreach (var file in Directory.GetFiles(folder, "*.xaml", SearchOption.AllDirectories))
            {
                if (Generated(file)) continue;
                var doc = XDocument.Load(file);
                foreach (var attribute in doc.Descendants().Attributes().Where(a => a.Name.LocalName == "DialogDismiss.ClosesOnOutsideClick"))
                    Assert.True(attribute.Value.Equals("True", StringComparison.OrdinalIgnoreCase), $"{file} 用本地值绕过点外关闭");
                if (doc.Root?.Name.LocalName != "ContentDialog") continue;
                var localStyle = doc.Root.Attribute("Style")?.Value;
                if (localStyle is not null)
                {
                    var key = ResourceKey(localStyle);
                    Assert.True(key is not null && byKey.TryGetValue(key, out var style) && CarriesOutsideClick(style, byKey),
                        $"{file} 的本地 Style 没有继承点外关闭");
                }
                Assert.False(doc.Root.Elements().Any(e => e.Name.LocalName == "ContentDialog.Style"), $"{file} 不应内联替换弹窗样式");
            }

            // 探针能走几何判定与 Hide，按下的接线只有源码看得见。框架把遮罩搬进了另一个 Popup，
            // 只挂在弹窗本体上会漏掉那一层（2026-10-30 实测点遮罩不关），所以这里连遮罩一起钉住；
            // 用 Unloaded 退订会在 Opened 后两毫秒把刚接上的处理器摘掉（同一天实测），一并钉住。
            var dismiss = File.ReadAllText(Path.Combine(folder, "Views", "DialogDismiss.cs"));
            Assert.Contains("GetOpenPopupsForXamlRoot", dismiss);
            Assert.Contains("SmokeLayerBackground", dismiss);
            Assert.Contains("AddHandler(UIElement.PointerPressedEvent, _pressed, handledEventsToo: true)", dismiss);
            Assert.Contains("ReferenceEquals(sender, _overlay)", dismiss);
            Assert.Contains("dialog.Loaded += OnLoaded", dismiss);
            Assert.Contains("dialog.Closed += OnClosed", dismiss);
            Assert.False(dismiss.Contains("dialog.Unloaded +=", StringComparison.Ordinal), "用 Unloaded 退订会把刚接上的遮罩订阅摘掉");
            Assert.False(dismiss.Contains("dialog.PointerPressed +=", StringComparison.Ordinal), "又退回只挂弹窗本体那条路了");
            Assert.Contains("dialog.Hide()", dismiss);
        });

        Test("弹窗约定：交互 Popup 必须开启点外关闭，只有动画残影例外", () =>
        {
            var folder = Path.Combine(Root(), "src", "Momoka.Shell");
            foreach (var file in Directory.GetFiles(folder, "*.xaml", SearchOption.AllDirectories).Where(file => !Generated(file)))
            {
                var doc = XDocument.Load(file);
                foreach (var popup in doc.Descendants().Where(e => e.Name.LocalName == "Popup"))
                    Assert.True(PopupAllowsOutsideClick(popup), $"{file} 的交互 Popup 未开启点外关闭");
            }
            foreach (var file in Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories).Where(file => !Generated(file)))
            {
                var source = File.ReadAllText(file);
                if (Path.GetFileName(file) != "DialogDismiss.cs")
                {
                    Assert.False(source.Contains("SetClosesOnOutsideClick(", StringComparison.Ordinal), $"{file} 不应逐窗覆盖共享关闭行为");
                    Assert.False(source.Contains("ClosesOnOutsideClickProperty,", StringComparison.Ordinal), $"{file} 不应绕过共享附加属性");
                }
                foreach (Match popup in Regex.Matches(source, @"new\s+(?:[\w.]+\.)?Popup\s*\{(?<body>[^}]+)\}", RegexOptions.CultureInvariant))
                    Assert.True(PopupAllowsOutsideClick(popup.Groups["body"].Value), $"{file} 的交互 Popup 未在创建时开启点外关闭");
            }
            Assert.True(PopupAllowsOutsideClick(XElement.Parse("<Popup IsLightDismissEnabled='True'/>")));
            Assert.True(PopupAllowsOutsideClick(XElement.Parse("<Popup IsHitTestVisible='False'/>")));
            Assert.False(PopupAllowsOutsideClick(XElement.Parse("<Popup/>")));
            Assert.False(PopupAllowsOutsideClick(XElement.Parse("<Popup IsLightDismissEnabled='False'/>")));
            Assert.True(PopupAllowsOutsideClick("IsLightDismissEnabled = true,"));
            Assert.True(PopupAllowsOutsideClick("IsHitTestVisible = false,"));
            Assert.False(PopupAllowsOutsideClick("Child = panel,"));
            Assert.False(PopupAllowsOutsideClick("IsLightDismissEnabled = false,"));
        });

        Test("弹窗约定：继承链本地 False、缺失和环不能被祖先 True 放过", () =>
        {
            var enabled = XElement.Parse("<Style><Setter Property='local:DialogDismiss.ClosesOnOutsideClick' Value='True'/></Style>");
            var disabled = XElement.Parse("<Style BasedOn='{StaticResource enabled}'><Setter Property='local:DialogDismiss.ClosesOnOutsideClick' Value='False'/></Style>");
            var inherit = XElement.Parse("<Style BasedOn='{StaticResource enabled}'/>");
            var missing = XElement.Parse("<Style BasedOn='{StaticResource missing}'/>");
            var a = XElement.Parse("<Style BasedOn='{StaticResource b}'/>");
            var b = XElement.Parse("<Style BasedOn='{StaticResource a}'/>");
            var styles = new Dictionary<string, XElement> { ["enabled"] = enabled, ["a"] = a, ["b"] = b };
            Assert.True(CarriesOutsideClick(enabled, styles));
            Assert.True(CarriesOutsideClick(inherit, styles));
            Assert.False(CarriesOutsideClick(disabled, styles));
            Assert.False(CarriesOutsideClick(missing, styles));
            Assert.False(CarriesOutsideClick(a, styles));
            Assert.False(CarriesOutsideClick(XElement.Parse("<Style/>"), styles));
        });

        Test("弹窗约定：间接派生和全名类型也必须登记，普通控件与继承环排除", () =>
        {
            var source = "class BaseDialog : Microsoft.UI.Xaml.Controls.ContentDialog {} class FutureDialog(string title) : BaseDialog {} class Ordinary : Border {}";
            var bases = Subclass.Matches(source).ToDictionary(m => m.Groups[1].Value, m => new[] { m.Groups[2].Value.Split('.').Last() });
            Assert.True(InheritsDialog("BaseDialog", bases));
            Assert.True(InheritsDialog("FutureDialog", bases));
            Assert.False(InheritsDialog("Ordinary", bases));
            bases["A"] = ["B"];
            bases["B"] = ["A"];
            Assert.False(InheritsDialog("A", bases));
        });
    }

    /// <summary>本地 Setter 优先，false 不能被祖先的 true 洗掉；循环继承也必须判失败。</summary>
    private static bool CarriesOutsideClick(XElement style, Dictionary<string, XElement> byKey)
    {
        var seen = new HashSet<XElement>();
        for (var current = style; current is not null && seen.Add(current);)
        {
            var local = current.Elements().LastOrDefault(setter => setter.Name.LocalName == "Setter"
                && setter.Attribute("Property")?.Value == OutsideClickProperty);
            if (local is not null) return local.Attribute("Value")?.Value.Equals("True", StringComparison.OrdinalIgnoreCase) == true;
            var key = ResourceKey(current.Attribute("BasedOn")?.Value);
            current = key is not null && byKey.TryGetValue(key, out var parent) ? parent : null;
        }
        return false;
    }

    private static bool PopupAllowsOutsideClick(XElement popup) =>
        popup.Attribute("IsLightDismissEnabled")?.Value.Equals("True", StringComparison.OrdinalIgnoreCase) == true
        || popup.Attribute("IsHitTestVisible")?.Value.Equals("False", StringComparison.OrdinalIgnoreCase) == true;

    private static bool PopupAllowsOutsideClick(string initializer) =>
        Regex.IsMatch(initializer, @"\bIsLightDismissEnabled\s*=\s*true\b", RegexOptions.CultureInvariant)
        || Regex.IsMatch(initializer, @"\bIsHitTestVisible\s*=\s*false\b", RegexOptions.CultureInvariant);

    private static string? ResourceKey(string? value) => value is not null
        && value.StartsWith("{StaticResource ", StringComparison.Ordinal) && value.EndsWith('}')
            ? value["{StaticResource ".Length..^1].Trim() : null;

    private static string[] DialogTypes(string folder)
    {
        var bases = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories).Where(file => !Generated(file))
            .SelectMany(file => Subclass.Matches(File.ReadAllText(file)).Select(match =>
                (Type: match.Groups[1].Value, Base: match.Groups[2].Value.Split('.').Last())))
            .GroupBy(pair => pair.Type, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.Base).Distinct().ToArray(), StringComparer.Ordinal);
        return bases.Keys.Where(type => InheritsDialog(type, bases)).ToArray();
    }

    private static bool InheritsDialog(string type, Dictionary<string, string[]> bases)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>();
        pending.Enqueue(type);
        while (pending.TryDequeue(out var current))
        {
            if (!seen.Add(current) || !bases.TryGetValue(current, out var parents)) continue;
            foreach (var parent in parents)
            {
                if (parent == "ContentDialog") return true;
                pending.Enqueue(parent);
            }
        }
        return false;
    }

    private static bool Generated(string file) => Path.GetRelativePath(Root(), file).Split(Path.DirectorySeparatorChar)
        .Any(part => part is "bin" or "obj");

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
