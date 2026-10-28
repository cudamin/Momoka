using System.Buffers.Binary;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Mpv;
using Momoka.Playback;

namespace Momoka.Tests;

internal static class SvpTests
{
    internal static void Register()
    {
        TestHarness.Test("SVP 默认关闭，缺键旧配置不启用", () =>
        {
            var settings = JsonSerializer.Deserialize<AppSettings>("{}")!;
            Assert.False(settings.Mpv.SvpEnabled);
            Assert.Equal("", settings.Mpv.SvpVapourSynthPath);
            Assert.False(MpvSvp.IsEnabled(settings.Mpv));
        });
        TestHarness.Test("SVP 后端与自定义配置显隐不清空偏好", () =>
        {
            var mpv = new MpvSettings { SvpEnabled = true, SvpVapourSynthPath = "retained", Backend = MpvBackendKind.BuiltInLibMpv };
            Assert.True(MpvSvp.IsEnabled(mpv));
            mpv.Backend = MpvBackendKind.ExternalMpv;
            Assert.False(MpvSvp.IsAvailable(mpv));
            Assert.False(MpvSvp.IsEnabled(mpv));
            mpv.Backend = MpvBackendKind.BuiltInLibMpv;
            mpv.CustomConfigEnabled = true;
            Assert.False(MpvSvp.IsEnabled(mpv));
            mpv.Pipeline = VideoPipelineKind.Integrated;
            Assert.True(MpvSvp.IsEnabled(mpv));
            Assert.True(mpv.SvpEnabled);
            Assert.Equal("retained", mpv.SvpVapourSynthPath);
        });
        TestHarness.Test("SVP 路径迁移清理引号和 null，不修改开关", () =>
        {
            var settings = new AppSettings();
            settings.Mpv.SvpEnabled = true;
            settings.Mpv.SvpVapourSynthPath = " \"retained\" ";
            SettingsMigration.Normalize(settings);
            Assert.Equal("retained", settings.Mpv.SvpVapourSynthPath);
            settings.Mpv.SvpVapourSynthPath = null!;
            SettingsMigration.Normalize(settings);
            Assert.Equal("", settings.Mpv.SvpVapourSynthPath);
            Assert.True(settings.Mpv.SvpEnabled);
        });
        foreach (var mode in Enum.GetValues<FrameInterpolationMode>())
        {
            TestHarness.Test($"SVP 接管 {mode} 时不叠加 CPU／平滑补帧且保留偏好", () =>
            {
                var video = new VideoSettings
                {
                    FrameInterpolationMode = mode,
                    HardwareDecoding = "d3d11va",
                    VideoSync = "display-resample",
                    Interpolation = true,
                    Tscale = "mitchell",
                    QualityPreset = "high-quality"
                };
                var frames = FrameInterpolationOptions.Resolve(video, new(3840, 2160, 10, 24, true), svp: true);
                Assert.Equal(FrameInterpolationMode.Off, frames.Mode);
                Assert.Null(frames.Filter);
                Assert.Equal("d3d11va-copy", frames.HardwareDecoding);
                var options = MpvOutputOptions.Build(video, new AudioSettings(), svp: true);
                Assert.Equal("audio", Value(options, "video-sync"));
                Assert.Equal("no", Value(options, "interpolation"));
                Assert.Equal("no", Value(options, "hr-seek-framedrop"));
                Assert.False(options.Any(pair => pair.Value.Contains("minterpolate", StringComparison.Ordinal)));
                Assert.Equal(mode, video.FrameInterpolationMode);
                Assert.Equal("d3d11va", video.HardwareDecoding);
                Assert.True(video.Interpolation);
                Assert.Equal("display-resample", video.VideoSync);
                Assert.Equal("mitchell", video.Tscale);
            });
        }
        TestHarness.Test("SVP 关闭恢复原有 CPU 补帧与复制硬解选择", () =>
        {
            var video = new VideoSettings
            {
                FrameInterpolationMode = FrameInterpolationMode.MotionCompensated,
                HardwareDecoding = "nvdec",
                FrameInterpolationFps = 72
            };
            Assert.Null(FrameInterpolationOptions.Resolve(video, svp: true).Filter);
            Assert.Contains("fps=72", FrameInterpolationOptions.Resolve(video).Filter);
            Assert.Equal("nvdec-copy", FrameInterpolationOptions.Resolve(video).HardwareDecoding);
            Assert.Equal("nvdec", video.HardwareDecoding);
        });
        TestHarness.Test("SVP 不丢弃 HDR 滤镜、音频／字幕和着色器偏好", () =>
        {
            var video = new VideoSettings { DolbyVisionMetadata = false, Hdr10PlusMetadata = false };
            var plain = MpvOutputOptions.Build(video, new AudioSettings());
            var svp = MpvOutputOptions.Build(video, new AudioSettings(), svp: true);
            Assert.Equal(Value(plain, "vf"), Value(svp, "vf"));
            Assert.True(plain.Where(pair => pair.Key is not ("hwdec" or "video-sync" or "interpolation" or "tscale"))
                .All(pair => svp.Contains(pair)));
        });
        TestHarness.Test("SVP 同窗换片双方标志均拒绝，防止旧异步滤镜写入下一集", () =>
        {
            var plain = new PlaybackLaunchSignature(VideoPipelineKind.Standalone, [], [], [], []);
            Assert.True(InlineSwitch.SameSignature(plain, plain));
            Assert.False(InlineSwitch.SameSignature(plain with { UsesSvp = true }, plain));
            Assert.False(InlineSwitch.SameSignature(plain, plain with { UsesSvp = true }));
            Assert.False(InlineSwitch.SameSignature(plain with { UsesSvp = true }, plain with { UsesSvp = true }));
        });
        TestHarness.Test("SVP 菜单：滤镜／同步／原生硬解收起，软件／复制硬解和其他功能保留", () =>
        {
            Assert.True(ReferenceEquals(PlayerMenuCatalog.Root, MpvSvp.Menu(false)));
            var menu = MpvSvp.Menu(true);
            var nodes = PlayerMenuCatalog.Flatten(menu).ToArray();
            var commands = nodes.Where(node => node.Kind == PlayerMenuKind.Command).ToArray();
            Assert.False(commands.SelectMany(node => node.Commands).Any(MpvSvp.BlocksCommand));
            Assert.True(commands.Any(node => node.Label == "软解"));
            Assert.True(commands.Any(node => node.Label == "d3d11va-copy 硬解"));
            Assert.True(commands.Any(node => node.Label.Contains("截图", StringComparison.Ordinal)));
            Assert.True(nodes.Where(node => node.Kind == PlayerMenuKind.Group).All(node => node.Children.Count > 0));
            Assert.False(nodes.Any(node => node.Label == "视频滤镜"));
            foreach (var group in nodes.Where(node => node.Kind == PlayerMenuKind.Group))
            {
                Assert.True(group.Children[0].Kind != PlayerMenuKind.Separator && group.Children[^1].Kind != PlayerMenuKind.Separator);
                for (var index = 1; index < group.Children.Count; index++)
                    Assert.False(group.Children[index].Kind == PlayerMenuKind.Separator && group.Children[index - 1].Kind == PlayerMenuKind.Separator);
            }
        });
        TestHarness.Test("SVP 命令：拒绝直接覆盖滤镜／插值／同步，允许复制硬解与播放控制", () =>
        {
            foreach (var command in new string[][] { ["vf", "clr", ""], ["set", "vf", ""],
                ["cycle", "interpolation"], ["set", "video-sync", "display-resample"], ["set", "hwdec", "nvdec"] })
                Assert.True(MpvSvp.BlocksCommand(command));
            foreach (var command in new string[][] { ["set", "hwdec", "no"], ["set", "hwdec", "auto-copy-safe"],
                ["set", "hwdec", "nvdec-copy"], ["seek", "10", "relative+exact"], ["set", "pause", "yes"],
                ["set", "volume", "70"], ["show-text", "notice", "1000"] })
                Assert.False(MpvSvp.BlocksCommand(command));
        });
        foreach (var machine in new ushort[] { 0x8664, 0x014c, 0xaa64 })
        {
            TestHarness.Test($"SVP 本机匿名组件检测 machine={machine:x4}，不加载 DLL", () =>
            {
                using var file = new LocalLibrary(machine);
                var status = MpvSvp.FindLibrary(file.Path, []);
                Assert.Equal(machine == 0x8664, status.Found);
                Assert.Equal(machine == 0x8664 ? file.Path : null, status.Path);
            });
        }
        TestHarness.Test("SVP 自动检测略过不可用候选，显式无效路径不偷换另一份 DLL", () =>
        {
            using var file = new LocalLibrary(0x8664);
            Assert.Equal(file.Path, MpvSvp.FindLibrary("", ["missing", file.Path]).Path);
            Assert.False(MpvSvp.FindLibrary("missing", [file.Path]).Found);
            Assert.Equal(file.Path, MpvSvp.FindLibrary($" \"{file.Path}\" ", []).Path);
        });
        foreach (var bad in new[] { "https://example.invalid/VSScript.dll", @"\\server\share\VSScript.dll",
            "VSScript.dll", "other.dll", @"\\?\C:\VSScript.dll" })
            TestHarness.Test($"SVP 拒绝非本机组件路径 {bad}", () => Assert.False(MpvSvp.FindLibrary(bad, []).Found));
        TestHarness.Test("SVP 拒绝截断与越界 PE 头，不抛未处理读取异常", () =>
        {
            using var file = new LocalLibrary(0x8664);
            File.WriteAllBytes(file.Path, [0x4d, 0x5a]);
            Assert.False(MpvSvp.FindLibrary(file.Path, []).Found);
            var bytes = new byte[80];
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, 0x5a4d);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x3c), int.MaxValue);
            File.WriteAllBytes(file.Path, bytes);
            Assert.False(MpvSvp.FindLibrary(file.Path, []).Found);
        });
        TestHarness.Test("SVP 选项不进入外部 argv，原有安全起播边界保持", () =>
        {
            var request = new PlaybackRequest
            {
                MediaUrl = new("https://example.invalid/video"),
                Title = "anonymous",
                SvpEnabled = true,
                SvpVapourSynthPath = "anonymous-local-component",
                HttpHeaders = [new("X-Emby-Token", "placeholder-token")]
            };
            var args = string.Join(" ", MpvArgumentBuilder.Build(request, MpvIpcClient.ToPipePath("anonymous-startup")));
            Assert.DoesNotContain("mpvpipe", args);
            Assert.DoesNotContain("placeholder-token", args);
            Assert.DoesNotContain("anonymous-local-component", args);
            Assert.DoesNotContain(request.MediaUrl.ToString(), args);
        });
    }

    private static string? Value(IReadOnlyList<KeyValuePair<string, string>> options, string name) =>
        options.LastOrDefault(pair => pair.Key == name).Value;

    private sealed class LocalLibrary : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "momoka-svp-test-" + Guid.NewGuid().ToString("N"));
        internal string Path { get; }
        internal LocalLibrary(ushort machine)
        {
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "VSScript.dll");
            var bytes = new byte[128];
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, 0x5a4d);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x3c), 64);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(64), 0x00004550);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(68), machine);
            File.WriteAllBytes(Path, bytes);
        }
        public void Dispose()
        {
            File.Delete(Path);
            Directory.Delete(_directory);
        }
    }
}
