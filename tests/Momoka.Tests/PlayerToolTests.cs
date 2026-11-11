using System.Globalization;
using System.Net;
using System.Text;
using Momoka.Configuration;
using Momoka.Playback;
using Momoka.Translation;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class PlayerToolTests
{
    public static void Register()
    {
        Test("播放工具：无损片段使用真实流索引和不覆盖参数", () =>
        {
            WithFiles((settings, media, output, _) =>
            {
                var plan = PlayerToolPlan.Create(settings, PlayerToolAction.ExportClip, new(media, output + ".mkv")
                { StartSeconds = 1.25, EndSeconds = 4.75, VideoStreamIndex = 3, AudioStreamIndex = 5 });
                Assert.True(plan.Arguments.Contains("file,pipe"));
                Assert.True(plan.Arguments.Contains("0:3"));
                Assert.True(plan.Arguments.Contains("0:5"));
                Assert.True(plan.Arguments.Contains("copy"));
                Assert.True(plan.Arguments.Contains("-n"));
                Assert.False(plan.Arguments.Contains("-y"));
                Assert.Equal("3.5", plan.Arguments[plan.Arguments.ToList().IndexOf("-t") + 1]);
            });
        });
        Test("播放工具：网络、播放列表和无效范围在启动前拒绝", () =>
        {
            WithFiles((settings, media, output, _) =>
            {
                Reject(() => PlayerToolPlan.Create(settings, PlayerToolAction.ExportClip, new("https://example.invalid/a.mkv", output + ".mkv")));
                Reject(() => PlayerToolPlan.Create(settings, PlayerToolAction.ExportClip, new(media, output + ".mkv") { StartSeconds = double.NaN, EndSeconds = 3 }));
                Reject(() => PlayerToolPlan.Create(settings, PlayerToolAction.ExportClip, new(media, output + ".mkv") { StartSeconds = 3, EndSeconds = 3 }));
                File.WriteAllText(media, "#EXTM3U\nhttps://example.invalid/segment.ts");
                Reject(() => PlayerToolPlan.Create(settings, PlayerToolAction.GenerateSubtitle, new(media, output + ".srt")));
            });
        });
        Test("播放工具：现有输出和非法格式不会被工具覆盖", () =>
        {
            WithFiles((settings, media, output, _) =>
            {
                File.WriteAllText(output + ".mkv", "keep");
                Reject(() => PlayerToolPlan.Create(settings, PlayerToolAction.ExportClip, new(media, output + ".mkv") { EndSeconds = 3 }));
                Reject(() => PlayerToolPlan.Create(settings, PlayerToolAction.ExportClip, new(media, output + ".mp4") { EndSeconds = 3 }));
                Assert.Equal("keep", File.ReadAllText(output + ".mkv"));
            });
        });
        Test("播放工具：字幕导出区分文字编码与 PGS 复制", () =>
        {
            WithFiles((settings, media, output, _) =>
            {
                var text = PlayerToolPlan.Create(settings, PlayerToolAction.ExportSubtitle, new(media, output + ".srt") { SubtitleStreamIndex = 8 });
                Assert.True(text.Arguments.Contains("0:8"));
                Assert.True(text.Arguments.Contains("srt"));
                var pgs = PlayerToolPlan.Create(settings, PlayerToolAction.ExportSubtitle, new(media, output + ".sup") { SubtitleStreamIndex = 8 });
                Assert.True(pgs.Arguments.Contains("copy"));
                Reject(() => PlayerToolPlan.Create(settings, PlayerToolAction.ExportSubtitle, new(media, output + ".srt")));
            });
        });
        Test("播放工具：同步引擎使用各自的明确参数协议", () =>
        {
            WithFiles((settings, media, output, subtitle) =>
            {
                var ff = PlayerToolPlan.Create(settings, PlayerToolAction.SynchronizeSubtitle,
                    new(media, output + ".srt") { SubtitlePath = subtitle, AudioStreamIndex = 4 });
                Assert.True(ff.Arguments.Contains("--reference-stream"));
                Assert.True(ff.Arguments.Contains("0:4"));
                var alass = PlayerToolPlan.Create(settings, PlayerToolAction.SynchronizeSubtitle,
                    new(media, output + ".srt") { SubtitlePath = subtitle, SyncEngine = PlayerSubtitleSyncEngine.Alass });
                Assert.Equal(3, alass.Arguments.Count);
                Assert.Equal(subtitle, alass.Arguments[1]);
            });
        });
        Test("播放工具：Whisper 返回实际生成名并保留本地参数边界", () =>
        {
            WithFiles((settings, media, output, _) =>
            {
                settings.WhisperLanguage = "ja";
                var plan = PlayerToolPlan.Create(settings, PlayerToolAction.GenerateSubtitle, new(media, output + ".srt"));
                Assert.Equal(Path.Combine(Path.GetDirectoryName(output)!, "media.srt"), plan.OutputPath);
                Assert.True(plan.Arguments.Contains("--output_dir"));
                Assert.True(plan.Arguments.Contains("ja"));
                settings.WhisperLanguage = "ja --evil";
                Reject(() => PlayerToolPlan.Create(settings, PlayerToolAction.GenerateSubtitle, new(media, output + ".srt")));
            });
        });
        Test("播放工具：GIF 字幕路径作为一个参数且时轴还原", () =>
        {
            WithFiles((settings, media, output, subtitle) =>
            {
                var previous = CultureInfo.CurrentCulture;
                try
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                    var plan = PlayerToolPlan.Create(settings, PlayerToolAction.ExportGifWithSubtitles,
                        new(media, output + ".gif") { SubtitlePath = subtitle, StartSeconds = 12.5, EndSeconds = 14 });
                    var filter = plan.Arguments[plan.Arguments.ToList().IndexOf("-filter_complex") + 1];
                    Assert.True(filter.Contains("setpts=PTS+12.5/TB", StringComparison.Ordinal));
                    Assert.True(filter.Contains("palettegen", StringComparison.Ordinal));
                    Assert.True(filter.StartsWith("[0:v:0]", StringComparison.Ordinal));
                    Assert.True(plan.Arguments.Contains("[gif]"));
                    Assert.False(plan.Arguments.Contains("powershell"));
                }
                finally { CultureInfo.CurrentCulture = previous; }
            });
        });
        Test("播放工具：取消请求在执行任何子进程前结束", () =>
        {
            WithFiles((settings, media, output, _) =>
            {
                var plan = PlayerToolPlan.Create(settings, PlayerToolAction.ExportClip, new(media, output + ".mkv") { EndSeconds = 3 });
                try
                {
                    PlayerToolRunner.RunAsync(plan, new CancellationToken(true)).GetAwaiter().GetResult();
                    throw new AssertionException("未拒绝取消的工具操作");
                }
                catch (OperationCanceledException) { }
                Assert.False(File.Exists(plan.OutputPath));
            });
        });
        Test("SRT：解析序号多行和毫秒，序列化保留每条时间轴", () =>
        {
            var doc = SrtDocument.Parse("\uFEFF1\r\n00:00:01,250 --> 00:00:03,000\r\n一行\r\n两行\r\n\r\n2\r\n01:00:00.001 --> 01:00:01.002\r\n第二句\r\n");
            Assert.Equal(2, doc.Cues.Count);
            Assert.Equal(1250L, doc.Cues[0].StartMilliseconds);
            Assert.Equal("一行\n两行", doc.Cues[0].Text);
            var copy = SrtDocument.Parse(doc.ToText());
            Assert.Equal(doc.Cues[1], copy.Cues[1]);
            Reject(() => SrtDocument.Parse("1\n00:00:03,000 --> 00:00:01,000\n坏时间"));
            Reject(() => SrtDocument.Parse("1\n00:99:00,000 --> 01:00:00,000\n坏分钟"));
        });
        Test("SRT：全片翻译保留时间轴，关闭时零请求", () =>
        {
            var handler = new TranslationHandler();
            using var http = new HttpClient(handler);
            using var client = new SubtitleTranslationClient(http);
            var doc = SrtDocument.Parse("1\n00:00:01,000 --> 00:00:02,000\nOriginal\n");
            var settings = new SubtitleTranslationSettings();
            Reject(() => doc.TranslateAsync(client, settings, "placeholder", CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(0, handler.Calls);
            settings.Enabled = true;
            var translated = doc.TranslateAsync(client, settings, "placeholder", CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal(1, handler.Calls);
            Assert.Equal(1000L, translated.Cues[0].StartMilliseconds);
            Assert.Equal("翻译", translated.Cues[0].Text);
            Assert.Equal("Original", doc.Cues[0].Text);
        });
        Test("图片工具：裁切参数仅使用选区像素，不执行命令模板", () =>
        {
            WithFiles((settings, media, output, _) =>
            {
                var image = Path.ChangeExtension(media, ".png");
                File.Copy(media, image);
                var plan = PlayerImageTools.Crop(settings, image, output + ".png", new(2, 3, 10, 12));
                Assert.True(plan.Arguments.Contains("crop=10:12:2:3"));
                Assert.False(plan.Arguments.Contains("powershell"));
                Reject(() => PlayerImageTools.Crop(settings, image, output + ".png", new(-1, 3, 10, 12)));
            });
        });
        Test("图片工具：拼图按顺序接入每张图，数量不足拒绝", () =>
        {
            WithFiles((settings, media, output, _) =>
            {
                var image = Path.ChangeExtension(media, ".png");
                File.Copy(media, image);
                var plan = PlayerImageTools.Stitch(settings, [image, image], output + ".png");
                Assert.Equal(2, plan.Arguments.Count(argument => argument == "-i"));
                Assert.True(plan.Arguments.Contains("[stitched]"));
                Reject(() => PlayerImageTools.Stitch(settings, [image], output + ".png"));
                Reject(() => PlayerImageTools.Stitch(settings, Enumerable.Repeat(image, 101).ToArray(), output + ".png"));
            });
        });
        Test("AI 字幕：提取选中音轨与片段，未知音轨拒绝", () =>
        {
            WithFiles((settings, media, output, _) =>
            {
                var input = new PlayerToolInput(media, output + ".wav") { AudioStreamIndex = 4, StartSeconds = 12.5, EndSeconds = 14 };
                var plan = PlayerToolPlan.PrepareAudio(settings, input);
                Assert.True(plan.Arguments.Contains("0:4"));
                Assert.True(plan.Arguments.Contains("16000"));
                Assert.True(plan.Arguments.Contains("pcm_s16le"));
                Assert.True(plan.Arguments.Contains("file,pipe"));
                Assert.Equal("1.5", plan.Arguments[plan.Arguments.ToList().IndexOf("-t") + 1]);
                Reject(() => PlayerToolPlan.PrepareAudio(settings, input with { AudioStreamIndex = null }));
                Reject(() => PlayerToolPlan.PrepareAudio(settings, input with { EndSeconds = 10 }));
            });
        });
        Test("AI 字幕：片段时间轴加回起点且不改变原字幕", () =>
        {
            var document = SrtDocument.Parse("1\n00:00:01,000 --> 00:00:02,000\nfixture\n");
            var shifted = document.ShiftMilliseconds(12500);
            Assert.Equal(13500L, shifted.Cues[0].StartMilliseconds);
            Assert.Equal(14500L, shifted.Cues[0].EndMilliseconds);
            Assert.Equal(1000L, document.Cues[0].StartMilliseconds);
            Reject(() => document.ShiftMilliseconds(-2000));
            Reject(() => document.ShiftMilliseconds(long.MaxValue));
        });
        Test("工具执行：退出失败清掉本次半成品", () =>
        {
            if (!OperatingSystem.IsWindows()) return;
            WithFiles((_, media, output, _) =>
            {
                var executable = Environment.ProcessPath!;
                var assembly = typeof(PlayerToolTests).Assembly.Location;
                var target = output + ".srt";
                var args = new List<string>();
                if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) args.Add(assembly);
                args.AddRange(["player-tool-child-failure", target]);
                var plan = PlayerToolPlan.Image(executable, args.AsReadOnly(), target, Array.AsReadOnly(new[] { media }));
                Reject(() => PlayerToolRunner.RunAsync(plan, CancellationToken.None).GetAwaiter().GetResult());
                Assert.False(File.Exists(target));
            });
        });
        Test("工具执行：取消时等待子进程退出并清理半成品", () =>
        {
            if (!OperatingSystem.IsWindows()) return;
            WithFiles((_, media, output, _) =>
            {
                var executable = Environment.ProcessPath!;
                var assembly = typeof(PlayerToolTests).Assembly.Location;
                var target = output + ".srt";
                var args = new List<string>();
                if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) args.Add(assembly);
                args.AddRange(["player-tool-child-wait", target]);
                var plan = PlayerToolPlan.Image(executable, args.AsReadOnly(), target, Array.AsReadOnly(new[] { media }));
                using var cancel = new CancellationTokenSource();
                var running = PlayerToolRunner.RunAsync(plan, cancel.Token);
                var deadline = Environment.TickCount64 + 10_000;
                var pid = 0;
                var canceled = false;
                try
                {
                    while (pid == 0 && !running.IsCompleted && Environment.TickCount64 < deadline)
                    {
                        // File creation precedes WriteAllText closing its handle and publishing the complete PID.
                        try { int.TryParse(File.ReadAllText(target), NumberStyles.Integer, CultureInfo.InvariantCulture, out pid); }
                        catch (IOException) { }
                        if (pid == 0) Thread.Sleep(10);
                    }
                    Assert.True(pid > 0, "受控工具应已完整写出半成品和进程号");
                }
                finally
                {
                    cancel.Cancel();
                    try { running.GetAwaiter().GetResult(); }
                    catch (OperationCanceledException) { canceled = true; }
                }
                Assert.True(canceled, "未取消");
                try
                {
                    using var process = System.Diagnostics.Process.GetProcessById(pid);
                    Assert.True(process.HasExited);
                }
                catch (ArgumentException) { }
                Assert.False(File.Exists(target));
            });
        });
    }

    private static void WithFiles(Action<PlayerToolsSettings, string, string, string> run)
    {
        var root = Path.Combine(Path.GetTempPath(), "momoka-player-tools-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var exe = Path.Combine(root, "fixture.exe");
            var ffmpeg = Path.Combine(root, "ffmpeg.exe");
            var ffprobe = Path.Combine(root, "ffprobe.exe");
            var media = Path.Combine(root, "media.mkv");
            var subtitle = Path.Combine(root, "字幕 'x [一],y.srt");
            File.WriteAllBytes(exe, [0]);
            File.WriteAllBytes(ffmpeg, [0]);
            File.WriteAllBytes(ffprobe, [0]);
            File.WriteAllBytes(media, [0x1a, 0x45, 0xdf, 0xa3]);
            File.WriteAllText(subtitle, "1\n00:00:01,000 --> 00:00:02,000\nfixture\n");
            run(new() { FfmpegPath = ffmpeg, FfprobePath = ffprobe, FfsubsyncPath = exe, AlassPath = exe, FasterWhisperPath = exe }, media,
                Path.Combine(root, "output"), subtitle);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    private static void Reject(Action run)
    {
        try { run(); }
        catch (PlayerToolException) { return; }
        throw new AssertionException("未拒绝无效输入");
    }
    private sealed class TranslationHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"data\":{\"translations\":[{\"translatedText\":\"翻译\"}]}}", Encoding.UTF8, "application/json") });
        }
    }
}
