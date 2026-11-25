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
        Test("本机整理：目录、识别方式与元数据目录持久化，不进入可分享配置", () =>
        {
            var settings = new AppSettings();
            var property = typeof(AppSettings).GetProperty("LocalMedia");
            Assert.NotNull(property, "缺少独立本机整理设置");
            var local = property!.GetValue(settings)!;
            var type = local.GetType();
            foreach (var name in new[] { "Provider", "Recursive", "MetadataDirectory", "ProtectedTmdbToken", "Libraries" })
                Assert.NotNull(type.GetProperty(name), "缺少本机整理设置：" + name);
            type.GetProperty("Provider")!.SetValue(local, "moviepilot");
            type.GetProperty("Recursive")!.SetValue(local, false);
            type.GetProperty("MetadataDirectory")!.SetValue(local, @"D:\fixture\metadata");
            type.GetProperty("ProtectedTmdbToken")!.SetValue(local, "encrypted-fixture");
            var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
            var reread = property.GetValue(restored)!;
            Assert.Equal("moviepilot", type.GetProperty("Provider")!.GetValue(reread));
            Assert.Equal(false, type.GetProperty("Recursive")!.GetValue(reread));
            Assert.Equal(@"D:\fixture\metadata", type.GetProperty("MetadataDirectory")!.GetValue(reread));
            var backup = property.GetValue(SettingsPreferences.ToBackupDocument(settings))!;
            Assert.Equal("", type.GetProperty("MetadataDirectory")!.GetValue(backup));
            Assert.Equal("", type.GetProperty("ProtectedTmdbToken")!.GetValue(backup));
            SettingsPreferences.Apply(settings, new());
            Assert.Equal(@"D:\fixture\metadata", type.GetProperty("MetadataDirectory")!.GetValue(local));
        });
        Test("本机整理：旧链接与目标字段不再出现在媒体库模型", () =>
        {
            Assert.Null(typeof(LocalMediaLibrarySettings).GetProperty("TargetDirectory"));
            Assert.Null(typeof(LocalMediaLibrarySettings).GetProperty("Method"));
            Assert.Null(typeof(LocalMediaLibrarySettings).GetProperty("Provider"));
        });
    }
}
