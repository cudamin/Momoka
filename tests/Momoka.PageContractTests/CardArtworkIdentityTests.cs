using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Momoka.Shell.Media;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class CardArtworkIdentityTests
{
    internal static void Register()
    {
        Test("实际封面引用：下载Primary后记录，释放图后不可用，热缓存恢复原引用", () => CardImageContext.Run(async () =>
        {
            await using var fixture = new CardImageFixture();
            Assert.Null(fixture.Card.LoadedPosterArtwork);
            var load = fixture.Card.EnsurePosterAsync();
            (await fixture.Handler.AtAsync(0)).Complete("primary");
            await load;
            var expected = new ArtworkRef(fixture.Item.Id, EmbyImageStore.Primary, "image-tag");
            Assert.Equal(expected, fixture.Card.LoadedPosterArtwork!.Value);
            fixture.Card.AbandonPoster();
            Assert.Null(fixture.Card.LoadedPosterArtwork);
            await fixture.Card.EnsurePosterAsync();
            Assert.Equal(expected, fixture.Card.LoadedPosterArtwork!.Value);
            Assert.Equal(1, fixture.Handler.Count);
            fixture.Card.AbandonPoster();
        }));
        Test("实际封面引用：全新卡缓存命中同步记录Primary而不是未知", () => CardImageContext.Run(async () =>
        {
            await using var fixture = new CardImageFixture();
            PosterCache.Remember(fixture.Key(170), new BitmapImage { FixtureImage = "cached primary" });
            await fixture.Card.EnsurePosterAsync();
            Assert.Equal(new ArtworkRef(fixture.Item.Id, EmbyImageStore.Primary, "image-tag"), fixture.Card.LoadedPosterArtwork!.Value);
            Assert.Equal(0, fixture.Handler.Count);
            fixture.Card.AbandonPoster();
        }));
        Test("实际封面引用：宽卡Thumb确实成功时记录Thumb，不冒充可用Primary", () => CardImageContext.Run(async () =>
        {
            await using var fixture = new CardImageFixture();
            fixture.Item.ImageTags[EmbyImageStore.Thumb] = "thumb-tag";
            var card = new CardItem(fixture.Item, fixture.Images, 170, wide: true);
            var load = card.EnsurePosterAsync();
            (await fixture.Handler.AtAsync(0)).Complete("thumb");
            await load;
            Assert.Equal(new ArtworkRef(fixture.Item.Id, EmbyImageStore.Thumb, "thumb-tag"), card.LoadedPosterArtwork!.Value);
            card.AbandonPoster();
        }));
        Test("实际封面引用：Primary失败后Thumb回退明确记录，不按首选候选猜", () => CardImageContext.Run(async () =>
        {
            await using var fixture = new CardImageFixture();
            fixture.Item.ImageTags[EmbyImageStore.Thumb] = "thumb-tag";
            var load = fixture.Card.EnsurePosterAsync();
            (await fixture.Handler.AtAsync(0)).Missing();
            (await fixture.Handler.AtAsync(1)).Complete("fallback thumb");
            await load;
            Assert.Equal(new ArtworkRef(fixture.Item.Id, EmbyImageStore.Thumb, "thumb-tag"), fixture.Card.LoadedPosterArtwork!.Value);
            fixture.Card.AbandonPoster();
        }));
        Test("实际封面引用：继续观看宽卡先要剧集缩略图并记录真实引用", () => CardImageContext.Run(async () =>
        {
            await using var fixture = new CardImageFixture();
            fixture.Item.Type = EmbyItemType.Episode;
            fixture.Item.SeriesId = "series-thumb-item";
            fixture.Item.SeriesThumbImageTag = "series-thumb-tag";
            var card = new CardItem(fixture.Item, fixture.Images, 256, wide: true, preferShowThumb: true);
            var load = card.EnsurePosterAsync();
            (await fixture.Handler.AtAsync(0)).Complete("series thumb");
            await load;
            Assert.Equal(new ArtworkRef("series-thumb-item", EmbyImageStore.Thumb, "series-thumb-tag"),
                card.LoadedPosterArtwork!.Value);
            Assert.Equal(1, fixture.Handler.Count);
            card.AbandonPoster();
        }));
        Test("实际封面引用：剧集没有缩略图时继续观看宽卡退回自己的图", () => CardImageContext.Run(async () =>
        {
            await using var fixture = new CardImageFixture();
            fixture.Item.Type = EmbyItemType.Episode;
            var card = new CardItem(fixture.Item, fixture.Images, 256, wide: true, preferShowThumb: true);
            var load = card.EnsurePosterAsync();
            (await fixture.Handler.AtAsync(0)).Complete("own primary");
            await load;
            Assert.Equal(new ArtworkRef(fixture.Item.Id, EmbyImageStore.Primary, "image-tag"),
                card.LoadedPosterArtwork!.Value);
            card.AbandonPoster();
        }));
        Test("实际封面引用：继续观看货架把缩略图开关交给卡片", () => CardImageContext.Run(async () =>
        {
            await using var fixture = new CardImageFixture();
            fixture.Item.Type = EmbyItemType.Episode;
            fixture.Item.SeriesId = "series-thumb-item";
            fixture.Item.SeriesThumbImageTag = "series-thumb-tag";
            var shelf = new CardShelf("继续观看", fixture.Images, 256, wide: true, preferShowThumb: true);
            var card = shelf.NewCard(fixture.Item);
            var load = card.EnsurePosterAsync();
            (await fixture.Handler.AtAsync(0)).Complete("series thumb");
            await load;
            Assert.Equal(new ArtworkRef("series-thumb-item", EmbyImageStore.Thumb, "series-thumb-tag"),
                card.LoadedPosterArtwork!.Value);
            Assert.Equal(1, fixture.Handler.Count);
            card.AbandonPoster();
        }));
    }
}
