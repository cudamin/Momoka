using System.Xml.Linq;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class NeutralFeedbackTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static void Register()
    {
        Test("中性反馈：搜索框聚焦保持中性细边，其他输入框仍有主题色焦点", () =>
        {
            var search = View("LibraryPage.xaml").Descendants().Single(element => Name(element) == "SearchInput");
            var resources = search.Elements().Single(element => element.Name.LocalName == "AutoSuggestBox.Resources");
            Assert.Equal("EgBorderBrush", resources.Elements().Single(element =>
                element.Attribute(Xaml + "Key")?.Value == "TextControlBorderBrushFocused").Attribute("ResourceKey")?.Value);
            Assert.Equal("1", resources.Elements().Single(element =>
                element.Attribute(Xaml + "Key")?.Value == "TextControlBorderThemeThicknessFocused").Value);
            Assert.Equal("OnSearchSubmitted", search.Attribute("QuerySubmitted")?.Value);
            Assert.Equal("OnSearchTextChanged", search.Attribute("TextChanged")?.Value);
            var palette = XDocument.Load(Path.Combine(Root(), "src", "Momoka.Shell", "Theme", "Palette.xaml"));
            Assert.True(palette.Descendants().Where(element =>
                element.Attribute(Xaml + "Key")?.Value == "TextControlBorderBrushFocused").All(element =>
                element.Attribute("Color")?.Value == "{StaticResource EgAccentColor}"));
        });

        Test("中性反馈：主页卡片保留中性的键盘焦点提示", () =>
        {
            var template = View("HomePage.xaml").Descendants().Single(element =>
                element.Attribute(Xaml + "Key")?.Value == "CardTemplate");
            var host = template.Elements().Single(element => element.Name.LocalName == "Button");
            Assert.Equal("{ThemeResource EgTextBrush}", host.Attribute("FocusVisualPrimaryBrush")?.Value);
            Assert.Equal("{ThemeResource EgWindowBrush}", host.Attribute("FocusVisualSecondaryBrush")?.Value);
            Assert.False(host.Attribute("UseSystemFocusVisuals")?.Value == "False");
            Assert.Equal("OnCardClicked", host.Attribute("Click")?.Value);
        });

        Test("中性反馈：主页 MoviePilot 订阅海报不留主题色外圈", () =>
        {
            var view = View("MoviePilotSubscriptionPoster.xaml");
            var cover = view.Descendants().Single(element => Name(element) == "SubscriptionCover");
            Assert.Equal("{ThemeResource EgTextBrush}", cover.Attribute("FocusVisualPrimaryBrush")?.Value);
            Assert.Equal("{ThemeResource EgWindowBrush}", cover.Attribute("FocusVisualSecondaryBrush")?.Value);
            var code = File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "MoviePilotSubscriptionPoster.xaml.cs"));
            Assert.False(code.Contains("EgFrameActiveStyle", StringComparison.Ordinal));
            Assert.Equal("OnOpen", cover.Attribute("Click")?.Value);
        });

        Test("中性反馈：播放键默认灰底白字，保留圆形尺寸与播放路由", () =>
        {
            var card = View("PosterCard.xaml");
            var style = card.Descendants().Single(element => element.Attribute(Xaml + "Key")?.Value == "EgCardPlayButtonStyle");
            Assert.Equal("{StaticResource DefaultButtonStyle}", style.Attribute("BasedOn")?.Value);
            Assert.Equal("{ThemeResource EgSurfaceHoverBrush}", Setter(style, "Background"));
            Assert.Equal("{ThemeResource EgTextBrush}", Setter(style, "Foreground"));
            Assert.Equal("48", Setter(style, "Width"));
            Assert.Equal("48", Setter(style, "Height"));
            Assert.Equal("24", Setter(style, "CornerRadius"));
            Assert.Equal("0", Setter(style, "BorderThickness"));
            var button = card.Descendants().Single(element => Name(element) == "PlayButton");
            Assert.Equal("OnPlayClicked", button.Attribute("Click")?.Value);
            Assert.Equal("{x:Bind Card.PlayVisibility, Mode=OneWay}", button.Attribute("Visibility")?.Value);
        });

        Test("中性反馈：强调色只覆盖播放按钮的指针和按下状态", () =>
        {
            var card = View("PosterCard.xaml");
            var button = card.Descendants().Single(element => Name(element) == "PlayButton");
            var resources = button.Elements().Single(element => element.Name.LocalName == "Button.Resources");
            var expected = new Dictionary<string, string>
            {
                ["ButtonBackgroundPointerOver"] = "EgAccentBrush",
                ["ButtonBackgroundPressed"] = "EgAccentPressedBrush",
                ["ButtonForegroundPointerOver"] = "EgTextOnAccentBrush",
                ["ButtonForegroundPressed"] = "EgTextOnAccentBrush"
            };
            Assert.Equal(expected.Count, resources.Elements().Count());
            foreach (var (key, role) in expected)
                Assert.Equal(role, resources.Elements().Single(element =>
                    element.Attribute(Xaml + "Key")?.Value == key).Attribute("ResourceKey")?.Value);
            Assert.False(card.Descendants().Any(element => Name(element) == "Hover"
                && element.Attribute("Background")?.Value?.Contains("Accent", StringComparison.Ordinal) == true));
        });

        Test("中性反馈：自检同时核对无外圈及播放键两态，不再要求封面变主题色", () =>
        {
            var code = File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "ShellSelfCheck.Reads.cs"));
            Assert.Contains("corner.FrameBorderless", code);
            Assert.Contains("Format(corner.FrameActive) == Format(corner.FrameRest)", code);
            Assert.Contains("Format(corner.PlayRest) == ColorOf(\"EgSurfaceHoverBrush\")", code);
            Assert.Contains("Format(corner.PlayHover) == accent", code);
            Assert.False(code.Contains("Format(corner.FrameActive) == accent", StringComparison.Ordinal));
        });
    }

    private static string? Name(XElement element) => element.Attribute(Xaml + "Name")?.Value;
    private static string? Setter(XElement style, string property) => style.Elements().Single(element =>
        element.Attribute("Property")?.Value == property).Attribute("Value")?.Value;
    private static XDocument View(string name) => XDocument.Load(Path.Combine(Root(), "src", "Momoka.Shell", "Views", name));
    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("未找到当前工作树");
    }
}
