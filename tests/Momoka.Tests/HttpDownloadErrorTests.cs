using System.Net;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>Runs the real long-download transport against stalled error bodies; never opens a socket.</summary>
internal static class HttpDownloadErrorTests
{
    private static readonly Uri Url = new("https://download-error.example.test/emby/Videos/file");
    private static readonly EmbyHttp.RequestContext Context = new(DeviceIdentity.Create("download-error-test", "1"), null);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(4);
    private static readonly HttpStatusCode[] Statuses =
        [HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.ServiceUnavailable];

    public static void Register()
    {
        foreach (var status in Statuses)
        {
            foreach (var prefix in new[] { false, true })
            {
                Test($"HTTP 长下载错误：{(int)status} {(prefix ? "部分正文" : "空正文")}停住仍及时保留原状态", () =>
                    WithPath(path => CheckStalledBody(path, status, prefix)));
            }

            Test($"HTTP 长下载错误：{(int)status} 正文等待时调用方取消优先于状态翻译", () =>
                WithPath(path =>
                {
                    using var body = new StalledReadStream([]);
                    using var content = new TrackedContent(body);
                    using var http = new EmbyHttp(new ReplyTransport(() => Reply(status, content)), RequestTimeout);
                    using var caller = new CancellationTokenSource();
                    var download = http.DownloadToFileAsync(Url, path, Context, null, caller.Token);
                    try
                    {
                        body.Started.WaitAsync(TestTimeout).GetAwaiter().GetResult();
                        caller.Cancel();
                        Assert.Throws<OperationCanceledException>(() => download.WaitAsync(TestTimeout).GetAwaiter().GetResult());
                        Assert.True(content.Disposed);
                        Assert.True(body.Disposed);
                        AssertOldFile(path);
                    }
                    finally { caller.Cancel(); ObserveCompletion(download); }
                }));

            Test($"HTTP 长下载错误：{(int)status} 小正文读完仍按原状态释放响应", () =>
                WithPath(path =>
                {
                    using var content = new TrackedContent(new MemoryStream([1, 2, 3]));
                    using var http = new EmbyHttp(new ReplyTransport(() => Reply(status, content)), RequestTimeout);
                    var error = Assert.Catch<EmbyApiException>(() =>
                        http.DownloadToFileAsync(Url, path, Context, null, CancellationToken.None).GetAwaiter().GetResult());
                    AssertStatus(status, error);
                    Assert.True(content.Disposed);
                    AssertOldFile(path);
                }));
        }

        Test("HTTP 长下载错误：取得错误流时停住也受独立截止约束", () =>
            WithPath(path =>
            {
                using var content = new StalledOpenContent();
                using var http = new EmbyHttp(new ReplyTransport(() => Reply(HttpStatusCode.ServiceUnavailable, content)), RequestTimeout);
                using var cleanup = new CancellationTokenSource();
                var download = http.DownloadToFileAsync(Url, path, Context, null, cleanup.Token);
                try
                {
                    content.Started.WaitAsync(TestTimeout).GetAwaiter().GetResult();
                    var error = Assert.Catch<EmbyApiException>(() => download.WaitAsync(TestTimeout).GetAwaiter().GetResult());
                    AssertStatus(HttpStatusCode.ServiceUnavailable, error);
                    Assert.False(cleanup.IsCancellationRequested);
                    Assert.True(content.Disposed);
                    AssertOldFile(path);
                }
                finally { cleanup.Cancel(); ObserveCompletion(download); }
            }));

        Test("HTTP 长下载错误：可选正文IO失败不覆盖已知503", () =>
            WithPath(path =>
            {
                using var content = new TrackedContent(new FailedReadStream());
                using var http = new EmbyHttp(new ReplyTransport(() => Reply(HttpStatusCode.ServiceUnavailable, content)), RequestTimeout);
                var error = Assert.Catch<EmbyApiException>(() =>
                    http.DownloadToFileAsync(Url, path, Context, null, CancellationToken.None).GetAwaiter().GetResult());
                AssertStatus(HttpStatusCode.ServiceUnavailable, error);
                Assert.True(content.Disposed);
                Assert.DoesNotContain("optional body failure", error.ToString());
                AssertOldFile(path);
            }));

        Test("HTTP 长下载错误：正文忽略取消并返回时仍保留调用方取消", () =>
            WithPath(path =>
            {
                using var caller = new CancellationTokenSource();
                using var content = new TrackedContent(new CancelAndEndStream(caller));
                using var http = new EmbyHttp(new ReplyTransport(() => Reply(HttpStatusCode.Forbidden, content)), RequestTimeout);
                Assert.Throws<OperationCanceledException>(() =>
                    http.DownloadToFileAsync(Url, path, Context, null, caller.Token).GetAwaiter().GetResult());
                Assert.True(content.Disposed);
                AssertOldFile(path);
            }));

        Test("HTTP 长下载错误：大正文最多消耗8000字节且不留下临时文件", () =>
            WithPath(path =>
            {
                using var body = new CountingReadStream(new byte[16000]);
                using var content = new TrackedContent(body);
                using var http = new EmbyHttp(new ReplyTransport(() => Reply(HttpStatusCode.Forbidden, content)), RequestTimeout);
                var error = Assert.Catch<EmbyApiException>(() =>
                    http.DownloadToFileAsync(Url, path, Context, null, CancellationToken.None).GetAwaiter().GetResult());
                AssertStatus(HttpStatusCode.Forbidden, error);
                Assert.Equal(8000, body.BytesRead);
                Assert.True(content.Disposed);
                AssertOldFile(path);
            }));

        Test("HTTP 长下载错误：失败释放后同一客户端的后续排队下载可完成", () =>
            WithPath(path =>
            {
                using var body = new StalledReadStream([1]);
                using var content = new TrackedContent(body);
                var requests = 0;
                using var http = new EmbyHttp(new ReplyTransport(() => ++requests == 1
                    ? Reply(HttpStatusCode.ServiceUnavailable, content)
                    : Reply(HttpStatusCode.OK, new ByteArrayContent([7, 8, 9]))), RequestTimeout);
                using var gate = new SemaphoreSlim(1);
                using var cleanup = new CancellationTokenSource();
                async Task<long> QueuedDownload(string destination)
                {
                    await gate.WaitAsync(cleanup.Token).ConfigureAwait(false);
                    try { return await http.DownloadToFileAsync(Url, destination, Context, null, cleanup.Token).ConfigureAwait(false); }
                    finally { gate.Release(); }
                }
                var first = QueuedDownload(path);
                Task<long>? second = null;
                try
                {
                    body.Started.WaitAsync(TestTimeout).GetAwaiter().GetResult();
                    second = QueuedDownload(path + ".next");
                    Assert.Equal(1, requests, "前项仍在等错误正文时后项保持排队");
                    var error = Assert.Catch<EmbyApiException>(() => first.WaitAsync(TestTimeout).GetAwaiter().GetResult());
                    AssertStatus(HttpStatusCode.ServiceUnavailable, error);
                    Assert.Equal(3L, second.WaitAsync(TestTimeout).GetAwaiter().GetResult());
                    Assert.True(File.ReadAllBytes(path + ".next").SequenceEqual(new byte[] { 7, 8, 9 }));
                    Assert.Equal(2, requests);
                    Assert.Equal(1, gate.CurrentCount);
                    Assert.True(content.Disposed);
                    AssertOldFile(path);
                    Assert.False(File.Exists(path + ".next.part"));
                }
                finally
                {
                    cleanup.Cancel();
                    ObserveCompletion(first);
                    if (second is not null) ObserveCompletion(second);
                }
            }));

        Test("HTTP 长下载成功：正文超过错误正文预算和普通期限仍可完成", () =>
            WithPath(path =>
            {
                using var body = new StalledReadStream([4, 5, 6]);
                using var http = new EmbyHttp(new ReplyTransport(() => Reply(HttpStatusCode.OK, new StreamContent(body))), RequestTimeout);
                using var cleanup = new CancellationTokenSource();
                var download = http.DownloadToFileAsync(Url, path, Context, null, cleanup.Token);
                try
                {
                    body.Started.WaitAsync(TestTimeout).GetAwaiter().GetResult();
                    Task.Delay(TimeSpan.FromMilliseconds(2400)).GetAwaiter().GetResult();
                    Assert.False(download.IsCompleted, "成功长下载不能套用短错误正文截止");
                    body.Resume();
                    Assert.Equal(3L, download.WaitAsync(TestTimeout).GetAwaiter().GetResult());
                    Assert.True(File.ReadAllBytes(path).SequenceEqual(new byte[] { 4, 5, 6 }));
                    Assert.False(File.Exists(path + ".part"));
                }
                finally { cleanup.Cancel(); ObserveCompletion(download); }
            }));
    }

