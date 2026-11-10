using System.Text.Json;
using Momoka.Configuration;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class LocalMediaSettingsTests
{
    internal static void Register()
    {
        Test("本地媒体库：旧单目录迁移一次且多个库独立保存", () =>
        {
            var property = typeof(LocalMediaSettings).GetProperty("Libraries");
            Assert.NotNull(property, "缺少多个媒体库配置");
            var settings = new AppSettings();
            settings.LocalMedia.SourceDirectory = @"C:\fixture\movies";
            settings.LocalMedia.TargetDirectory = @"D:\fixture\organized";
            var normalized = SettingsMigration.Normalize(settings);
            var libraries = ((System.Collections.IList)property!.GetValue(normalized.LocalMedia)!);
            Assert.Equal(1, libraries.Count);
            var item = libraries[0]!;
            Assert.Equal(@"C:\fixture\movies", item.GetType().GetProperty("SourceDirectory")!.GetValue(item));
            Assert.Equal(false, item.GetType().GetProperty("MonitorEnabled")!.GetValue(item), "迁移不能偷偷启用已有目录监控");
            SettingsMigration.Normalize(settings);
            Assert.Equal(1, libraries.Count);
            var restored = SettingsMigration.FromJson(JsonSerializer.Serialize(settings), PassthroughSecretProtector.Instance);
            Assert.Equal(1, ((System.Collections.IList)property.GetValue(restored.LocalMedia)!).Count);
            Assert.Equal(0, ((System.Collections.IList)property.GetValue(SettingsPreferences.ToBackupDocument(settings).LocalMedia)!).Count);
        });
        Test("本机整理：目录和链接偏好持久化，不进入可分享配置", () =>
        {
            var settings = new AppSettings();
            var property = typeof(AppSettings).GetProperty("LocalMedia");
            Assert.NotNull(property, "缺少独立本机整理设置");
            var local = property!.GetValue(settings)!;
            var type = local.GetType();
            foreach (var name in new[] { "SourceDirectory", "TargetDirectory", "Method", "Recursive", "Provider", "ProtectedTmdbToken" })
                Assert.NotNull(type.GetProperty(name), "缺少本机整理设置：" + name);
            type.GetProperty("SourceDirectory")!.SetValue(local, @"C:\fixture\source");
            type.GetProperty("TargetDirectory")!.SetValue(local, @"D:\fixture\library");
            type.GetProperty("Method")!.SetValue(local, "symboliclink");
            type.GetProperty("Recursive")!.SetValue(local, false);
            type.GetProperty("ProtectedTmdbToken")!.SetValue(local, "encrypted-fixture");
            var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
            var reread = property.GetValue(restored)!;
            Assert.Equal(@"C:\fixture\source", type.GetProperty("SourceDirectory")!.GetValue(reread));
            Assert.Equal(@"D:\fixture\library", type.GetProperty("TargetDirectory")!.GetValue(reread));
            Assert.Equal("symboliclink", type.GetProperty("Method")!.GetValue(reread));
            Assert.Equal(false, type.GetProperty("Recursive")!.GetValue(reread));
            var backup = property.GetValue(SettingsPreferences.ToBackupDocument(settings))!;
            Assert.Equal("", type.GetProperty("SourceDirectory")!.GetValue(backup));
            Assert.Equal("", type.GetProperty("ProtectedTmdbToken")!.GetValue(backup));
            SettingsPreferences.Apply(settings, new());
            Assert.Equal(@"C:\fixture\source", type.GetProperty("SourceDirectory")!.GetValue(local));
        });
    }
}
