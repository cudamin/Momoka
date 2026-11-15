using System.Xml.Linq;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 商店详情版式与「从头开始」入口的源码契约（2026-11-14 用户令：参考微软商店条目页重排详情页，并把
/// 从头开始折进「更多」）。实际父子关系、尺寸和命中区由 Shell 原生探针核对；这里钉的是 XAML 结构和
/// 两条接线，免得改版时入口悄悄消失。
/// </summary>
internal static class StoreDetailLayoutTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static void Register()
    {
        Test("商店渐变：窄窗材料与图片共用最终遮罩，离页清缓存，浅色前景同步", () =>
        {
            foreach (var name in new[] { "DetailPage", "MoviePilotSubscriptionPage" })
            {
                var page = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", name + ".xaml"));
                var acrylic = page.Descendants().Single(element => (string?)element.Attribute(X + "Name") == "StoreBackdropAcrylic");
                Assert.Equal("StoreBackdropContent", (string?)acrylic.Parent!.Attribute(X + "Name"));
                Assert.Null(acrylic.Attribute("Margin"));
                var source = Source("Views/" + name + ".xaml.cs");
                Assert.Contains("_storeAmbientBrush.Clear();", source);
                Assert.Contains("_storeTint.Clear();", source);
            }
            var reading = Source("Views/DetailPage.Appearance.cs");
            Assert.Contains("ViewModel.StoreMode && !ThemeHost.Current.IsDark", reading);
            Assert.Contains("? accent : storeInk", reading);
            var mp = Source("Views/MoviePilotSubscriptionPage.Showcase.cs");
            Assert.Contains("MediaViewModel.Plain && !ThemeHost.Current.IsDark", mp);
            Assert.Contains("MediaMeta.Foreground = new SolidColorBrush(heroInk);", mp);
            var trailer = Source("Views/DetailPage.StoreTrailer.cs");
            Assert.Contains("_storeAmbientBrush.Frame", trailer);
            Assert.Contains("_storeVideoBrush.Clear();", trailer);
            Assert.Contains("StoreTrailerFoundation.Background = null;", trailer);
            Assert.Contains("StoreTrailerFoundation.Visibility = Visibility.Collapsed;", trailer);
            Assert.Contains("_storeTrailerUserPaused || _storeTrailerScrollPaused", trailer);
            Assert.Contains("nameof(DetailViewModel.StoreAmbientHeight)", Source("Views/DetailPage.Trailers.cs"));
            var detail = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "DetailPage.xaml"));
            Assert.Equal("1", (string?)detail.Descendants().Single(element => (string?)element.Attribute(X + "Name") == "StoreTrailerFade").Attribute("Canvas.ZIndex"));
            var content = detail.Descendants().Single(element => (string?)element.Attribute(X + "Name") == "StoreScrollContent");
            var layers = content.Elements().Select(element => (string?)element.Attribute(X + "Name")).ToArray();
            Assert.True(Array.IndexOf(layers, "StoreTrailerFoundation") < Array.IndexOf(layers, "StoreAmbient")
                && Array.IndexOf(layers, "StoreAmbient") < Array.IndexOf(layers, "StoreTrailerHost"), "视频局部底层遮住了环境图或视频");
        });
        Test("商店详情：简介撑满封面右栏，文件选项和操作键排在封面下方", () =>
        {
            var page = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "DetailPage.xaml"));
            var details = page.Descendants().Single(element => (string?)element.Attribute(X + "Name") == "StoreHeroDetails");
            Assert.Equal("1", (string?)details.Attribute("Grid.Row"));
            Assert.Equal("2", (string?)details.Attribute("Grid.ColumnSpan"));
            // 简介内部拆成「正文（星号行）+ 底部导演行」：撑满时正文吃掉差额，导演那一行钉在封面下沿。
            var overview = page.Descendants().Single(element => (string?)element.Attribute(X + "Name") == "OverviewPanel");
            Assert.True(overview.Descendants().Any(element => (string?)element.Attribute(X + "Name") == "OverviewBody"),
                "简介没有拆出可撑满的正文容器 OverviewBody");
            Assert.True(overview.Descendants().Any(element => (string?)element.Attribute(X + "Name") == "OverviewFooter"),
                "简介没有拆出底部导演行 OverviewFooter");
            // 商店简介的左内边距是 0，而 EgBodyPanelStyle 带 16 的圆角；圆角留着会把第一个字的左上角剪掉
            // （2026-11-15 用户报「剧情说明第一个字的左上角少了一点」）。这块本来就不画底和框，圆角归零。
            Assert.Equal("0", (string?)overview.Attribute("CornerRadius"));
            var store = Source("Views/DetailPage.Store.cs");
            Assert.Contains("HeroStack.Children.Add(OverviewPanel);", store);
            Assert.Contains("StoreHeroDetails.Children.Add(FileOptions);", store);
            Assert.Contains("StoreHeroDetails.Children.Add(ColumnActions);", store);
            Assert.Contains("SyncStoreOverviewFill();", store);
            Assert.Contains("DetailHero.OverviewLines(room, OverviewLineHeight);", store);
            // 文件选项保持各页通用的行样式（标签在外、选择框自己一颗），不再排成商店「版本」框。
            Assert.False(store.Contains("PlaceStorePickerBoxes", StringComparison.Ordinal),
                "商店文件选项又排成了版本框");
        });

        Test("详情操作：从头开始只在更多菜单里，紧跟继续播放一行", () =>
        {
            var page = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "DetailPage.xaml"));
            foreach (var gone in new[] { "ColumnRestartButton", "CompactRestartButton" })
                Assert.False(page.Descendants().Any(element =>
                        (string?)element.Attribute(X + "Name") == gone
                        || (string?)element.Attribute("AutomationProperties.AutomationId") == gone),
                    "详情操作里仍有 " + gone);
            Assert.False(page.Descendants().Any(element => (string?)element.Attribute("Command") == "{x:Bind ViewModel.RestartCommand}"),
                "从头开始仍是操作键上的一颗按钮");
            var code = Source("Views/DetailPage.xaml.cs");
            Assert.Contains("restart: ViewModel.RestartVisibility == Visibility.Visible", code);
            var commands = Source("Views/ItemCommands.cs");
            Assert.Contains("Text = \"从头开始\"", commands);
            Assert.Contains("var play = rows.FindIndex(row => row is MenuFlyoutItem { Tag: ItemCommand.Play });", commands);
            Assert.Contains("rows.Insert(play < 0 ? 0 : play + 1, entry);", commands);
            Assert.Contains("DetailRestartMenuItem", commands);
            Assert.False(Source("Strings/zh-Hans/Resources.resw").Contains("DetailPage_TextBlock1.Text", StringComparison.Ordinal),
                "旧按钮的本地化字串没有退役");
        });
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new AssertionException("找不到本次编译所属的工作树");
    }

    private static string Source(string path) =>
        File.ReadAllText(Path.Combine(FindRoot(), "src", "Momoka.Shell", path.Replace('/', Path.DirectorySeparatorChar)));
}
