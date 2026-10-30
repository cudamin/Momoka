using System.Xml.Linq;
using Momoka.Shell;
using Momoka.Theming;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class InputFocusThemeTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string FocusKey = "TextControlBorderBrushFocused";
    private const string NeutralColor = "{StaticResource EgBorderStrongColor}";

    public static void Register()
    {
        Test("输入焦点：深浅首帧字典都使用中性边线", () =>
            Assert.True(IsNeutralPalette(Palette())));

        Test("输入焦点：切换主题不会把全局焦点重新涂成强调色", () =>
        {
            var pick = PickBrush(FocusKey);
            foreach (var theme in UiThemes.All)
            {
                Assert.Equal(theme.Colors.BorderStrong, pick(theme));
                Assert.False(pick(theme) == theme.Colors.Accent);
            }
        });

        Test("输入焦点：所有搜索框的局部覆盖也只能使用中性角色", () =>
        {
            foreach (var path in Directory.EnumerateFiles(Path.Combine(Root(), "src", "Momoka.Shell", "Views"), "*.xaml"))
                foreach (var resource in XDocument.Load(path).Descendants().Where(element =>
                    element.Attribute(Xaml + "Key")?.Value == FocusKey))
                    Assert.True(resource.Attribute("ResourceKey")?.Value is "EgBorderBrush" or "EgBorderStrongBrush"
                        || resource.Attribute("Color")?.Value is "{StaticResource EgBorderColor}" or NeutralColor,
                        Path.GetFileName(path) + " 的搜索焦点仍引用强调色");
        });

        Test("输入焦点：快捷键抓取使用中性边线并保留键盘焦点", () =>
        {
            var view = XDocument.Load(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "ShortcutCaptureBox.xaml"));
            var capture = view.Descendants().Single(element => element.Attribute(Xaml + "Name")?.Value == "Capturing");
            Assert.Equal("{ThemeResource EgBorderStrongBrush}", capture.Descendants().Single(element =>
                element.Attribute("Target")?.Value == "ComboBorder.BorderBrush").Attribute("Value")?.Value);
            Assert.Equal("{ThemeResource EgWindowBrush}", capture.Descendants().Single(element =>
                element.Attribute("Target")?.Value == "ComboBorder.Background").Attribute("Value")?.Value);
            Assert.Equal("True", view.Root!.Attribute("IsTabStop")?.Value);
            Assert.Equal("True", view.Root.Attribute("UseSystemFocusVisuals")?.Value);
            Assert.Equal("{ThemeResource EgTextBrush}", view.Root.Attribute("FocusVisualPrimaryBrush")?.Value);
            Assert.Equal("{ThemeResource EgWindowBrush}", view.Root.Attribute("FocusVisualSecondaryBrush")?.Value);
        });

        Test("输入底色：文本密码搜索与数字框四态都跟页面背景一致", () =>
        {
            foreach (var dictionary in Painted(Palette()))
            {
                foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver",
                    "TextControlBackgroundFocused", "TextControlBackgroundDisabled" })
                    Assert.Equal("{StaticResource EgWindowColor}", dictionary.Elements().Single(element =>
                        element.Attribute(Xaml + "Key")?.Value == key).Attribute("Color")?.Value);
                Assert.Equal("{StaticResource EgBorderColor}", dictionary.Elements().Single(element =>
                    element.Attribute(Xaml + "Key")?.Value == "TextControlBorderBrushDisabled").Attribute("Color")?.Value);
            }
        });

        Test("输入底色：文本输入运行期换主题沿用窗口底色，不退回卡片色", () =>
        {
            foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver",
                "TextControlBackgroundFocused", "TextControlBackgroundDisabled" })
            {
                var pick = PickBrush(key);
                foreach (var theme in UiThemes.All)
                    Assert.Equal(theme.Colors.Window, pick(theme));
            }
        });

        Test("输入底色：闭合下拉框各态使用输入玻璃，弹层材质不改", () =>
        {
            foreach (var dictionary in Painted(Palette()))
            {
                foreach (var key in new[] { "ComboBoxBackground", "ComboBoxBackgroundPointerOver", "ComboBoxBackgroundPressed",
                    "ComboBoxBackgroundFocused", "ComboBoxBackgroundUnfocused", "ComboBoxBackgroundDisabled" })
                    Assert.Equal("EgInputGlassBrush", dictionary.Elements().Single(element =>
                        element.Attribute(Xaml + "Key")?.Value == key).Attribute("ResourceKey")?.Value);
                Assert.Equal("AcrylicBrush", dictionary.Elements().Single(element =>
                    element.Attribute(Xaml + "Key")?.Value == "ComboBoxDropDownBackground").Name.LocalName);
            }
        });

        Test("输入底色：设置搜索保持原样，快捷键框接输入玻璃", () =>
        {
            var settings = XDocument.Load(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "SettingsPage.xaml"));
            Assert.Equal("{ThemeResource EgWindowBrush}", settings.Descendants().Single(element =>
                element.Attribute(Xaml + "Name")?.Value == "SettingsSearchBox").Attribute("Background")?.Value);
            var shortcut = XDocument.Load(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "ShortcutCaptureBox.xaml"));
            Assert.Equal("{ThemeResource EgInputGlassBrush}", shortcut.Descendants().Single(element =>
                element.Attribute(Xaml + "Name")?.Value == "ComboBorder").Attribute("Background")?.Value);
        });

        Test("输入底色：播放器输入沿用画面玻璃背景，不被普通表单底色覆盖", () =>
        {
            var player = XDocument.Load(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "PlayerPage.xaml"));
            var input = player.Descendants().Single(element => element.Attribute(Xaml + "Name")?.Value == "DanmakuInputBox");
            Assert.Equal("{StaticResource PlayerGlassTopBrush}", input.Attribute("Background")?.Value);
            foreach (var dictionary in Painted(new XDocument(new XElement(input))))
                foreach (var key in new[] { "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" })
                    Assert.Equal("PlayerGlassTopBrush", dictionary.Elements().Single(element =>
                        element.Attribute(Xaml + "Key")?.Value == key).Attribute("ResourceKey")?.Value);
            Assert.Equal(2, Painted(new XDocument(new XElement(input))).Count());
        });

        Test("输入焦点：高对比度保留系统颜色与框架输入焦点", () =>
        {
            var contrast = Palette().Descendants().Single(element => element.Name.LocalName == "ResourceDictionary"
                && element.Attribute(Xaml + "Key")?.Value == "HighContrast");
            Assert.False(contrast.Elements().Any(element => element.Attribute(Xaml + "Key")?.Value == FocusKey));
            Assert.Equal("{ThemeResource SystemColorWindowTextColor}", contrast.Elements().Single(element =>
                element.Attribute(Xaml + "Key")?.Value == "EgBorderStrongBrush").Attribute("Color")?.Value);
        });

        Test("输入焦点：回归检查拒绝任一主题恢复强调色或漏掉焦点资源", () =>
        {
            foreach (var key in new[] { "Dark", "Light" })
            {
                var colored = Palette();
                FocusResource(colored, key).SetAttributeValue("Color", "{StaticResource EgAccentColor}");
                Assert.False(IsNeutralPalette(colored));
                var missing = Palette();
                FocusResource(missing, key).Remove();
                Assert.False(IsNeutralPalette(missing));
            }
        });
    }

    private static Func<UiTheme, ThemeColor> PickBrush(string key) =>
        InputControlTheme.Brushes.Single(entry => entry.Key == key).Pick;

    private static bool IsNeutralPalette(XDocument palette)
    {
        var dictionaries = Painted(palette).ToArray();
        return dictionaries.Length == 2 && dictionaries.All(dictionary =>
        {
            var resources = dictionary.Elements().Where(element => element.Attribute(Xaml + "Key")?.Value == FocusKey).ToArray();
            return resources.Length == 1 && resources[0].Attribute("Color")?.Value == NeutralColor;
        });
    }

    private static IEnumerable<XElement> Painted(XDocument palette) => palette.Descendants().Where(element =>
        element.Name.LocalName == "ResourceDictionary" && element.Attribute(Xaml + "Key")?.Value is "Dark" or "Light");
    private static XElement FocusResource(XDocument palette, string dictionary) => Painted(palette).Single(element =>
        element.Attribute(Xaml + "Key")?.Value == dictionary).Elements().Single(element => element.Attribute(Xaml + "Key")?.Value == FocusKey);
    private static XDocument Palette() => XDocument.Load(Path.Combine(Root(), "src", "Momoka.Shell", "Theme", "Palette.xaml"));
    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("未找到当前工作树");
    }
}
