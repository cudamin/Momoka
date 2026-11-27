using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>自动位置保护继续观看季集文字行；固定位置和版式约束独立保留。</summary>
internal static class HomeFoldTests
{
    public static void Register()
    {
        Test("主页媒体库位置：商店与轮播关闭固定下方，整宽保留全部位置偏好", () =>
        {
            foreach (var preference in Enum.GetValues<HomeLibraryPlacement>())
            {
                Assert.Equal(HomeLibraryPlacement.BelowCarousel,
                    HomeFold.EffectivePlacement(CarouselLayout.Store, true, preference));
                Assert.Equal(HomeLibraryPlacement.BelowCarousel,
                    HomeFold.EffectivePlacement(CarouselLayout.Store, false, preference));
                Assert.Equal(HomeLibraryPlacement.BelowCarousel,
                    HomeFold.EffectivePlacement(CarouselLayout.Full, false, preference));
                Assert.Equal(preference, HomeFold.EffectivePlacement(CarouselLayout.Full, true, preference));
            }
        });

        Test("媒体库自动上移：季集行距裁切还有八像素就上移", () =>
        {
            Assert.True(HomeFold.LibraryOnBanner(HomeLibraryPlacement.Auto, 608, 600));
            Assert.True(HomeFold.LibraryOnBanner(HomeLibraryPlacement.Auto, 607, 600));
            Assert.False(HomeFold.LibraryOnBanner(HomeLibraryPlacement.Auto, 609, 600));
        });

        Test("媒体库自动上移：文字行已在裁切边界或屏外继续上移", () =>
        {
            Assert.True(HomeFold.LibraryOnBanner(HomeLibraryPlacement.Auto, 600, 600));
            Assert.True(HomeFold.LibraryOnBanner(HomeLibraryPlacement.Auto, 590, 600));
        });

        Test("媒体库自动上移：相同文字余量不设窗口高矮门槛", () =>
        {
            foreach (var viewport in new[] { 120d, 300d, 744d, 1080d, 2160d })
            {
                Assert.True(HomeFold.LibraryOnBanner(HomeLibraryPlacement.Auto, viewport, viewport - 8));
                Assert.False(HomeFold.LibraryOnBanner(HomeLibraryPlacement.Auto, viewport, viewport - 9));
            }
        });

        Test("媒体库自动上移：完整露出文字并有余量时留在下方", () =>
        {
            Assert.False(HomeFold.LibraryOnBanner(HomeLibraryPlacement.Auto, 744, 590));
            Assert.False(HomeFold.LibraryOnBanner(HomeLibraryPlacement.Auto, 900, 800));
        });

        Test("媒体库自动上移：尚未测到文字行或无效几何不触发", () =>
        {
            foreach (var bottom in new[] { 0d, -1d, double.NaN, double.PositiveInfinity })
                Assert.False(HomeFold.LibraryOnBanner(HomeLibraryPlacement.Auto, 600, bottom));
            foreach (var viewport in new[] { double.NaN, double.PositiveInfinity })
                Assert.False(HomeFold.LibraryOnBanner(HomeLibraryPlacement.Auto, viewport, 600));
        });

        Test("主页媒体库位置：固定在轮播图中不看文字行位置", () =>
        {
            Assert.True(HomeFold.LibraryOnBanner(HomeLibraryPlacement.OnCarousel, 1000, 600));
            Assert.True(HomeFold.LibraryOnBanner(HomeLibraryPlacement.OnCarousel, 120, 600));
            Assert.True(HomeFold.LibraryOnBanner(HomeLibraryPlacement.OnCarousel, 300, 0));
        });

        Test("主页媒体库位置：固定在轮播图下方不受临近裁切影响", () =>
        {
            Assert.False(HomeFold.LibraryOnBanner(HomeLibraryPlacement.BelowCarousel, 608, 600));
            Assert.False(HomeFold.LibraryOnBanner(HomeLibraryPlacement.BelowCarousel, 300, 600));
        });

        Test("主页媒体库位置：量不到视口时三档都不上移", () =>
        {
            foreach (var placement in Enum.GetValues<HomeLibraryPlacement>())
            {
                Assert.False(HomeFold.LibraryOnBanner(placement, 0, 600));
                Assert.False(HomeFold.LibraryOnBanner(placement, -1, 600));
            }
        });
    }
}
