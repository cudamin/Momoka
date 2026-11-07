using System.Xml.Linq;
using Momoka.Emby;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class CardPresentationTests
{
    internal static void Register()
    {
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
