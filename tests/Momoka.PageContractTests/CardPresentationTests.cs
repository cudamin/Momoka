using System.Xml.Linq;
using Momoka.Emby;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class CardPresentationTests
{
    internal static void Register()
    {
        Test("原生搜索结果名称：外层导航按钮和两种卡墙容器绑定当前卡片标题", () =>
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) directory = directory.Parent;
            Assert.NotNull(directory);
            var page = XDocument.Load(Path.Combine(directory!.FullName, "src/Momoka.Shell/Views/LibraryPage.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            foreach (var key in new[] { "ShelfCardTemplate", "PosterTemplate", "ThumbTemplate" })
            {
                var template = page.Descendants().Single(element => (string?)element.Attribute(x + "Key") == key);
                var root = template.Elements().Single();
                Assert.True(root.Name.LocalName is "Button" or "ItemContainer");
                Assert.Equal("{x:Bind Title, Mode=OneWay}", (string?)root.Attribute("AutomationProperties.Name"));
                Assert.False(root.Attributes().Any(attribute => attribute.Name.LocalName == "Template"));
            }
        });
        Test("原生结果命名数据：正式CardItem标题覆盖与来源身份", () => CardImageContext.Run(async () =>
        {
            await using var fixture = new CardImageFixture(false, MediaServerKind.Emby);
            var item = fixture.Scope.Bind(new EmbyItem { Id = "named-result", Name = "服务器影片名", Type = EmbyItemType.Movie });
            var card = new CardItem(item, fixture.Images, 170, title: "展示影片名");
            Assert.Equal("展示影片名", card.Title);
            Assert.True(ReferenceEquals(item.Source, card.Item.Source));
            Assert.True(card.Title.Length > 0);
            card.AbandonPoster();
        }));
        Test("媒体多选：选择时收起单项动作，退出恢复并通知", () => CardImageContext.Run(async () =>
        {
            await using var fixture = new CardImageFixture(false, MediaServerKind.Emby);
            var card = new CardItem(fixture.Scope.Bind(new EmbyItem
            {
                Id = "movie",
                Type = EmbyItemType.Movie,
                Name = "影片",
                UserData = new EmbyUserData { IsFavorite = true }
            }), fixture.Images, 170);
            var original = new[] { card.PlayVisibility, card.UserStateVisibility, card.FavoriteActionVisibility, card.MoreVisibility };
            var selecting = typeof(CardItem).GetProperty("IsSelecting");
            Assert.NotNull(selecting);
            var changed = new HashSet<string>();
            card.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);
            selecting!.SetValue(card, true);
            Assert.Equal("Collapsed", card.PlayVisibility.ToString());
            Assert.Equal("Collapsed", card.UserStateVisibility.ToString());
            Assert.Equal("Collapsed", card.FavoriteActionVisibility.ToString());
            Assert.Equal("Collapsed", card.MoreVisibility.ToString());
            Assert.Equal("Collapsed", card.FavoriteBadgeVisibility.ToString());
            foreach (var name in new[] { "PlayVisibility", "UserStateVisibility", "FavoriteActionVisibility", "MoreVisibility", "FavoriteBadgeVisibility" })
                Assert.True(changed.Contains(name));
            selecting.SetValue(card, false);
            Assert.Equal(original[0], card.PlayVisibility);
            Assert.Equal(original[1], card.UserStateVisibility);
            Assert.Equal(original[2], card.FavoriteActionVisibility);
            Assert.Equal(original[3], card.MoreVisibility);
            Assert.Equal("Visible", card.FavoriteBadgeVisibility.ToString());
        }));
        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin, MediaServerKind.FnNas, MediaServerKind.Plex })
            Test("MP 订阅库导航卡不绑定媒体服务器 " + kind, () => CardImageContext.Run(async () =>
            {
                await using var fixture = new CardImageFixture(false, kind);
                var opens = 0;
                var item = new EmbyItem { Id = "mp-subscriptions", Type = EmbyItemType.CollectionFolder, Name = "MP 订阅库" };
                var card = new CardItem(item, fixture.Images, 208, wide: true, navigate: () => opens++);
                Assert.Null(card.Scope);
                Assert.Null(item.Source);
                Assert.True(card.IsNavigation);
                Assert.Equal("Collapsed", card.PlayVisibility.ToString());
                Assert.Equal("Collapsed", card.UserStateVisibility.ToString());
                Assert.Equal("Collapsed", card.FavoriteActionVisibility.ToString());
                Assert.Equal("Collapsed", card.MoreVisibility.ToString());
                card.OpenNavigation();
                Assert.Equal(1, opens);
                var shelf = new Momoka.Shell.ViewModels.CardShelf("媒体库", fixture.Images, 208, true);
                var library = fixture.Scope.Bind(new EmbyItem { Id = "library", Name = "电影", Type = EmbyItemType.CollectionFolder });
                shelf.Apply([library]);
                var normal = shelf.Cards[0];
                shelf.Cards.Add(card);
                var changes = 0;
                shelf.Cards.CollectionChanged += (_, _) => changes++;
                shelf.Apply([library]);
                Assert.Equal(0, changes);
                Assert.True(ReferenceEquals(normal, shelf.Cards[0]));
                Assert.True(ReferenceEquals(card, shelf.Cards[1]));
                shelf.Apply([]);
                Assert.Equal(1, shelf.Cards.Count);
                Assert.True(ReferenceEquals(card, shelf.Cards[0]));
                shelf.Clear();
                Assert.Equal(0, shelf.Cards.Count);
            }));
        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin, MediaServerKind.FnNas })
            Test("回退卡片展示：保留服务器原名与单集原文 " + kind, () => CardImageContext.Run(async () =>
            {
                await using var fixture = new CardImageFixture(false, kind);
                var item = fixture.Scope.Bind(new EmbyItem
                {
                    Id = "episode",
                    Type = EmbyItemType.Episode,
                    Name = "延康双雄 (2021) {tmdbid=123}",
                    SeriesName = "牧神记",
                    ProductionYear = 2021
                });
                var card = new CardItem(item, fixture.Images, 170, centeredText: kind == MediaServerKind.FnNas);
                Assert.Equal(item.Name, card.Title);
                Assert.Equal(item.CardSubtitle, card.Subtitle);
                Assert.Equal(kind == MediaServerKind.FnNas, card.CenteredText);
                Assert.True(ReferenceEquals(item, card.Item));
            }));
        Test("回退卡片展示：恢复单行标题与图标缺图占位", () =>
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) directory = directory.Parent;
            Assert.NotNull(directory);
            var card = XDocument.Load(Path.Combine(directory!.FullName, "src/Momoka.Shell/Views/PosterCard.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var title = card.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "TitleLine");
            Assert.Equal("1", (string?)title.Attribute("MaxLines"));
            Assert.Null(title.Attribute("Height"));
            Assert.False(card.Descendants().Any(element => (string?)element.Attribute(x + "Name") == "PlaceholderTitle"));
        });
        Test("轮播媒体库：三卡宽度、透明标题且没有底部遮罩或模糊", () =>
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) directory = directory.Parent;
            Assert.NotNull(directory);
            var card = XDocument.Load(Path.Combine(directory!.FullName, "src/Momoka.Shell/Views/PosterCard.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var surface = card.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "CaptionSurface");
            Assert.Null(surface.Attribute("Height"));
            Assert.Equal("Transparent", (string?)surface.Attribute("Background"));
            Assert.True(surface.Descendants().Any(element => (string?)element.Attribute(x + "Name") == "TitleLine"));
            var states = card.Descendants().Where(element => element.Name.LocalName == "VisualState").ToArray();
            var plain = states.Single(element => (string?)element.Attribute(x + "Name") == "CaptionPlain");
            Assert.False(plain.Descendants().Any());
            var scrim = states.Single(element => (string?)element.Attribute(x + "Name") == "CaptionOnScrim");
            Assert.False(scrim.Descendants().Any(element => ((string?)element.Attribute("Target"))?.StartsWith("CaptionSurface.", StringComparison.Ordinal) == true));
            var home = XDocument.Load(Path.Combine(directory.FullName, "src/Momoka.Shell/Views/HomePage.xaml"));
            var overlay = home.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "LibraryOverlay");
            Assert.Equal("660", (string?)overlay.Attribute("MaxWidth"));
            Assert.False(overlay.Descendants().Any(element => ((string?)element.Attribute(x + "Name"))?.StartsWith("LibraryReading", StringComparison.Ordinal) == true));
            Assert.True(overlay.Elements().Any(element => (string?)element.Attribute(x + "Name") == "LibraryHost"));
            var palette = XDocument.Load(Path.Combine(directory.FullName, "src/Momoka.Shell/Theme/Palette.xaml"));
            var contrast = palette.Descendants().Single(element => (string?)element.Attribute(x + "Key") == "HighContrast");
            foreach (var (key, color) in new[]
            {
                ("EgCardCaptionForegroundBrush", "SystemColorWindowTextColor"),
                ("EgCardCaptionSecondaryBrush", "SystemColorWindowTextColor")
            })
            {
                var brush = contrast.Descendants().Single(element => (string?)element.Attribute(x + "Key") == key);
                Assert.Equal("{ThemeResource " + color + "}", (string?)brush.Attribute("Color"));
                Assert.False(palette.Root!.Elements().Any(element => (string?)element.Attribute(x + "Key") == key),
                    "顶层同名资源会遮住高对比度字典");
            }
        });
    }
}
