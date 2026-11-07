using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class GaussianBackdropTests
{
    public static void Register()
    {
        Test("背景亚克力：高斯模糊与颜色遮罩两档退役，背景固定走亚克力", () =>
        {
            var root = Root();
            foreach (var key in new[] { "UseGaussianBackdrop", "UseAcrylicBackdrop", "BackdropOpacity" })
                Assert.True(typeof(UiSettings).GetProperty(key) is null, "设置里仍然留着已退役的键：" + key);
            var page = File.ReadAllText(Path.Combine(root, "src/Momoka.Shell/Views/DetailPage.Backdrop.cs"));
            Assert.Contains("AcrylicBrush", page);
            Assert.False(page.Contains("SolidColorBrush(ReadingShadeColor)", StringComparison.Ordinal));
            var model = File.ReadAllText(Path.Combine(root, "src/Momoka.Shell/ViewModels/DetailViewModel.cs"));
            Assert.False(model.Contains("UseGaussianBackdrop", StringComparison.Ordinal));
            Assert.False(model.Contains("GaussianBackdrop.SigmaFor", StringComparison.Ordinal));
            // 首页轮播那套高斯扩边仍在，删的只是详情页那一支。
            var carousel = File.ReadAllText(Path.Combine(root, "src/Momoka.Core/Emby/CarouselBackdrop.cs"));
            Assert.Contains("GaussianBackdrop.Apply", carousel);
        });
        Test("背景亚克力：四项染色参数缺键读出 55，显式值与 JSON 往返原样保留", () =>
        {
            var settings = SettingsMigration.FromJson("{\"SchemaVersion\":28,\"Ui\":{}}", PassthroughSecretProtector.Instance);
            Assert.Equal(UiSettings.DefaultAcrylicTintOpacity, settings.Ui.ControlTintOpacity);
            Assert.Equal(UiSettings.DefaultAcrylicTintOpacity, settings.Ui.ControlTintLuminosityOpacity);
            Assert.Equal(UiSettings.DefaultAcrylicTintOpacity, settings.Ui.BackdropTintOpacity);
            Assert.Equal(UiSettings.DefaultAcrylicTintOpacity, settings.Ui.BackdropTintLuminosityOpacity);
            settings.Ui.ControlTintOpacity = 12;
            settings.Ui.ControlTintLuminosityOpacity = 34;
            settings.Ui.BackdropTintOpacity = 56;
            settings.Ui.BackdropTintLuminosityOpacity = 78;
            var restored = SettingsMigration.FromJson(JsonSerializer.Serialize(settings), PassthroughSecretProtector.Instance);
            Assert.Equal(12d, restored.Ui.ControlTintOpacity);
            Assert.Equal(34d, restored.Ui.ControlTintLuminosityOpacity);
            Assert.Equal(56d, restored.Ui.BackdropTintOpacity);
            Assert.Equal(78d, restored.Ui.BackdropTintLuminosityOpacity);
            var live = new AppSettings();
            live.Ui.WindowWidth = 1280;
            SettingsPreferences.Apply(live, SettingsPreferences.ToBackupDocument(restored));
            Assert.Equal(56d, live.Ui.BackdropTintOpacity);
            Assert.Equal(78d, live.Ui.BackdropTintLuminosityOpacity);
            SettingsPreferences.Apply(live, new AppSettings());
            Assert.Equal(UiSettings.DefaultAcrylicTintOpacity, live.Ui.BackdropTintOpacity);
            Assert.Equal(UiSettings.DefaultAcrylicTintOpacity, live.Ui.ControlTintLuminosityOpacity);
            Assert.Equal(1280d, live.Ui.WindowWidth);
        });
        Test("背景亚克力：旧文件里的退役键被忽略，不再迁移出浓度或效果", () =>
        {
            var settings = SettingsMigration.FromJson(
                "{\"SchemaVersion\":27,\"Ui\":{\"UseGaussianBackdrop\":true,\"UseAcrylicBackdrop\":true,\"BackdropOpacity\":37}}",
                PassthroughSecretProtector.Instance);
            Assert.Equal(UiSettings.DefaultAcrylicTintOpacity, settings.Ui.BackdropTintOpacity);
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(settings));
            var ui = document.RootElement.GetProperty("Ui");
            foreach (var key in new[] { "UseGaussianBackdrop", "UseAcrylicBackdrop", "BackdropOpacity" })
                Assert.False(ui.TryGetProperty(key, out _), "保存文档仍在写已退役的键：" + key);
        });
        Test("背景亚克力：非有限值回出厂 55，0 与 100 两个端点原样保留", () =>
        {
            foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                var settings = new AppSettings
                {
                    Ui = new UiSettings
                    {
                        ControlTintOpacity = value,
                        ControlTintLuminosityOpacity = value,
                        BackdropTintOpacity = value,
                        BackdropTintLuminosityOpacity = value
                    }
                };
                SettingsMigration.Normalize(settings);
                Assert.Equal(UiSettings.DefaultAcrylicTintOpacity, settings.Ui.ControlTintOpacity);
                Assert.Equal(UiSettings.DefaultAcrylicTintOpacity, settings.Ui.ControlTintLuminosityOpacity);
                Assert.Equal(UiSettings.DefaultAcrylicTintOpacity, settings.Ui.BackdropTintOpacity);
                Assert.Equal(UiSettings.DefaultAcrylicTintOpacity, settings.Ui.BackdropTintLuminosityOpacity);
            }
            foreach (var (input, expected) in new[] { (0d, 0d), (100d, 100d), (-20d, 0d), (180d, 100d), (37.5, 37.5) })
            {
                var settings = new AppSettings { Ui = new UiSettings { BackdropTintOpacity = input } };
                SettingsMigration.Normalize(settings);
                Assert.Equal(expected, settings.Ui.BackdropTintOpacity);
            }
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
        Test("背景文字：详情页不再挂字形软影，货架标题也只剩读色", () =>
        {
            var root = Root();
            var page = File.ReadAllText(Path.Combine(root, "src/Momoka.Shell/Views/DetailPage.xaml"));
            foreach (var name in new[] { "DetailTitleInk", "DetailOverviewInk", "DetailScoreInk", "DetailVideoInk" })
                Assert.False(page.Contains(name, StringComparison.Ordinal), "详情页仍留着高斯专用的字形影子宿主：" + name);
            var shelf = File.ReadAllText(Path.Combine(root, "src/Momoka.Shell/Views/ShelfHead.xaml.cs"));
            Assert.False(shelf.Contains("InkProperty", StringComparison.Ordinal));
            Assert.Contains("ApplyBackdropInk", shelf);
        });
    }

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new InvalidOperationException("Missing repository root.");
    }
}
