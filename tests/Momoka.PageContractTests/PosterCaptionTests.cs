using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Momoka.Emby;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class PosterCaptionTests
{
    internal static void Register()
    {
        Test("详情压图文字：真实样式方法保留标题与副行层级，首页库卡独立", () => CardImageContext.Run(async () =>
        {
            SetStyles();
            await using var fixture = new CardImageFixture(false, MediaServerKind.Emby);
            var poster = new PosterCard { OnScrim = true };
            foreach (var type in new[] { EmbyItemType.Person, EmbyItemType.Movie, EmbyItemType.Series, EmbyItemType.Season, EmbyItemType.Episode })
            {
                poster.Card = new CardItem(fixture.Scope.Bind(new EmbyItem { Id = type, Type = type }), fixture.Images, 170);
                poster.ApplyCaptionStyles();
                Assert.Equal("CaptionOnScrim", poster.CaptionStateReading);
                Assert.Equal("EgOnScrimBodyStyle", poster.TitleStyleReading);
                Assert.Equal("EgOnScrimDataStyle", poster.SubtitleStyleReading);
            }
            poster.Card = new CardItem(fixture.Scope.Bind(new EmbyItem { Id = "library", Type = EmbyItemType.CollectionFolder }), fixture.Images, 208);
            poster.ApplyCaptionStyles();
            Assert.Equal("CaptionLibrary", poster.CaptionStateReading);
            poster.Card = new CardItem(fixture.Scope.Bind(new EmbyItem { Id = "person", Type = EmbyItemType.Person }), fixture.Images, 170);
            poster.ApplyCaptionStyles();
            Assert.Equal("CaptionOnScrim", poster.CaptionStateReading);
            poster.OnScrim = false;
            poster.ApplyCaptionStyles();
            Assert.Equal("CaptionPlain", poster.CaptionStateReading);
            Assert.Equal("EgBodyStyle", poster.TitleStyleReading);
            Assert.Equal("EgDataStyle", poster.SubtitleStyleReading);
            poster.Card = new CardItem(fixture.Scope.Bind(new EmbyItem { Id = "upcoming", Type = EmbyItemType.Episode }), fixture.Images, 170, upcoming: true);
            poster.ApplyCaptionStyles();
            Assert.Equal("EgCaptionStyle", poster.SubtitleStyleReading);
        }));
        Test("详情压图文字：可执行视觉状态资源链与固定副行吻合，高对比度可覆盖", () =>
        {
            var root = FindRoot();
            var card = XDocument.Load(Path.Combine(root, "src/Momoka.Shell/Views/PosterCard.xaml"));
            var palette = XDocument.Load(Path.Combine(root, "src/Momoka.Shell/Theme/Palette.xaml"));
            var styles = XDocument.Load(Path.Combine(root, "src/Momoka.Shell/Theme/Styles.xaml"));
            var state = Named(card, "CaptionOnScrim");
            var library = Named(card, "CaptionLibrary");
            foreach (var (target, role) in new[] { ("TitleLine.Foreground", "Foreground"), ("SubLine.Foreground", "Secondary"),
                ("SourceLine.Foreground", "Secondary"), ("NoteLine.Foreground", "Secondary") })
            {
                Assert.Equal("{ThemeResource EgMediaCaption" + role + "Brush}", (string?)state.Descendants().Single(element => (string?)element.Attribute("Target") == target).Attribute("Value"));
                Assert.Equal("{ThemeResource EgCardCaption" + role + "Brush}", (string?)library.Descendants().Single(element => (string?)element.Attribute("Target") == target).Attribute("Value"));
            }
            foreach (var theme in new[] { "Dark", "Light" })
            {
                var dictionary = Keyed(palette, theme);
                Assert.Equal("EgOnScrimBrush", (string?)Keyed(dictionary, "EgMediaCaptionForegroundBrush").Attribute("ResourceKey"));
                Assert.Equal("EgOnScrimDimBrush", (string?)Keyed(dictionary, "EgMediaCaptionSecondaryBrush").Attribute("ResourceKey"));
                Assert.Equal("EgTextBrush", (string?)Keyed(dictionary, "EgCardCaptionForegroundBrush").Attribute("ResourceKey"));
                Assert.Equal("EgTextBrush", (string?)Keyed(dictionary, "EgCardCaptionSecondaryBrush").Attribute("ResourceKey"));
            }
            var contrast = Keyed(palette, "HighContrast");
            foreach (var role in new[] { "Foreground", "Secondary" })
            {
                var key = "EgMediaCaption" + role + "Brush";
                Assert.Equal("{ThemeResource SystemColorWindowTextColor}", (string?)Keyed(contrast, key).Attribute("Color"));
                Assert.False(palette.Root!.Elements().Any(element => (string?)element.Attribute(Xaml + "Key") == key), "顶层别名会遮住高对比度资源");
            }
            Assert.Equal("#FFC9D0D9", (string?)Keyed(palette, "EgOnScrimDimBrush").Attribute("Color"));
            var data = Keyed(styles, "EgOnScrimDataStyle");
            Assert.Equal("{StaticResource EgOnScrimDimBrush}", (string?)data.Elements().Single(element => (string?)element.Attribute("Property") == "Foreground").Attribute("Value"));
            var original = XDocument.Parse(card.ToString());
            Named(original, "CaptionOnScrim").Descendants().Single(element => (string?)element.Attribute("Target") == "SubLine.Foreground")
                .SetAttributeValue("Value", "{ThemeResource EgCardCaptionSecondaryBrush}");
            Assert.False(MediaRoleUsed(Named(original, "CaptionOnScrim")), "反例未检出首页角色覆盖详情副行");
            Assert.True(MediaRoleUsed(state));
        });
        Test("人物卡双模板：整组卡片都接收货架文字模式", () =>
        {
            var source = File.ReadAllText(Path.Combine(FindRoot(), "src/Momoka.Shell/Views/ShelfStrip.xaml.cs"));
            var start = source.IndexOf("private void PaintInk", StringComparison.Ordinal);
            var end = source.IndexOf("internal PosterCard? CardElement", start, StringComparison.Ordinal);
            Assert.Contains("foreach (var card in CardsIn(container)) card.OnScrim = OnScrim;", source[start..end]);
            var template = XDocument.Load(Path.Combine(FindRoot(), "src/Momoka.Shell/Views/DetailPage.xaml"));
            var person = template.Descendants().Single(element => (string?)element.Attribute(Xaml + "Key") == "PersonTemplate");
            Assert.Equal(2, person.Descendants().Count(element => element.Name.LocalName == "PosterCard"));
        });
        Test("详情压图文字：OnScrim切换与回收换卡均调用同一生产样式入口", () =>
        {
            var source = File.ReadAllText(Path.Combine(FindRoot(), "src/Momoka.Shell/Views/PosterCard.xaml.cs"));
            var start = source.IndexOf("private static void OnScrimChanged", StringComparison.Ordinal);
            var end = source.IndexOf("private readonly Dictionary<TextBlock", start, StringComparison.Ordinal);
            Assert.Contains("card.ApplyCaptionStyles();", source[start..end]);
            start = source.IndexOf("private static void OnCardChanged", StringComparison.Ordinal);
            end = source.IndexOf("private void OnLoaded", start, StringComparison.Ordinal);
            Assert.Contains("card.ApplyCaptionStyles();", source[start..end]);
        });
    }

    private static bool MediaRoleUsed(XElement state) => state.Descendants().Single(element => (string?)element.Attribute("Target") == "SubLine.Foreground")
        .Attribute("Value")?.Value == "{ThemeResource EgMediaCaptionSecondaryBrush}";
    private static void SetStyles()
    {
        foreach (var key in new[] { "EgBodyStyle", "EgOnScrimBodyStyle", "EgDataStyle", "EgOnScrimDataStyle", "EgCaptionStyle" })
            Application.Current.Resources[key] = new Style(key);
    }
    private static XElement Named(XContainer document, string name) => document.Descendants().Single(element => (string?)element.Attribute(Xaml + "Name") == name);
    private static XElement Keyed(XContainer document, string key) => document.Descendants().Single(element => (string?)element.Attribute(Xaml + "Key") == key);
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new InvalidOperationException("测试不在Momoka树中");
    }
}
