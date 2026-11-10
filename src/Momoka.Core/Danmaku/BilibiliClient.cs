using System.Net;
using System.Text.Json;
using Momoka.Configuration;

namespace Momoka.Danmaku;

/// <summary>B站会话验证、作品定位与XML弹幕读取。固定官方地址，不发送弹幕，也不访问视频字节。</summary>
public sealed partial class BilibiliClient : IDisposable, IAsyncDisposable
{
    public const int MaximumResponseBytes = 32 * 1024 * 1024;
    private const string ApiOrigin = "https://api.bilibili.com";
    private readonly AppSettings _settings;
    private readonly ISecretProtector _protector;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _requests = new(1, 1);
    private readonly object _lifetimeGate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private TaskCompletionSource? _idle;
    private Task? _disposeTask;
    private int _activeRequests;
    private bool _closing;

    public BilibiliClient(AppSettings settings, ISecretProtector protector, HttpMessageHandler? handler = null)
    {
        _settings = settings;
        _protector = protector;
        _http = new HttpClient(handler ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            UseCookies = false,
        }) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public Task<DandanPlaySearchResult> SearchAsync(string title, string? episode, CancellationToken cancellationToken = default) =>
        RunAsync(token => SearchCoreAsync(title, episode, Session(), token), cancellationToken);

    public Task<IReadOnlyList<DanmakuComment>> GetCommentsAsync(long cid, CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<DanmakuComment>>(token => ReadCommentsAsync(cid, token), cancellationToken);

    public Task<BilibiliAccount> ValidateSessionAsync(string cookie, CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            var normalized = BilibiliSession.NormalizeCookie(cookie);
            if (normalized.Length == 0) throw new DandanPlayException("B站登录信息无效，请重新登录。");
            using var document = await GetJsonAsync("/x/web-interface/nav", normalized, token).ConfigureAwait(false);
            var data = Payload(document);
            return Account(data);
        }, cancellationToken);

    private LoginSession Session()
    {
        if (!DanmakuSourceRules.IsSourceReady(_settings, DanmakuSource.Bilibili)
            || !DanmakuSourceRules.Includes(_settings.Danmaku.Source, DanmakuSource.Bilibili))
            throw new DandanPlayException("请先启用弹幕并在设置 → 弹幕中登录 B站。");
        var userId = _settings.Danmaku.BilibiliUserId;
        var cookie = BilibiliSession.Unprotect(_protector, _settings.Danmaku.ProtectedBilibiliCookie, out var error);
        if (cookie.Length == 0) throw new DandanPlayException(error);
        return new(cookie, userId);
    }

    private async Task<JsonDocument> ValidateIdentityAsync(LoginSession session, CancellationToken token)
    {
        var document = await GetJsonAsync("/x/web-interface/nav", session.Cookie, token).ConfigureAwait(false);
        try
        {
            if (Account(Payload(document)).UserId != session.UserId)
                throw new DandanPlayException("B站登录账号已变化，请在设置 → 弹幕中重新登录。");
            return document;
        }
        catch { document.Dispose(); throw; }
    }

    private static BilibiliAccount Account(JsonElement data)
    {
        if (!data.TryGetProperty("isLogin", out var login) || login.ValueKind != JsonValueKind.True
            || Number(data, "mid") is not > 0 || CleanText(data, "uname").Length == 0)
            throw new DandanPlayException("B站登录已失效，请在设置 → 弹幕中重新登录。");
        return new(Number(data, "mid"), CleanText(data, "uname"));
    }

