using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Momoka.Configuration;
using Momoka.Translation;

namespace Momoka.SubtitleTranslationTests;

internal sealed class Fixture : IDisposable
{
    // 匿名占位，永不使用用户配置、真实凭据或网络。
    public const string Key = "placeholder-subtitle-api-key";
    public const string BodyMarker = "UNTRUSTED_RESPONSE_MARKER";
    public FakeHandler Handler { get; }
    public HttpClient Http { get; }
    public SubtitleTranslationClient Client { get; }

    public Fixture(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? send = null, TimeSpan? timeout = null)
    {
        Handler = new FakeHandler(send ?? ((_, _) => Task.FromResult(Json(GoogleJson))));
        Http = new HttpClient(Handler) { Timeout = Timeout.InfiniteTimeSpan };
        Client = timeout is { } value ? new SubtitleTranslationClient(Http, value) : new SubtitleTranslationClient(Http);
    }

    public static SubtitleTranslationSettings Settings(SubtitleTranslationProvider provider = SubtitleTranslationProvider.Google) =>
        new() { Enabled = true, Provider = provider };
    public const string GoogleJson = "{\"data\":{\"translations\":[{\"translatedText\":\"你好\"}]}}";
    public const string MicrosoftJson = "[{\"translations\":[{\"text\":\"你好\",\"to\":\"zh-Hans\"}]}]";
    public const string AiJson = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"你好\"},\"finish_reason\":\"stop\"}]}";
    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    public void Dispose() { Client.Dispose(); Http.Dispose(); }
}

internal sealed record RequestSnapshot(Uri Uri, string Body, string ContentType, IReadOnlyDictionary<string, string[]> Headers);

internal sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    public ConcurrentQueue<RequestSnapshot> Requests { get; } = new();
    public bool Disposed { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Test.Equal(HttpMethod.Post, request.Method);
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Enqueue(new RequestSnapshot(request.RequestUri!, body, request.Content?.Headers.ContentType?.ToString() ?? "",
            request.Headers.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase)));
        return await send(request, cancellationToken);
    }
    protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
}

internal sealed class BlockingStream : Stream
{
    public override bool CanRead => true;
    public override bool CanWrite => false;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return 0;
    }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class UnseekableStream(byte[] bytes) : Stream
{
    private int _position;
    public override bool CanRead => true;
    public override bool CanWrite => false;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var count = Math.Min(buffer.Length, bytes.Length - _position);
        bytes.AsMemory(_position, count).CopyTo(buffer);
        _position += count;
        return ValueTask.FromResult(count);
    }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class PoisonContent : HttpContent
{
    public bool ReadAttempted { get; private set; }
    protected override bool TryComputeLength(out long length) { length = 0; return false; }
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        ReadAttempted = true;
        throw new InvalidOperationException(Fixture.Key + Fixture.BodyMarker);
    }
}