    private static void CheckStalledBody(string path, HttpStatusCode status, bool prefix)
    {
        using var body = new StalledReadStream(prefix ? [1, 2, 3] : []);
        using var content = new TrackedContent(body);
        using var http = new EmbyHttp(new ReplyTransport(() => Reply(status, content)), RequestTimeout);
        using var cleanup = new CancellationTokenSource();
        var download = http.DownloadToFileAsync(Url, path, Context, null, cleanup.Token);
        try
        {
            body.Started.WaitAsync(TestTimeout).GetAwaiter().GetResult();
            var error = Assert.Catch<EmbyApiException>(() => download.WaitAsync(TestTimeout).GetAwaiter().GetResult());
            AssertStatus(status, error);
            Assert.False(cleanup.IsCancellationRequested, "不能依赖用户取消解除错误正文等待");
            Assert.True(content.Disposed, "读取超时必须释放响应正文");
            Assert.True(body.Disposed);
            AssertOldFile(path);
        }
        finally { cleanup.Cancel(); ObserveCompletion(download); }
    }

    private static HttpResponseMessage Reply(HttpStatusCode status, HttpContent content) => new(status) { Content = content };

    private static void AssertStatus(HttpStatusCode status, EmbyApiException error)
    {
        Assert.Equal<HttpStatusCode?>(status, error.StatusCode);
        Assert.False(error is EmbyUnreachableException, "状态已知，错误正文短截止不能改成网络超时");
        Assert.Equal(status == HttpStatusCode.Unauthorized, error is EmbyTokenExpiredException);
    }

