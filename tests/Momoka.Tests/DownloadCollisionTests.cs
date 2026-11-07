using System.Net;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>匿名 HTTP 与本测试输出目录；不读取用户下载目录或打开网络连接。</summary>
internal static class DownloadCollisionTests
{
    private static readonly Uri Url = new("https://download.example.test/emby/Items/file/Download");
    private static readonly EmbyHttp.RequestContext Context = new(DeviceIdentity.Create("collision-test", "1"), null);

    public static void Register()
    {
        Test("下载碰撞：同名影片依次保存且已有内容不被替换", () => WithFolder(folder =>
        {
            var path = Path.Combine(folder, "Movie.mkv");
            File.WriteAllText(path, "existing");
            using var http = new EmbyHttp(new ReplyTransport());
            var second = Download(http, path);
            var third = Download(http, path);
            Assert.Equal(7L, second.Bytes);
            Assert.Equal(Path.Combine(folder, "Movie (2).mkv"), second.Path);
            Assert.Equal(Path.Combine(folder, "Movie (3).mkv"), third.Path);
            Assert.Equal("existing", File.ReadAllText(path));
            Assert.Equal("payload", File.ReadAllText(Path.Combine(folder, "Movie (2).mkv")));
            Assert.Equal("payload", File.ReadAllText(Path.Combine(folder, "Movie (3).mkv")));
            Assert.Equal(3, Directory.GetFiles(folder).Length);
        }));
        Test("下载碰撞：跳过已有编号与目录并保留无后缀文件", () => WithFolder(folder =>
        {
            var path = Path.Combine(folder, "Movie");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(folder, "Movie (2)"), "existing");
            using var http = new EmbyHttp(new ReplyTransport());
            Download(http, path);
            Assert.True(Directory.Exists(path));
            Assert.Equal("existing", File.ReadAllText(Path.Combine(folder, "Movie (2)")));
            Assert.Equal("payload", File.ReadAllText(Path.Combine(folder, "Movie (3)")));
        }));
        Test("下载碰撞：不覆盖或清理其他下载留下的part文件", () => WithFolder(folder =>
        {
            var path = Path.Combine(folder, "Movie.mkv");
            File.WriteAllText(path + ".part", "other partial download");
            using var http = new EmbyHttp(new ReplyTransport());
            Download(http, path);
            Assert.Equal("other partial download", File.ReadAllText(path + ".part"));
            Assert.Equal("payload", File.ReadAllText(path));
            Assert.Equal(2, Directory.GetFiles(folder).Length);
        }));
        foreach (var titles in new[] { new[] { "A:B", "A?B" }, new[] { new string('A', 96) + "B", new string('A', 96) + "C" } })
            Test($"下载碰撞：标题清洗或截断后同名仍保留两份（{titles[0].Length}字）", () => WithFolder(folder =>
            {
                using var http = new EmbyHttp(new ReplyTransport());
                foreach (var title in titles)
                    Download(http, Path.Combine(folder, DownloadPlan.FileName(new EmbyItem { Name = title }, new MediaSource { Container = "mkv" })));
                Assert.Equal(2, Directory.GetFiles(folder).Length);
                Assert.True(Directory.GetFiles(folder).All(file => File.ReadAllText(file) == "payload"));
            }));
        Test("下载碰撞：并发同名下载各自完整保存", () => WithFolder(folder =>
        {
            using var barrier = new Barrier(6);
            using var http = new EmbyHttp(new ReplyTransport());
            var tasks = Enumerable.Range(0, 6).Select(_ => Task.Factory.StartNew(() =>
            {
                Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(5)));
                Download(http, Path.Combine(folder, "Movie.mkv"));
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
            Task.WhenAll(tasks).GetAwaiter().GetResult();
            Assert.Equal(6, Directory.GetFiles(folder).Length);
            Assert.True(Directory.GetFiles(folder).All(file => File.ReadAllText(file) == "payload"));
        }));
        Test("下载碰撞：最后提交前目标出现仍保留已有文件", () => WithFolder(folder =>
        {
            var path = Path.Combine(folder, "Movie.mkv");
            using var http = new EmbyHttp(new ReplyTransport(() => File.WriteAllText(path, "arrived during download")));
            Download(http, path);
            Assert.Equal("arrived during download", File.ReadAllText(path));
            Assert.Equal("payload", File.ReadAllText(Path.Combine(folder, "Movie (2).mkv")));
        }));
    }

    private static (long Bytes, string Path) Download(EmbyHttp http, string path) =>
        http.DownloadToFileAsync(Url, path, Context, null, CancellationToken.None).GetAwaiter().GetResult();

    private static void WithFolder(Action<string> check)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "download-collision-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        check(folder);
    }

    private sealed class ReplyTransport(Action? duringDownload = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            duringDownload?.Invoke();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("payload") });
        }
    }
}
