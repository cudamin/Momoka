#if CARD_IMAGE_RESIZE_FIXTURE
using Momoka.Shell.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// Build with CARD_IMAGE_RESIZE_FIXTURE and compile-link real CardItem.cs / PosterCache.cs,
/// current Core and TestHarness.cs. The checked-in PageContractTests project runs these cases with
/// fake WinUI value/bitmap boundaries; its entry registers this suite rather than using the standalone
/// Main below. Ordinary Core tests do not register this conditional suite.
/// </summary>
internal static class CardImageResizeTests
{
    public static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("R-012: compile-linked production CardItem.cs and PosterCache.cs; windowless fake image boundaries.");
        Register();
        return TestHarness.Run();
    }

    public static void Register()
    {
        Test("卡片夹具：完成的失败任务仍传播原异常，不因去同步等待而洗绿", () =>
        {
            var expected = new InvalidOperationException("synthetic fixture failure");
            var actual = Assert.Catch<InvalidOperationException>(() => CardImageContext.Run(() => Task.FromException(expected)));
            Assert.True(ReferenceEquals(expected, actual));
        });
        Test("卡片夹具：完成的取消任务不能被计为成功", () =>
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.Throws<TaskCanceledException>(() => CardImageContext.Run(() => Task.FromCanceled(canceled.Token)));
        });

        Case("R-012：可见首图在飞时换宽自动重取，旧下载先成功不能回填", async () =>
        {
            await using var fixture = new CardImageFixture();
            var card = fixture.Card;
            var item = card.Item;
            var cells = card.Cells;
            var initial = fixture.Begin();
            var old = await fixture.Handler.AtAsync(0);
            var oldToken = fixture.LoadingToken!.Value;
            Assert.Equal(400, old.Width);
            Assert.Null(card.Poster);

            card.Resize(250);
            Assert.True(oldToken.IsCancellationRequested);
            var current = await fixture.Handler.AtAsync(1);
            Assert.Equal(560, current.Width, "请求宽度沿用实际 RequestWidth 阶梯");
            await fixture.DeliverAsync(old, "old-width");
            await initial;
            Assert.Null(card.Poster, "旧下载成功或进磁盘缓存都不能回填卡片");
            Assert.Equal(0, PosterLoader.Calls.Count);

            await fixture.DeliverAsync(current, "current-width");
            await PosterAsync(fixture, 250, "current-width");
            Assert.True(ReferenceEquals(card, fixture.Card));
            Assert.True(ReferenceEquals(item, card.Item));
            Assert.True(ReferenceEquals(cells, card.Cells), "原地卡片和格对象保持稳定，不靠换页/换卡");
            Assert.True(fixture.Changes.Contains(nameof(card.CardWidth)));
            Assert.True(fixture.Changes.Contains(nameof(card.PosterHeight)));
            Assert.True(fixture.Changes.Contains(nameof(card.Poster)));
            Assert.False(PosterCache.TryGet(fixture.Key(170), out _));
            Assert.True(PosterCache.TryGet(fixture.Key(250), out var cached));
            Assert.True(ReferenceEquals(card.Poster, cached));
        });

        Case("R-012：新图已填回后旧下载迟到不覆盖或污染解码缓存", async () =>
        {
            await using var fixture = new CardImageFixture();
            var initial = fixture.Begin();
            var old = await fixture.Handler.AtAsync(0);
            fixture.Card.Resize(250);
            var current = await fixture.Handler.AtAsync(1);
            await fixture.DeliverAsync(current, "new-first");
            await PosterAsync(fixture, 250, "new-first");
            var poster = fixture.Card.Poster;

            await fixture.DeliverAsync(old, "old-last");
            await initial;
            Assert.True(ReferenceEquals(poster, fixture.Card.Poster));
            Assert.Equal(1, PosterLoader.Calls.Count);
            Assert.False(PosterCache.TryGet(fixture.Key(170), out _));
        });

        Case("R-012：旧解码在飞时换宽，旧解码成功也不得回填", async () =>
        {
            await using var fixture = new CardImageFixture();
            PosterLoader.Hold = true;
            var initial = fixture.Begin();
            await fixture.DeliverAsync(await fixture.Handler.AtAsync(0), "old-decode");
            await CardImageFixture.UntilAsync(() => PosterLoader.Calls.Count == 1, "old decode did not start");
            var old = PosterLoader.Calls[0];
            Assert.Equal(170, old.Width);

            fixture.Card.Resize(250);
            await fixture.DeliverAsync(await fixture.Handler.AtAsync(1), "new-decode");
            await CardImageFixture.UntilAsync(() => PosterLoader.Calls.Count == 2, "new decode did not start");
            var current = PosterLoader.Calls[1];
            Assert.Equal(250, current.Width);
            old.Complete();
            await initial;
            Assert.Null(fixture.Card.Poster);
            Assert.False(PosterCache.TryGet(fixture.Key(170), out _));
            current.Complete();
            await PosterAsync(fixture, 250, "new-decode");
        });

        Case("R-012：反复原地换宽撤销每个旧代次，只有最后宽度填回", async () =>
        {
            await using var fixture = new CardImageFixture();
            var initial = fixture.Begin();
            var old = new List<CardImageRequest> { await fixture.Handler.AtAsync(0) };
            foreach (var width in new[] { 250, 330, 410 })
            {
                var token = fixture.LoadingToken!.Value;
                fixture.Card.Resize(width);
                Assert.True(token.IsCancellationRequested);
                old.Add(await fixture.Handler.AtAsync(old.Count));
            }
            Assert.Equal(4, fixture.Handler.Count);
            Assert.Equal(880, old[^1].Width);
            await fixture.DeliverAsync(old[^1], "last-width");
            await PosterAsync(fixture, 410, "last-width");
            var poster = fixture.Card.Poster;
            foreach (var request in old.Take(old.Count - 1).Reverse())
                await fixture.DeliverAsync(request, "obsolete-width");
            await initial;
            Assert.True(ReferenceEquals(poster, fixture.Card.Poster));
            Assert.Equal(1, PosterLoader.Calls.Count);
        });

        Case("R-012：宽度A→B→A可复用共享下载但旧代次不清除新等待", async () =>
        {
            await using var fixture = new CardImageFixture();
            var initial = fixture.Begin();
            var first = await fixture.Handler.AtAsync(0);
            fixture.Card.Resize(250);
            var middle = await fixture.Handler.AtAsync(1);
            fixture.Card.Resize(170);
            var currentToken = fixture.LoadingToken!.Value;
            await initial;
            Assert.False(currentToken.IsCancellationRequested);
            Assert.Equal<CancellationToken?>(currentToken, fixture.LoadingToken, "旧 finally 不能清除新 CTS");

            await fixture.DeliverAsync(first, "shared-A");
            await PosterAsync(fixture, 170, "shared-A");
            var poster = fixture.Card.Poster;
            await fixture.DeliverAsync(middle, "obsolete-B");
            Assert.True(ReferenceEquals(poster, fixture.Card.Poster));
            Assert.Equal(2, fixture.Handler.Count, "相同请求宽度仍合并下载，不另造网络策略");
            Assert.Equal(1, PosterLoader.Calls.Count);
            Assert.False(PosterCache.TryGet(fixture.Key(250), out _));
        });

        Case("R-012：已有图片换宽仍更新，旧解码不当作新宽度图片", async () =>
        {
            await using var fixture = new CardImageFixture();
            var initial = fixture.Begin();
            await fixture.DeliverAsync(await fixture.Handler.AtAsync(0), "initial-poster");
            await initial;
            await PosterAsync(fixture, 170, "initial-poster");
            var previous = fixture.Card.Poster;

            fixture.Card.Resize(250);
            Assert.Null(fixture.Card.Poster, "保留现有换宽时释放旧尺寸画面的行为");
            await fixture.DeliverAsync(await fixture.Handler.AtAsync(1), "resized-poster");
            await PosterAsync(fixture, 250, "resized-poster");
            Assert.False(ReferenceEquals(previous, fixture.Card.Poster));
            Assert.True(PosterCache.TryGet(fixture.Key(170), out var oldCached));
            Assert.True(ReferenceEquals(previous, oldCached));
        });

        Case("R-012：从未上屏的卡换宽不开请求，Loaded显式取图采用新宽度", async () =>
        {
            await using var fixture = new CardImageFixture();
            fixture.Card.Resize(250);
            Assert.Equal(0, fixture.Handler.Count);
            Assert.Null(fixture.LoadingToken);
            var loaded = fixture.Begin();
            var request = await fixture.Handler.AtAsync(0);
            Assert.Equal(560, request.Width);
            await fixture.DeliverAsync(request, "loaded-current");
            await loaded;
            await PosterAsync(fixture, 250, "loaded-current");
        });

        foreach (var keepWaiting in new[] { false, true })
            Case($"R-012：离屏释放 keepWaiting={keepWaiting} 后换宽不重启，Loaded后重取", async () =>
            {
                await using var fixture = new CardImageFixture();
                var initial = fixture.Begin();
                var old = await fixture.Handler.AtAsync(0);
                fixture.Card.ReleasePoster(fixture.Container, keepWaiting);
                fixture.Card.Resize(250);
                Assert.Equal(1, fixture.Handler.Count, "继续等旧结果不等于离屏后仍要开启新请求");
                Assert.Null(fixture.LoadingToken);
                await fixture.DeliverAsync(old, "offscreen-old");
                await initial;
                Assert.Null(fixture.Card.Poster);
                Assert.Equal(0, PosterLoader.Calls.Count);

                var reloaded = fixture.Begin();
                var current = await fixture.Handler.AtAsync(1);
                Assert.Equal(560, current.Width);
                await fixture.DeliverAsync(current, "visible-again");
                await reloaded;
                await PosterAsync(fixture, 250, "visible-again");
            });

        Case("R-012：同一容器重复离树和Loaded只Ensure，离屏换宽始终不发请求", async () =>
        {
            await using var fixture = new CardImageFixture();
            var initial = fixture.Begin();
            await fixture.DeliverAsync(await fixture.Handler.AtAsync(0), "first-visible");
            await initial;
            fixture.Card.ReleasePoster(fixture.Container);
            fixture.Card.Resize(250);
            Assert.Equal(1, fixture.Handler.Count);
            Assert.Null(fixture.Card.Poster);

            // PosterCard.OnLoaded only calls EnsurePosterAsync; it does not ClaimCard again.
            var reloaded = fixture.Ensure();
            await fixture.DeliverAsync(await fixture.Handler.AtAsync(1), "reloaded-visible");
            await reloaded;
            await PosterAsync(fixture, 250, "reloaded-visible");
            fixture.Card.ReleasePoster(fixture.Container);
            Assert.Null(fixture.Card.Poster, "临时离树后仍要认得同一持有者再次放手");
            fixture.Card.Resize(330);
            Assert.Equal(2, fixture.Handler.Count);
            Assert.Null(fixture.LoadingToken);

            var loadedAgain = fixture.Ensure();
            await fixture.DeliverAsync(await fixture.Handler.AtAsync(2), "second-reload");
            await loadedAgain;
            await PosterAsync(fixture, 330, "second-reload");
        });

        Case("R-012：暂离树不换宽仍继续等旧图，随后离屏换宽不开新请求", async () =>
        {
            await using var fixture = new CardImageFixture();
            var initial = fixture.Begin();
            var request = await fixture.Handler.AtAsync(0);
            var token = fixture.LoadingToken!.Value;
            fixture.Card.ReleasePoster(fixture.Container);
            Assert.False(token.IsCancellationRequested, "保留既有回收时继续等旧请求的契约");
            await fixture.DeliverAsync(request, "kept-waiting");
            await initial;
            await PosterAsync(fixture, 170, "kept-waiting");
            fixture.Card.Resize(250);
            Assert.Equal(1, fixture.Handler.Count);
            Assert.Null(fixture.Card.Poster);
            Assert.Null(fixture.LoadingToken);
        });

        Case("R-012：同下载宽度阶梯也按新逻辑宽度解码和缓存", async () =>
        {
            await using var fixture = new CardImageFixture();
            var initial = fixture.Begin();
            var shared = await fixture.Handler.AtAsync(0);
            var oldToken = fixture.LoadingToken!.Value;
            Assert.Equal(400, shared.Width);
            fixture.Card.Resize(200);
            Assert.True(oldToken.IsCancellationRequested);
            Assert.NotNull(fixture.LoadingToken);
            await fixture.DeliverAsync(shared, "same-step");
            await initial;
            await PosterAsync(fixture, 200, "same-step");
            Assert.Equal(1, fixture.Handler.Count, "170与200共用400宽下载，不共用解码位图");
            Assert.Equal(1, PosterLoader.Calls.Count);
            Assert.Equal(200, PosterLoader.Calls[0].Width);
            Assert.False(PosterCache.TryGet(fixture.Key(170), out _));
            Assert.True(PosterCache.TryGet(fixture.Key(200), out var cached));
            Assert.True(ReferenceEquals(cached, fixture.Card.Poster));
        });

        Case("R-012：旧容器释放不能关闭现持有者的换宽重取", async () =>
        {
            await using var fixture = new CardImageFixture();
            var initial = fixture.Begin();
            var old = await fixture.Handler.AtAsync(0);
            var actualHolder = new object();
            fixture.Card.ClaimCard(actualHolder);
            fixture.Card.ReleasePoster(fixture.Container, keepWaiting: false);
            fixture.Card.Resize(250);
            await fixture.DeliverAsync(await fixture.Handler.AtAsync(1), "current-holder");
            await PosterAsync(fixture, 250, "current-holder");
            await fixture.DeliverAsync(old, "previous-holder");
            await initial;
            Assert.Equal("current-holder", fixture.Card.Poster!.FixtureImage);
            GC.KeepAlive(actualHolder);
        });

        Case("R-012：同宽和非法宽度不取消有效等待或重发请求", async () =>
        {
            await using var fixture = new CardImageFixture();
            var initial = fixture.Begin();
            var old = await fixture.Handler.AtAsync(0);
            var token = fixture.LoadingToken!.Value;
            fixture.Card.Resize(170);
            fixture.Card.Resize(0);
            fixture.Card.Resize(-1);
            Assert.False(token.IsCancellationRequested);
            Assert.Equal(1, fixture.Handler.Count);
            Assert.Equal(170d, fixture.Card.CardWidth);
            Assert.Equal(0, fixture.Changes.Count);
            await fixture.DeliverAsync(old, "unchanged");
            await initial;
            await PosterAsync(fixture, 170, "unchanged");
        });

        Case("R-012：没有图片的卡换宽仍不开请求", async () =>
        {
            await using var fixture = new CardImageFixture(artwork: false);
            await fixture.Begin();
            fixture.Card.Resize(250);
            await fixture.Ensure();
            Assert.Equal(0, fixture.Handler.Count);
            Assert.Null(fixture.Card.Poster);
        });

        Case("R-012：服务器明确缺图后换宽保留终局答案，不无条件重试", async () =>
        {
            await using var fixture = new CardImageFixture();
            var initial = fixture.Begin();
            (await fixture.Handler.AtAsync(0)).Missing();
            await initial;
            fixture.Card.Resize(250);
            await fixture.Ensure();
            Assert.Equal(1, fixture.Handler.Count);
            Assert.Null(fixture.Card.Poster);
            Assert.Equal(0, PosterLoader.Calls.Count);
        });

        Case("R-012：离页取消首图后换宽不复活，旧下载成功只留字节缓存", async () =>
        {
            await using var fixture = new CardImageFixture();
            var initial = fixture.Begin();
            var old = await fixture.Handler.AtAsync(0);
            var token = fixture.LoadingToken!.Value;
            fixture.Card.AbandonPoster();
            fixture.Card.Resize(250);
            Assert.True(token.IsCancellationRequested);
            Assert.Equal(1, fixture.Handler.Count);
            await fixture.DeliverAsync(old, "abandoned");
            await initial;
            Assert.Null(fixture.Card.Poster);
            Assert.Equal(0, PosterLoader.Calls.Count);
        });

        Case("R-012：换宽后离页取消新旧等待，迟到结果不填图", async () =>
        {
            await using var fixture = new CardImageFixture();
            var initial = fixture.Begin();
            var old = await fixture.Handler.AtAsync(0);
            fixture.Card.Resize(250);
            var current = await fixture.Handler.AtAsync(1);
            var token = fixture.LoadingToken!.Value;
            fixture.Card.AbandonPoster();
            Assert.True(token.IsCancellationRequested);
            await fixture.DeliverAsync(current, "cancelled-new");
            await fixture.DeliverAsync(old, "cancelled-old");
            await initial;
            fixture.Card.Resize(330);
            Assert.Equal(2, fixture.Handler.Count);
            Assert.Null(fixture.Card.Poster);
            Assert.Equal(0, PosterLoader.Calls.Count);
        });

        Case("R-012：来源寿命撤销后换宽不请求，迟到新旧结果不解码", async () =>
        {
            await using var fixture = new CardImageFixture();
            var initial = fixture.Begin();
            var old = await fixture.Handler.AtAsync(0);
            fixture.Card.Resize(250);
            var current = await fixture.Handler.AtAsync(1);
            fixture.RevokeSource();
            current.Complete("revoked-new");
            old.Complete("revoked-old");
            await initial;
            await CardImageFixture.UntilAsync(() => fixture.LoadingToken is null, "revoked card load did not finish");
            fixture.Card.Resize(330);
            await fixture.Ensure();
            Assert.Equal(2, fixture.Handler.Count);
            Assert.Null(fixture.Card.Poster);
            Assert.Equal(0, PosterLoader.Calls.Count);
        });

        Case("R-012：新解码宽度缓存已存在时同步取回，取消旧请求不另下载", async () =>
        {
            await using var fixture = new CardImageFixture();
            var kept = new BitmapImage { DecodePixelWidth = 250, FixtureImage = "cached-current" };
            PosterCache.Remember(fixture.Key(250), kept);
            var initial = fixture.Begin();
            var old = await fixture.Handler.AtAsync(0);
            fixture.Card.Resize(250);
            Assert.True(ReferenceEquals(kept, fixture.Card.Poster));
            Assert.Equal(1, fixture.Handler.Count);
            await fixture.DeliverAsync(old, "obsolete-cached-wait");
            await initial;
            Assert.True(ReferenceEquals(kept, fixture.Card.Poster));
            Assert.Equal(0, PosterLoader.Calls.Count);
        });
    }

    private static Task PosterAsync(CardImageFixture fixture, int width, string image) =>
        CardImageFixture.UntilAsync(() => fixture.Card.Poster is { } poster
            && poster.DecodePixelWidth == width && poster.FixtureImage == image, $"Expected poster {image} decoded at {width}");

    private static void Case(string name, Func<Task> run) => Test(name, () => CardImageContext.Run(run));
}
#endif