    private static void WithPath(Action<string> check)
    {
        // Preserved inside this test output, not the user's Downloads or product profile.
        var directory = Path.Combine(AppContext.BaseDirectory, "http-download-error-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "download.bin");
        File.WriteAllBytes(path, [91, 92]);
        check(path);
    }

    private static void AssertOldFile(string path)
    {
        Assert.True(File.ReadAllBytes(path).SequenceEqual(new byte[] { 91, 92 }), "错误响应不得覆盖已有目标");
        Assert.False(File.Exists(path + ".part"));
    }

    private static void ObserveCompletion(Task task)
    {
        try { task.WaitAsync(TestTimeout).GetAwaiter().GetResult(); }
        catch (Exception) { }
    }

    private sealed class ReplyTransport(Func<HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(reply());
    }

    private sealed class TrackedContent(Stream body) : StreamContent(body)
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            if (disposing) Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class StalledOpenContent : HttpContent
    {
        private readonly TaskCompletionSource<bool> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Started => _started.Task;
        public bool Disposed { get; private set; }
        protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
        {
            _started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return Stream.Null;
        }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new NotSupportedException();
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override void Dispose(bool disposing)
        {
            if (disposing) Disposed = true;
            base.Dispose(disposing);
        }
    }

    private class CountingReadStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int BytesRead { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            BytesRead += read;
            return read;
        }
    }

    private sealed class StalledReadStream(byte[] prefix) : Stream
    {
        private readonly MemoryStream _source = new(prefix);
        private readonly TaskCompletionSource<bool> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Started => _started.Task;
        public bool Disposed { get; private set; }
        public void Resume() => _resume.TrySetResult(true);
        public override bool CanRead => !Disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_source.Position < _source.Length)
                return await _source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            _started.TrySetResult(true);
            await _resume.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disposed = true;
                _resume.TrySetCanceled();
                _source.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private sealed class FailedReadStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("optional body failure"));
    }

    private sealed class CancelAndEndStream(CancellationTokenSource caller) : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            caller.Cancel();
            return ValueTask.FromResult(0);
        }
    }
}
