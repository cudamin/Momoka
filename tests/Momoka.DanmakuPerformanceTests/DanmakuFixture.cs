using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Danmaku;

namespace Momoka.Tests;

/// <summary>只有进程内固定凭据、无 inner handler 的 HTTP 替身和本轮新建缓存；不创建 socket。</summary>
internal sealed class DanmakuFixture : IDisposable
{
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;
    internal string Cache { get; } = Path.Combine(Program.OutputRoot, "cache-" + Guid.NewGuid().ToString("N"));
    internal AppSettings Settings { get; } = new() { Danmaku = new() { Enabled = true } };
    internal FakeCredentials Credentials { get; } = new();
    internal FakeTransport Transport { get; } = new();
    internal DandanPlayClient Client { get; }
    internal Action? CacheReadHook { get; set; }
    internal int CacheThread { get; private set; }

    internal DanmakuFixture(byte[] body)
    {
        Transport.Body = body;
        Client = new(Settings, Cache, Transport, () =>
        {
            CacheThread = Environment.CurrentManagedThreadId;
            CacheReadHook?.Invoke();
            return _now;
        }, Credentials);
    }

    internal async Task WarmAsync(long episode = 7)
    {
        var result = await Client.GetCommentsAsync(episode, true).ConfigureAwait(false);
        if (result.Count == 0) throw new InvalidOperationException("合成缓存未生成。");
    }

    internal static byte[] Body(int count, int textLength = 24)
    {
        using var memory = new MemoryStream();
        using (var writer = new Utf8JsonWriter(memory))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("success", true);
            writer.WriteNumber("errorCode", 0);
            writer.WriteStartArray("comments");
            for (var index = 0; index < count; index++)
            {
                // 7919 与 40000 互质：固定打乱的时间轴，不能误测本就有序的数组。
                var time = (index * 7919L % Math.Max(1, count)) / 40d;
                writer.WriteStartObject();
                writer.WriteString("p", time.ToString(CultureInfo.InvariantCulture) + ",1,16777215,fixture");
                writer.WriteString("m", new string('x', textLength));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return memory.ToArray();
    }

    public void Dispose() => Client.Dispose(); // 保留本轮私有缓存为证据；不触碰任何旧目录。

    internal sealed class FakeCredentials : IDandanPlayCredentialSource
    {
        internal int LastThread { get; private set; }
        internal int Calls { get; private set; }
        internal Func<CancellationToken, Task>? BeforeReturn { get; set; }
        public async Task<DandanPlayCredential> GetAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            LastThread = Environment.CurrentManagedThreadId;
            if (BeforeReturn is { } callback) await callback(cancellationToken).ConfigureAwait(false);
            return new("fixture-performance-app", "fixture-performance-secret", "fixture-performance-kid");
        }
        public void Invalidate() { }
    }

    internal sealed class FakeTransport : HttpMessageHandler
    {
        private int _requests;
        internal int Requests => Volatile.Read(ref _requests);
        internal byte[] Body { get; set; } = [];
        internal bool Forbid { get; set; }
        internal Func<CancellationToken, Task>? BeforeReturn { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref _requests);
            if (Forbid) throw new InvalidOperationException("有效缓存命中不应联网。");
            if (BeforeReturn is { } callback) await callback(token).ConfigureAwait(false);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Body) };
        }
    }
}

/// <summary>单线程 FIFO SynchronizationContext，不是 WinUI DispatcherQueue 或实际屏幕帧时延。</summary>
internal sealed class FifoContext : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly Thread _thread;
    private readonly TaskCompletionSource<int> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int ThreadId => _ready.Task.GetAwaiter().GetResult();

    internal FifoContext()
    {
        _thread = new(() =>
        {
            SetSynchronizationContext(this);
            _ready.SetResult(Environment.CurrentManagedThreadId);
            foreach (var (callback, state) in _queue.GetConsumingEnumerable()) callback(state);
        }) { IsBackground = true, Name = "Momoka synthetic FIFO UI" };
        _thread.Start();
    }

    public override void Post(SendOrPostCallback callback, object? state) => _queue.Add((callback, state));
    internal Task<T> InvokeAsync<T>(Func<Task<T>> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(async _ =>
        {
            try { completion.SetResult(await action().ConfigureAwait(true)); }
            catch (Exception error) { completion.SetException(error); }
        }, null);
        return completion.Task;
    }
    internal Task<T> Invoke<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(_ =>
        {
            try { completion.SetResult(action()); }
            catch (Exception error) { completion.SetException(error); }
        }, null);
        return completion.Task;
    }
    public void Dispose()
    {
        _queue.CompleteAdding();
        if (!_thread.Join(TimeSpan.FromSeconds(10))) throw new InvalidOperationException("合成 UI 队列未退出。");
        _queue.Dispose();
    }
}
