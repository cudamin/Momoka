using System.Text.Json;
using System.IO;
using Momoka.Configuration;
using Momoka.Scrobbling;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class ScrobblingSettingsTests
{
    internal static void Register()
    {
        Test("打格子设置：播放匹配不暴露手动添加关联入口", () =>
        {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Momoka.Shell", "ViewModels", "SettingsViewModel.Scrobbling.cs"));
            Assert.DoesNotContain("添加作品关联", source);
            Assert.DoesNotContain("移除关联", source);
            Assert.DoesNotContain("ScrobblingMappingDialog", source);
        });

        Test("打格子弹窗：预取消不打开，显示期间取消才关闭且结束后退订", () =>
        {
            var shown = false;
            Assert.Throws<OperationCanceledException>(() => Momoka.Shell.Platform.CancelableDialog.ShowAsync(
                () => { shown = true; return Task.FromResult(1); }, () => { }, action => action(), new CancellationToken(true))
                .GetAwaiter().GetResult());
            Assert.False(shown);
            using var cancellation = new CancellationTokenSource();
            var closed = 0;
            var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.Throws<OperationCanceledException>(() => Momoka.Shell.Platform.CancelableDialog.ShowAsync(() =>
            {
                shown = true;
                cancellation.Cancel();
                return completion.Task;
            }, () =>
            {
                Assert.True(shown);
                closed++;
                completion.TrySetResult(0);
            }, action => action(), cancellation.Token).GetAwaiter().GetResult());
            Assert.Equal(1, closed);
            using var after = new CancellationTokenSource();
            Assert.Equal(3, Momoka.Shell.Platform.CancelableDialog.ShowAsync(() => Task.FromResult(3),
                () => closed++, action => action(), after.Token).GetAwaiter().GetResult());
            after.Cancel();
            Assert.Equal(1, closed);
        });
        Test("豆瓣表单：只允许精确站点收藏提交路径", () =>
        {
            var script = DoubanProtocol.PrepareMarkScript(8, false, "fixture-nonce");
            Assert.Contains("'/j/subject/'+id+'/interest'", script);
            Assert.Contains("'/subject/'+id+'/interest'", script);
            Assert.Contains("action.search || action.hash", script);
        });
        Test("打格子配置：缺键和空分区均默认关闭", () =>
        {
            foreach (var json in new[] { "{\"SchemaVersion\":29}", "{\"SchemaVersion\":29,\"Scrobbling\":null}",
                "{\"SchemaVersion\":29,\"Scrobbling\":{\"Bangumi\":null,\"Douban\":null,\"Mappings\":[{\"Provider\":0,\"SubjectId\":42}]}}" })
            {
                var settings = SettingsMigration.FromJson(json, PassthroughSecretProtector.Instance);
                Assert.False(settings.Scrobbling.Bangumi.Enabled);
                Assert.False(settings.Scrobbling.Douban.Enabled);
                Assert.DoesNotContain("Mappings", JsonSerializer.Serialize(settings.Scrobbling));
            }
        });
        Test("打格子配置：显式空密文按损坏配置拒绝", () =>
            Assert.Throws<InvalidDataException>(() => SettingsMigration.FromJson(
                "{\"SchemaVersion\":29,\"Scrobbling\":{\"Bangumi\":{\"ProtectedAccessToken\":null}}}", PassthroughSecretProtector.Instance)));
        Test("打格子配置：旧媒体用户名忽略且不回写，密钥保留", () =>
        {
            var settings = SettingsMigration.FromJson(
                "{\"SchemaVersion\":29,\"Scrobbling\":{\"Bangumi\":{\"Enabled\":true,\"MediaServerUsername\":\"old-user\",\"ProtectedAccessToken\":\"synthetic-placeholder\"},\"Douban\":{\"Enabled\":true,\"MediaServerUsername\":\"old-user\"}}}",
                PassthroughSecretProtector.Instance);
            Assert.True(settings.Scrobbling.Bangumi.Enabled);
            Assert.True(settings.Scrobbling.Douban.Enabled);
            Assert.Equal("synthetic-placeholder", settings.Scrobbling.Bangumi.ProtectedAccessToken);
            Assert.DoesNotContain("MediaServerUsername", JsonSerializer.Serialize(settings.Scrobbling));
        });
        Test("打格子配置：偏好备份和恢复不迁移本机连接", () =>
        {
            var settings = new AppSettings();
            settings.Scrobbling.Bangumi.Enabled = true;
            settings.Scrobbling.Bangumi.ProtectedAccessToken = "synthetic-protected-token";
            var json = JsonSerializer.Serialize(SettingsPreferences.ToBackupDocument(settings));
            Assert.DoesNotContain("synthetic-protected-token", json);
            SettingsPreferences.Apply(settings, new AppSettings());
            Assert.True(settings.Scrobbling.Bangumi.Enabled);
            Assert.Equal("synthetic-protected-token", settings.Scrobbling.Bangumi.ProtectedAccessToken);
        });
        Test("豆瓣来源：只接受精确可信HTTPS主机", () =>
        {
            Assert.True(DoubanProtocol.IsTrustedNavigation("https://movie.douban.com/subject/1/"));
            Assert.True(DoubanProtocol.IsTrustedNavigation("https://accounts.douban.com/passport/login", true));
            Assert.True(DoubanProtocol.IsTrustedNavigation("https://search.douban.com/movie/subject_search?search_text=%E6%B5%8B%E8%AF%95&cat=1002"));
            foreach (var address in new[] { "http://movie.douban.com/", "https://movie.douban.com.evil.invalid/", "https://evil.invalid/",
                "https://movie.douban.com:8443/", "https://user@movie.douban.com/", "file:///x", "https://accounts.douban.com/",
                "https://search.douban.com/movie/subject_search?search_text=x&cat=1001",
                "https://search.douban.com/movie/other?search_text=x&cat=1002" })
                Assert.False(DoubanProtocol.IsTrustedNavigation(address), address);
        });
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new InvalidOperationException("本树源码缺失");
    }
}
