using System.Text;
using Momoka.Emby;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class NavigationArtworkTests
{
    internal static void Register()
    {
        foreach (var kind in Enum.GetValues<MediaServerKind>())
            Test("订阅入口封面独立于媒体服务器 " + kind, () => CardImageContext.Run(async () =>
            {
                await using var fixture = new CardImageFixture(false, kind);
                var reads = 0;
                var card = Create(fixture, _ => { reads++; return Task.FromResult(Encoding.UTF8.GetBytes("movie cover")); });
                await card.EnsurePosterAsync();
                Assert.Null(card.Scope);
                Assert.Null(card.Item.Source);
                Assert.Equal("movie cover", card.Poster!.FixtureImage);
                Assert.Equal(208, card.Poster.DecodePixelWidth);
                await card.EnsurePosterAsync();
                Assert.Equal(1, reads);
                Assert.Equal(0, fixture.Handler.Count);
            }));
        Test("订阅入口离页取消并拒绝迟到封面", () => CardImageContext.Run(async () =>
        {
            await using var fixture = new CardImageFixture(false);
            var pending = new TaskCompletionSource<byte[]>();
            CancellationToken captured = default;
            var card = Create(fixture, token => { captured = token; return pending.Task; });
            var load = card.EnsurePosterAsync();
            card.AbandonPoster();
            Assert.True(captured.IsCancellationRequested);
            pending.SetResult(Encoding.UTF8.GetBytes("old cover"));
            await load;
            Assert.Null(card.Poster);
        }));
        Test("订阅入口封面读取失败保留导航并遵守冷却", () => CardImageContext.Run(async () =>
        {
            await using var fixture = new CardImageFixture(false);
            var reads = 0;
            var card = Create(fixture, _ => { reads++; throw new HttpRequestException("fixture failure"); });
            await card.EnsurePosterAsync();
            await card.EnsurePosterAsync();
            Assert.Null(card.Poster);
            Assert.Equal(1, reads);
            Assert.True(card.IsNavigation);
        }));
    }

    private static CardItem Create(CardImageFixture fixture, Func<CancellationToken, Task<byte[]>> fetch)
    {
        var constructor = typeof(CardItem).GetConstructors().Single();
        var parameters = constructor.GetParameters();
        Assert.True(parameters.Any(parameter => parameter.Name == "fetchPoster"), "导航卡缺少独立封面读取入口");
        var args = parameters.Select(parameter => parameter.HasDefaultValue ? parameter.DefaultValue : null).ToArray();
        args[0] = new EmbyItem { Id = HomeLayout.MoviePilotMovies, Name = "MP 电影订阅", Type = EmbyItemType.CollectionFolder };
        args[1] = fixture.Images;
        args[2] = 208;
        args[Array.FindIndex(parameters, parameter => parameter.Name == "navigate")] = (Action)(() => { });
        args[Array.FindIndex(parameters, parameter => parameter.Name == "fetchPoster")] = fetch;
        return (CardItem)constructor.Invoke(args);
    }
}
