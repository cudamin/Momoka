using System.Reflection;
using System.Runtime.InteropServices;
using Momoka.Diagnostics;
using Momoka.Configuration;
using Momoka.Mpv;
using Momoka.Playback;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class BackendLifecycleTests
{
    public static void Register()
    {
        Test("内置起播：预取消优先于DLL定位和原生初始化", () =>
        {
            var backend = new LibMpvBackend(new MpvSettings { ExecutablePath = "absent/mpv.exe" },
                new PlaybackSettings(), () => throw new AssertionException("不能取画布"));
            Assert.Throws<OperationCanceledException>(() => backend.StartAsync(Request(), new CancellationToken(true)).GetAwaiter().GetResult());
        });

        // 截图模板 2026-10-11 起是常量（MpvBaseline.ScreenshotTemplate），回到启动基线里：两部片名不同的
        // 票带着同一条模板，签名照样逐项相等，集名不同的常规换集不丢快路；链尾（scale）照旧被掐掉。
        Test("内置换片：常量截图模板进基线，链尾照旧掐掉", () =>
        {
            var first = Request() with
            {
                Title = "与你相恋到生命尽头 S01E08",
                PlayerOptions = [new("screenshot-template", MpvBaseline.ScreenshotTemplate), new("scale", "one")],
                ShaderOptionCount = 1,
            };
            var next = Request() with
            {
                Title = "与你相恋到生命尽头 S01E09",
                PlayerOptions = [new("screenshot-template", MpvBaseline.ScreenshotTemplate), new("scale", "two")],
                ShaderOptionCount = 1,
            };
            var settings = new MpvSettings { Pipeline = VideoPipelineKind.Standalone };
            Assert.True(InlineSwitch.SameSignature(
                LibMpvBackend.Signature(settings, new PlaybackSettings(), first, []),
                LibMpvBackend.Signature(settings, new PlaybackSettings(), next, [])));

            // 真改了启动配置（模板换了形状）基线就不等：那是有人动了模板，不是集名 —— 走重启才对。
            Assert.False(InlineSwitch.SameSignature(
                LibMpvBackend.Signature(settings, new PlaybackSettings(), first, []),
                LibMpvBackend.Signature(settings, new PlaybackSettings(),
                    next with { PlayerOptions = [new("screenshot-template", "%{media-title}-%n"), new("scale", "two")] }, [])));

            settings.Backend = MpvBackendKind.ExternalMpv;
            Assert.False(InlineSwitch.SameSignature(
                LibMpvBackend.Signature(new MpvSettings { Pipeline = VideoPipelineKind.Standalone }, new PlaybackSettings(), first, []),
                LibMpvBackend.Signature(settings, new PlaybackSettings(), next, [])));
        });

        // 独占管线的图形接口在换片签名里（2026-10-11 起）：gpu-api 在 initialize 之后改不动，改了设置
        // 必须走重启，不能让旧实例带着旧接口接着播。
        Test("内置换片：图形接口设置变了签名必须变", () =>
        {
            var vulkan = Request() with { PlayerOptions = [new("gpu-api", "vulkan")] };
            var d3d11 = Request() with { PlayerOptions = [new("gpu-api", "d3d11")] };
            var noApi = Request() with { PlayerOptions = [] };

            Assert.False(InlineSwitch.SameSignature(
                LibMpvBackend.Signature(new MpvSettings { Pipeline = VideoPipelineKind.Standalone }, new PlaybackSettings(), vulkan, []),
                LibMpvBackend.Signature(new MpvSettings { Pipeline = VideoPipelineKind.Standalone }, new PlaybackSettings(), d3d11, [])),
                "vulkan 与 d3d11 的契约不同，不能同一个实例换着用");
            Assert.False(InlineSwitch.SameSignature(
                LibMpvBackend.Signature(new MpvSettings { Pipeline = VideoPipelineKind.Standalone }, new PlaybackSettings(), vulkan, []),
                LibMpvBackend.Signature(new MpvSettings { Pipeline = VideoPipelineKind.Standalone }, new PlaybackSettings(), noApi, [])),
                "vulkan 与自动（空）的契约不同，不能同一个实例换着用");
            Assert.True(InlineSwitch.SameSignature(
                LibMpvBackend.Signature(new MpvSettings { Pipeline = VideoPipelineKind.Standalone }, new PlaybackSettings(), noApi, []),
                LibMpvBackend.Signature(new MpvSettings { Pipeline = VideoPipelineKind.Standalone }, new PlaybackSettings(),
                    Request() with { PlayerOptions = [new("scale", "one")], ShaderOptionCount = 1 }, [])),
                "空票与只有逐片着色器选项的票都没有图形接口，契约相同");

            // 集成管线的 api 归契约、不读票：签名里契约那一段与票里的值无关
            // （基线那一段照旧带着票里的选项 —— 那是「这次启动的配置」的一部分，不在本条关心范围）。
            var integrated = new MpvSettings { Pipeline = VideoPipelineKind.Integrated };
            Assert.Equal(
                string.Join(",", LibMpvBackend.Signature(integrated, new PlaybackSettings(), vulkan, []).PipelineOptions),
                string.Join(",", LibMpvBackend.Signature(integrated, new PlaybackSettings(), d3d11, []).PipelineOptions));
        });

        Test("内置换片：依赖的start-file和结束事件必须在订阅表", () =>
        {
            Assert.False(LibMpvBackend.UnusedEvents.Contains(LibMpvNative.EventStartFile));
            Assert.False(LibMpvBackend.UnusedEvents.Contains(LibMpvNative.EventEndFile));
            Assert.False(LibMpvBackend.UnusedEvents.Contains(LibMpvNative.EventPlaybackRestart));
        });

        Test("内置换片：旧命令执行票不通过新影片的原生闸门", () =>
        {
            var gate = new LibMpvLifetimeGate();
            var old = gate.Generation;
            var called = 0;
            gate.NextGeneration();
            Assert.False(gate.RunFor(old, () => { called++; return true; }));
            Assert.True(gate.RunFor(gate.Generation, () => { called++; return true; }));
            Assert.Equal(1, called);
            gate.Destroy(() => { });
            Assert.False(gate.RunFor(gate.Generation, () => { called++; return true; }));
            Assert.Equal(1, called);
        });

        Test("原生模块：成功绑定后只允许同一绝对路径并复用模块", () =>
        {
            var module = new NativeModuleBinding();
            var loads = 0;
            var first = Path.GetFullPath("first/libmpv-2.dll");
            var second = Path.GetFullPath("second/libmpv-2.dll");
            module.Select(first);
            Assert.Equal(new IntPtr(1), module.Resolve(_ => { loads++; return new IntPtr(1); }));
            module.Select(first);
            Assert.Equal(new IntPtr(1), module.Resolve(_ => { loads++; return new IntPtr(2); }));
            Assert.Throws<InvalidOperationException>(() => module.Select(second));
            Assert.Equal(1, loads);
        });

        Test("原生模块：加载失败未钉死路径，可修正后重试", () =>
        {
            var module = new NativeModuleBinding();
            module.Select(Path.GetFullPath("first/libmpv-2.dll"));
            Assert.Throws<DllNotFoundException>(() => module.Resolve(_ => throw new DllNotFoundException()));
            module.Select(Path.GetFullPath("second/libmpv-2.dll"));
            Assert.Equal(new IntPtr(2), module.Resolve(_ => new IntPtr(2)));
        });

        Test("原生诊断：错误正文和头部占位凭据不进入日志或播放结果", () =>
        {
            const string secret = "OFFLINE-NATIVE-PLACEHOLDER";
            var logs = new RingBufferLogSink();
            var text = Marshal.StringToCoTaskMemUTF8("Failed X-Emby-Token: " + secret);
            var memory = Marshal.AllocHGlobal(Marshal.SizeOf<LibMpvNative.MpvEventLogMessage>());
            Log.UseSink(logs);
            try
            {
                Marshal.StructureToPtr(new LibMpvNative.MpvEventLogMessage { Text = text, LogLevel = 20 }, memory, false);
                typeof(LibMpvHandle).GetMethod("RememberLog", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [memory]);
                var failure = (string)typeof(LibMpvHandle).GetMethod("DescribeFailure", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [-13])!;
                Assert.DoesNotContain(secret, failure);
                Assert.DoesNotContain(secret, string.Join("\n", logs.Snapshot()));
                Assert.Contains("-13", failure);
                Assert.True(logs.Snapshot().Count > 0);
            }
            finally
            {
                Log.UseSink(NullLogSink.Instance);
                Marshal.FreeHGlobal(memory);
                Marshal.FreeCoTaskMem(text);
            }
        });

        Test("外部退出：主动停止优先于缺失结束事件和强杀非零码", () =>
        {
            Assert.Equal(PlaybackEndReason.Stopped, MpvProcessHandle.Classify(null, 1, true));
            Assert.Equal(PlaybackEndReason.Stopped, MpvProcessHandle.Classify(null, 0, true));
            Assert.Equal(PlaybackEndReason.Error, MpvProcessHandle.Classify(null, 1, false));
            Assert.Equal(PlaybackEndReason.EndOfFile, MpvProcessHandle.Classify(MpvEndFileReason.Eof, 0, false));
        });
    }

    private static PlaybackRequest Request() => new() { MediaUrl = new Uri("https://media.invalid/file.mkv"), Title = "offline" };
}
