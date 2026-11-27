using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class DetailSectionCardTests
{
    internal static void Register()
    {
        Test("商店详情卡：生产切换保留分区和内容身份、显隐及原模式留白", () =>
        {
            foreach (var key in new[] { "EgStoreDetailCardStyle", "EgBodyPanelStyle", "EgStoreDetailCardHeaderStyle", "EgStoreDetailCardContentStyle" })
                Application.Current.Resources[key] = new Style(key);
            var head = new ShelfHead();
            var header = new Border { Child = head };
            var content = new StackPanel();
            var item = new FrameworkElement();
            content.Children.Add(item);
            var panel = new StackPanel { Visibility = Visibility.Collapsed };
            panel.Children.Add(header);
            panel.Children.Add(content);
            foreach (var store in new[] { true, false, true, false })
            {
                DetailSectionCard.Apply(panel, store, new Thickness(12), 16);
                Assert.Equal(store ? "EgStoreDetailCardStyle" : "EgBodyPanelStyle", panel.Style!.Key);
                Assert.Equal(store, head.StoreCard);
                Assert.Equal(store ? VerticalAlignment.Center : VerticalAlignment.Stretch, head.VerticalAlignment);
                Assert.Equal(store ? default : new Thickness(12), panel.Padding);
                Assert.Equal(store ? 0d : 16d, panel.Spacing);
                Assert.Equal(16d, content.Spacing);
                Assert.Equal(store ? "EgStoreDetailCardHeaderStyle" : null, header.Style?.Key);
                Assert.Equal(store ? "EgStoreDetailCardContentStyle" : null, content.Style?.Key);
                Assert.Equal(Visibility.Collapsed, panel.Visibility);
                Assert.True(ReferenceEquals(header, panel.Children[0]) && ReferenceEquals(content, panel.Children[1]));
                Assert.True(ReferenceEquals(item, content.Children.Single()));
            }
        });
        foreach (var page in new[] { ("DetailPage.xaml", new[] { "EpisodePanel", "InfoPanel", "SeasonPanel", "CastPanel", "SimilarPanel" }),
            ("MoviePilotSubscriptionPage.xaml", new[] { "SeasonPanel", "PeoplePanel", "RecommendationsPanel", "SimilarPanel" }) })
            Test("商店详情卡：卡头与内容随同一分区隐藏和重排 " + page.Item1, () =>
            {
                var xml = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", page.Item1));
                XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
                foreach (var name in page.Item2)
                {
                    var panel = xml.Descendants().Single(element => (string?)element.Attribute(x + "Name") == name);
                    Assert.True(HasCardStructure(panel), name);
                    Assert.True(panel.Attribute("Visibility") is not null);
                    Assert.True(panel.Attribute("Padding") is null && panel.Attribute("Spacing") is null,
                        "模式切换留白只由生产帮助类写入，避免绑定后置覆盖");
                    var movedHead = new XElement(panel);
                    movedHead.Elements().First().Remove();
                    Assert.False(HasCardStructure(movedHead), "反例：标题移出可隐藏根必须被拒绝");
                }
            });
        Test("商店详情卡：两页共用主题表面、标题分隔与内容样式", () =>
        {
            var xml = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Theme", "Styles.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var styles = xml.Root!.Elements().Where(element => element.Name.LocalName == "Style" && element.Attribute(x + "Key") is not null)
                .ToDictionary(element => (string)element.Attribute(x + "Key")!);
            Assert.Equal("{ThemeResource EgCardFillBrush}", Setter(styles["EgStoreDetailCardStyle"], "Background"));
            Assert.Equal("0,0,0,1", Setter(styles["EgStoreDetailCardHeaderStyle"], "BorderThickness"));
            Assert.Equal("{ThemeResource DividerStrokeColorDefaultBrush}", Setter(styles["EgStoreDetailCardHeaderStyle"], "BorderBrush"));
            Assert.Equal("24", Setter(styles["EgStoreDetailCardContentStyle"], "Padding"));
        });
    }

    private static bool HasCardStructure(XElement panel) => panel.Elements().Select(element => element.Name.LocalName)
        .SequenceEqual(new[] { "Border", "StackPanel" })
        && panel.Elements().First().Elements().SingleOrDefault()?.Name.LocalName == "ShelfHead"
        && !panel.Descendants().Any(element => element.Name.LocalName is "ScrollViewer" or "ScrollView");

    private static string? Setter(XElement style, string property) => style.Elements()
        .Single(element => (string?)element.Attribute("Property") == property).Attribute("Value")?.Value;

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new InvalidOperationException("找不到当前工作树");
    }
}
