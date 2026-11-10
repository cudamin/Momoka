using System.Net;
using Momoka.Playback;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class PlayerToolDownloadTests
{
    public static void Register()
    {
        Test("章节导出：CHP/OGM保持毫秒排序并拒绝注入换行", () =>
        {
            var chapters = new[] { new SkipChapter(63.125, "第二章\n伪造"), new SkipChapter(0, "开场") };
            Assert.Equal("00:00:00.000 开场\n00:01:03.125 第二章 伪造\n", PlayerChapterDocument.Write(chapters, false));
            Assert.Equal("CHAPTER01=00:00:00.000\nCHAPTER01NAME=开场\nCHAPTER02=00:01:03.125\nCHAPTER02NAME=第二章 伪造\n",
                PlayerChapterDocument.Write(chapters, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => PlayerChapterDocument.Write([new(double.NaN, "无效")], false));
        });
        Test("播放工具偏好：备份和导入不替换本机可执行程序", () =>
        {
            var live = new Momoka.Configuration.AppSettings();
            live.PlayerTools.FfmpegPath = "C:\\local\\ffmpeg.exe";
            live.PlayerTools.WhisperModel = "small";
            var backup = Momoka.Configuration.SettingsPreferences.ToBackupDocument(live);
            Assert.Equal("", backup.PlayerTools.FfmpegPath);
            Assert.Equal("small", backup.PlayerTools.WhisperModel);
            backup.PlayerTools.FfmpegPath = "C:\\other\\program.exe";
            Momoka.Configuration.SettingsPreferences.Apply(live, backup);
            Assert.Equal("C:\\local\\ffmpeg.exe", live.PlayerTools.FfmpegPath);
        });
        Test("工具下载：离开原来源后不再携带认证，回原站也不恢复", () => Run(async directory =>
        {
            var calls = 0;
            using var client = new HttpClient(new Handler(request =>
            {
                Assert.Equal(calls == 0, request.Headers.Contains("X-Emby-Token"));
                calls++;
                if (calls == 3) return new(HttpStatusCode.OK) { Content = new ByteArrayContent([0x1a, 0x45, 0xdf, 0xa3]) };
                var response = new HttpResponseMessage(HttpStatusCode.Redirect);
                response.Headers.Location = new Uri(calls == 1 ? "https://cdn.invalid/file" : "https://media.invalid/back");
                return response;
            }));
            var path = Path.Combine(directory, "source.media");
            await PlayerToolDownload.CopyAsync(client, new Uri("https://media.invalid/start"),
                [new("X-Emby-Token", "fixture-placeholder")], path, null, CancellationToken.None);
            Assert.Equal(3, calls);
            Assert.Equal(4L, new FileInfo(path).Length);
        }));
        Test("工具下载：重定向降级失败且不留下半成品", () => Run(async directory =>
        {
            using var client = new HttpClient(new Handler(_ => new(HttpStatusCode.Redirect)
            { Headers = { Location = new Uri("http://media.invalid/file") } }));
            var path = Path.Combine(directory, "source.media");
            try
            {
                await PlayerToolDownload.CopyAsync(client, new Uri("https://media.invalid/start"), [], path, null, CancellationToken.None);
                throw new InvalidOperationException("应该拒绝降级");
            }
            catch (PlayerToolDownloadException) { }
            Assert.False(File.Exists(path));
        }));
        Test("工具下载：已有文件不可覆盖，取消不留下文件", () => Run(async directory =>
        {
            using var client = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) }));
            var path = Path.Combine(directory, "existing.media");
            await File.WriteAllTextAsync(path, "keep");
            try { await PlayerToolDownload.CopyAsync(client, new Uri("https://media.invalid/file"), [], path, null, CancellationToken.None); }
            catch (PlayerToolDownloadException) { }
            Assert.Equal("keep", await File.ReadAllTextAsync(path));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var other = Path.Combine(directory, "cancelled.media");
            try { await PlayerToolDownload.CopyAsync(client, new Uri("https://media.invalid/file"), [], other, null, cancelled.Token); }
            catch (OperationCanceledException) { }
            Assert.False(File.Exists(other));
        }));
    }

    private static void Run(Func<string, Task> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Momoka-tool-download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { test(directory).GetAwaiter().GetResult(); }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handle(request));
    }
}
