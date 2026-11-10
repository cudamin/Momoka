using Momoka.Shell.Media;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class LocalLibraryArtworkTests
{
    public static void Register()
    {
        Test("本地媒体库图片：仅接受本机绝对图片路径，拒绝 URL、网络和备用数据流", () =>
        {
            foreach (var path in new[] { "poster.jpg", "https://example.invalid/poster.jpg", "file:///C:/poster.jpg",
                "\\\\server\\share\\poster.jpg", "C:\\poster.exe", "C:\\poster.jpg:hidden.jpg" })
                Assert.Equal("", LocalLibraryArtwork.NormalizePath(path), path);
            var local = Path.Combine(Path.GetTempPath(), "momoka-local-poster.JPG");
            Assert.Equal(Path.GetFullPath(local), LocalLibraryArtwork.NormalizePath(local));
        });
        Test("本地媒体库图片：真实文件读取保留字节并拒绝超过12MiB的文件", () =>
        {
            using var directory = new TemporaryDirectory();
            var file = Path.Combine(directory.Path, "poster.png");
            File.WriteAllBytes(file, [1, 2, 3, 4]);
            Assert.True(new byte[] { 1, 2, 3, 4 }.SequenceEqual(Read(file)));
            using (var stream = File.OpenWrite(file)) stream.SetLength(LocalLibraryArtwork.MaximumBytes + 1L);
            Assert.Equal(0, Read(file).Length);
        });
        Test("本地媒体库图片：取消在读取前生效，零长度与远程路径不进入解码", () =>
        {
            using var directory = new TemporaryDirectory();
            var file = Path.Combine(directory.Path, "poster.webp");
            File.WriteAllBytes(file, []);
            Assert.Equal(0, Read(file).Length);
            Assert.Equal(0, Read("https://example.invalid/poster.png").Length);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.Throws<OperationCanceledException>(() => Read(file, cancelled.Token));
        });
    }

    private static byte[] Read(string path, CancellationToken token = default)
    {
        byte[] bytes = [];
        // 文件IO测试在worker同步汇合，没有UI线程或SynchronizationContext。
        Exception? error = null;
        using var finished = new ManualResetEventSlim();
        _ = Task.Run(async () =>
        {
            try { bytes = await LocalLibraryArtwork.ReadAsync(path, token); }
            catch (Exception caught) { error = caught; }
            finally { finished.Set(); }
        });
        Assert.True(finished.Wait(TimeSpan.FromSeconds(10)), "本地图片读取没有在限时内结束");
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        return bytes;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "momoka-local-artwork-" + Guid.NewGuid().ToString("N"));
        internal TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
