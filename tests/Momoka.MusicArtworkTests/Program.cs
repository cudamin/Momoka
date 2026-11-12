using System.Reflection;
using System.Xml.Linq;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Momoka.Music;
using Momoka.Shell.Media;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MusicArtworkProgram
{
    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Register();
        return TestHarness.Run();
    }

    private static void Register()
    {
        Test("音乐图片入口：曲目和封面墙各自绑定图片需求与动态首图", () =>
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "Momoka.sln"))) root = root.Parent;
            Assert.NotNull(root);
            var document = XDocument.Load(Path.Combine(root!.FullName, "src/Momoka.Shell/Views/MusicLibraryPage.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            foreach (var name in new[] { "MusicTrackTemplate", "MusicTileTemplate" })
            {
                var template = document.Descendants().Single(element => (string?)element.Attribute(x + "Key") == name);
                var image = template.Descendants().Single(element => element.Name.LocalName == "Image");
                Assert.Equal("{x:Bind Mode=OneWay}", (string?)image.Attributes().Single(attribute => attribute.Name.LocalName == "CardArtwork.Item").Value);
                Assert.Equal("{x:Bind RowPoster, Mode=OneWay}", (string?)image.Attribute("Source"));
                Assert.Equal("UniformToFill", (string?)image.Attribute("Stretch"));
            }
        });

        foreach (var (type, width) in new[]
        {
            (EmbyItemType.Audio, MusicCatalog.TrackArtWidth),
            (EmbyItemType.MusicAlbum, MusicCatalog.WallTileWidth),
            (EmbyItemType.MusicArtist, MusicCatalog.WallTileWidth),
            (EmbyItemType.Playlist, MusicCatalog.WallTileWidth)
        })
            Case("音乐图片：进树后读取并按实际尺寸解码 " + type, async () =>
            {
                await using var fixture = new CardImageFixture();
                var item = fixture.Scope.Bind(new EmbyItem { Id = "music-" + type, Type = type, ImageTags = new() { [EmbyImageStore.Primary] = "music" } });
                var card = new CardItem(item, fixture.Images, width, aspect: 1);
                var image = new Image();
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                BrowsePresentation.Ready = release.Task;
                BrowsePresentation.ExplicitWaits = 0;
                try
                {
                    CardArtwork.SetItem(image, card);
                    Assert.Equal(0, fixture.Handler.Count, "首次离树绑定不能读取整页");
                    image.Load();
                    Assert.Equal(1, BrowsePresentation.ExplicitWaits);
                    Assert.Equal(0, fixture.Handler.Count, "首图等待所属页面首帧放行");
                    release.TrySetResult();
                    var request = await fixture.Handler.AtAsync(0);
                    Assert.Equal(EmbyImageStore.RequestWidth(width), request.Width);
                    request.Complete(type);
                    await CardImageFixture.UntilAsync(() => card.RowPoster is not null, "音乐图未回填");
                    Assert.Equal(width, card.RowPoster!.DecodePixelWidth);
                    image.Unload();
                    Assert.Null(card.RowPoster);
                    image.Load();
                    Assert.NotNull(card.RowPoster, "缓存图需要同步恢复");
                    Assert.Equal(1, fixture.Handler.Count);
                }
                finally { CardArtwork.SetItem(image, null); card.AbandonPoster(); BrowsePresentation.Ready = Task.CompletedTask; }
                Assert.Equal(0, ImageArrivalFade.Living);
            });

        Case("音乐图片：回收幻影不清掉图片且重复 Loaded 不双发", async () =>
        {
            await using var f = new CardImageFixture();
            var image = new Image();
            CardArtwork.SetItem(image, f.Card);
            image.Load();
            var request = await f.Handler.AtAsync(0);
            image.Unload(phantom: true);
            image.Load();
            request.Complete("phantom");
            await CardImageFixture.UntilAsync(() => f.Card.Poster is not null, "幻影卸载丢图");
            var poster = f.Card.Poster;
            image.Unload(phantom: true);
            Assert.True(ReferenceEquals(poster, f.Card.Poster));
            Assert.Equal(1, f.Handler.Count);
            CardArtwork.SetItem(image, null);
            Assert.Null(f.Card.Poster);
            Assert.Equal(0, ImageArrivalFade.Living);
        });

        Case("音乐图片：真离树的晚到图只留有限缓存，回树同步恢复", async () =>
        {
            await using var f = new CardImageFixture();
            var image = new Image();
            CardArtwork.SetItem(image, f.Card);
            image.Load();
            var request = await f.Handler.AtAsync(0);
            image.Unload();
            Assert.Equal(0, ImageArrivalFade.Living);
            request.Complete("offscreen");
            await CardImageFixture.UntilAsync(() => PosterCache.TryGet(f.Key(170), out _), "晚到图没有进有限缓存");
            Assert.Null(f.Card.Poster);
            image.Load();
            Assert.Equal("offscreen", f.Card.Poster!.FixtureImage);
            Assert.Equal(1, f.Handler.Count);
            CardArtwork.SetItem(image, null);
        });

        Case("音乐图片：容器换项拒绝旧图回填，新项照常读取", async () =>
        {
            await using var f = new CardImageFixture();
            var second = new CardItem(f.Scope.Bind(new EmbyItem { Id = "second", Type = EmbyItemType.Audio, ImageTags = new() { [EmbyImageStore.Primary] = "other" } }), f.Images, 44, aspect: 1);
            var image = new Image();
            CardArtwork.SetItem(image, f.Card);
            image.Load();
            var old = await f.Handler.AtAsync(0);
            CardArtwork.SetItem(image, second);
            var current = await f.Handler.AtAsync(1);
            current.Complete("current");
            await CardImageFixture.UntilAsync(() => second.Poster is not null, "新项图未回填");
            old.Complete("old");
            await CardImageFixture.UntilAsync(() => PosterCache.TryGet(f.Key(170), out _), "旧项有限缓存未完成");
            Assert.Null(f.Card.Poster);
            Assert.Equal("current", second.Poster!.FixtureImage);
            CardArtwork.SetItem(image, null);
            second.AbandonPoster();
        });

        Case("音乐图片：旧容器迟到卸载不能清掉新持有者", async () =>
        {
            await using var f = new CardImageFixture();
            var old = new Image();
            CardArtwork.SetItem(old, f.Card);
            old.Load();
            (await f.Handler.AtAsync(0)).Complete("shared");
            await CardImageFixture.UntilAsync(() => f.Card.Poster is not null, "共享图未回填");
            var current = new Image();
            CardArtwork.SetItem(current, f.Card);
            current.Load();
            var poster = f.Card.Poster;
            old.Unload();
            CardArtwork.SetItem(old, null);
            Assert.True(ReferenceEquals(poster, f.Card.Poster));
            current.Unload();
            Assert.Null(f.Card.Poster);
            CardArtwork.SetItem(current, null);
            Assert.Equal(0, ImageArrivalFade.Living);
        });

        Case("音乐图片：移除附加项清订阅并拒绝晚到回填", async () =>
        {
            await using var f = new CardImageFixture();
            var image = new Image();
            CardArtwork.SetItem(image, f.Card);
            image.Load();
            var request = await f.Handler.AtAsync(0);
            CardArtwork.SetItem(image, null);
            Assert.Equal(0, image.LoadedHandlers);
            Assert.Equal(0, image.UnloadedHandlers);
            Assert.Equal(0, ImageArrivalFade.Living);
            request.Complete("detached");
            await CardImageFixture.UntilAsync(() => PosterCache.TryGet(f.Key(170), out _), "已卸项缓存未完成");
            Assert.Null(f.Card.Poster);
            image.Load();
            Assert.Null(f.Card.Poster);
            CardArtwork.SetItem(image, f.Card);
            Assert.NotNull(f.Card.Poster);
            Assert.Equal(1, image.LoadedHandlers);
            Assert.Equal(1, image.UnloadedHandlers);
            CardArtwork.SetItem(image, null);
        });

        Case("音乐图片：来源撤销取消读取且迟到响应不能回填", async () =>
        {
            await using var f = new CardImageFixture();
            var image = new Image();
            CardArtwork.SetItem(image, f.Card);
            image.Load();
            var request = await f.Handler.AtAsync(0);
            f.RevokeSource();
            request.Complete("revoked");
            await CardImageFixture.UntilAsync(() => f.DownloadRunning == 0, "来源撤销未结清");
            Assert.Null(f.Card.Poster);
            image.Load();
            Assert.Equal(1, f.Handler.Count);
            CardArtwork.SetItem(image, null);
        });

        Case("音乐图片：404为终态，重新显示不反复请求", async () =>
        {
            await using var f = new CardImageFixture();
            var image = new Image();
            CardArtwork.SetItem(image, f.Card);
            image.Load();
            (await f.Handler.AtAsync(0)).Missing();
            await CardImageFixture.UntilAsync(() => (bool)typeof(CardItem).GetField("_missing", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Card)!, "缺图未记终态");
            image.Unload();
            image.Load();
            Assert.Null(f.Card.Poster);
            Assert.Equal(1, f.Handler.Count);
            CardArtwork.SetItem(image, null);
        });

        Case("音乐图片：暂态坏解码保留重试，回树按当前需求恢复", async () =>
        {
            await using var f = new CardImageFixture();
            var image = new Image();
            PosterLoader.ErrorFor = _ => new InvalidDataException("anonymous bad image");
            try
            {
                CardArtwork.SetItem(image, f.Card);
                image.Load();
                (await f.Handler.AtAsync(0)).Complete("bad");
                // 实际图片Store会先丢弃坏缓存并重取一次，两次都坏才交还CardItem的暂态失败。
                (await f.Handler.AtAsync(1)).Complete("bad again");
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                await CardImageFixture.UntilAsync(() => (DateTime)typeof(CardItem).GetField("_failedAt", flags)!.GetValue(f.Card)! != default,
                    "坏图没有保留暂态失败");
                Assert.False((bool)typeof(CardItem).GetField("_missing", flags)!.GetValue(f.Card)!, "坏图不能永久记无图");
                image.Unload();
                PosterLoader.ErrorFor = null;
                typeof(CardItem).GetField("_failedAt", flags)!.SetValue(f.Card, DateTime.UtcNow.AddMinutes(-5));
                image.Load();
                (await f.Handler.AtAsync(2)).Complete("recovered");
                await CardImageFixture.UntilAsync(() => f.Card.Poster is not null, "当前容器没有重试坏图");
                Assert.Equal("recovered", f.Card.Poster!.FixtureImage);
            }
            finally { CardArtwork.SetItem(image, null); }
        });

        foreach (var total in new[] { 100, 1_000, 10_000 })
            Case("音乐图片：匿名元数据与20个模拟容器分离，回收不保留全列表 " + total, async () =>
            {
                await using var f = new CardImageFixture();
                var cards = Enumerable.Range(0, total).Select(index => new CardItem(f.Scope.Bind(new EmbyItem
                { Id = "large-" + index, Type = EmbyItemType.MusicAlbum, ImageTags = new() { [EmbyImageStore.Primary] = "large" } }), f.Images, 170, aspect: 1)).ToArray();
                Assert.Equal(0, f.Handler.Count);
                var containers = Enumerable.Range(0, 20).Select(_ => new Image()).ToArray();
                for (var index = 0; index < containers.Length; index++)
                {
                    CardArtwork.SetItem(containers[index], cards[index]);
                    containers[index].Load();
                }
                for (var index = 0; index < containers.Length; index++) (await f.Handler.AtAsync(index)).Complete("visible");
                await CardImageFixture.UntilAsync(() => cards.Take(20).All(card => card.Poster is not null), "首批模拟容器未完成");
                Assert.Equal(20, cards.Count(card => card.Poster is not null));
                for (var index = 0; index < containers.Length; index++)
                {
                    CardArtwork.SetItem(containers[index], cards[20 + index]);
                    (await f.Handler.AtAsync(20 + index)).Complete("next");
                }
                await CardImageFixture.UntilAsync(() => cards.Skip(20).Take(20).All(card => card.Poster is not null), "次批模拟容器未完成");
                Assert.Equal(20, cards.Count(card => card.Poster is not null));
                Assert.True(cards.Take(20).All(card => card.Poster is null));
                Assert.Equal(40, f.Handler.Count);
                foreach (var image in containers) { image.Unload(); CardArtwork.SetItem(image, null); }
                Assert.Equal(0, cards.Count(card => card.Poster is not null));
                Assert.Equal(0, ImageArrivalFade.Living);
                foreach (var card in cards) card.AbandonPoster();
            });

        Test("音乐图片：附加属性误用非Image保持无副作用", () =>
        {
            var target = new Microsoft.UI.Xaml.DependencyObject();
            var card = new CardItem(new EmbyItem { Id = "inert" }, null!, 170, navigate: () => { });
            CardArtwork.SetItem(target, card);
            Assert.True(ReferenceEquals(card, CardArtwork.GetItem(target)));
            Assert.Null(card.Poster);
            Assert.Equal(0, ImageArrivalFade.Living);
        });
    }

    private static void Case(string name, Func<Task> body) => Test(name, () => CardImageContext.Run(body));
}
