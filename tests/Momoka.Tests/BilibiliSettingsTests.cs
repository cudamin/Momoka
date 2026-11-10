using System.Text.Json;
using Momoka.Configuration;
using Momoka.Danmaku;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class BilibiliSettingsTests
{
    internal static void Register()
    {
        Test("B站弹幕：新增来源保留旧枚举含义，未登录不可用", () =>
        {
            var settings = new AppSettings();
            settings.Danmaku.Enabled = true;
            settings.Danmaku.Source = (DanmakuSourceMode)3;
            settings.Danmaku.Normalize();
            Assert.Equal(3, (int)settings.Danmaku.Source);
            Assert.Equal("哔哩哔哩", DanmakuSourceRules.ModeLabel(settings.Danmaku.Source));
            Assert.False(DanmakuSourceRules.IsAvailable(settings));
            Assert.False(DanmakuSourceRules.Includes(DanmakuSourceMode.Both, (DanmakuSource)2));
            Assert.False(DanmakuSourceRules.CanSend(settings.Danmaku.Source));
        });

        Test("B站弹幕：登录身份不导出且恢复偏好保留本机身份", () =>
        {
            var live = new AppSettings();
            live.Danmaku = JsonSerializer.Deserialize<DanmakuSettings>("""{"Enabled":true,"Source":3,"ProtectedBilibiliCookie":"encrypted-placeholder","BilibiliUserId":42,"BilibiliUserName":"匿名测试用户"}""")!;
            Assert.True(DanmakuSourceRules.IsAvailable(live));
            var backup = JsonSerializer.Serialize(SettingsPreferences.ToBackupDocument(live));
            Assert.False(backup.Contains("encrypted-placeholder"));
            Assert.False(backup.Contains("匿名测试用户"));
            SettingsPreferences.Apply(live, new AppSettings());
            var json = JsonSerializer.SerializeToElement(live.Danmaku);
            Assert.Equal("encrypted-placeholder", json.GetProperty("ProtectedBilibiliCookie").GetString());
            Assert.Equal(42L, json.GetProperty("BilibiliUserId").GetInt64());
            Assert.Equal("匿名测试用户", json.GetProperty("BilibiliUserName").GetString());
        });
    }
}
