using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 主页轮播卡片四周那圈染色的取色（2026-11-12 用户令「参考上图给轮播图添加圆角，和四周染色的效果」）。
/// <para>
/// 这一组钉的是 <see cref="CarouselAmbient"/> 的取舍：借来的是「有颜色、不过暗、不过亮」的那些像素，
/// 而不是整张图的平均色；借不到就退回平均色；坏输入返回 <see cref="ArtworkAmbient.None"/> 而不是抛异常。
/// 屏上那圈光晕浓不浓、好不好看，只能靠实屏截图，这里不冒充。
/// </para>
/// </summary>
internal static class CarouselAmbientTests
{
    public static void Register()
    {
        Test("轮播染色：饱和的红整张图，借出来还是红", () =>
        {
            var pixels = Solid(64, 36, r: 200, g: 32, b: 24);
            var ambient = CarouselAmbient.Sample(pixels, 64, 36);

            Assert.True(ambient.R > ambient.G && ambient.R > ambient.B,
                $"红色剧照借出来的不是红：{ambient}");
            Assert.True(ambient.R >= 96 && ambient.R <= 200,
                $"峰值通道没有落进 96–200 那一档：{ambient}");
        });

        Test("轮播染色：雪原里一条红围巾，借的是围巾不是灰", () =>
        {
            // 九成是浅灰的雪，一成是红围巾：平均色是灰，而屏上想要的是围巾的颜色。
            var pixels = Solid(100, 100, r: 210, g: 214, b: 218);
            for (var y = 40; y < 50; y++)
                for (var x = 40; x < 50; x++)
                    Set(pixels, 100, x, y, r: 190, g: 30, b: 40);

            var ambient = CarouselAmbient.Sample(pixels, 100, 100);

            Assert.True(ambient.R > ambient.G + 20 && ambient.R > ambient.B + 20,
                $"灰底上那块红没有占到主导：{ambient}");
        });

        Test("轮播染色：纯灰剧照借平均色，三个通道贴在一起", () =>
        {
            var ambient = CarouselAmbient.Sample(Solid(32, 18, r: 120, g: 120, b: 120), 32, 18);

            Assert.True(!ambient.IsNone, "灰图不该什么都没有");
            Assert.True(Math.Abs(ambient.R - ambient.G) <= 2 && Math.Abs(ambient.G - ambient.B) <= 2,
                $"灰图借出来的不是中性色：{ambient}");
        });

        Test("轮播染色：近黑的颜色会被抬到看得见的那一档", () =>
        {
            var ambient = CarouselAmbient.Sample(Solid(48, 27, r: 20, g: 24, b: 60), 48, 27);

            Assert.True(Math.Max(ambient.R, Math.Max(ambient.G, ambient.B)) >= 96,
                $"近黑的蓝没有抬起来，压在深色底上等于没有：{ambient}");
            Assert.True(ambient.B > ambient.R, $"抬亮度把色相弄丢了：{ambient}");
        });

        Test("轮播染色：没有像素、尺寸不对、整张全黑都返回 None", () =>
        {
            Assert.True(CarouselAmbient.Sample([], 0, 0).IsNone, "空输入不是 None");
            Assert.True(CarouselAmbient.Sample(new byte[16], 8, 8).IsNone, "字节数不够时不是 None");
            Assert.True(CarouselAmbient.Sample(Solid(8, 8, 0, 0, 0), 8, 8).IsNone, "全黑图不是 None");
        });
    }

    /// <summary>一张单色 BGRA8 图（顶行在前，alpha 全 255）。</summary>
    private static byte[] Solid(int width, int height, byte r, byte g, byte b)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                Set(pixels, width, x, y, r, g, b);
        return pixels;
    }

    private static void Set(byte[] pixels, int width, int x, int y, byte r, byte g, byte b)
    {
        var at = (y * width + x) * 4;
        pixels[at] = b;
        pixels[at + 1] = g;
        pixels[at + 2] = r;
        pixels[at + 3] = 0xFF;
    }
}
