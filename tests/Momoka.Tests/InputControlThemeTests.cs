using System.Xml.Linq;
using Momoka.Shell;
using Momoka.Theming;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>编译链接真实输入角色表，并核对首帧资源与局部覆盖；真实像素另由设置探针验。</summary>
internal static class InputControlThemeTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly string[] TextBackgroundKeys =
    [
        "TextControlBackground", "TextControlBackgroundPointerOver",
        "TextControlBackgroundFocused", "TextControlBackgroundDisabled"
    ];
    private static readonly string[] ComboBackgroundKeys =
    [
        "ComboBoxBackground", "ComboBoxBackgroundPointerOver", "ComboBoxBackgroundPressed",
        "ComboBoxBackgroundFocused", "ComboBoxBackgroundUnfocused", "ComboBoxBackgroundDisabled"
    ];
    private static readonly string[] StrongBorderKeys =
    [
        "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused",
        "ComboBoxBackgroundBorderBrushFocused"
    ];
    private static readonly string[] BorderKeys = ["TextControlBorderBrush", "TextControlBorderBrushDisabled"];

    public static void Register()
    {
        Test("输入主题：真实角色表无重复或遗漏", () =>
        {
            var expected = TextBackgroundKeys.Concat(ComboBackgroundKeys).Concat(StrongBorderKeys)
                .Concat(BorderKeys).Append("EgInputGlassBrush").Order().ToArray();
            var actual = InputControlTheme.Brushes.Select(entry => entry.Key).Order().ToArray();
            Assert.Equal(string.Join('|', expected), string.Join('|', actual));
            Assert.Equal(actual.Length, actual.Distinct(StringComparer.Ordinal).Count());
        });
        Test("输入主题：文本输入沿用侧边栏底色，选择框使用输入玻璃", () =>
        {
            foreach (var theme in UiThemes.All)
            {
                foreach (var key in TextBackgroundKeys)
                    Assert.Equal(theme.Colors.Window, Pick(key, theme), $"{theme.Id}/{key}");
                foreach (var key in ComboBackgroundKeys)
                    Assert.Equal(InputControlTheme.Glass(theme), Pick(key, theme), $"{theme.Id}/{key}");
                Assert.Equal(InputControlTheme.Glass(theme), Pick("EgInputGlassBrush", theme), theme.Id);
            }
        });
        Test("输入主题：玻璃 tint 深色取悬停面、浅色取凹面", () =>
        {
            foreach (var theme in UiThemes.All)
                Assert.Equal(theme.IsDark ? theme.Colors.SurfaceHover : theme.Colors.SurfaceAlt,
                    InputControlTheme.Glass(theme), theme.Id);
        });
        Test("输入主题：聚焦和悬停只加强中性边线，不使用强调色", () =>
        {
            foreach (var theme in UiThemes.All)
            {
                foreach (var key in StrongBorderKeys)
                {
                    Assert.Equal(theme.Colors.BorderStrong, Pick(key, theme), $"{theme.Id}/{key}");
                    Assert.True(Pick(key, theme) != theme.Colors.Accent, $"{theme.Id}/{key} 仍包主题色");
                }
                foreach (var key in BorderKeys)
                    Assert.Equal(theme.Colors.Border, Pick(key, theme), $"{theme.Id}/{key}");
            }
        });
        Test("输入主题：Dark 与 Light 首帧资源和真实选色规则一致", () =>
        {
            foreach (var dictionary in Dictionaries())
            {
                foreach (var key in TextBackgroundKeys) AssertBrush(dictionary, key, "EgWindowColor");
                foreach (var key in ComboBackgroundKeys) AssertAlias(dictionary, key, "EgInputGlassBrush");
                foreach (var key in StrongBorderKeys) AssertBrush(dictionary, key, "EgBorderStrongColor");
                foreach (var key in BorderKeys) AssertBrush(dictionary, key, "EgBorderColor");

                var glass = Resource(dictionary, "EgInputGlassBrush");
                var themeName = dictionary.Attribute(X + "Key")?.Value;
                var glassRole = themeName == "Light" ? "EgSurfaceAltColor" : "EgSurfaceHoverColor";
                Assert.Equal("AcrylicBrush", glass.Name.LocalName);
                Assert.Equal($"{{StaticResource {glassRole}}}", (string?)glass.Attribute("TintColor"),
                    themeName + "/玻璃 tint");
                Assert.Equal($"{{StaticResource {glassRole}}}", (string?)glass.Attribute("FallbackColor"),
                    themeName + "/玻璃回退色");
            }
        });
        Test("输入主题：普通主题焦点四边保持 1 DIP，高对比度不覆盖系统输入资源", () =>
        {
            foreach (var dictionary in Dictionaries())
                Assert.Equal("1", Resource(dictionary, "TextControlBorderThemeThicknessFocused").Value.Trim());
            var highContrast = Palette().Descendants(Xaml + "ResourceDictionary")
                .Single(dictionary => (string?)dictionary.Attribute(X + "Key") == "HighContrast");
            var overrides = highContrast.Elements().Select(element => (string?)element.Attribute(X + "Key")).ToArray();
            Assert.True(!overrides.Any(key => key is not null &&
                (key.StartsWith("TextControl", StringComparison.Ordinal) ||
                 key.StartsWith("ComboBoxBackground", StringComparison.Ordinal))), "高对比度输入资源被产品配色覆盖");
            var glass = Resource(highContrast, "EgInputGlassBrush");
            Assert.Equal("SolidColorBrush", glass.Name.LocalName);
            Assert.Equal("{ThemeResource SystemColorWindowColor}", (string?)glass.Attribute("Color"));
        });
        Test("输入主题：设置卡片区的输入静止玻璃、聚焦回侧栏色，搜索框留在原样", () =>
        {
            var page = Read("src/Momoka.Shell/Views/SettingsPage.xaml");
            var scroll = page.Descendants(Xaml + "ScrollViewer")
                .Single(element => (string?)element.Attribute(X + "Name") == "SettingsScroll");
            var resources = scroll.Element(Xaml + "ScrollViewer.Resources")!;
            AssertAlias(resources, "TextControlBackground", "EgInputGlassBrush");
            AssertAlias(resources, "TextControlBackgroundPointerOver", "EgInputGlassBrush");
            Assert.True(!resources.Elements().Any(element =>
                (string?)element.Attribute(X + "Key") == "TextControlBackgroundFocused"), "聚焦底色被局部覆盖，回不到侧栏色");

            var search = page.Descendants(Xaml + "AutoSuggestBox")
                .Single(element => (string?)element.Attribute(X + "Name") == "SettingsSearchBox");
            Assert.Equal("{ThemeResource EgWindowBrush}", (string?)search.Attribute("Background"));
            Assert.True(!search.Descendants().Attributes().Any(attribute => attribute.Value.Contains("EgAccentBrush", StringComparison.Ordinal)),
                "搜索框局部资源仍强制主题色边框");

            foreach (var number in page.Descendants(Xaml + "NumberBox"))
            {
                Assert.Equal("Hidden", (string?)number.Attribute("SpinButtonPlacementMode"));
                Assert.Equal("Center", (string?)number.Attribute("VerticalAlignment"));
            }
        });
        Test("输入主题：详情页的电影/剧/季选择框钉回原窗口底色，不跟全局玻璃", () =>
        {
            var page = Read("src/Momoka.Shell/Views/DetailPage.xaml");
            var column = page.Descendants(Xaml + "StackPanel")
                .Single(element => (string?)element.Attribute(X + "Name") == "ColumnPickers");
            var columnResources = column.Element(Xaml + "StackPanel.Resources")!;
            var shelf = page.Descendants()
                .Single(element => (string?)element.Attribute(X + "Name") == "EpisodeHead");
            var shelfResources = shelf.Element(shelf.Name.Namespace + "ShelfHead.Resources")!;
            foreach (var resources in new[] { columnResources, shelfResources })
                foreach (var key in ComboBackgroundKeys)
                    AssertAlias(resources, key, "EgWindowBrush");
        });
        Test("输入主题：点空白处把焦点交给卡片区，输入框回到玻璃", () =>
        {
            var page = Read("src/Momoka.Shell/Views/SettingsPage.xaml");
            Assert.Equal("OnSettingsPointerPressed", (string?)page.Root!.Attribute("PointerPressed"));
            var code = File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "SettingsPage.xaml.cs"));
            Assert.Contains("SettingsScroll.Focus(FocusState.Pointer)", code);
            foreach (var control in new[] { "TextBox", "PasswordBox", "NumberBox", "ComboBox", "AutoSuggestBox",
                "ButtonBase", "ToggleSwitch", "Slider", "ListView", "ListViewItem", "ScrollBar" })
                Assert.Contains(control, code);
        });
        Test("输入主题：设置卡片改用亚克力，深色 tint 与浅色 tint 各按字典", () =>
        {
            foreach (var dictionary in Dictionaries())
            {
                var card = Resource(dictionary, "CardBackgroundFillColorDefaultBrush");
                var themeName = dictionary.Attribute(X + "Key")?.Value;
                var role = themeName == "Light" ? "EgSurfaceColor" : "EgSurfaceHoverColor";
                Assert.Equal("AcrylicBrush", card.Name.LocalName);
                Assert.Equal($"{{StaticResource {role}}}", (string?)card.Attribute("TintColor"), themeName + "/卡片 tint");
                Assert.Equal($"{{StaticResource {role}}}", (string?)card.Attribute("FallbackColor"), themeName + "/卡片回退色");
            }
        });
        Test("输入主题：设置搜索框不再局部覆盖为卡片底色", () =>
        {
            var page = Read("src/Momoka.Shell/Views/SettingsPage.xaml");
            var search = page.Descendants(Xaml + "AutoSuggestBox")
                .Single(element => (string?)element.Attribute(X + "Name") == "SettingsSearchBox");
            Assert.Equal("{ThemeResource EgWindowBrush}", (string?)search.Attribute("Background"));
            Assert.True(!search.Descendants().Attributes().Any(attribute => attribute.Value.Contains("EgAccentBrush", StringComparison.Ordinal)),
                "搜索框局部资源仍强制主题色边框");
        });
        Test("输入主题：播放器悬浮输入区仍沿用玻璃背景，不强制改成普通表单底色", () =>
        {
            var player = Read("src/Momoka.Shell/Views/PlayerPage.xaml");
            var input = player.Descendants(Xaml + "TextBox")
                .Single(element => (string?)element.Attribute(X + "Name") == "DanmakuInputBox");
            Assert.Equal("{StaticResource PlayerGlassTopBrush}", (string?)input.Attribute("Background"));
            var themes = input.Descendants(Xaml + "ResourceDictionary")
                .Where(dictionary => (string?)dictionary.Attribute(X + "Key") is "Dark" or "Light").ToArray();
            Assert.Equal(2, themes.Length);
            foreach (var dictionary in themes)
                foreach (var key in new[] { "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" })
                    Assert.Equal("PlayerGlassTopBrush", (string?)Resource(dictionary, key).Attribute("ResourceKey"));
        });
        Test("输入主题：快捷键抓键框静止用输入玻璃，抓键中回侧栏色和中性焦点", () =>
        {
            var capture = Read("src/Momoka.Shell/Views/ShortcutCaptureBox.xaml");
            var border = capture.Descendants(Xaml + "Border")
                .Single(element => (string?)element.Attribute(X + "Name") == "ComboBorder");
            Assert.Equal("{ThemeResource EgInputGlassBrush}", (string?)border.Attribute("Background"));
            var focus = capture.Descendants(Xaml + "Setter")
                .Single(element => (string?)element.Attribute("Target") == "ComboBorder.BorderBrush");
            Assert.Equal("{ThemeResource EgBorderStrongBrush}", (string?)focus.Attribute("Value"));
            var fill = capture.Descendants(Xaml + "Setter")
                .Single(element => (string?)element.Attribute("Target") == "ComboBorder.Background");
            Assert.Equal("{ThemeResource EgWindowBrush}", (string?)fill.Attribute("Value"));
        });
    }

    private static ThemeColor Pick(string key, UiTheme theme) =>
        InputControlTheme.Brushes.Single(entry => entry.Key == key).Pick(theme);

    private static void AssertBrush(XElement dictionary, string key, string color) =>
        Assert.Equal($"{{StaticResource {color}}}", (string?)Resource(dictionary, key).Attribute("Color"),
            $"{dictionary.Attribute(X + "Key")?.Value}/{key}");

    private static void AssertAlias(XElement dictionary, string key, string resource) =>
        Assert.Equal(resource, (string?)Resource(dictionary, key).Attribute("ResourceKey"),
            $"{dictionary.Attribute(X + "Key")?.Value}/{key}");

    private static XElement Resource(XElement dictionary, string key) =>
        dictionary.Elements().Single(element => (string?)element.Attribute(X + "Key") == key);

    private static IEnumerable<XElement> Dictionaries() => Palette().Descendants(Xaml + "ResourceDictionary")
        .Where(dictionary => (string?)dictionary.Attribute(X + "Key") is "Dark" or "Light");

    private static XDocument Palette() => Read("src/Momoka.Shell/Theme/Palette.xaml");

    private static XDocument Read(string relativePath) =>
        XDocument.Load(Path.Combine(Root(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Momoka.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
