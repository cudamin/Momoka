using System.Net;
using System.Text;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Momoka.Shell.Media;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Shell.Views
{
    // Only the presentation boundary is controlled. Production card, slide, HTTP/store and caches execute.
    internal static class BrowsePresentation
    {
        private static Task _images = Task.CompletedTask;
        internal static int Waits { get; private set; }
        internal static Task WaitForImagesAsync() { Waits++; return _images; }
        internal static IDisposable Hold(out TaskCompletionSource release)
        {
            release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _images = release.Task;
            Waits = 0;
            return new ReleaseGate(release);
        }
        private sealed class ReleaseGate(TaskCompletionSource release) : IDisposable
        {
            public void Dispose() { release.TrySetResult(); _images = Task.CompletedTask; }
        }
    }
}

namespace Momoka.Tests
{
    internal static class BrowseImageGateTests
    {
        internal static void Register()
        {
            Case("首帧图片：单张新请求先等待，重复调用不双发", async () =>
            {
                await using var f = new CardImageFixture();
                using var gate = BrowsePresentation.Hold(out var release);
                var load = f.Begin();
                await f.Ensure();
                Assert.Equal(1, BrowsePresentation.Waits, "新请求必须先进入首帧等待边界");
                Assert.Equal(0, f.Handler.Count);
                Assert.Equal(0, PosterLoader.Calls.Count);
                Assert.False(load.IsCompleted);
                release.TrySetResult();
                (await f.Handler.AtAsync(0)).Complete("first after presentation");
                await load;
                Assert.Equal(1, f.Handler.Count);
                Assert.Equal("first after presentation", f.Card.Poster!.FixtureImage);
            });
            Case("首帧图片：等待期间离页取消，不等旧首帧放行且不补发", async () =>
            {
                await using var f = new CardImageFixture();
                using var gate = BrowsePresentation.Hold(out var release);
                var load = f.Begin();
                Assert.Equal(1, BrowsePresentation.Waits);
                f.Card.AbandonPoster();
                await load;
                Assert.Equal(0, f.Handler.Count);
                release.TrySetResult();
                Assert.Null(f.Card.Poster);
                Assert.Equal(0, f.Handler.Count);
            });
            Case("首帧图片：旧卡等待换宽，旧门控释放只能发最新宽度", async () =>
            {
                await using var f = new CardImageFixture();
                using var gate = BrowsePresentation.Hold(out var release);
                var old = f.Begin();
                f.Card.Resize(250);
                await old;
                Assert.Equal(0, f.Handler.Count);
                release.TrySetResult();
                var request = await f.Handler.AtAsync(0);
                Assert.Equal(560, request.Width);
                request.Complete("new width");
                await CardImageFixture.UntilAsync(() => f.Card.Poster is not null, "new-width image not assigned");
                Assert.Equal(1, f.Handler.Count);
            });
            Case("首帧图片：已解码卡片缓存同步恢复，不闪空不等待", async () =>
            {
                await using var f = new CardImageFixture();
                var kept = new BitmapImage { PixelWidth = 170, PixelHeight = 255, FixtureImage = "kept" };
                PosterCache.Remember(f.Key(170), kept);
                using var gate = BrowsePresentation.Hold(out _);
                var load = f.Begin();
                Assert.True(load.IsCompleted);
                Assert.True(ReferenceEquals(kept, f.Card.Poster));
                Assert.Equal(0, BrowsePresentation.Waits);
                Assert.Equal(0, f.Handler.Count);
            });
            foreach (var type in new[] { EmbyItemType.Folder, EmbyItemType.CollectionFolder })
                Case("首帧图片：拼图缓存先上屏，新格等待 " + type, async () =>
                {
                    await using var f = new CardImageFixture(false);
                    f.Item.Type = type;
                    f.Item.PosterPaths = ["kept", "new"];
                    var key = PosterCache.Key(f.Scope.Connection, f.Item.Id, EmbyImageStore.Primary, "kept", 170);
                    var kept = new BitmapImage { PixelWidth = 170, PixelHeight = 170, FixtureImage = "kept" };
                    PosterCache.Remember(key, kept);
                    using var gate = BrowsePresentation.Hold(out var release);
                    var load = f.Begin();
                    Assert.True(ReferenceEquals(kept, type == EmbyItemType.Folder ? f.Card.FolderTile0 : f.Card.LibraryTile0));
                    Assert.Equal(0, f.Handler.Count);
                    release.TrySetResult();
                    (await f.Handler.AtAsync(0)).Complete("new tile");
                    await load;
                    Assert.Equal(1, f.Handler.Count);
                });
            Case("首帧图片：轮播磁盘冷图等待后才解码，不重复请求", async () =>
            {
                using var f = new BannerCacheTests.BannerFixture();
                f.Seed("gate", "banner");
                BannerPictureLoader.Reset();
                using var gate = BrowsePresentation.Hold(out var release);
                var slide = f.Slide("gate");
                var load = slide.EnsureAsync();
                await slide.EnsureAsync();
                Assert.Equal(1, BrowsePresentation.Waits);
                Assert.Equal(0, BannerPictureLoader.Decodes);
                Assert.False(load.IsCompleted);
                release.TrySetResult();
                await load;
                Assert.Equal(1, BannerPictureLoader.Decodes);
                Assert.NotNull(slide.Picture);
            });
            Case("首帧图片：轮播等待取消立即结束，旧门控不回填", async () =>
            {
                using var f = new BannerCacheTests.BannerFixture();
                f.Seed("gate", "banner");
                BannerPictureLoader.Reset();
                using var gate = BrowsePresentation.Hold(out var release);
                var slide = f.Slide("gate");
                var load = slide.EnsureAsync();
                Assert.Equal(1, BrowsePresentation.Waits);
                slide.Release();
                await load;
                release.TrySetResult();
                Assert.Equal(0, BannerPictureLoader.Decodes);
                Assert.Null(slide.Picture);
            });
            Case("首帧图片：暖轮播图不等待，新增徽标等待首帧", async () =>
            {
                var http = new ImageHttp();
                using var f = new BannerCacheTests.BannerFixture(http);
                f.Seed("gate", "banner");
                var first = f.Slide("gate");
                await first.EnsureAsync();
                var kept = first.Picture;
                first.Release();
                var slide = f.Slide("gate");
                slide.Item.ImageTags[EmbyImageStore.Logo] = "logo";
                slide = new BannerSlide(slide.Item, f.Images);
                using var gate = BrowsePresentation.Hold(out var release);
                var load = slide.EnsureAsync();
                Assert.True(ReferenceEquals(kept, slide.Picture));
                Assert.Equal(0, http.Count);
                release.TrySetResult();
                await load;
                Assert.Equal(1, http.Count);
                Assert.NotNull(slide.Logo);
            });
            Case("首帧图片：纯暖轮播缓存无需等门控", async () =>
            {
                using var f = new BannerCacheTests.BannerFixture();
                f.Seed("warm", "banner");
                var first = f.Slide("warm");
                await first.EnsureAsync();
                var kept = first.Picture;
                first.Release();
                using var gate = BrowsePresentation.Hold(out _);
                var returned = f.Slide("warm");
                var load = returned.EnsureAsync();
                Assert.True(load.IsCompleted);
                Assert.True(ReferenceEquals(kept, returned.Picture));
                Assert.Equal(0, BrowsePresentation.Waits);
            });
            Case("首帧图片：轮播来源退出取消首帧等待", async () =>
            {
                using var f = new BannerCacheTests.BannerFixture();
                f.Seed("gate", "banner");
                BannerPictureLoader.Reset();
                using var gate = BrowsePresentation.Hold(out var release);
                var slide = f.Slide("gate");
                var load = slide.EnsureAsync();
                f.Revoke();
                await load;
                release.TrySetResult();
                Assert.Equal(0, BannerPictureLoader.Decodes);
                Assert.Null(slide.Picture);
            });
        }

        private static void Case(string name, Func<Task> run) => Test(name, () => CardImageContext.Run(run));
        private sealed class ImageHttp : HttpMessageHandler
        {
            internal int Count { get; private set; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                Assert.True(request.Method == HttpMethod.Get && request.RequestUri?.Host == "banner.invalid");
                Count++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("logo")) });
            }
        }
    }
}
