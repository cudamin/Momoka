using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;
using ArtworkPixels = Momoka.MoviePilot.MoviePilotLibraryArtwork.ArtworkPixels;

namespace Momoka.Tests;

internal static class MoviePilotLibraryArtworkTests
{
    internal static void Register()
    {
        Test("订阅分类封面：海报优先，坏地址退回背景，去重并保持顺序", () =>
        {
            var selected = MoviePilotLibraryArtwork.Select(
            [
                Subscription(" https://images.invalid/poster-a ", "https://images.invalid/backdrop-a"),
                Subscription("https://images.invalid/poster-a", "https://images.invalid/backdrop-duplicate"),
                Subscription("file:///private.png", "http://images.invalid/backdrop-b"),
                Subscription("/relative.png", "ftp://images.invalid/backdrop"),
                Subscription(null, "https://images.invalid/backdrop-c"),
                Subscription("https://images.invalid/poster-d", null),
                Subscription("https://images.invalid/poster-e", null)
            ]);
            Assert.Equal(4, selected.Count);
            Assert.Equal("https://images.invalid/poster-a", selected[0]);
            Assert.Equal("http://images.invalid/backdrop-b", selected[1]);
            Assert.Equal("https://images.invalid/backdrop-c", selected[2]);
            Assert.Equal("https://images.invalid/poster-d", selected[3]);
        });
        Test("订阅分类封面：空列表和缺图不伪造海报", () =>
        {
            Assert.Equal(0, MoviePilotLibraryArtwork.Select([]).Count);
            Assert.Equal(0, MoviePilotLibraryArtwork.Select([Subscription(null, ""), Subscription("data:image/png;base64,AA", null)]).Count);
            var cover = MoviePilotLibraryArtwork.Compose([]);
            Assert.Equal(576, cover.Width);
            Assert.Equal(324, cover.Height);
            Assert.Equal(576 * 324 * 4, cover.Pixels.Length);
            for (var offset = 0; offset < cover.Pixels.Length; offset += 4)
            {
                Assert.Equal((byte)17, cover.Pixels[offset]);
                Assert.Equal((byte)14, cover.Pixels[offset + 1]);
                Assert.Equal((byte)12, cover.Pixels[offset + 2]);
                Assert.Equal((byte)255, cover.Pixels[offset + 3]);
            }
        });
        foreach (var count in new[] { 1, 2, 3, 4 })
            Test("订阅分类封面：实际 " + count + " 张图片填满主画面且不复制", () =>
            {
                var sources = Enumerable.Range(1, count).Select(value => Solid(12, 18, (byte)(value * 40), 55, 75)).ToArray();
                var cover = MoviePilotLibraryArtwork.Compose(sources);
                var seen = new List<byte>();
                var gaps = 0;
                for (var x = 0; x < cover.Width; x++)
                {
                    var blue = Pixel(cover, x, 40, 0);
                    if (blue == 17) { gaps++; continue; }
                    if (seen.Count == 0 || seen[^1] != blue) seen.Add(blue);
                    Assert.Equal((byte)55, Pixel(cover, x, 40, 1));
                    Assert.Equal((byte)75, Pixel(cover, x, 40, 2));
                }
                Assert.True(seen.SequenceEqual(Enumerable.Range(1, count).Select(value => (byte)(value * 40))));
                Assert.Equal(2 * (count - 1), gaps);
                Assert.Equal((byte)40, Pixel(cover, 0, 0, 0));
                Assert.Equal((byte)(count * 40), Pixel(cover, cover.Width - 1, 271, 0));
                for (var offset = 3; offset < cover.Pixels.Length; offset += 4)
                    Assert.Equal((byte)255, cover.Pixels[offset]);
            });
        Test("订阅分类封面：居中等比裁满不把圆形拉成椭圆", () =>
        {
            var source = Solid(144, 144, 0, 0, 0);
            for (var y = 0; y < source.Height; y++)
                for (var x = 0; x < source.Width; x++)
                    if (Math.Pow(x - 71.5, 2) + Math.Pow(y - 71.5, 2) < 20 * 20)
                    {
                        var offset = (y * source.Width + x) * 4;
                        source.Pixels[offset] = source.Pixels[offset + 1] = source.Pixels[offset + 2] = 255;
                    }
            var cover = MoviePilotLibraryArtwork.Compose([source]);
            var columns = Enumerable.Range(0, cover.Width).Where(x => Pixel(cover, x, 135, 0) > 128).ToArray();
            var rows = Enumerable.Range(0, 272).Where(y => Pixel(cover, 287, y, 0) > 128).ToArray();
            Assert.True(columns.Length is >= 158 and <= 162, "圆形必须按统一比例放大");
            Assert.True(Math.Abs(columns.Length - rows.Length) <= 2, "水平和竖直比例不一致");
            Assert.True(Math.Abs((columns[0] + columns[^1]) / 2d - 287.5) < 1);
            Assert.True(Math.Abs((rows[0] + rows[^1]) / 2d - 135.5) < 1, "裁切没有居中");
        });
        Test("订阅分类封面：倒影沿下沿翻转并逐渐淡到深底", () =>
        {
            var source = Solid(576, 272, 17, 14, 12);
            for (var y = 0; y < source.Height; y++)
            {
                // Bottom red, then green, then blue: reflection must preserve that reversed order.
                var color = y >= 268 ? (B: (byte)17, G: (byte)14, R: (byte)255)
                    : y >= 260 ? (B: (byte)17, G: (byte)255, R: (byte)12)
                    : y >= 244 ? (B: (byte)255, G: (byte)14, R: (byte)12)
                    : (B: (byte)17, G: (byte)14, R: (byte)12);
                for (var x = 0; x < source.Width; x++)
                {
                    var offset = (y * source.Width + x) * 4;
                    source.Pixels[offset] = color.B;
                    source.Pixels[offset + 1] = color.G;
                    source.Pixels[offset + 2] = color.R;
                }
            }
            var cover = MoviePilotLibraryArtwork.Compose([source]);
            Assert.True(Pixel(cover, 200, 272, 2) > 100, "倒影第一行没有镜像主图下沿");
            Assert.Equal((byte)14, Pixel(cover, 200, 272, 1));
            Assert.True(Pixel(cover, 200, 278, 1) > 60);
            Assert.True(Pixel(cover, 200, 288, 0) > 30);
            Assert.True(Pixel(cover, 200, 296, 0) < Pixel(cover, 200, 288, 0), "倒影没有逐渐淡出");
            Assert.Equal((byte)17, Pixel(cover, 200, 300, 0));
            Assert.Equal((byte)17, Pixel(cover, 200, 323, 0));
            Assert.Equal((byte)14, Pixel(cover, 200, 323, 1));
            Assert.Equal((byte)12, Pixel(cover, 200, 323, 2));
        });
        Test("订阅分类封面：半透明图片合成不透明深底，透明边缘颜色不渗出", () =>
        {
            var translucent = MoviePilotLibraryArtwork.Compose([Solid(1, 1, 217, 214, 212, 128)]);
            Assert.True(Math.Abs(Pixel(translucent, 20, 20, 0) - 117) <= 1);
            Assert.True(Math.Abs(Pixel(translucent, 20, 20, 1) - 114) <= 1);
            Assert.True(Math.Abs(Pixel(translucent, 20, 20, 2) - 112) <= 1);
            var transparent = MoviePilotLibraryArtwork.Compose([Solid(2, 2, 255, 255, 255, 0)]);
            Assert.Equal((byte)17, Pixel(transparent, 100, 100, 0));
            Assert.Equal((byte)14, Pixel(transparent, 100, 100, 1));
            Assert.Equal((byte)12, Pixel(transparent, 100, 100, 2));
            Assert.Equal((byte)255, Pixel(transparent, 100, 100, 3));
        });
        Test("订阅分类封面：拒绝损坏像素、超大图和超过四张", () =>
        {
            Assert.Throws<ArgumentException>(() => MoviePilotLibraryArtwork.Compose([new([], 1, 1)]));
            Assert.Throws<ArgumentOutOfRangeException>(() => MoviePilotLibraryArtwork.Compose([new([], 0, 1)]));
            Assert.Throws<ArgumentOutOfRangeException>(() => MoviePilotLibraryArtwork.Compose([new([], 1, -1)]));
            Assert.Throws<ArgumentOutOfRangeException>(() => MoviePilotLibraryArtwork.Compose([new([], int.MaxValue, int.MaxValue)]));
            Assert.Throws<ArgumentOutOfRangeException>(() => MoviePilotLibraryArtwork.Compose(Enumerable.Repeat(Solid(1, 1, 0, 0, 0), 5).ToArray()));
            Assert.Throws<ArgumentNullException>(() => MoviePilotLibraryArtwork.Select(null!));
            Assert.Throws<ArgumentNullException>(() => MoviePilotLibraryArtwork.Compose(null!));
        });
        Test("订阅分类封面：九宫格按上限取九张不同海报，默认合成仍只取四张", () =>
        {
            var subscriptions = Enumerable.Range(0, 12)
                .Select(index => Subscription($"https://images.invalid/poster-{index}", null)).ToArray();
            var cover = MoviePilotLibraryArtwork.Select(subscriptions);
            Assert.Equal(MoviePilotLibraryArtwork.CoverPosters, cover.Count);
            Assert.Equal(4, cover.Count);
            var wall = MoviePilotLibraryArtwork.Select(subscriptions, MoviePilotLibraryArtwork.WallPosters);
            Assert.Equal(9, wall.Count);
            Assert.Equal(9, wall.Distinct().Count());
            Assert.True(wall.SequenceEqual(Enumerable.Range(0, 9)
                .Select(index => $"https://images.invalid/poster-{index}")));
            Assert.Throws<ArgumentOutOfRangeException>(() => MoviePilotLibraryArtwork.Select(subscriptions, 0));
        });
        Test("订阅分类封面：取消在像素处理前生效", () =>
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => MoviePilotLibraryArtwork.Compose([Solid(2, 3, 40, 60, 80)], cancellation.Token));
            Assert.Throws<OperationCanceledException>(() => MoviePilotLibraryArtwork.Compose([], cancellation.Token));
        });
        Test("订阅分类封面：合成不修改输入且连续调用结果稳定", () =>
        {
            var source = Solid(4, 6, 40, 60, 80);
            var original = source.Pixels.ToArray();
            var first = MoviePilotLibraryArtwork.Compose([source]);
            var second = MoviePilotLibraryArtwork.Compose([source]);
            Assert.True(source.Pixels.SequenceEqual(original));
            Assert.True(first.Pixels.SequenceEqual(second.Pixels));
        });
    }

    private static MoviePilotSubscription Subscription(string? poster, string? backdrop) => new()
    {
        Id = 1,
        Data = default,
        Media = new MoviePilotMedia { Title = "封面夹具", PosterUrl = poster, BackdropUrl = backdrop }
    };

    private static ArtworkPixels Solid(int width, int height, byte blue, byte green, byte red, byte alpha = 255)
    {
        var pixels = new byte[width * height * 4];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = blue;
            pixels[offset + 1] = green;
            pixels[offset + 2] = red;
            pixels[offset + 3] = alpha;
        }
        return new ArtworkPixels(pixels, width, height);
    }

    private static byte Pixel(ArtworkPixels pixels, int x, int y, int channel) => pixels.Pixels[(y * pixels.Width + x) * 4 + channel];
}