    private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        using var operation = BeginOperation(cancellationToken);
        try
        {
            await _requests.WaitAsync(operation.Token).ConfigureAwait(false);
            try { return await Task.Run(() => action(operation.Token), operation.Token).ConfigureAwait(false); }
            finally { _requests.Release(); }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_shutdown.IsCancellationRequested)
        {
            throw new DandanPlayException("B站请求超时，请稍后重试。");
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException)
        {
            // HTTP/解析异常会夹带URL或响应内容，用户消息不继承异常链。
            throw new DandanPlayException("无法读取 B站数据，请检查网络后重试。");
        }
    }

    private async Task<JsonDocument> GetJsonAsync(string path, string cookie, CancellationToken token)
    {
        var bytes = await GetBytesAsync(new Uri(ApiOrigin + path), cookie, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.Number || !code.TryGetInt32(out var value))
        {
            document.Dispose();
            throw new DandanPlayException("B站返回了无法识别的数据。");
        }
        if (value != 0)
        {
            document.Dispose();
            throw new DandanPlayException(value switch
            {
                -101 => "B站登录已失效，请在设置 → 弹幕中重新登录。",
                -403 or -352 or -412 => "B站暂时限制了访问，请稍后重试或重新登录。",
                -404 => "B站未找到这个作品或剧集。",
                _ => "B站暂时无法提供数据，请稍后重试。",
            });
        }
        return document;
    }

    private async Task<byte[]> GetBytesAsync(Uri uri, string? cookie, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        request.Headers.Referrer = new Uri("https://www.bilibili.com/");
        request.Headers.AcceptEncoding.ParseAdd("gzip, deflate");
        if (cookie is { Length: > 0 }) request.Headers.Add("Cookie", cookie);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.StatusCode is >= HttpStatusCode.MultipleChoices and < HttpStatusCode.BadRequest)
            throw new DandanPlayException("B站返回了跳转，已阻止。");
        if (!response.IsSuccessStatusCode)
            throw new DandanPlayException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "B站登录已失效，请重新登录。",
                HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests => "B站暂时限制了访问，请稍后重试。",
                HttpStatusCode.NotFound => "B站未找到这个作品或弹幕。",
                _ => "B站暂时无法提供数据，请稍后重试。",
            });
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            throw new DandanPlayException("B站返回的数据过大，已停止读取。");
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
            if (read == 0) break;
            if (memory.Length + read > MaximumResponseBytes) throw new DandanPlayException("B站返回的数据过大，已停止读取。");
            memory.Write(buffer, 0, read);
        }
        token.ThrowIfCancellationRequested();
        return memory.ToArray();
    }

    private static JsonElement Payload(JsonDocument document)
    {
        if (document.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object) return data;
        if (document.RootElement.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object) return result;
        throw new DandanPlayException("B站返回了无法识别的数据。");
    }

    private static long Number(JsonElement entry, string name) => entry.ValueKind == JsonValueKind.Object
        && entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : 0;

    private static string CleanText(JsonElement entry, string name)
    {
        if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) return "";
        return new string((value.GetString() ?? "").Where(character => !char.IsControl(character)).Take(300).ToArray()).Trim();
    }

    private RequestOperation BeginOperation(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lifetimeGate)
        {
            if (_closing) throw new OperationCanceledException("B站客户端正在关闭", new CancellationToken(canceled: true));
            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
            lifetime.CancelAfter(TimeSpan.FromSeconds(30));
            if (_activeRequests++ == 0) _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return new(this, lifetime);
        }
    }

    private void EndOperation()
    {
        lock (_lifetimeGate)
        {
            if (--_activeRequests == 0) _idle?.TrySetResult();
        }
    }

    public Task ShutdownAsync()
    {
        lock (_lifetimeGate)
        {
            if (_disposeTask is not null) return _disposeTask;
            _closing = true;
            return _disposeTask = DisposeCoreAsync(_activeRequests == 0 ? Task.CompletedTask : _idle!.Task);
        }
    }

    private async Task DisposeCoreAsync(Task idle)
    {
        try { await _shutdown.CancelAsync().ConfigureAwait(false); }
        catch (AggregateException) { /* 回调异常不能跳过在途操作排空。 */ }
        await idle.ConfigureAwait(false);
        _http.Dispose();
        _requests.Dispose();
        _shutdown.Dispose();
    }

    public void Dispose() => _ = ShutdownAsync();
    public ValueTask DisposeAsync() => new(ShutdownAsync());

    private sealed record LoginSession(string Cookie, long UserId);

    private sealed class RequestOperation(BilibiliClient owner, CancellationTokenSource lifetime) : IDisposable
    {
        internal CancellationToken Token => lifetime.Token;
        public void Dispose()
        {
            lifetime.Dispose();
            owner.EndOperation();
        }
    }
}
