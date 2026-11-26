#if CARD_IMAGE_RESIZE_FIXTURE
using System.Text;
using Momoka.Emby;
using Momoka.Shell.Media;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class BannerCacheTests
{
    private static void RegisterExtensionOwnership()
    {
        foreach (var extended in new[] { false, true })
            Case("轮播扩边归属：" + extended + "缓存命中后拒绝另一模式迟到解码", async () =>
            {
                using var fixture = new BannerFixture();
                fixture.Seed("mode-tag", "source");
                BannerPictureLoader.Reset();
                BannerPictureLoader.IdentifyExtension = true;
                var slide = fixture.Slide("mode-tag");
                slide.SetLeftExtension(extended);
                await slide.EnsureAsync();
                var warm = slide.Picture;
                Assert.Equal("source" + Mark(extended), warm!.FixtureImage);
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                BannerPictureLoader.HoldDecodeByExtension = mode =>
                {
                    if (mode == extended) return Task.CompletedTask;
                    entered.TrySetResult(); return release.Task;
                };
                slide.SetLeftExtension(!extended);
                var old = slide.EnsureAsync();
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                    Assert.Null(slide.Picture);
                    slide.SetLeftExtension(extended);
                    Assert.True(ReferenceEquals(warm, slide.Picture), "回原模式应立即命中原缓存");
                    release.TrySetResult();
                    await old;
                    Assert.True(ReferenceEquals(warm, slide.Picture), "旧解码不能覆盖已经命中的原模式成品图");
                    var key = Key(slide, extended);
                    Assert.True(BannerBackdropCache.TryGet(key, out var cached));
                    Assert.True(ReferenceEquals(warm, cached));
                    Assert.True(BannerBackdropCache.TryGetPixels(key, out var pixels));
                    Assert.Equal("source" + Mark(extended), Encoding.UTF8.GetString(pixels!.Pixels), "旧像素不能污染原模式缓存");
                    Assert.False(BannerBackdropCache.TryGetPixels(Key(slide, !extended), out _), "被取消的模式没有已提交像素");
                    Assert.False(BannerBackdropCache.TryGet(Key(slide, !extended), out _));
                    BannerPictureLoader.HoldDecodeByExtension = null;
                    slide.SetLeftExtension(!extended);
                    await slide.EnsureAsync();
                    Assert.Equal("source" + Mark(!extended), slide.Picture!.FixtureImage, "取消后再次进入冷模式仍可完整加载");
                }
                finally { release.TrySetResult(); await old; }
            });
        Case("轮播扩边归属：构造命中整宽暖图，首次切冷商店必须丢旧图再按原图解码", async () =>
        {
            using var fixture = new BannerFixture();
            fixture.Seed("mode-tag", "source");
            BannerPictureLoader.Reset();
            BannerPictureLoader.IdentifyExtension = true;
            var first = fixture.Slide("mode-tag");
            await first.EnsureAsync();
            var warm = first.Picture;
            var rebuilt = fixture.Slide("mode-tag");
            Assert.True(ReferenceEquals(warm, rebuilt.Picture));
            rebuilt.SetLeftExtension(false);
            Assert.Null(rebuilt.Picture, "尚未调用Ensure的构造暖图也属于旧扩边模式");
            Assert.Null(rebuilt.BackdropPixels);
            await rebuilt.EnsureAsync();
            Assert.Equal("source|original", rebuilt.Picture!.FixtureImage);
            Assert.Equal(2, BannerPictureLoader.Decodes);
            Assert.True(BannerBackdropCache.TryGet(Key(rebuilt, true), out var extended));
            Assert.True(ReferenceEquals(warm, extended), "正确旧模式缓存应继续可用");
        });
    }

    private static string Mark(bool extended) => extended ? "|extended" : "|original";
    private static string Key(Momoka.Shell.Views.BannerSlide slide, bool extended) =>
        BannerBackdropCache.Key(slide.Item.Source!.Connection, ItemArtwork.Banner(slide.Item)!.Value, 1280, extended);
}
#endif
