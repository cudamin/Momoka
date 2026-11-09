using System.Xml.Linq;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class NeutralFeedbackTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static void Register()
    {
        Test("中性反馈：搜索框与其他输入框聚焦都保持中性边线", () =>
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
                element.Attribute("Color")?.Value == "{StaticResource EgBorderStrongColor}"));
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
            var shade = view.Descendants().SingleOrDefault(element => Name(element) == "HoverShade");
            Assert.NotNull(shade);
            Assert.Equal("{ThemeResource EgScrimBrush}", shade!.Attribute("Background")?.Value);
            Assert.Equal("False", shade.Attribute("IsHitTestVisible")?.Value);
        });

        Test("中性反馈：播放键默认灰底白字，保留圆形尺寸与播放路由", () =>
        {
            // 2026-11-08 起这一份样式住进 Styles.xaml，海报卡和详情页左上角的封面合用它（见材质专项）。
            var styles = XDocument.Load(Path.Combine(Root(), "src", "Momoka.Shell", "Theme", "Styles.xaml"));
            var style = styles.Descendants().Single(element => element.Attribute(Xaml + "Key")?.Value == "EgOverlayPlayButtonStyle");
            Assert.Equal("{StaticResource DefaultButtonStyle}", style.Attribute("BasedOn")?.Value);
            Assert.Equal("{ThemeResource EgPlayGlassBrush}", Setter(style, "Background"));
            Assert.Equal("{ThemeResource EgTextBrush}", Setter(style, "Foreground"));
            Assert.Equal("48", Setter(style, "Width"));
            Assert.Equal("48", Setter(style, "Height"));
            Assert.Equal("24", Setter(style, "CornerRadius"));
            Assert.Equal("0", Setter(style, "BorderThickness"));
            var glyph = styles.Descendants().Single(element => element.Attribute(Xaml + "Key")?.Value == "EgPlayGlyphStyle");
            Assert.Equal("20", Setter(glyph, "FontSize"));
            var card = View("PosterCard.xaml");
            var button = card.Descendants().Single(element => Name(element) == "PlayButton");
            Assert.Equal("OnPlayClicked", button.Attribute("Click")?.Value);
            Assert.Equal("{StaticResource EgOverlayPlayButtonStyle}", button.Attribute("Style")?.Value);
            Assert.Equal("{StaticResource EgPlayGlyphStyle}", button.Elements().Single(element => element.Name.LocalName == "FontIcon").Attribute("Style")?.Value);
            Assert.Equal("{x:Bind Card.PlayVisibility, Mode=OneWay}", button.Attribute("Visibility")?.Value);
        });

        Test("中性反馈：指针和焦点只更新动画，不重设封面样式", () =>
        {
            var code = File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "PosterCard.xaml.cs"));
            var begin = code.IndexOf("    public PosterCard()", StringComparison.Ordinal);
            var end = code.IndexOf("    public event EventHandler<CardActionEventArgs>? ActionRequested;", begin, StringComparison.Ordinal);
            Assert.True(begin >= 0 && end > begin, "构造与事件边界必须仍可定位");
            var constructor = code[begin..end];
            Assert.False(constructor.Contains("Paint();", StringComparison.Ordinal), "悬停或焦点不应反复重设同一封面样式");
            Assert.Equal(2, constructor.Split("AnimateCard();", StringSplitOptions.None).Length - 1,
                "指针与焦点仍须各自更新动画");
            Assert.True(constructor.Contains("new ImageArrivalFade(Poster)", StringComparison.Ordinal),
                "海报须按实际图片Source到达启动淡入");
        });

        Test("中性反馈：播放键探针先应用模板，异常时也恢复默认态", () =>
        {
            var code = File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "PosterCard.xaml.cs"));
            var begin = code.IndexOf("    private (Windows.UI.Color Still, Windows.UI.Color Active) ProbePlay()", StringComparison.Ordinal);
            var end = code.IndexOf("    private static Windows.UI.Color Colour(", begin, StringComparison.Ordinal);
            var probe = code[begin..end];
            var template = probe.IndexOf("PlayButton.ApplyTemplate();", StringComparison.Ordinal);
            var hover = probe.IndexOf("VisualStateManager.GoToState(PlayButton, \"PointerOver\"", StringComparison.Ordinal);
            Assert.True(template >= 0 && hover > template, "必须先应用播放键模板，再切指针态");
            var cleanup = probe.IndexOf("finally", StringComparison.Ordinal);
            var normal = probe.IndexOf("VisualStateManager.GoToState(PlayButton, \"Normal\"", StringComparison.Ordinal);
            Assert.True(cleanup > hover && normal > cleanup, "探针失败也必须恢复默认态");
        });

        Test("中性反馈：强调色只覆盖播放按钮的指针和按下状态", () =>
        {
            var card = View("PosterCard.xaml");
            var button = card.Descendants().Single(element => Name(element) == "PlayButton");
            var resources = button.Elements().Single(element => element.Name.LocalName == "Button.Resources");
            var expected = new Dictionary<string, string>
            {
                ["ButtonBackgroundPointerOver"] = "EgPlayGlassHoverBrush",
                ["ButtonBackgroundPressed"] = "EgPlayGlassPressedBrush",
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

        Test("中性反馈：搜索框逐尺寸一次聚焦，宽帧仍检查全部主题", () =>
        {
            var code = File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "ShellNavigationProbe.SearchLayout.cs"));
            var begin = code.IndexOf("    private async Task SearchHeaderLayoutAsync()", StringComparison.Ordinal);
            var end = code.IndexOf("    private async Task SearchInteractionAsync()", begin, StringComparison.Ordinal);
            var probe = code[begin..end];
            Assert.Equal(1, probe.Split(".Focus(FocusState.Programmatic)", StringSplitOptions.None).Length - 1,
                "不要重复聚焦、等待布局并检查同一搜索框");
            Assert.Contains("new[] { 1600, 1040, 760, 520, 360 }", probe);
            Assert.Contains("foreach (var theme in UiThemes.All)", probe);
            Assert.Contains("focusEdge.BorderThickness == new Thickness(1)", probe);
            Assert.Contains("search-focus-neutral-{theme.Id}-screen.png", probe);
            Assert.Contains("搜索标题重新显示的反例未被检测", probe);
            Assert.Contains("来源胶囊被改成方形的反例未被检测", probe);
        });

        Test("中性反馈：自检同时核对无外圈及播放键两态，不再要求封面变主题色", () =>
        {
            var code = File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "ShellSelfCheck.Reads.cs"));
            Assert.Contains("corner.FrameBorderless", code);
            Assert.Contains("Format(corner.FrameActive) == Format(corner.FrameRest)", code);
            Assert.Contains("Format(corner.PlayRest) == ColorOf(\"EgWindowBrush\")", code);
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
