using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Momoka.Shell.Media;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class CardArtworkRecoveryTests
{
    internal static void Register()
    {
        foreach (var type in new[] { EmbyItemType.Folder, EmbyItemType.CollectionFolder })
            foreach (var keepWaiting in new[] { false, true })
                Test($"R16-004：{type} 在飞后缓存命中遵守等待所有权 {keepWaiting}", () => CardImageContext.Run(async () =>
                {
                    await using var fixture = new CardImageFixture(false);
                    fixture.Item.Type = type;
                    fixture.Item.PosterPaths = ["tile-zero", "tile-one"];
                    var key = PosterCache.Key(fixture.Scope.Connection, fixture.Item.Id, EmbyImageStore.Primary, "tile-one", 170);
                    PosterCache.Remember(key, new BitmapImage { PixelWidth = 170, PixelHeight = 170, FixtureImage = "cached tile" });
                    var load = fixture.Begin();
                    var first = await fixture.Handler.AtAsync(0);
                    fixture.Card.ReleasePoster(fixture.Container, keepWaiting);
                    first.Complete("downloaded tile");
                    await load;
                    var tile0 = type == EmbyItemType.Folder ? fixture.Card.FolderTile0 : fixture.Card.LibraryTile0;
                    var tile1 = type == EmbyItemType.Folder ? fixture.Card.FolderTile1 : fixture.Card.LibraryTile1;
                    Assert.Equal(keepWaiting, tile0 is not null);
                    Assert.Equal(keepWaiting, tile1 is not null);
                    Assert.Equal(1, fixture.Handler.Count);
                    if (!keepWaiting)
                    {
                        await fixture.Card.EnsurePosterAsync();
                        Assert.NotNull(type == EmbyItemType.Folder ? fixture.Card.FolderTile1 : fixture.Card.LibraryTile1);
                        Assert.Equal(1, fixture.Handler.Count);
                    }
                }));
    }
}
