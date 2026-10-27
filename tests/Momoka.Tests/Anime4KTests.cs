using System.Security.Cryptography;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Infrastructure;
using Momoka.Mpv;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Anime4KTests
{
    public static void Register()
    {
        Test("Anime4K：着色器离线探针分区可独立或与设置一起运行，未知分区仍拒绝", () =>
        {
            Assert.True(ShellProbeSections.IsKnown("shaders"));
            Assert.Equal(1, ShellProbeSections.All.Count(section => section == "shaders"));
            var selected = StartupArgs.ShellProbeSectionFilter(["--probe-shell", "--probe-section", "SHADERS"]);
            Assert.Equal("shaders", string.Join(",", selected!));
            selected = StartupArgs.ShellProbeSectionFilter(["--probe-shell", "--probe-section", "settings,shaders"]);
            Assert.Equal("settings,shaders", string.Join(",", selected!));
            Assert.Throws<ArgumentException>(() => StartupArgs.ShellProbeSectionFilter(
                ["--probe-shell", "--probe-section", "shader-nonsense"]));
        });

        Test("Anime4K：固定配置组在三档显卡和两根片源轴下各只出现一次", () =>
        {
            var preset = Group();
            foreach (var gpu in Enum.GetValues<GpuTier>())
                foreach (var vintage in new[] { false, true })
                    foreach (var fast in new[] { false, true })
                    {
                        var choices = ShaderGroupCatalog.For(gpu, vintage, fast);
                        Assert.Equal(1, choices.Count(group => group.Id == "anime4k"));
                        Assert.True(ReferenceEquals(preset, ShaderGroupCatalog.Find(" ANIME4K ", gpu, vintage, fast)),
                            "显卡、老片源和帧率不改写用户手选的上游固定链");
                        Assert.Equal("anime4k", choices[^1].Id, "原有八档次序不变，配置组追加在后面");
                    }
            Assert.Equal(1, ShaderGroupCatalog.Ids.Count(id => id == "anime4k"));
            Assert.Equal(1, ShaderGroupCatalog.All.Count(group => group.Id == "anime4k"), "固定链不重复算十二次");
            Assert.Contains("Anime4K", preset.DisplayName);
            Assert.DoesNotContain("倍", preset.DisplayName, "固定配置组不冒充某一放大倍率档位");
        });

        Test("Anime4K：保留 dyphire 配置的四文件次序，不冒充 CNN 倍增放大器", () =>
        {
            var preset = Group();
            Assert.Equal("igv/KrigBilateral.glsl|Anime4K/glsl/Restore/Anime4K_Restore_CNN_Soft_M.glsl|"
                + "Anime4K/glsl/Restore/Anime4K_Clamp_Highlights.glsl|igv/SSimDownscaler.glsl",
                string.Join("|", preset.Files));
            Assert.Equal(0, preset.Packages(ShaderRole.LumaUpscale));
            Assert.Equal(1, preset.Packages(ShaderRole.ChromaReconstruct));
            Assert.Equal(0, preset.Shaders.Sum(shader => shader.ComputePasses));
            Assert.Equal(8, preset.Shaders[1].Passes);
            Assert.Equal(3, preset.Shaders[2].Passes);
            Assert.Equal("", string.Join("\n", ShaderChainRules.Problems(preset)));
        });

        Test("Anime4K：缩小前置参数随链下发，每个参数都能还原", () =>
        {
            var preset = Group();
            var options = preset.Options.ToDictionary(pair => pair.Key, pair => pair.Value);
            Assert.Equal("ewa_lanczossharp", options["scale"]);
            Assert.Equal("mitchell", options["dscale"], "不照搬与 SSimDownscaler 不兼容的全局 dscale=lanczos");
            Assert.Equal("no", options["linear-downscaling"]);
            Assert.Equal("yes", options["correct-downscaling"]);
            Assert.Equal("yes", options["sigmoid-upscaling"]);
            Assert.False(options.ContainsKey("deband"), "固定配置不偷偷叠加老片源修复");
            foreach (var name in options.Keys)
                Assert.True(ShaderGroupCatalog.NeutralOptions.Any(pair => pair.Key == name), name);
            Assert.True(ShaderChainRules.ConflictsWith(preset, ["set", "dscale", "lanczos"]));
            Assert.False(ShaderChainRules.ConflictsWith(preset, ["set", "dscale", "mitchell"]));
        });

        Test("Anime4K：长期偏好规范化与设置文件往返保留配置组", () =>
        {
            foreach (var gpu in Enum.GetValues<GpuTier>())
            {
                var settings = SettingsMigration.NewDefaults();
                settings.Shaders.Enabled = true;
                settings.Shaders.Gpu = gpu;
                settings.Shaders.ManualGroup = " ANIME4K ";
                SettingsMigration.Normalize(settings);
                Assert.Equal("anime4k", settings.Shaders.ManualGroup);
                var loaded = SettingsMigration.FromJson(JsonSerializer.Serialize(settings, SettingsSerializer.WriteOptions),
                    PassthroughSecretProtector.Instance);
                Assert.Equal("anime4k", loaded.Shaders.ManualGroup);
                Assert.Equal(gpu, loaded.Shaders.Gpu);
                Assert.True(loaded.Shaders.Enabled);
            }
        });

        Test("Anime4K：手动选择跨缩小到大倍数，不被动画识别、老片源或高帧率悄悄替换", () =>
        {
            var settings = new ShaderAutomationSettings
            {
                Enabled = true,
                ManualGroup = "anime4k",
                AutoAnimeProfile = false,
                RestoreVintageSources = true
            };
            foreach (var source in new[] { (3840, 2160), (1920, 1080), (1280, 720), (854, 480) })
                foreach (var gpu in Enum.GetValues<GpuTier>())
                {
                    settings.Gpu = gpu;
                    var result = settings.Resolve(false, source.Item1, source.Item2, 2560, 1440, 60);
                    Assert.True(ReferenceEquals(Group(), result.Group));
                    Assert.Contains("手动指定", result.Reason);
                    Assert.Contains("Anime4K", result.Reason);
                    Assert.DoesNotContain("已换成便宜的链", result.Reason);
                    Assert.DoesNotContain("老片源修复", result.Reason);
                }
        });

        Test("Anime4K：关闭总开关与八千分辨率保护仍然优先，恢复后偏好未丢", () =>
        {
            var settings = new ShaderAutomationSettings { Enabled = false, ManualGroup = "anime4k" };
            Assert.Null(settings.Resolve(true, 1920, 1080, 2560, 1440).Group);
            Assert.Equal("anime4k", settings.ManualGroup);
            settings.Enabled = true;
            Assert.Null(settings.Resolve(true, 7680, 4320, 2560, 1440).Group);
            Assert.Equal("anime4k", settings.ManualGroup);
            settings.DisableForUltraHighRes = false;
            Assert.Equal("anime4k", settings.Resolve(true, 7680, 4320, 2560, 1440).Group?.Id);
        });

        Test("Anime4K：新增手动配置不改变原有自动档位及 ArtCNN 选择", () =>
        {
            _ = Group();
            foreach (var gpu in Enum.GetValues<GpuTier>())
                foreach (var vintage in new[] { false, true })
                    foreach (var fast in new[] { false, true })
                        foreach (var animated in new[] { false, true })
                            foreach (var tier in Enum.GetValues<UpscaleTier>())
                            {
                                var automatic = ShaderGroupCatalog.Resolve(animated, tier, gpu, vintage, fast);
                                Assert.Equal($"{(animated ? "anime" : "live")}-{tier.ToString().ToLowerInvariant()}", automatic.Id);
                                Assert.DoesNotContain("Anime4K", automatic.Description);
                                if (animated && !fast && tier != UpscaleTier.Shrink)
                                    Assert.Contains("ArtCNN", automatic.Description);
                            }
        });

        // 「新增功能开关，播放动漫时自动启用 anime4k」（用户的话，2026-10-27）。开关是自动选择的一个分支，
        // 不是第二个手动指定：手选优先，识别关掉就不生效，总开关和 8K 保护照旧先说话。
        Test("Anime4K：动画自动开关只换识别为动画的片源，真人、手选与各道保护各归各位", () =>
        {
            var settings = new ShaderAutomationSettings
            {
                Enabled = true,
                AutoAnimeProfile = true,
                AutoAnime4K = true,
                Gpu = GpuTier.Low
            };

            var animated = settings.Resolve(true, 1920, 1080, 2560, 1440);
            Assert.Equal(ShaderGroupCatalog.Anime4KId, animated.Group?.Id);
            Assert.Contains("动画自动", animated.Reason);
            Assert.Contains("Anime4K", animated.Reason);
            Assert.Equal(string.Join("|", Group().Files), string.Join("|", animated.Group!.Files),
                "自动挑中的就是手动那条固定链本身，不是另一份近似的链");

            var live = settings.Resolve(false, 1920, 1080, 2560, 1440);
            Assert.Equal("live-slight", live.Group?.Id, "真人片源仍走自动档位");

            // 关掉自动识别动画之后没有「动画」可言，这个开关也就不该挑中任何片源。
            settings.AutoAnimeProfile = false;
            var unidentified = settings.Resolve(true, 1920, 1080, 2560, 1440);
            Assert.Equal("live-slight", unidentified.Group?.Id);
            Assert.DoesNotContain("Anime4K", unidentified.Reason);

            // 手选一条链之后，自动开关不许把它顶掉。
            settings.AutoAnimeProfile = true;
            settings.ManualGroup = "anime-sweet";
            var picked = settings.Resolve(true, 1920, 1080, 2560, 1440);
            Assert.Equal("anime-sweet", picked.Group?.Id);
            Assert.Contains("手动指定", picked.Reason);

            settings.ManualGroup = "";
            settings.Enabled = false;
            Assert.Null(settings.Resolve(true, 1920, 1080, 2560, 1440).Group);

            settings.Enabled = true;
            Assert.Null(settings.Resolve(true, 7680, 4320, 2560, 1440).Group);

            settings.DisableForUltraHighRes = false;
            settings.AutoAnime4K = false;
            Assert.Equal("anime-slight", settings.Resolve(true, 1920, 1080, 2560, 1440).Group?.Id,
                "关掉开关就回到动画半表的 ArtCNN 自动链");
        });

        Test("Anime4K：自动挑中的固定链不随帧率与老片源换链，理由里写明代价", () =>
        {
            var settings = new ShaderAutomationSettings { Enabled = true, AutoAnime4K = true, Gpu = GpuTier.High };

            var dvd = settings.Resolve(true, 720, 480, 2560, 1440, 24);
            Assert.Equal(ShaderGroupCatalog.Anime4KId, dvd.Group?.Id);
            Assert.Contains("固定链不含老片源修复", dvd.Reason);
            Assert.DoesNotContain("老片源修复", dvd.Group!.Name);
            Assert.False(dvd.Group.Vintage);

            var fast = settings.Resolve(true, 1920, 1080, 2560, 1440, 60);
            Assert.Equal(ShaderGroupCatalog.Anime4KId, fast.Group?.Id);
            Assert.Contains("仍用固定链", fast.Reason);
            Assert.DoesNotContain("已换成便宜的链", fast.Reason, "固定链不做自动降级替换");
        });

        Test("Anime4K：动画自动开关缺键读成关，打开后往返保留", () =>
        {
            var defaults = SettingsMigration.NewDefaults();
            Assert.False(defaults.Shaders.AutoAnime4K, "装机默认关，缺键读出来也是关");
            defaults.Shaders.AutoAnime4K = true;
            var loaded = SettingsMigration.FromJson(
                JsonSerializer.Serialize(defaults, SettingsSerializer.WriteOptions), PassthroughSecretProtector.Instance);
            Assert.True(loaded.Shaders.AutoAnime4K);
            Assert.True(defaults.Shaders.Snapshot().AutoAnime4K, "快照要带上它，否则播放期读到的还是旧值");
            Assert.False(new ShaderAutomationSettings().AutoAnime4K);
        });

        Test("Anime4K：启动、切到其他链、返回和关闭都恢复本场播放的参数", () =>
        {
            var preset = Group();
            KeyValuePair<string, string>[] baseline =
            [
                new("scale", "spline36"), new("cscale", "bilinear"), new("dscale", "lanczos"),
                new("linear-downscaling", "yes"), new("correct-downscaling", "no"),
                new("sigmoid-upscaling", "no"), new("deband", "yes")
            ];
            var chain = preset.ToMpvOptions(@"C:\shaders");
            KeyValuePair<string, string>[] launch = [.. baseline, .. chain];
            var live = launch.GroupBy(pair => pair.Key)
                .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);
            foreach (var other in ShaderGroupCatalog.All)
            {
                Apply(other);
                Apply(preset);
                var expected = baseline.Concat(chain).GroupBy(pair => pair.Key)
                    .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);
                foreach (var (name, value) in expected) Assert.Equal(value, live[name], name);
            }
            Apply(null);
            Assert.Equal("", live["glsl-shaders"]);
            foreach (var (name, value) in baseline) Assert.Equal(value, live[name], name);

            void Apply(ShaderGroup? group)
            {
                var changes = ShaderSwitch.Options(launch, chain.Count, group, @"C:\shaders");
                Assert.Equal("glsl-shaders", changes[^1].Key, "先把参数恢复和应用完整，再挂目标链");
                foreach (var (name, value) in changes) live[name] = value;
            }
        });

        Test("Anime4K：独占菜单消息允许新配置组，仍拒绝任意名称和路径", () =>
        {
            _ = Group();
            Assert.Equal("anime4k", VideoWindowContract.Parse([VideoWindowContract.Shader, "anime4k"])?.Value);
            Assert.Null(VideoWindowContract.Parse([VideoWindowContract.Shader, "Anime4K;quit"]));
            Assert.Null(VideoWindowContract.Parse([VideoWindowContract.Shader, @"C:\arbitrary.glsl"]));
        });

        Test("Anime4K：三份新增源码与参考版本逐字节一致，保留许可并登记出处", () =>
        {
            _ = Group();
            var root = RepositoryRoot();
            var readme = File.ReadAllText(Path.Combine(root, "assets", "shaders", "README.md"));
            var notice = File.ReadAllText(Path.Combine(root, "assets", "shaders", "NOTICE.txt"));
            (string File, string Hash, string License)[] files =
            [
                ("Anime4K/glsl/Restore/Anime4K_Restore_CNN_Soft_M.glsl",
                    "71C0C2ECFD1EDBA74C2B35BC469D4A16B4CC1CB81FF5D055D31AD1FA2693EEE0", "MIT License"),
                ("Anime4K/glsl/Restore/Anime4K_Clamp_Highlights.glsl",
                    "A2A9BF7FBC1D75D09660CA2E701E4D7FB0CF5457B94DA47E1825032FA2B3671A", "MIT License"),
                ("igv/KrigBilateral.glsl",
                    "8A4798ABB77B83646FCE0D71985DDFD14C9806E3BE486E8FC7F5A5A8BD940CB0", "Lesser General Public")
            ];
            foreach (var (file, hash, license) in files)
            {
                var path = Path.Combine(root, "assets", "shaders", file.Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(path), file);
                var bytes = File.ReadAllBytes(path);
                Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(bytes)), file);
                Assert.Contains(license, File.ReadAllText(path));
                Assert.Contains(Path.GetFileName(file), readme);
                Assert.Contains(Path.GetFileName(file), notice);
                var thirdParty = File.ReadAllText(Path.Combine(root, "THIRD-PARTY-NOTICES.md"));
                Assert.Contains(Path.GetFileName(file), thirdParty);
                Assert.False(bytes.Contains((byte)'\r'), "随包源码保持 LF");
            }
            Assert.Contains("b6c687b4c08144e87524a738152d07ad3735915b", readme);
        });
    }

    private static ShaderGroup Group()
    {
        var group = ShaderGroupCatalog.Find("anime4k", GpuTier.Low);
        Assert.NotNull(group, "目录必须提供 anime4k 手动配置组");
        return group!;
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new AssertionException("找不到当前工作树，不能跳过源码和许可核验");
    }
}
