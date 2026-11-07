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
        foreach (var firstMissing in new[] { true, false })
            Test($"R16-003：首选图{(firstMissing ? "404" : "坏图")}后继续备用，返回容器复用成功图", () => CardImageContext.Run(async () =>
            {
                await using var fixture = new CardImageFixture();
                fixture.Item.ImageTags[EmbyImageStore.Thumb] = "thumb-tag";
                var card = new CardItem(fixture.Item, fixture.Images, 170, imageTypes: [EmbyImageStore.Thumb, EmbyImageStore.Primary]);
                if (!firstMissing) PosterLoader.ErrorFor = value => value == "bad first" ? new InvalidDataException("bad image") : null;
                var load = card.EnsurePosterAsync();
                var first = await fixture.Handler.AtAsync(0);
                if (firstMissing) first.Missing();
                else
                {
                    first.Complete("bad first");
                    (await fixture.Handler.AtAsync(1)).Complete("bad first");
                }
                (await fixture.Handler.AtAsync(firstMissing ? 1 : 2)).Complete("good fallback");
                await load;
                Assert.Equal("good fallback", card.Poster!.FixtureImage);
                Assert.Equal(firstMissing ? 2 : 3, fixture.Handler.Count);
                var holder = new object();
                card.ClaimCard(holder);
                card.ReleasePoster(holder, false);
                await card.EnsurePosterAsync();
                Assert.Equal("good fallback", card.Poster!.FixtureImage);
                Assert.Equal(firstMissing ? 2 : 3, fixture.Handler.Count);
                card.AbandonPoster();
            }));

        foreach (var badFirst in new[] { false, true })
            Test($"R16-003：全部候选失败时只将明确全404记为永久缺图 {badFirst}", () => CardImageContext.Run(async () =>
            {
                await using var fixture = new CardImageFixture();
                fixture.Item.ImageTags[EmbyImageStore.Thumb] = "thumb-tag";
                var card = new CardItem(fixture.Item, fixture.Images, 170, imageTypes: [EmbyImageStore.Thumb, EmbyImageStore.Primary]);
                if (badFirst) PosterLoader.ErrorFor = _ => new InvalidDataException("bad image");
                var load = card.EnsurePosterAsync();
                if (badFirst)
                {
                    (await fixture.Handler.AtAsync(0)).Complete("bad first");
                    (await fixture.Handler.AtAsync(1)).Complete("bad first");
                }
                else (await fixture.Handler.AtAsync(0)).Missing();
                (await fixture.Handler.AtAsync(badFirst ? 2 : 1)).Missing();
                await load;
                Assert.Null(card.Poster);
                var missing = (bool)typeof(CardItem).GetField("_missing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(card)!;
                Assert.Equal(!badFirst, missing);
                var requests = fixture.Handler.Count;
                await card.EnsurePosterAsync();
                Assert.Equal(requests, fixture.Handler.Count, "明确缺图不重试；暂态失败尚在冷却也不重试");
                card.AbandonPoster();
            }));

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
