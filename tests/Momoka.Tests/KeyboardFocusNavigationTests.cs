using System.Globalization;
using System.Xml.Linq;

namespace Momoka.Tests;

/// <summary>第四阶段重构契约：卡片/剧集行键盘焦点与无障碍、AlphaBar 单 Tab 停靠、AniBT ItemsView 导航及最小 32 DIP 命中热区。</summary>
internal static class KeyboardFocusNavigationTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static void Register()
    {
        TestHarness.Test("卡片与剧集行：键盘焦点显示快捷操作并同步中英文 HelpText 到宿主容器", () =>
        {
            var cardXaml = LoadView("PosterCard.xaml");
            var cardRoot = Named(cardXaml, "Root");
            Assert.Equal("PosterCard_Root", (string?)cardRoot.Attribute(Xaml + "Uid"));

            var cardCode = ReadSource("src/Momoka.Shell/Views/PosterCard.xaml.cs");
            Assert.Contains("var actionsVisible = _hovered || keyboardFocused;", cardCode);
            Assert.Contains("if (actionsVisible) Hover.Visibility = Visibility.Visible;", cardCode);
            Assert.Contains("if (!actionsVisible) Hover.Visibility = Visibility.Collapsed;", cardCode);
            Assert.Contains("IsDescendantOf(focusedControl, Root)", cardCode);
            Assert.Contains("AutomationProperties.SetHelpText(host, help);", cardCode);

            var rowXaml = LoadView("EpisodeRow.xaml");
            var rowRoot = Named(rowXaml, "Root");
            Assert.Equal("EpisodeRow_Root", (string?)rowRoot.Attribute(Xaml + "Uid"));
            var rowPlay = Named(rowXaml, "HoverPlayButton");
            Assert.Equal("{x:Bind Card.PlayActionTip, Mode=OneWay}",
                (string?)rowPlay.Attribute("AutomationProperties.Name"));
            Assert.Equal("{x:Bind local:EpisodeRow.HoverPlayTabStop(ShowTrailingPlay), Mode=OneWay}",
                (string?)rowPlay.Attribute("IsTabStop"));

            var rowCode = ReadSource("src/Momoka.Shell/Views/EpisodeRow.xaml.cs");
            Assert.Contains("HoverPlayTabStop(bool showTrailingPlay) => !showTrailingPlay;", rowCode);
            Assert.Contains("UpdateHover();", rowCode);
            Assert.Contains("IsDescendantOf(focusedControl, Root)", rowCode);
            Assert.Contains("AutomationProperties.SetHelpText(host, help);", rowCode);

            foreach (var locale in new[] { "zh-Hans", "en-US" })
            {
                var resw = LoadResw(locale);
                foreach (var key in new[]
                {
                    "PosterCard_Root.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.HelpText",
                    "EpisodeRow_Root.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.HelpText"
                })
                {
                    var value = resw.Descendants("data")
                        .Single(item => (string?)item.Attribute("name") == key)
                        .Element("value")?.Value;
                    Assert.True(!string.IsNullOrWhiteSpace(value), $"{locale} 缺少 {key}");
                }
            }
        });

        TestHarness.Test("字母导航条：启用单 Tab 停靠与漫游焦点，消除 27 次顺序穿越", () =>
        {
            var xaml = LoadView("AlphaBar.xaml");
            Assert.Equal("False", (string?)Named(xaml, "Letters").Attribute("IsTabStop"));

            var code = ReadSource("src/Momoka.Shell/Views/AlphaBar.xaml.cs");
            Assert.Contains("TabFocusNavigation = KeyboardNavigationMode.Once", code);
            Assert.Contains("XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled", code);
            Assert.Contains("IsTabStop = letter == Alphabet[0]", code);
            Assert.DoesNotContain("TabIndex = Array.IndexOf", code);
            Assert.Contains("ActivateTabStop(target);", code);
            var activateAt = code.IndexOf("ActivateTabStop(target);", StringComparison.Ordinal);
            var focusAt = code.IndexOf("target.Focus(FocusState.Keyboard)", StringComparison.Ordinal);
            Assert.True(activateAt >= 0 && focusAt > activateAt, "方向键切换焦点前须先启用目标按钮 IsTabStop");
        });

        TestHarness.Test("主页轮播：左右翻页键键盘可达且底部分页圆点放宽横向热区", () =>
        {
            var banner = LoadView("HomeBanner.xaml");
            foreach (var name in new[] { "PrevButton", "NextButton" })
            {
                var button = Named(banner, name);
                Assert.True(button.Attribute("IsTabStop") is null, $"{name} 不应禁用 Tab 停靠");
            }
            var dotStyle = banner.Descendants()
                .Single(element => (string?)element.Attribute(Xaml + "Key") == "EgBannerDotButtonStyle");
            var minWidth = dotStyle.Elements()
                .Single(setter => (string?)setter.Attribute("Property") == "MinWidth")
                .Attribute("Value")?.Value;
            Assert.True(double.Parse(minWidth ?? "0", CultureInfo.InvariantCulture) >= 28d);
        });

        TestHarness.Test("AniBT 面板：Results 使用 ItemsView + ItemContainer 单层焦点容器", () =>
        {
            var anibt = LoadView("AniBtSearchPanel.xaml");
            var results = Named(anibt, "AnimeResults");
            Assert.Equal("ItemsView", results.Name.LocalName);
            Assert.Equal("True", (string?)results.Attribute("IsItemInvokedEnabled"));
            Assert.Equal("OnAnimeItemInvoked", (string?)results.Attribute("ItemInvoked"));
            var itemTemplate = results.Descendants().Single(element => element.Name.LocalName == "DataTemplate");
            var container = itemTemplate.Elements().Single();
            Assert.Equal("ItemContainer", container.Name.LocalName);
            Assert.Equal("OpenAnimeResources", (string?)container.Attribute(Xaml + "Name"));
            Assert.False(itemTemplate.Descendants().Any(element => element.Name.LocalName == "Button"),
                "ItemsView 模板内不应再嵌套独立 Button 造成双重焦点");

            var code = ReadSource("src/Momoka.Shell/Views/AniBtSearchPanel.xaml.cs");
            Assert.Contains("UniformGridLayout", code);
            Assert.Contains("OnAnimeItemInvoked", code);
        });

        TestHarness.Test("订阅骨架与全仓交互热区：匹配操作键高度及满足 >= 32 DIP 下限", () =>
        {
            var mp = LoadView("MoviePilotSubscriptionPage.xaml");
            var skeleton = Named(mp, "DetailLoadingSkeleton");
            var skeletonActions = skeleton.Descendants()
                .Where(element => element.Name.LocalName == "WrapRow")
                .SelectMany(wrap => wrap.Elements())
                .ToArray();
            Assert.Equal(4, skeletonActions.Length);
            foreach (var placeholder in skeletonActions)
                Assert.Equal("{StaticResource EgActionHeight}", (string?)placeholder.Attribute("Height"));

            var diagnostics = LoadView("DiagnosticsPage.xaml");
            var detailsButton = Named(diagnostics, "DiagnosticsDetailsButton");
            Assert.True(double.Parse((string?)detailsButton.Attribute("MinHeight") ?? "0", CultureInfo.InvariantCulture) >= 32d);
            Assert.True(double.Parse((string?)detailsButton.Attribute("MinWidth") ?? "0", CultureInfo.InvariantCulture) >= 32d);

            var player = LoadView("PlayerPage.xaml");
            var danmakuInput = Named(player, "DanmakuInputBox");
            Assert.True(double.Parse((string?)danmakuInput.Attribute("Height") ?? "0", CultureInfo.InvariantCulture) >= 32d);
            Assert.True(double.Parse((string?)danmakuInput.Attribute("MinHeight") ?? "0", CultureInfo.InvariantCulture) >= 32d);
            var danmakuSend = Named(player, "SendDanmakuButton");
            Assert.True(double.Parse((string?)danmakuSend.Attribute("Height") ?? "0", CultureInfo.InvariantCulture) >= 32d);
            Assert.Contains("DanmakuInputBox.Height = BigChrome ? 40 : 32;",
                ReadSource("src/Momoka.Shell/Views/PlayerPage.Danmaku.cs"));

            var settings = LoadView("SettingsPage.xaml");
            var searchButton = Named(settings, "SettingsSearchButton");
            Assert.Equal("Center", (string?)searchButton.Attribute("VerticalAlignment"));
            Assert.Equal("False", (string?)searchButton.Attribute("IsTabStop"));

            foreach (var file in new[] { "MusicPlayerBar.xaml", "MusicPlayerPage.xaml" })
            {
                var doc = LoadView(file);
                var sliderHeight = Named(doc, "SeekSlider").Descendants()
                    .Single(element => (string?)element.Attribute(Xaml + "Key") == "SliderHorizontalHeight")
                    .Value;
                Assert.True(double.Parse(sliderHeight, CultureInfo.InvariantCulture) >= 32d,
                    file + " SeekSlider 命中高度低于 32 DIP");
            }
        });
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln")))
                return directory.FullName;
        throw new DirectoryNotFoundException("未找到当前 Momoka 工作树");
    }

    private static XDocument LoadView(string file) =>
        XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", file));

    private static XDocument LoadResw(string culture) =>
        XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Strings", culture, "Resources.resw"));

    private static string ReadSource(string relative) =>
        File.ReadAllText(Path.Combine(FindRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private static XElement Named(XDocument document, string name) =>
        document.Descendants().Single(element => (string?)element.Attribute(Xaml + "Name") == name);
}
