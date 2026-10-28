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
    private static readonly string[] BackgroundKeys =
    [
        "ComboBoxBackground", "ComboBoxBackgroundPointerOver", "ComboBoxBackgroundPressed",
        "ComboBoxBackgroundFocused", "ComboBoxBackgroundUnfocused", "ComboBoxBackgroundDisabled",
        "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
        "TextControlBackgroundDisabled"
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
            var expected = BackgroundKeys.Concat(StrongBorderKeys).Concat(BorderKeys).Order().ToArray();
            var actual = InputControlTheme.Brushes.Select(entry => entry.Key).Order().ToArray();
            Assert.Equal(string.Join('|', expected), string.Join('|', actual));
            Assert.Equal(actual.Length, actual.Distinct(StringComparer.Ordinal).Count());
        });
        Test("输入主题：全部主题与全部输入状态沿用侧边栏底色", () =>
        {
            foreach (var theme in UiThemes.All)
                foreach (var key in BackgroundKeys)
                    Assert.Equal(theme.Colors.Window, Pick(key, theme), $"{theme.Id}/{key}");
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
                foreach (var key in BackgroundKeys) AssertBrush(dictionary, key, "EgWindowColor");
                foreach (var key in StrongBorderKeys) AssertBrush(dictionary, key, "EgBorderStrongColor");
                foreach (var key in BorderKeys) AssertBrush(dictionary, key, "EgBorderColor");
            }
        });
        Test("输入主题：普通主题焦点四边保持 1 DIP，高对比度不覆盖系统输入资源", () =>
        {
            foreach (var dictionary in Dictionaries())
                Assert.Equal("1", Resource(dictionary, "TextControlBorderThemeThicknessFocused").Value.Trim());
            var highContrast = Palette().Descendants(Xaml + "ResourceDictionary")
                .Single(dictionary => (string?)dictionary.Attribute(X + "Key") == "HighContrast");
            var overrides = highContrast.Elements().Select(element => (string?)element.Attribute(X + "Key"));
            Assert.True(!overrides.Any(key => key is not null &&
                (key.StartsWith("TextControl", StringComparison.Ordinal) ||
                 key.StartsWith("ComboBoxBackground", StringComparison.Ordinal))), "高对比度输入资源被产品配色覆盖");
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
        Test("输入主题：快捷键抓键框与其他输入框共用底色和中性焦点", () =>
        {
            var capture = Read("src/Momoka.Shell/Views/ShortcutCaptureBox.xaml");
            var border = capture.Descendants(Xaml + "Border")
                .Single(element => (string?)element.Attribute(X + "Name") == "ComboBorder");
            Assert.Equal("{ThemeResource EgWindowBrush}", (string?)border.Attribute("Background"));
            var focus = capture.Descendants(Xaml + "Setter")
                .Single(element => (string?)element.Attribute("Target") == "ComboBorder.BorderBrush");
            Assert.Equal("{ThemeResource EgBorderStrongBrush}", (string?)focus.Attribute("Value"));
        });
    }

    private static ThemeColor Pick(string key, UiTheme theme) =>
        InputControlTheme.Brushes.Single(entry => entry.Key == key).Pick(theme);

    private static void AssertBrush(XElement dictionary, string key, string color) =>
        Assert.Equal($"{{StaticResource {color}}}", (string?)Resource(dictionary, key).Attribute("Color"),
            $"{dictionary.Attribute(X + "Key")?.Value}/{key}");

    private static XElement Resource(XElement dictionary, string key) =>
        dictionary.Elements().Single(element => (string?)element.Attribute(X + "Key") == key);

    private static IEnumerable<XElement> Dictionaries() => Palette().Descendants(Xaml + "ResourceDictionary")
        .Where(dictionary => (string?)dictionary.Attribute(X + "Key") is "Dark" or "Light");

    private static XDocument Palette() => Read("src/Momoka.Shell/Theme/Palette.xaml");

    private static XDocument Read(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Momoka.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return XDocument.Load(Path.Combine(directory!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
