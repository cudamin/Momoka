using System.Text.Json;
using Momoka.Configuration;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class LocalMediaSettingsTests
{
    internal static void Register()
    {
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
