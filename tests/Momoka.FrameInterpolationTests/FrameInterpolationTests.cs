using System.Globalization;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Mpv;
using Momoka.Playback;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class FrameInterpolationTests
{
    private static readonly SourceProfile Sdr24 = new(1920, 1080, 8, 24, false);
    private const string Profiles = """
        [{"name":"high-quality","options":[
            {"key":"interpolation","value":"yes"},{"key":"tscale","value":"mitchell"},
            {"key":"video-sync","value":"display-resample"},{"key":"hwdec","value":"nvdec"}]},
         {"name":"fast","options":[
            {"key":"interpolation","value":"yes"},{"key":"video-sync","value":"display-vdrop"}]}]
        """;

    internal static void Register()
    {
        Test("补帧契约：默认关闭、60fps、均衡，枚举序号稳定", () =>
        {
            var video = new VideoSettings();
            Assert.Equal(FrameInterpolationMode.Off, video.FrameInterpolationMode);
            Assert.Equal(60, video.FrameInterpolationFps);
            Assert.Equal(FrameInterpolationQuality.Balanced, video.FrameInterpolationQuality);
            Assert.Equal(0, (int)FrameInterpolationMode.Off);
            Assert.Equal(1, (int)FrameInterpolationMode.Smooth);
            Assert.Equal(2, (int)FrameInterpolationMode.MotionCompensated);
            Assert.Equal(0, (int)FrameInterpolationQuality.Fast);
            Assert.Equal(1, (int)FrameInterpolationQuality.Balanced);
            Assert.Equal(2, (int)FrameInterpolationQuality.High);
        });
        Test("补帧 Off：未启用旧插值时不发送滤镜或算法", () =>
        {
            var output = Last(Build(new VideoSettings { Tscale = "mitchell", HighFrameRateAudioSync = false }));
            Assert.False(output.ContainsKey("vf"));
            Assert.False(output.ContainsKey("tscale"));
            Assert.False(output.ContainsKey("interpolation"), "默认预设保留旧功能的省略语义");
        });
        Test("补帧 Off：保留全部旧插值算法与显式显示同步", () =>
        {
            foreach (var kernel in MpvOutputOptions.InterpolationKernels)
            {
                var video = new VideoSettings
                {
                    FrameInterpolationMode = FrameInterpolationMode.Off, Interpolation = true,
                    Tscale = kernel.Value, VideoSync = "display-vdrop", HighFrameRateAudioSync = false
                };
                var output = Last(Build(video));
                Assert.Equal("yes", output["interpolation"]);
                Assert.Equal("display-vdrop", output["video-sync"]);
                Assert.Equal(kernel.Value, output["tscale"]);
                Assert.False(output.ContainsKey("vf"));
            }
        });
        Test("补帧 Off：旧音频同步抑制旧插值但不清空偏好", () =>
        {
            var video = new VideoSettings { Interpolation = true, VideoSync = "audio", Tscale = "bicubic" };
            var output = Last(Build(video));
            Assert.Equal("audio", output["video-sync"]);
            Assert.Equal("no", output["interpolation"]);
            Assert.False(output.ContainsKey("tscale"));
            Assert.True(video.Interpolation);
            Assert.Equal("bicubic", video.Tscale);
        });
        Test("补帧 Smooth：覆盖本次同步并使用共用算法，不改旧开关", () =>
        {
            foreach (var sync in new[] { "", "audio", "display-vdrop" })
            {
                var video = new VideoSettings
                {
                    FrameInterpolationMode = FrameInterpolationMode.Smooth, VideoSync = sync,
                    Tscale = "mitchell", Interpolation = false, HighFrameRateAudioSync = false,
                    HardwareDecoding = "d3d11va"
                };
                var output = Last(Build(video));
                Assert.Equal("display-resample", output["video-sync"]);
                Assert.Equal("yes", output["interpolation"]);
                Assert.Equal("mitchell", output["tscale"]);
                Assert.Equal("d3d11va", output["hwdec"]);
                Assert.False(output.ContainsKey("vf"));
                Assert.Equal(sync, video.VideoSync);
                Assert.False(video.Interpolation);
            }
        });
        Test("补帧 Smooth：空算法使用 oversample，不下发运动补偿参数", () =>
        {
            var video = new VideoSettings
            {
                FrameInterpolationMode = FrameInterpolationMode.Smooth, Tscale = "", FrameInterpolationFps = 120,
                FrameInterpolationQuality = FrameInterpolationQuality.High, HighFrameRateAudioSync = false
            };
            var output = Last(Build(video));
            Assert.Equal("oversample", output["tscale"]);
            Assert.False(output.ContainsKey("vf"));
        });
        Test("补帧 Smooth：高刷新率回退显式关闭，正常刷新率恢复", () =>
        {
            var video = new VideoSettings
            {
                FrameInterpolationMode = FrameInterpolationMode.Smooth, Interpolation = false,
                HighFrameRateAudioSync = true, HighRefreshRateLimitHz = 120
            };
            var blocked = Last(MpvOutputOptions.Build(video, new AudioSettings(), source: Sdr24, displayRefreshHz: 144));
            Assert.Equal("audio", blocked["video-sync"]);
            Assert.Equal("no", blocked["interpolation"]);
            Assert.False(blocked.ContainsKey("tscale"));
            var allowed = Last(MpvOutputOptions.Build(video, new AudioSettings(), source: Sdr24, displayRefreshHz: 120));
            Assert.Equal("display-resample", allowed["video-sync"]);
            Assert.Equal("yes", allowed["interpolation"]);
            Assert.Equal(FrameInterpolationMode.Smooth, video.FrameInterpolationMode);
        });
        Test("补帧 Smooth：高片源帧率回退和关闭回退遵循旧规则", () =>
        {
            var video = new VideoSettings { FrameInterpolationMode = FrameInterpolationMode.Smooth, HighFrameRateAudioSync = true };
            var high = new SourceProfile(1920, 1080, 8, 60, false);
            Assert.Equal("no", Last(Build(video, high))["interpolation"]);
            video.HighFrameRateAudioSync = false;
            Assert.Equal("yes", Last(Build(video, high))["interpolation"]);
        });
        Test("补帧 Motion：准确输出均衡参数、音频同步及插值复位", () =>
        {
            var video = Motion();
            video.Interpolation = true;
            video.VideoSync = "display-vdrop";
            var output = Last(Build(video));
            Assert.Equal("@momoka-frame-interpolation:lavfi=[minterpolate=fps=60:mi_mode=mci:mc_mode=aobmc:me_mode=bilat:vsbmc=0]", output["vf"]);
            Assert.Equal("audio", output["video-sync"]);
            Assert.Equal("no", output["interpolation"]);
            Assert.False(output.ContainsKey("tscale"));
            Assert.Equal("auto-copy-safe", output["hwdec"]);
            Assert.True(video.Interpolation);
            Assert.Equal("display-vdrop", video.VideoSync);
        });
        Test("补帧 Motion：三档质量只使用官方支持的运动补偿参数", () =>
        {
            foreach (var (quality, tail) in new[]
            {
                (FrameInterpolationQuality.Fast, "mc_mode=obmc:me_mode=bidir:vsbmc=0]"),
                (FrameInterpolationQuality.Balanced, "mc_mode=aobmc:me_mode=bilat:vsbmc=0]"),
                (FrameInterpolationQuality.High, "mc_mode=aobmc:me_mode=bilat:vsbmc=1]")
            })
            {
                var video = Motion();
                video.FrameInterpolationQuality = quality;
                var output = Last(Build(video));
                Assert.Contains(tail, output["vf"]);
                Assert.Contains(":mi_mode=mci:", output["vf"]);
                Assert.DoesNotContain("rife", output["vf"]);
                Assert.DoesNotContain("svp", output["vf"]);
            }
        });
        Test("补帧边界：fps 在有效输出端夹到24–120且不改存值", () =>
        {
            foreach (var fps in new[] { int.MinValue, 0, 23, 24, 60, 120, 121, int.MaxValue })
            {
                var video = Motion();
                video.FrameInterpolationFps = fps;
                var decision = FrameInterpolationOptions.Resolve(video);
                var clamped = Math.Clamp(fps, 24, 120);
                Assert.Equal(clamped, decision.Fps);
                Assert.Contains($"fps={clamped}:", decision.Filter);
                Assert.Equal(fps, video.FrameInterpolationFps);
            }
        });
        Test("补帧边界：未知枚举安全回退并保留原设置", () =>
        {
            var video = Motion();
            video.FrameInterpolationQuality = (FrameInterpolationQuality)99;
            Assert.Equal(FrameInterpolationQuality.Balanced, FrameInterpolationOptions.Resolve(video).Quality);
            Assert.Contains("mc_mode=aobmc:me_mode=bilat:vsbmc=0]", Last(Build(video))["vf"]);
            Assert.Equal((FrameInterpolationQuality)99, video.FrameInterpolationQuality);
            video.FrameInterpolationMode = (FrameInterpolationMode)(-1);
            Assert.Equal(FrameInterpolationMode.Off, FrameInterpolationOptions.Resolve(video).Mode);
            Assert.False(Last(Build(video)).ContainsKey("vf"));
            Assert.Equal((FrameInterpolationMode)(-1), video.FrameInterpolationMode);
        });
        Test("补帧硬解：原生模式转复制或软件，已有复制档保留", () =>
        {
            foreach (var (stored, effective) in new[]
            {
                ("no", "no"), ("", "no"), ("auto", "auto-copy-safe"), ("auto-safe", "auto-copy-safe"),
                ("auto-copy", "auto-copy-safe"), ("auto-copy-safe", "auto-copy-safe"),
                ("d3d11va", "d3d11va-copy"), ("d3d11va-copy", "d3d11va-copy"),
                ("d3d12va-copy", "d3d12va-copy"), ("dxva2", "dxva2-copy"), ("dxva2-copy", "dxva2-copy"),
                ("nvdec", "nvdec-copy"), ("nvdec-copy", "nvdec-copy"), ("vulkan", "no"), ("unknown", "no"),
                (" D3D11VA ", "d3d11va-copy")
            })
            {
                var video = Motion();
                video.HardwareDecoding = stored;
                Assert.Equal(effective, Last(Build(video))["hwdec"]);
                Assert.Equal(stored, video.HardwareDecoding);
                video.FrameInterpolationMode = FrameInterpolationMode.Off;
                var output = Last(Build(video));
                Assert.Equal(stored.Trim(), output.GetValueOrDefault("hwdec", ""));
            }
        });
        Test("补帧 Motion：不受显示同步高刷新率回退误关", () =>
        {
            var video = Motion();
            video.HighFrameRateAudioSync = true;
            var output = Last(MpvOutputOptions.Build(video, new AudioSettings(), source: Sdr24, displayRefreshHz: 240));
            Assert.Contains("minterpolate", output["vf"]);
            Assert.Equal("audio", output["video-sync"]);
        });
        Test("补帧 Motion：HDR及高位深跳过，不量化或改写偏好", () =>
        {
            foreach (var source in new[]
            {
                new SourceProfile(3840, 2160, 10, 24, true), new SourceProfile(1920, 1080, 8, 24, true),
                new SourceProfile(1920, 1080, 10, 24, false), new SourceProfile(1920, 1080, 12, 24, false)
            })
            {
                var video = Motion();
                video.HardwareDecoding = "d3d11va";
                var decision = FrameInterpolationOptions.Resolve(video, source);
                Assert.Equal(FrameInterpolationMode.Off, decision.Mode);
                Assert.Null(decision.Filter);
                Assert.Contains("8-bit SDR", decision.StandDown);
                var output = Last(Build(video, source));
                Assert.False(output.ContainsKey("vf"));
                Assert.Equal("d3d11va", output["hwdec"]);
                Assert.Equal(FrameInterpolationMode.MotionCompensated, video.FrameInterpolationMode);
            }
        });
        Test("补帧 Motion：片源已达目标时不隐藏降帧，未知帧率仍可配置", () =>
        {
            var video = Motion();
            foreach (var rate in new[] { 60.0, 120.0 })
            {
                var source = new SourceProfile(1920, 1080, 8, rate, false);
                Assert.Null(FrameInterpolationOptions.Resolve(video, source).Filter);
                Assert.False(Last(Build(video, source)).ContainsKey("vf"));
            }
            foreach (var rate in new[] { 0.0, double.NaN, double.PositiveInfinity, 23.976 })
                Assert.NotNull(FrameInterpolationOptions.Resolve(video, new(1920, 1080, 8, rate, false)).Filter);
        });
        Test("补帧滤镜：HDR format标签与运动补偿合并为一条vf", () =>
        {
            var video = Motion();
            video.DolbyVisionMetadata = false;
            video.DolbyVisionEnhancementLayer = false;
            video.Hdr10PlusMetadata = false;
            var options = Build(video);
            Assert.Equal(1, options.Count(option => option.Key == "vf"));
            var filter = Last(options)["vf"];
            Assert.Contains("@momoka-hdr:format=dolbyvision=no:enhancement-layer=no:hdr10plus=no,", filter);
            Assert.Contains(FrameInterpolationOptions.FilterLabel, filter);
            Assert.False(options.Any(option => option.Key == "vf-append"));
            video.FrameInterpolationMode = FrameInterpolationMode.Off;
            var restored = Last(Build(video))["vf"];
            Assert.Contains("@momoka-hdr:format=", restored);
            Assert.DoesNotContain("minterpolate", restored);
        });
        Test("补帧滤镜：append保留图的逗号、格式滤镜与独立着色器列表", () =>
        {
            var options = new List<KeyValuePair<string, string>>
            {
                new("vf", "@existing:lavfi=[format=yuv420p,hflip],@format:format=colorlevels=limited"),
                new("glsl-shaders", @"C:\shader one.glsl;C:\shader two.hook"), new("scale", "ewa_lanczos")
            };
            FrameInterpolationOptions.AppendVideoFilter(options, FrameInterpolationOptions.MotionFilter(60, FrameInterpolationQuality.Fast));
            Assert.Equal(3, options.Count);
            Assert.Contains("[format=yuv420p,hflip],@format:format=colorlevels=limited,@momoka-frame-interpolation:", options[0].Value);
            Assert.Equal(@"C:\shader one.glsl;C:\shader two.hook", options[1].Value);
            Assert.Equal("ewa_lanczos", options[2].Value);
            Assert.Throws<ArgumentException>(() => FrameInterpolationOptions.AppendVideoFilter(options, " "));
        });
        Test("补帧预设：高质量及fast不能覆盖Smooth或Motion的有效选项", () =>
        {
            foreach (var profile in new[] { "high-quality", "fast", " fast " })
            {
                var video = Motion();
                video.QualityPreset = profile;
                var motion = Last(MpvProfiles.Expand(Build(video), Profiles));
                Assert.Equal("audio", motion["video-sync"]);
                Assert.Equal("no", motion["interpolation"]);
                Assert.Equal("auto-copy-safe", motion["hwdec"]);
                video.FrameInterpolationMode = FrameInterpolationMode.Smooth;
                video.HighFrameRateAudioSync = false;
                var smooth = Last(MpvProfiles.Expand(Build(video), Profiles));
                Assert.Equal("display-resample", smooth["video-sync"]);
                Assert.Equal("yes", smooth["interpolation"]);
                Assert.Equal("oversample", smooth["tscale"]);
            }
        });
        Test("补帧预设：关闭新旧插值时显式抑制profile同步残留", () =>
        {
            foreach (var profile in new[] { "high-quality", "fast", " fast " })
            {
                var video = new VideoSettings { QualityPreset = profile, HighFrameRateAudioSync = false };
                var output = Last(MpvProfiles.Expand(Build(video), Profiles));
                Assert.Equal("no", output["interpolation"]);
                Assert.Equal("audio", output["video-sync"]);
                video.Interpolation = true;
                video.Tscale = "bicubic";
                output = Last(MpvProfiles.Expand(Build(video), Profiles));
                Assert.Equal("yes", output["interpolation"]);
                Assert.Equal("bicubic", output["tscale"]);
            }
        });
        Test("补帧换片：既有InlineSwitch包含vf、硬解、同步、插值复位", () =>
        {
            foreach (var name in new[] { "vf", "hwdec", "video-sync", "interpolation" })
                Assert.True(InlineSwitch.PerFilmNames.Contains(name), name);
            var video = Motion();
            var motion = Last(InlineSwitch.FilmScoped(Defaults(), Request(Build(video))));
            Assert.Contains("minterpolate", motion["vf"]);
            Assert.Equal("no", motion["interpolation"]);
            video.FrameInterpolationMode = FrameInterpolationMode.Off;
            var off = Last(InlineSwitch.FilmScoped(Defaults(), Request(Build(video))));
            Assert.Equal("", off["vf"]);
            Assert.Equal("no", off["interpolation"]);
            Assert.Equal("audio", off["video-sync"]);
        });
        Test("补帧换片：Motion到Off及Smooth双向恢复旧插值/硬解/格式链", () =>
        {
            var video = Motion();
            video.HardwareDecoding = "d3d11va";
            video.Interpolation = true;
            video.Tscale = "mitchell";
            video.VideoSync = "display-vdrop";
            video.HighFrameRateAudioSync = false;
            video.DolbyVisionMetadata = false;
            foreach (var mode in new[] { FrameInterpolationMode.MotionCompensated, FrameInterpolationMode.Off,
                FrameInterpolationMode.Smooth, FrameInterpolationMode.MotionCompensated })
            {
                video.FrameInterpolationMode = mode;
                var plan = Last(InlineSwitch.FilmScoped(Defaults(), Request(Build(video))));
                Assert.Contains("@momoka-hdr:format=", plan["vf"]);
                if (mode == FrameInterpolationMode.MotionCompensated)
                {
                    Assert.Contains("minterpolate", plan["vf"]);
                    Assert.Equal("d3d11va-copy", plan["hwdec"]);
                    Assert.Equal("audio", plan["video-sync"]);
                    Assert.Equal("no", plan["interpolation"]);
                }
                else
                {
                    Assert.DoesNotContain("minterpolate", plan["vf"]);
                    Assert.Equal("d3d11va", plan["hwdec"]);
                    Assert.Equal(mode == FrameInterpolationMode.Smooth ? "display-resample" : "display-vdrop", plan["video-sync"]);
                    Assert.Equal("yes", plan["interpolation"]);
                }
            }
        });
        Test("补帧快路：模式/fps变化不复用不兼容的启动签名", () =>
        {
            var video = Motion();
            var first = Signature(Build(video));
            video.FrameInterpolationFps = 120;
            Assert.False(InlineSwitch.SameSignature(first, Signature(Build(video))));
            video.FrameInterpolationMode = FrameInterpolationMode.Off;
            Assert.False(InlineSwitch.SameSignature(first, Signature(Build(video))));
            Assert.True(InlineSwitch.SameSignature(first, first));
        });
        Test("补帧有效规则：所有模式和回退不修改VideoSettings快照", () =>
        {
            foreach (var mode in Enum.GetValues<FrameInterpolationMode>())
            {
                var video = new VideoSettings
                {
                    FrameInterpolationMode = mode, FrameInterpolationFps = 200,
                    FrameInterpolationQuality = (FrameInterpolationQuality)99, HardwareDecoding = "nvdec",
                    VideoSync = "display-vdrop", Interpolation = true, Tscale = "bicubic"
                };
                var before = JsonSerializer.Serialize(video);
                foreach (var source in new SourceProfile?[] { null, Sdr24, new(3840, 2160, 10, 24, true) })
                {
                    _ = FrameInterpolationOptions.Resolve(video, source);
                    _ = MpvOutputOptions.ResolveSync(video, source, 144);
                    _ = MpvOutputOptions.Build(video, new AudioSettings(), source: source, displayRefreshHz: 144);
                    Assert.Equal(before, JsonSerializer.Serialize(video));
                }
            }
        });
        Test("补帧字符串：区域设置不改变小数诊断和整数fps输出", () =>
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var video = Motion();
                Assert.Contains("fps=60:", Last(Build(video))["vf"]);
                video.FrameInterpolationFps = 24;
                var skipped = FrameInterpolationOptions.Resolve(video, new(1920, 1080, 8, 59.94, false));
                Assert.Contains("59.94fps", skipped.StandDown);
            }
            finally { CultureInfo.CurrentCulture = original; }
        });
    }

    private static VideoSettings Motion() => new() { FrameInterpolationMode = FrameInterpolationMode.MotionCompensated };
    private static IReadOnlyList<KeyValuePair<string, string>> Build(VideoSettings video, SourceProfile? source = null) =>
        MpvOutputOptions.Build(video, new AudioSettings(), source: source ?? Sdr24);
    internal static Dictionary<string, string> Last(IEnumerable<KeyValuePair<string, string>> options) =>
        options.GroupBy(option => option.Key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);
    private static Dictionary<string, string> Defaults() => new()
    {
        ["vf"] = "", ["hwdec"] = "no", ["video-sync"] = "audio", ["interpolation"] = "no"
    };
    private static PlaybackRequest Request(IReadOnlyList<KeyValuePair<string, string>> options) => new()
    {
        MediaUrl = new Uri("file:///C:/no-media-is-loaded.mkv"), Title = "离线规则测试", PlayerOptions = options
    };
    private static PlaybackLaunchSignature Signature(IReadOnlyList<KeyValuePair<string, string>> options) =>
        new(VideoPipelineKind.Standalone, [], [], [], options);
}
