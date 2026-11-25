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
        Test("飞牛列表首图：目录拼图到达及释放会更新单图投影，普通海报不受影响", () =>
        {
            var item = new Momoka.Emby.EmbyItem { Id = "folder", Type = Momoka.Emby.EmbyItemType.Folder };
            var card = new Momoka.Shell.Views.CardItem(item, null!, 170, navigate: () => { });
            var property = card.GetType().GetProperty("RowPoster");
            Assert.True(property is not null, "列表行缺少目录封面首图投影");
            var changes = new List<string?>();
            card.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
            var bitmap = new BitmapImage();
            card.GetType().GetMethod("SetCollageTile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(card, [0, bitmap]);
            Assert.True(ReferenceEquals(bitmap, property!.GetValue(card)));
            Assert.True(changes.Contains("RowPoster"));
            card.GetType().GetMethod("ClearCollageTiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(card, null);
            Assert.Null(property.GetValue(card));
        });
        Test("本地库导航卡：原地更新标题数量并通知绑定", () =>
        {
            var method = typeof(Momoka.Shell.Views.CardItem).GetMethod("UpdateNavigationText", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(method, "首页实时更新不能仅换卡让可见容器保留旧数量");
            var card = new Momoka.Shell.Views.CardItem(new Momoka.Emby.EmbyItem { Id = "local-library:a", Name = "电影", Type = Momoka.Emby.EmbyItemType.CollectionFolder },
                null!, 200, subtitle: "本地 · 1 项", navigate: () => { });
            var changed = new List<string>();
            card.PropertyChanged += (_, args) => changed.Add(args.PropertyName ?? "");
            method!.Invoke(card, ["电影库", "本地 · 2 项"]);
            Assert.Equal("电影库", card.Title);
            Assert.Equal("本地 · 2 项", card.Subtitle);
            Assert.True(changed.Contains(nameof(card.Subtitle)));
        });
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
            var sentinel = fixture.KeepDownload(old);
            await CardImageFixture.UntilAsync(() => fixture.DownloadDemands == 2, "independent old-width demand did not join");
            var oldToken = fixture.LoadingToken!.Value;
            Assert.Equal(400, old.Width);
            Assert.Null(card.Poster);

            card.Resize(250);
            Assert.True(oldToken.IsCancellationRequested);
            var current = await fixture.Handler.AtAsync(1);
            Assert.Equal(560, current.Width, "请求宽度沿用实际 RequestWidth 阶梯");
            Assert.False(old.Token.IsCancellationRequested, "旁边有效需求继续下载，而旧卡片等待已取消");
            await fixture.DeliverAsync(old, "old-width");
            Assert.NotNull(await sentinel);
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
            var sentinel = fixture.KeepDownload(old);
            await CardImageFixture.UntilAsync(() => fixture.DownloadDemands == 2, "independent late-old demand did not join");
            fixture.Card.Resize(250);
            var current = await fixture.Handler.AtAsync(1);
            await fixture.DeliverAsync(current, "new-first");
            await PosterAsync(fixture, 250, "new-first");
            var poster = fixture.Card.Poster;

            Assert.False(old.Token.IsCancellationRequested);
            await fixture.DeliverAsync(old, "old-last");
            Assert.NotNull(await sentinel);
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

        Case("R-012：宽度A→B→A有独立有效需求时共享，旧代次不清除新等待", async () =>
        {
            await using var fixture = new CardImageFixture();
            var initial = fixture.Begin();
            var first = await fixture.Handler.AtAsync(0);
            var sentinel = fixture.KeepDownload(first);
            await CardImageFixture.UntilAsync(() => fixture.DownloadDemands == 2, "independent A demand did not join");
            fixture.Card.Resize(250);
            var middle = await fixture.Handler.AtAsync(1);
            fixture.Card.Resize(170);
            var currentToken = fixture.LoadingToken!.Value;
            await initial;
            Assert.False(currentToken.IsCancellationRequested);
            Assert.Equal<CancellationToken?>(currentToken, fixture.LoadingToken, "旧 finally 不能清除新 CTS");

            Assert.False(first.Token.IsCancellationRequested, "独立需求保活原A下载，不归首个卡片等待者所有");
            await fixture.DeliverAsync(first, "shared-A");
            Assert.NotNull(await sentinel);
            await PosterAsync(fixture, 170, "shared-A");
            var poster = fixture.Card.Poster;
            await fixture.DeliverAsync(middle, "obsolete-B");
            Assert.True(ReferenceEquals(poster, fixture.Card.Poster));
            Assert.Equal(2, fixture.Handler.Count, "相同请求宽度仍合并下载，不另造网络策略");
            Assert.Equal(1, PosterLoader.Calls.Count);
            Assert.False(PosterCache.TryGet(fixture.Key(250), out _));
        });

        Case("R-012：宽度A→B→A零需求撤销旧共享，自动新取A而旧响应不填回", async () =>
        {
            await using var fixture = new CardImageFixture();
            var initial = fixture.Begin();
            var oldA = await fixture.Handler.AtAsync(0);
            fixture.Card.Resize(250);
            var oldB = await fixture.Handler.AtAsync(1);
            fixture.Card.Resize(170);
            var currentA = await fixture.Handler.AtAsync(2);
            var currentToken = fixture.LoadingToken!.Value;
            Assert.Equal(400, currentA.Width, "零需求已撤销原A，回到A必须自动建立新需求");
            Assert.True(oldA.Token.IsCancellationRequested);
            Assert.True(oldB.Token.IsCancellationRequested);
            await fixture.DeliverAsync(oldA, "zero-demand-old-A");
            await fixture.DeliverAsync(oldB, "zero-demand-old-B");
            await initial;
            Assert.False(currentToken.IsCancellationRequested);
            Assert.Equal<CancellationToken?>(currentToken, fixture.LoadingToken, "旧finally不清新A等待");
            Assert.Equal(3, fixture.Handler.Count, "Deliver不能给取消的A或B再建GET");
            Assert.Null(fixture.Card.Poster);
            Assert.Equal(0, PosterLoader.Calls.Count);
            Assert.False(PosterCache.TryGet(fixture.Key(170), out _));
            Assert.False(PosterCache.TryGet(fixture.Key(250), out _));
            Assert.False(File.Exists(fixture.CacheFile(400)));
            await fixture.DeliverAsync(currentA, "fresh-A");
            await PosterAsync(fixture, 170, "fresh-A");
            Assert.Equal(1, PosterLoader.Calls.Count);
            Assert.True(PosterCache.TryGet(fixture.Key(170), out var cached));
            Assert.True(ReferenceEquals(cached, fixture.Card.Poster));
        });

        Case("R-012：已有图片换宽保持显示，新尺寸成功后原地替换", async () =>
        {
            await using var fixture = new CardImageFixture();
            var initial = fixture.Begin();
            await fixture.DeliverAsync(await fixture.Handler.AtAsync(0), "initial-poster");
            await initial;
            await PosterAsync(fixture, 170, "initial-poster");
            var previous = fixture.Card.Poster;
            var sawEmpty = false;
            fixture.Card.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(fixture.Card.Poster) && fixture.Card.Poster is null) sawEmpty = true;
            };

            fixture.Card.Resize(250);
            Assert.True(ReferenceEquals(previous, fixture.Card.Poster), "换宽等图期间不能清空已显示封面");
            Assert.Equal(250d, fixture.Card.CardWidth);
            Assert.Equal(375d, fixture.Card.PosterHeight);
            Assert.False(fixture.Card.ImagePending, "已有画面时不闪等待占位");
            await fixture.DeliverAsync(await fixture.Handler.AtAsync(1), "resized-poster");
            await PosterAsync(fixture, 250, "resized-poster");
            Assert.False(sawEmpty, "整次换宽不能发出空封面通知");
            Assert.False(ReferenceEquals(previous, fixture.Card.Poster));
            Assert.True(PosterCache.TryGet(fixture.Key(170), out var oldCached));
            Assert.True(ReferenceEquals(previous, oldCached));
        });

        Case("封面自适应：连续几何变化保留旧图，停下只取最终尺寸", async () =>
        {
            await using var fixture = new CardImageFixture();
            var previous = await ReadyPosterAsync(fixture);
            foreach (var width in new[] { 180, 200, 230, 250 })
            {
                fixture.Card.Resize(width, refreshImage: false);
                Assert.Equal((double)width, fixture.Card.CardWidth);
                Assert.True(ReferenceEquals(previous, fixture.Card.Poster));
                Assert.Null(fixture.LoadingToken);
            }
            Assert.Equal(1, fixture.Handler.Count, "拖动几何更新不能逐帧取图");
            fixture.Card.RefreshPosterSize();
            var final = await fixture.Handler.AtAsync(1);
            Assert.Equal(560, final.Width);
            fixture.Card.RefreshPosterSize();
            Assert.Equal(2, fixture.Handler.Count, "防抖补图和重复通知共用当前请求");
            await fixture.DeliverAsync(final, "settled-size");
            await PosterAsync(fixture, 250, "settled-size");
            fixture.Card.RefreshPosterSize();
            Assert.Equal(2, fixture.Handler.Count, "目标尺寸已到后不重复取图");
        });

        Case("封面自适应：首图在途时几何更新取消旧代次，落定后才重取", async () =>
        {
            await using var fixture = new CardImageFixture();
            var initial = fixture.Begin();
            var old = await fixture.Handler.AtAsync(0);
            var token = fixture.LoadingToken!.Value;
            fixture.Card.Resize(250, refreshImage: false);
            Assert.True(token.IsCancellationRequested);
            Assert.Null(fixture.LoadingToken);
            Assert.Equal(1, fixture.Handler.Count);
            await fixture.DeliverAsync(old, "obsolete-first");
            await initial;
            Assert.Null(fixture.Card.Poster);
            Assert.Equal(0, PosterLoader.Calls.Count);
            fixture.Card.RefreshPosterSize();
            await fixture.DeliverAsync(await fixture.Handler.AtAsync(1), "settled-first");
            await PosterAsync(fixture, 250, "settled-first");
        });

        Case("封面自适应：已有图容器重复Ensure不能绕过停拖防抖", async () =>
        {
            await using var fixture = new CardImageFixture();
            var previous = await ReadyPosterAsync(fixture);
            fixture.Card.Resize(250, refreshImage: false);
            await fixture.Ensure();
            fixture.Card.Resize(330, refreshImage: false);
            await fixture.Ensure();
            Assert.True(ReferenceEquals(previous, fixture.Card.Poster));
            Assert.Equal(1, fixture.Handler.Count, "Loaded或容器重新赋卡不能提前补旧封面的清晰度");
            Assert.Null(fixture.LoadingToken);
            fixture.Card.RefreshPosterSize();
            await fixture.DeliverAsync(await fixture.Handler.AtAsync(1), "after-container-reentry");
            await PosterAsync(fixture, 330, "after-container-reentry");
        });

        Case("封面自适应：首张没有图片时新容器Ensure仍可立即加载", async () =>
        {
            await using var fixture = new CardImageFixture();
            fixture.Card.Resize(250, refreshImage: false);
            var loaded = fixture.Begin();
            var request = await fixture.Handler.AtAsync(0);
            Assert.Equal(560, request.Width);
            await fixture.DeliverAsync(request, "first-visible-without-delay");
            await loaded;
            await PosterAsync(fixture, 250, "first-visible-without-delay");
        });

        Case("封面自适应：新尺寸解码失败保留旧图并遵守失败冷却", async () =>
        {
            await using var fixture = new CardImageFixture();
            var previous = await ReadyPosterAsync(fixture);
            PosterLoader.ErrorFor = _ => new IOException("synthetic replacement decode failure");
            fixture.Card.Resize(250);
            await fixture.DeliverAsync(await fixture.Handler.AtAsync(1), "failed-replacement");
            await CardImageFixture.UntilAsync(() => fixture.LoadingToken is null, "replacement failure did not settle");
            Assert.True(ReferenceEquals(previous, fixture.Card.Poster));
            Assert.False(fixture.Card.ImagePending);
            Assert.False(PosterCache.TryGet(fixture.Key(250), out _));
            fixture.Card.RefreshPosterSize();
            Assert.Equal(2, fixture.Handler.Count, "失败冷却不能因旧图存在而失效");
        });

        Case("封面自适应：新尺寸明确缺图仍保留旧图，不反复请求缺图", async () =>
        {
            await using var fixture = new CardImageFixture();
            var previous = await ReadyPosterAsync(fixture);
            fixture.Card.Resize(250);
            (await fixture.Handler.AtAsync(1)).Missing();
            await CardImageFixture.UntilAsync(() => fixture.LoadingToken is null, "missing replacement did not settle");
            Assert.True(ReferenceEquals(previous, fixture.Card.Poster));
            fixture.Card.RefreshPosterSize();
            Assert.Equal(2, fixture.Handler.Count);
        });

        Case("封面自适应：保留旧图不阻止目标尺寸热缓存同步替换", async () =>
        {
            await using var fixture = new CardImageFixture();
            await ReadyPosterAsync(fixture);
            var cached = new BitmapImage { DecodePixelWidth = 250, FixtureImage = "cached replacement" };
            PosterCache.Remember(fixture.Key(250), cached);
            fixture.Card.Resize(250, refreshImage: false);
            fixture.Card.RefreshPosterSize();
            Assert.True(ReferenceEquals(cached, fixture.Card.Poster));
            Assert.Null(fixture.LoadingToken);
            Assert.Equal(1, fixture.Handler.Count);
        });

        Case("封面自适应：高清补图再次被拖动取消，旧图持续到最终成功", async () =>
        {
            await using var fixture = new CardImageFixture();
            var previous = await ReadyPosterAsync(fixture);
            PosterLoader.Hold = true;
            fixture.Card.Resize(250);
            await fixture.DeliverAsync(await fixture.Handler.AtAsync(1), "middle-decode");
            await CardImageFixture.UntilAsync(() => PosterLoader.Calls.Count == 2, "middle decode not started");
            fixture.Card.Resize(330, refreshImage: false);
            PosterLoader.Calls[1].Complete();
            await CardImageFixture.UntilAsync(() => fixture.DownloadRunning == 0, "obsolete replacement did not leave");
            Assert.True(ReferenceEquals(previous, fixture.Card.Poster));
            Assert.False(PosterCache.TryGet(fixture.Key(250), out _));
            fixture.Card.RefreshPosterSize();
            await fixture.DeliverAsync(await fixture.Handler.AtAsync(2), "final-decode");
            await CardImageFixture.UntilAsync(() => PosterLoader.Calls.Count == 3, "final decode not started");
            PosterLoader.Calls[2].Complete();
            await PosterAsync(fixture, 330, "final-decode");
        });

        Case("封面自适应：拖宽再回原宽复用原图，不把显示宽度当解码宽度", async () =>
        {
            await using var fixture = new CardImageFixture();
            var previous = await ReadyPosterAsync(fixture);
            fixture.Card.Resize(250, refreshImage: false);
            fixture.Card.Resize(170, refreshImage: false);
            fixture.Card.RefreshPosterSize();
            Assert.True(ReferenceEquals(previous, fixture.Card.Poster));
            Assert.Equal(1, fixture.Handler.Count);
            Assert.Null(fixture.LoadingToken);
        });

        Case("封面自适应：保留图期间离屏立即释放，落定补图不能复活旧容器", async () =>
        {
            await using var fixture = new CardImageFixture();
            await ReadyPosterAsync(fixture);
            fixture.Card.Resize(250, refreshImage: false);
            fixture.Card.ReleasePoster(fixture.Container, keepWaiting: false);
            fixture.Card.RefreshPosterSize();
            Assert.Null(fixture.Card.Poster);
            Assert.Null(fixture.LoadingToken);
            Assert.Equal(1, fixture.Handler.Count);
            var reloaded = fixture.Begin();
            await fixture.DeliverAsync(await fixture.Handler.AtAsync(1), "new-container-size");
            await reloaded;
            await PosterAsync(fixture, 250, "new-container-size");
        });

        Case("封面自适应：目录拼图逐格保留，失败格留旧图并释放离屏画面", async () =>
        {
            await using var fixture = new CardImageFixture(artwork: false);
            fixture.Item.Type = Momoka.Emby.EmbyItemType.Folder;
            fixture.Item.PosterPaths = ["tile-a", "tile-b"];
            var initial = fixture.Begin();
            await fixture.DeliverAsync(await fixture.Handler.AtAsync(0), "old-a");
            await fixture.DeliverAsync(await fixture.Handler.AtAsync(1), "old-b");
            await initial;
            var old0 = fixture.Card.FolderTile0;
            var old1 = fixture.Card.FolderTile1;
            fixture.Card.Resize(250, refreshImage: false);
            Assert.True(ReferenceEquals(old0, fixture.Card.FolderTile0));
            Assert.True(ReferenceEquals(old1, fixture.Card.FolderTile1));
            fixture.Card.RefreshPosterSize();
            await fixture.DeliverAsync(await fixture.Handler.AtAsync(2), "new-a");
            var second = await fixture.Handler.AtAsync(3);
            Assert.Equal(250, fixture.Card.FolderTile0!.DecodePixelWidth);
            Assert.Equal("new-a", fixture.Card.FolderTile0.FixtureImage);
            Assert.True(ReferenceEquals(old1, fixture.Card.FolderTile1));
            PosterLoader.ErrorFor = _ => new IOException("synthetic tile failure");
            await fixture.DeliverAsync(second, "failed-b");
            await CardImageFixture.UntilAsync(() => fixture.LoadingToken is null, "tile replacement failure did not settle");
            Assert.True(ReferenceEquals(old1, fixture.Card.FolderTile1));
            fixture.Card.ReleasePoster(fixture.Container, keepWaiting: false);
            Assert.Null(fixture.Card.FolderTile0);
            Assert.Null(fixture.Card.FolderTile1);
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
            var sentinel = fixture.KeepDownload(shared);
            await CardImageFixture.UntilAsync(() => fixture.DownloadDemands == 2, "independent same-step demand did not join");
            var oldToken = fixture.LoadingToken!.Value;
            Assert.Equal(400, shared.Width);
            fixture.Card.Resize(200);
            Assert.True(oldToken.IsCancellationRequested);
            Assert.NotNull(fixture.LoadingToken);
            Assert.False(shared.Token.IsCancellationRequested);
            await fixture.DeliverAsync(shared, "same-step");
            Assert.NotNull(await sentinel);
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

            // 从这里开始数：请求上路那一刻会喊一声 ImagePending（卡片上那道流光靠它亮起来），
            // 而那一声属于上一段，不是这三次换宽要证明的东西。
            fixture.Changes.Clear();
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

        Case("R-012：离页零需求取消首图后换宽不复活，旧响应不留缓存", async () =>
        {
            await using var fixture = new CardImageFixture();
            var initial = fixture.Begin();
            var old = await fixture.Handler.AtAsync(0);
            var token = fixture.LoadingToken!.Value;
            using var propagation = fixture.HoldDownloadCancellation();
            fixture.Card.AbandonPoster();
            fixture.Card.Resize(250);
            Assert.True(token.IsCancellationRequested);
            Assert.Equal(1, fixture.Handler.Count);
            await propagation.Entered.WaitAsync(TimeSpan.FromSeconds(3));
            // The card waiter may finish before CancelAsync reaches HTTP's linked token callbacks.
            // Confirm that the old immediate assertion rejects this controlled interleaving, then
            // establish transport cancellation before releasing the deliberately late response.
            Assert.Throws<AssertionException>(() => Assert.True(old.Token.IsCancellationRequested));
            propagation.Release();
            await CardImageFixture.UntilAsync(() => old.Token.IsCancellationRequested, "transport cancellation did not propagate");
            await fixture.DeliverAsync(old, "abandoned");
            await initial;
            Assert.True(old.Token.IsCancellationRequested);
            await CardImageFixture.UntilAsync(() => fixture.DownloadRunning == 0, "zero-demand abandoned download did not exit");
            Assert.Equal(0, fixture.DownloadDemands);
            Assert.Equal(1, fixture.Handler.Count, "迟到交付不能复活旧键的新请求");
            Assert.False(File.Exists(fixture.CacheFile(400)), "零需求取消后的旧字节不写盘");
            Assert.False(PosterCache.TryGet(fixture.Key(170), out _));
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

        Case("R-012：等图那段 ImagePending 只在请求在飞时亮着，图到就收", async () =>
        {
            await using var fixture = new CardImageFixture();
            var card = fixture.Card;

            // 没上屏、没起请求的卡片不该亮：卡片清单里绝大多数卡片就是这个状态。
            Assert.False(card.ImagePending, "还没起请求的卡片不该显示等待");

            var initial = fixture.Begin();
            Assert.True(card.ImagePending, "请求上路之后应当亮着");

            var request = await fixture.Handler.AtAsync(0);
            await fixture.DeliverAsync(request, "pending-resolved");
            await PosterAsync(fixture, 170, "pending-resolved");
            Assert.False(card.ImagePending, "图到了就该收");
            await initial;
        });

        Case("R-012：放手一张还在等的卡，ImagePending 当场收掉", async () =>
        {
            await using var fixture = new CardImageFixture();
            var card = fixture.Card;
            var initial = fixture.Begin();
            Assert.True(card.ImagePending);

            card.AbandonPoster();
            Assert.False(card.ImagePending, "放手之后没有人在等，光要跟着收");
            await initial;
        });

        Case("R-012：没有封面的条目等完那一趟要收干净，不留一条永不消失的光", async () =>
        {
            await using var fixture = new CardImageFixture(artwork: false);
            var card = fixture.Card;
            var initial = fixture.Begin();
            await initial;
            Assert.Null(card.Poster);
            Assert.False(card.ImagePending, "服务器上就没有这张图，等的状态必须收掉");
        });
    }

    private static Task PosterAsync(CardImageFixture fixture, int width, string image) =>
        CardImageFixture.UntilAsync(() => fixture.Card.Poster is { } poster
            && poster.DecodePixelWidth == width && poster.FixtureImage == image, $"Expected poster {image} decoded at {width}");

    private static async Task<BitmapImage> ReadyPosterAsync(CardImageFixture fixture)
    {
        var initial = fixture.Begin();
        await fixture.DeliverAsync(await fixture.Handler.AtAsync(0), "initial-poster");
        await initial;
        await PosterAsync(fixture, 170, "initial-poster");
        return fixture.Card.Poster!;
    }

    private static void Case(string name, Func<Task> run) => Test(name, () => CardImageContext.Run(run));
}
#endif
