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
        Test("高斯背景：仅详情沉浸生效，普通版式和首页不读取高斯偏好", () =>
        {
            var root = Root();
            var model = File.ReadAllText(Path.Combine(root, "src/Momoka.Shell/ViewModels/DetailViewModel.cs"));
            Assert.Contains("public bool UseGaussianBackdrop => Showcase &&", model);
            var home = File.ReadAllText(Path.Combine(root, "src/Momoka.Shell/Views/HomeBanner.xaml.cs"));
            Assert.False(home.Contains("slide.SetGaussianBackdrop(ui.UseGaussianBackdrop)", StringComparison.Ordinal));
            Assert.False(home.Contains("slide.SetGaussianBackdrop(_appearanceSettings?.UseGaussianBackdrop", StringComparison.Ordinal));
        });
        Test("高斯背景：固定背景承担阅读遮罩，模糊不再撤掉保护", () =>
        {
            var source = File.ReadAllText(Path.Combine(Root(), "src/Momoka.Shell/Views/DetailPage.Backdrop.cs"));
            Assert.Contains("AppearanceOpacity.ReadingShadeAlpha", source);
            Assert.Contains("ViewModel.GaussianBackdropApplied", source);
            Assert.False(source.Contains("? Color.FromArgb(0, 0, 0, 0)", StringComparison.Ordinal));
        });
        Test("详情材质：旧高斯键往返与恢复默认固定亚克力，浓度独立保存", () =>
        {
            var settings = SettingsMigration.FromJson("{\"SchemaVersion\":26,\"Ui\":{}}", PassthroughSecretProtector.Instance);
            Assert.False(settings.Ui.UseGaussianBackdrop);
            Assert.True(settings.Ui.UseAcrylicControls);
            settings.Ui.UseGaussianBackdrop = true;
            settings.Ui.UseAcrylicControls = true;
            settings.Ui.BackdropOpacity = 37;
            var restored = SettingsMigration.FromJson(JsonSerializer.Serialize(settings), PassthroughSecretProtector.Instance);
            Assert.False(restored.Ui.UseGaussianBackdrop);
            Assert.True(restored.Ui.UseAcrylicBackdrop);
            Assert.True(restored.Ui.UseAcrylicControls);
            var live = new AppSettings();
            live.Ui.WindowWidth = 1280;
            SettingsPreferences.Apply(live, SettingsPreferences.ToBackupDocument(restored));
            Assert.False(live.Ui.UseGaussianBackdrop);
            Assert.True(live.Ui.UseAcrylicBackdrop);
            Assert.True(live.Ui.UseAcrylicControls);
            Assert.Equal(37d, live.Ui.BackdropOpacity);
            SettingsPreferences.Apply(live, new AppSettings());
            Assert.False(live.Ui.UseGaussianBackdrop);
            Assert.True(live.Ui.UseAcrylicControls);
            Assert.Equal(UiSettings.DefaultBackdropOpacity, live.Ui.BackdropOpacity);
            Assert.Equal(1280d, live.Ui.WindowWidth);
        });
        Test("亚克力背景：缺键与新安装默认固定开启", () =>
        {
            AssertStoredAcrylic(new AppSettings(), true);
            var settings = SettingsMigration.FromJson("{\"SchemaVersion\":27,\"Ui\":{}}", PassthroughSecretProtector.Instance);
            AssertStoredAcrylic(settings, true);
            Assert.False(settings.Ui.UseGaussianBackdrop);
        });
        Test("亚克力背景：JSON往返保留强度，旧关闭控件材质归一化开启", () =>
        {
            var settings = SettingsMigration.FromJson(
                "{\"SchemaVersion\":27,\"Ui\":{\"UseAcrylicBackdrop\":true,\"BackdropOpacity\":37,\"UseAcrylicControls\":false}}",
                PassthroughSecretProtector.Instance);
            var restored = SettingsMigration.FromJson(JsonSerializer.Serialize(settings), PassthroughSecretProtector.Instance);
            AssertStoredAcrylic(restored, true);
            Assert.False(restored.Ui.UseGaussianBackdrop);
            Assert.True(restored.Ui.UseAcrylicControls);
            Assert.Equal(37d, restored.Ui.BackdropOpacity);
        });
        Test("亚克力背景：导入偏好原地恢复，恢复默认仍开启并保留窗口", () =>
        {
            var source = SettingsMigration.FromJson(
                "{\"SchemaVersion\":27,\"Ui\":{\"UseAcrylicBackdrop\":true,\"BackdropOpacity\":63}}",
                PassthroughSecretProtector.Instance);
            var live = new AppSettings();
            var ui = live.Ui;
            ui.WindowWidth = 1280;
            ui.UseGaussianBackdrop = true;
            SettingsPreferences.Apply(live, SettingsPreferences.ToBackupDocument(source));
            Assert.True(ReferenceEquals(ui, live.Ui));
            AssertStoredAcrylic(live, true);
            Assert.False(live.Ui.UseGaussianBackdrop);
            Assert.Equal(63d, live.Ui.BackdropOpacity);
            SettingsPreferences.Apply(live, new AppSettings());
            AssertStoredAcrylic(live, true);
            Assert.False(live.Ui.UseGaussianBackdrop);
            Assert.Equal(UiSettings.DefaultBackdropOpacity, live.Ui.BackdropOpacity);
            Assert.Equal(1280d, live.Ui.WindowWidth);
        });
        Test("亚克力背景：冲突配置与旧高斯文件统一使用亚克力", () =>
        {
            var conflict = SettingsMigration.FromJson(
                "{\"SchemaVersion\":27,\"Ui\":{\"UseAcrylicBackdrop\":true,\"UseGaussianBackdrop\":true}}",
                PassthroughSecretProtector.Instance);
            Assert.False(conflict.Ui.UseGaussianBackdrop);
            AssertStoredAcrylic(conflict, true);
            var legacy = SettingsMigration.FromJson(
                "{\"SchemaVersion\":27,\"Ui\":{\"UseGaussianBackdrop\":true}}",
                PassthroughSecretProtector.Instance);
            Assert.False(legacy.Ui.UseGaussianBackdrop);
            AssertStoredAcrylic(legacy, true);
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

    private static void AssertStoredAcrylic(AppSettings settings, bool expected)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(settings));
        var ui = document.RootElement.GetProperty("Ui");
        Assert.True(ui.TryGetProperty("UseAcrylicBackdrop", out var acrylic), "持久化文档缺少亚克力背景偏好");
        Assert.Equal(expected, acrylic.GetBoolean());
    }

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new InvalidOperationException("Missing repository root.");
    }
}
