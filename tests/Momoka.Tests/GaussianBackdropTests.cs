using System.Text.Json;
using System.Xml.Linq;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class GaussianBackdropTests
{
    public static void Register()
    {
        Test("背景接缝：高斯跨图片和界面两侧，总范围5%且两端接原色", () =>
        {
            const int width = 4, height = 200;
            var pixels = Enumerable.Repeat((byte)240, width * height * 4).ToArray();
            var strip = GaussianBackdrop.Seam(pixels, width, height, 180, 10, [12, 17, 22], 0);
            Assert.Equal(width * 10 * 4, strip.Length);
            Assert.Equal((byte)240, strip[0]);
            Assert.True(strip[4 * width * 4] < 240);
            Assert.True(strip[5 * width * 4] > 12);
            Assert.Equal((byte)12, strip[^4]);
            Assert.Equal((byte)17, strip[^3]);
            Assert.Equal((byte)22, strip[^2]);
            Assert.True(Enumerable.Range(0, width * 10).All(index => strip[index * 4 + 3] == 255));
            Assert.True(pixels.All(value => value == 240));
            Assert.Throws<OperationCanceledException>(() => GaussianBackdrop.Seam(pixels, width, height, 180, 10,
                [12, 17, 22], 0, new CancellationToken(true)));
            var textured = new byte[width * height * 4];
            for (var i = 0; i < width * height; i++) textured[i * 4] = (byte)(i % width % 2 == 0 ? 255 : 0);
            var textureSeam = GaussianBackdrop.Seam(textured, width, height, 180, 10, [12, 17, 22], 0);
            Assert.Equal((byte)255, textureSeam[0]);
            Assert.True(textureSeam[width * 4] > 230);
        });
        Test("高斯背景：缺键默认暗罩和原底色，两模式JSON与恢复独立保存", () =>
        {
            var settings = SettingsMigration.FromJson("{\"SchemaVersion\":26,\"Ui\":{}}", PassthroughSecretProtector.Instance);
            Assert.False(settings.Ui.UseGaussianBackdrop);
            Assert.False(settings.Ui.UseAcrylicControls);
            settings.Ui.UseGaussianBackdrop = true;
            settings.Ui.UseAcrylicControls = true;
            settings.Ui.BackdropOpacity = 37;
            var restored = SettingsMigration.FromJson(JsonSerializer.Serialize(settings), PassthroughSecretProtector.Instance);
            Assert.True(restored.Ui.UseGaussianBackdrop);
            Assert.True(restored.Ui.UseAcrylicControls);
            var live = new AppSettings();
            live.Ui.WindowWidth = 1280;
            SettingsPreferences.Apply(live, SettingsPreferences.ToBackupDocument(restored));
            Assert.True(live.Ui.UseGaussianBackdrop);
            Assert.True(live.Ui.UseAcrylicControls);
            Assert.Equal(37d, live.Ui.BackdropOpacity);
            SettingsPreferences.Apply(live, new AppSettings());
            Assert.False(live.Ui.UseGaussianBackdrop);
            Assert.False(live.Ui.UseAcrylicControls);
            Assert.Equal(UiSettings.DefaultBackdropOpacity, live.Ui.BackdropOpacity);
            Assert.Equal(1280d, live.Ui.WindowWidth);
        });
        Test("真高斯：单点响应符合归一化指数核而非盒式", () =>
        {
            var pixels = new byte[13 * 4];
            for (var x = 0; x < 13; x++) pixels[x * 4 + 3] = 255;
            pixels[6 * 4] = 255;
            GaussianBackdrop.Apply(pixels, 13, 1, 1);
            var sum = Enumerable.Range(-3, 7).Sum(x => Math.Exp(-0.5 * x * x));
            for (var x = 0; x < 13; x++)
            {
                var distance = x - 6;
                var expected = Math.Abs(distance) > 3 ? 0 : (byte)Math.Round(255 * Math.Exp(-0.5 * distance * distance) / sum);
                Assert.Equal((byte)expected, pixels[x * 4]);
                Assert.Equal((byte)255, pixels[x * 4 + 3]);
            }
        });
        Test("背景收边：只模糊底部5%，保留上方95%像素和alpha", () =>
        {
            const int width = 24, height = 60;
            var pixels = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var at = (y * width + x) * 4;
                    pixels[at] = (byte)(x % 2 == 0 ? 255 : 0);
                    pixels[at + 3] = (byte)(y + 100);
                }
            var original = pixels.ToArray();
            GaussianBackdrop.ApplyBottom(pixels, width, height, 2);
            Assert.True(original.AsSpan(0, width * 57 * 4).SequenceEqual(pixels.AsSpan(0, width * 57 * 4)));
            Assert.True(pixels[(height - 1) * width * 4 + 8 * 4] < 200);
            for (var at = 3; at < pixels.Length; at += 4) Assert.Equal(original[at], pixels[at]);
            var before = pixels.ToArray();
            Assert.Throws<OperationCanceledException>(() => GaussianBackdrop.ApplyBottom(pixels, width, height, 2, new CancellationToken(true)));
            Assert.True(before.AsSpan().SequenceEqual(pixels));
            var cropped = original.ToArray();
            GaussianBackdrop.ApplyBottom(cropped, width, height, 2, end: 0.8);
            Assert.True(original.AsSpan(0, width * 45 * 4).SequenceEqual(cropped.AsSpan(0, width * 45 * 4)));
            Assert.True(original.AsSpan(width * 48 * 4).SequenceEqual(cropped.AsSpan(width * 48 * 4)));
            Assert.True(cropped[width * 47 * 4 + 8 * 4] < 200);
            var uniform = Enumerable.Repeat((byte)73, width * height * 4).ToArray();
            GaussianBackdrop.ApplyBottom(uniform, width, height, 2);
            Assert.True(uniform.All(value => value == 73));
        });
        Test("真高斯：纯色与alpha不变，边缘夹取没有黑圈", () =>
        {
            var pixels = new byte[17 * 9 * 4];
            for (var i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 31; pixels[i + 1] = 127; pixels[i + 2] = 213; pixels[i + 3] = (byte)(i % 255);
            }
            var original = pixels.ToArray();
            GaussianBackdrop.Apply(pixels, 17, 9, 12);
            Assert.True(original.AsSpan().SequenceEqual(pixels));
        });
        Test("真高斯：两趟分离卷积对称，零半径与取消不改原图", () =>
        {
            const int size = 15;
            var pixels = new byte[size * size * 4];
            pixels[(7 * size + 7) * 4] = 255;
            GaussianBackdrop.Apply(pixels, size, size, 1.5);
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    Assert.Equal(pixels[(y * size + x) * 4], pixels[(x * size + y) * 4]);
                    Assert.Equal(pixels[(y * size + x) * 4], pixels[(y * size + size - x - 1) * 4]);
                }
            var before = pixels.ToArray();
            GaussianBackdrop.Apply(pixels, size, size, 0);
            Assert.True(before.AsSpan().SequenceEqual(pixels));
            Assert.Throws<OperationCanceledException>(() => GaussianBackdrop.Apply(pixels, size, size, 12, new CancellationToken(true)));
            Assert.True(before.AsSpan().SequenceEqual(pixels));
            Assert.Throws<ArgumentException>(() => GaussianBackdrop.Apply(new byte[4], size, size, 12));
        });
        Test("高斯文字：详情读数视频导演与货架标题只加字形软影", () =>
        {
            var root = Root();
            var page = XDocument.Load(Path.Combine(root, "src/Momoka.Shell/Views/DetailPage.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            foreach (var name in new[] { "DetailScoreInk", "DetailScoreSourceInk", "DetailVideoInk", "DetailDirectorsInk" })
            {
                var host = page.Descendants().Single(element => (string?)element.Attribute(x + "Name") == name);
                Assert.Equal("Border", host.Name.LocalName);
                Assert.Null(host.Attribute("Background"));
                Assert.Equal("False", (string?)host.Attribute("IsHitTestVisible"));
                Assert.Equal("Grid", host.Parent?.Name.LocalName);
                Assert.Equal(host, host.Parent!.Elements().First());
            }
            var shelf = XDocument.Load(Path.Combine(root, "src/Momoka.Shell/Views/ShelfHead.xaml"));
            var titleInk = shelf.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "TitleInk");
            Assert.Null(titleInk.Attribute("Background"));
            var code = File.ReadAllText(Path.Combine(root, "src/Momoka.Shell/Views/ShelfHead.xaml.cs"));
            Assert.Contains("nameof(Ink)", code);
            Assert.Contains("TextInk.Attach", code);
            Assert.False(code.Contains("AttachHalo", StringComparison.Ordinal));
        });
    }

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new InvalidOperationException("Missing repository root.");
    }
}
