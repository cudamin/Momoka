using Momoka.Configuration;
using Momoka.Mpv;
using Momoka.Playback;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MpvCustomConfigTests
{
    public static void Register()
    {
        Test("自定义配置：只在启用的内置独占模式解析路径", () =>
        {
            var settings = new MpvSettings();
            Assert.Equal<string?>(null, MpvCustomConfig.Resolve(settings));
            settings.CustomConfigEnabled = true;
            Assert.Throws<InvalidOperationException>(() => MpvCustomConfig.Resolve(settings));
            settings.CustomConfigPath = "  \"C:\\配置文件\\mpv.conf\"  ";
            Assert.Equal(@"C:\配置文件\mpv.conf", MpvCustomConfig.Resolve(settings));
            settings.Pipeline = VideoPipelineKind.Integrated;
            Assert.Equal<string?>(null, MpvCustomConfig.Resolve(settings));
            settings.Pipeline = VideoPipelineKind.Standalone;
            settings.Backend = MpvBackendKind.ExternalMpv;
            Assert.Equal<string?>(null, MpvCustomConfig.Resolve(settings));
            Assert.True(settings.CustomConfigEnabled);
        });
        Test("自定义配置：偏好恢复和重置覆盖开关及路径", () =>
        {
            var settings = new AppSettings();
            var custom = new AppSettings { Mpv = new() { CustomConfigEnabled = true, CustomConfigPath = @"C:\配置\mpv.conf" } };
            SettingsPreferences.Apply(settings, custom);
            Assert.True(settings.Mpv.CustomConfigEnabled);
            Assert.Equal(custom.Mpv.CustomConfigPath, settings.Mpv.CustomConfigPath);
            SettingsPreferences.Apply(settings, new AppSettings());
            Assert.False(settings.Mpv.CustomConfigEnabled);
            Assert.Equal("", settings.Mpv.CustomConfigPath);
        });
        Test("自定义配置：展开资源及嵌套引用，保留配置组并隔离宿主选项", () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "momoka-config-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var source = Path.Combine(folder, "mpv.conf");
            var include = Path.Combine(folder, "画质.conf");
            var content = "gpu-api=d3d11\ninclude='画质.conf'\n[电影]\nprofile-cond=width > 720\nlog-file=secret.log\ninput-ipc-server=pipe\n";
            File.WriteAllText(source, content);
            File.WriteAllText(include, "glsl-shaders=~~/shaders/test.glsl\nosd-duration=2000\n");
            string prepared;
            string nested;
            try
            {
                using (var config = MpvCustomConfig.Prepare(source))
                {
                    prepared = config.FilePath;
                    var lines = File.ReadAllLines(prepared);
                    Assert.True(lines.Contains("gpu-api=d3d11") && lines.Contains("[电影]") && lines.Contains("profile-cond=width > 720"));
                    Assert.False(lines.Any(line => line.StartsWith("log-file") || line.StartsWith("input-ipc")));
                    nested = lines.Single(line => line.StartsWith("include="))[8..];
                    Assert.True(File.ReadAllText(nested).Contains(folder.Replace('\\', '/') + "/shaders/test.glsl"));
                    Assert.Equal(content, File.ReadAllText(source));
                }
                Assert.False(File.Exists(prepared));
                Assert.False(File.Exists(nested));
                File.WriteAllText(include, "include=mpv.conf");
                Assert.Throws<InvalidOperationException>(() => MpvCustomConfig.Prepare(source));
                Assert.Throws<InvalidOperationException>(() => MpvCustomConfig.Prepare(Path.Combine(folder, "missing.conf")));
            }
            finally { Directory.Delete(folder, recursive: true); }
        });
        Test("自定义配置：条件配置组不能开启日志和覆盖起播边界", () =>
        {
            foreach (var name in new[] { "log-file", "no-terminal", "scripts-append", "input-ipc-client", "playlist", "http-header-fields-append", "no-config", "save-watch-history" })
                Assert.True(MpvCustomConfig.IsHostOption(name));
            foreach (var name in new[] { "gpu-api", "gpu-context", "profile", "profile-cond", "include", "hwdec", "glsl-shaders-append", "sub-font-size" })
                Assert.False(MpvCustomConfig.IsHostOption(name));
        });
        Test("自定义配置：启用后换片重新建实例，关闭后恢复同窗复用", () =>
        {
            var signature = new PlaybackLaunchSignature(VideoPipelineKind.Standalone, [], [], [], []);
            Assert.True(InlineSwitch.SameSignature(signature, signature));
            Assert.False(InlineSwitch.SameSignature(signature, signature with { UsesCustomConfig = true }));
            Assert.False(InlineSwitch.SameSignature(signature with { UsesCustomConfig = true }, signature));
            Assert.False(InlineSwitch.SameSignature(signature with { UsesCustomConfig = true }, signature with { UsesCustomConfig = true }));
        });
    }
}
