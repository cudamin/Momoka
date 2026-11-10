using System.Xml.Linq;
using Momoka.Emby;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class CardPresentationTests
{
    internal static void Register()
    {
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
    }
}
