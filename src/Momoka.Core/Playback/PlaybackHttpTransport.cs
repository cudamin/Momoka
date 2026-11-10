using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Momoka.Diagnostics;
using Momoka.Emby;

namespace Momoka.Playback;

/// <summary>
/// One file's HTTP capability endpoints. Native players never receive server credentials: every
/// read, seek and reconnect passes through the same managed redirect boundary. Only explicitly
/// registered resources exist; relative playlist requests cannot expand this into an open proxy.
/// </summary>
public sealed class PlaybackHttpTransport : IAsyncDisposable
{
    private static readonly string[] ResponseHeaders =
        ["Content-Type", "Content-Length", "Content-Range", "Content-Encoding", "Accept-Ranges", "ETag", "Last-Modified"];
    private static readonly string[] NestedExtensions =
        [".m3u", ".m3u8", ".mpd", ".pls", ".asx", ".xspf", ".cue", ".edl", ".url", ".f4m"];
    private readonly Uri _origin;
    private readonly IReadOnlyList<KeyValuePair<string, string>> _headers;
    private readonly HttpClient _http;
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentDictionary<string, Uri> _resources = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _mapped = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<long, Task> _requests = new();
    private readonly ConcurrentDictionary<long, TcpClient> _clients = new();
    private readonly Task _accept;
    private readonly string _prefix = "/" + Guid.NewGuid().ToString("N") + "/";
    private readonly string _base;
    private long _requestId;
    private int _disposed;
    private int _cancelled;
    private readonly object _cancelGate = new();

    public PlaybackHttpTransport(PlaybackRequest request) : this(request, new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        Credentials = null,
        AutomaticDecompression = DecompressionMethods.None
    })
    { }

    internal PlaybackHttpTransport(PlaybackRequest request, HttpMessageHandler handler)
    {
        EmbyHttpRedirect.Validate(request.MediaUrl);
        _origin = request.MediaUrl;
        _headers = request.HttpHeaders.ToArray();
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _listener.Start();
        _base = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;
        _accept = AcceptAsync();
    }

    public static PlaybackHttpTransport? Create(PlaybackRequest request) =>
        request.MediaUrl.IsFile || request.HttpHeaders.Count == 0 ? null : new(request);

    public PlaybackRequest Prepare(PlaybackRequest request) => request with
    {
        MediaUrl = new Uri(Map(request.MediaUrl)),
        MediaParts = request.MediaParts.Select(part => part with { MediaUrl = new Uri(Map(part.MediaUrl)) }).ToArray(),
        ExternalSubtitles = request.ExternalSubtitles.Select(resource => new Uri(Map(resource))).ToArray(),
        SelectedExternalSubtitle = request.SelectedExternalSubtitle is { } selected ? new Uri(Map(selected)) : null,
        HttpHeaders = []
    };

    public string Map(Uri resource)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (resource.IsFile) return resource.AbsoluteUri;
        EmbyHttpRedirect.Validate(resource);
        var extension = Path.GetExtension(resource.AbsolutePath).ToLowerInvariant();
        if (NestedExtensions.Contains(extension, StringComparer.Ordinal))
            throw new InvalidOperationException("认证播放暂不支持 HLS、DASH 或播放列表，请选择原始媒体文件");
        return _mapped.GetOrAdd(resource.AbsoluteUri, _ =>
        {
            var path = _prefix + Guid.NewGuid().ToString("N")
                + (extension.Length <= 12 && extension.All(character => char.IsAsciiLetterOrDigit(character) || character == '.') ? extension : "");
            _resources[path] = resource;
            return _base + path;
        });
    }

    /// <summary>Track filenames keep the original identity used by initial server-subtitle selection.</summary>
    public string? Original(string? filename) =>
        filename is not null && filename.StartsWith(_base, StringComparison.Ordinal)
            && _resources.TryGetValue(filename[_base.Length..], out var resource) ? resource.AbsoluteUri : filename;

    public IReadOnlyList<MpvTrack> OriginalTracks(IReadOnlyList<MpvTrack> tracks) =>
        tracks.Select(track => track with { ExternalFilename = Original(track.ExternalFilename) }).ToArray();

    public string[] MapCommand(IReadOnlyList<string> arguments)
    {
        var copy = arguments.ToArray();
        if (copy.Length > 1 && copy[0] is "sub-add" or "audio-add" or "loadfile"
            && Uri.TryCreate(copy[1], UriKind.Absolute, out var resource)
            && resource.Scheme is "http" or "https") copy[1] = Map(resource);
        return copy;
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_lifetime.Token).ConfigureAwait(false);
                var id = Interlocked.Increment(ref _requestId);
                _clients[id] = client;
                var work = ServeAsync(client);
                _requests[id] = work;
                _ = RemoveAsync(id, work);
            }
        }
        catch (Exception error) when (error is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }

    private async Task RemoveAsync(long id, Task work)
    {
        try { await work.ConfigureAwait(false); }
        finally { _requests.TryRemove(id, out _); _clients.TryRemove(id, out _); }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            NetworkStream stream;
            try { stream = client.GetStream(); }
            catch (Exception error) when (_lifetime.IsCancellationRequested && error is ObjectDisposedException or InvalidOperationException) { return; }
            var wroteHeaders = false;
            using var requestLifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            Task? disconnected = null;
            try
            {
                using var reading = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                reading.CancelAfter(TimeSpan.FromSeconds(10));
                var text = await ReadHeadersAsync(stream, reading.Token).ConfigureAwait(false);
                var lines = text.Split("\r\n", StringSplitOptions.None);
                var start = lines[0].Split(' ');
                if (start.Length != 3 || start[2] != "HTTP/1.1" && start[2] != "HTTP/1.0"
                    || start[0] is not ("GET" or "HEAD")
                    || !_resources.TryGetValue(start[1], out var resource))
                {
                    await ErrorAsync(stream, 404, _lifetime.Token).ConfigureAwait(false);
                    return;
                }
                string? range = null;
                string? ifRange = null;
                foreach (var line in lines.Skip(1))
                {
                    var colon = line.IndexOf(':');
                    if (colon < 0) continue;
                    var name = line[..colon];
                    var value = line[(colon + 1)..].Trim();
                    if (name.Equals("Range", StringComparison.OrdinalIgnoreCase)) range = value;
                    if (name.Equals("If-Range", StringComparison.OrdinalIgnoreCase)) ifRange = value;
                }
                disconnected = CancelOnDisconnectAsync(stream, requestLifetime);
                using var response = await SendAsync(resource, new HttpMethod(start[0]), range, ifRange, requestLifetime.Token).ConfigureAwait(false);
                if (IsNestedContent(response.Content.Headers.ContentType?.MediaType))
                    throw new InvalidOperationException("认证播放不支持嵌套清单");
                await using var body = await response.Content.ReadAsStreamAsync(requestLifetime.Token).ConfigureAwait(false);
                var prefix = new byte[512];
                var count = 0;
                var inspect = start[0] != "HEAD" && response.Content.Headers.ContentEncoding.Count == 0;
                while (inspect && count < prefix.Length)
                {
                    var read = await body.ReadAsync(prefix.AsMemory(count), requestLifetime.Token).ConfigureAwait(false);
                    if (read == 0) break;
                    count += read;
                }
                var sniff = Encoding.UTF8.GetString(prefix, 0, count).TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
                if (sniff.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase)
                    || sniff.StartsWith("# mpv EDL", StringComparison.OrdinalIgnoreCase)
                    || sniff.StartsWith("[playlist]", StringComparison.OrdinalIgnoreCase)
                    || sniff.StartsWith("<ASX", StringComparison.OrdinalIgnoreCase)
                    || sniff.Contains("<MPD", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("认证播放不支持嵌套清单");
                var headers = new StringBuilder($"HTTP/1.1 {(int)response.StatusCode} Media\r\nConnection: close\r\n");
                foreach (var name in ResponseHeaders)
                {
                    if (!response.Headers.TryGetValues(name, out var values)
                        && !response.Content.Headers.TryGetValues(name, out values)) continue;
                    foreach (var value in values)
                        if (!value.Contains('\r') && !value.Contains('\n')) headers.Append(name).Append(": ").Append(value).Append("\r\n");
                }
                headers.Append("\r\n");
                await stream.WriteAsync(Encoding.ASCII.GetBytes(headers.ToString()), requestLifetime.Token).ConfigureAwait(false);
                wroteHeaders = true;
                if (start[0] != "HEAD")
                {
                    await stream.WriteAsync(prefix.AsMemory(0, count), requestLifetime.Token).ConfigureAwait(false);
                    await body.CopyToAsync(stream, requestLifetime.Token).ConfigureAwait(false);
                }
            }
            catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException
                or InvalidOperationException or EmbyApiException or ObjectDisposedException)
            {
                // Native and managed diagnostics must not retain URLs, capability paths or header values.
                if (!requestLifetime.IsCancellationRequested) Log.Warn("mpv", "播放 HTTP 转发失败，未向原生播放器交出服务器认证");
                if (!wroteHeaders && !requestLifetime.IsCancellationRequested)
                    try { await ErrorAsync(stream, 502, _lifetime.Token).ConfigureAwait(false); }
                    catch (Exception failure) when (failure is IOException or OperationCanceledException or ObjectDisposedException) { }
            }
            finally
            {
                requestLifetime.Cancel();
                if (disconnected is not null) await disconnected.ConfigureAwait(false);
            }
        }
    }

    private async Task<HttpResponseMessage> SendAsync(Uri resource, HttpMethod method, string? range, string? ifRange, CancellationToken token)
    {
        var authenticated = EmbyHttpRedirect.SameOrigin(_origin, resource);
        for (var hop = 0; ; hop++)
        {
            using var request = new HttpRequestMessage(method, resource);
            if (authenticated)
                foreach (var header in _headers) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
            if (range is not null) request.Headers.TryAddWithoutValidation("Range", range);
            if (ifRange is not null) request.Headers.TryAddWithoutValidation("If-Range", ifRange);
            using var headersDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            headersDeadline.CancelAfter(TimeSpan.FromSeconds(30));
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headersDeadline.Token).ConfigureAwait(false);
            if (!EmbyHttpRedirect.IsRedirect(response.StatusCode)) return response;
            using (response)
            {
                if (hop >= EmbyHttpRedirect.Limit) throw new InvalidOperationException("播放 HTTP 重定向次数过多");
                var next = EmbyHttpRedirect.Next(resource, response.Headers.Location, method, response.StatusCode, false);
                authenticated &= EmbyHttpRedirect.SameOrigin(resource, next);
                resource = next;
            }
        }
    }

    private static bool IsNestedContent(string? type) => type is not null
        && (type.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) || type.Contains("dash+xml", StringComparison.OrdinalIgnoreCase)
            || type.Contains("scpls", StringComparison.OrdinalIgnoreCase) || type.Contains("xspf", StringComparison.OrdinalIgnoreCase));

    private static async Task CancelOnDisconnectAsync(Stream stream, CancellationTokenSource lifetime)
    {
        try
        {
            // Clients send no request body. EOF, an unexpected body or pipelining ends this capability
            // read; seeks abort the outstanding upstream request rather than retaining it until Stop.
            await stream.ReadAsync(new byte[1], lifetime.Token).ConfigureAwait(false);
            lifetime.Cancel();
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or ObjectDisposedException)
        {
            if (!lifetime.IsCancellationRequested) lifetime.Cancel();
        }
    }

    private static async Task<string> ReadHeadersAsync(Stream stream, CancellationToken token)
    {
        var bytes = new byte[16 * 1024];
        var count = 0;
        while (count < bytes.Length)
        {
            if (await stream.ReadAsync(bytes.AsMemory(count, 1), token).ConfigureAwait(false) == 0) break;
            count++;
            if (count >= 4 && bytes[count - 4] == 13 && bytes[count - 3] == 10 && bytes[count - 2] == 13 && bytes[count - 1] == 10)
                return Encoding.ASCII.GetString(bytes, 0, count);
        }
        throw new IOException("无效的播放 HTTP 请求");
    }

    private static Task ErrorAsync(Stream stream, int status, CancellationToken token) =>
        stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Media\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), token).AsTask();

    public void Cancel()
    {
        lock (_cancelGate)
        {
            if (Interlocked.Exchange(ref _cancelled, 1) != 0) return;
            _lifetime.Cancel();
            _listener.Stop();
            foreach (var client in _clients.Values) client.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Cancel();
        await _accept.ConfigureAwait(false);
        await Task.WhenAll(_requests.Values).ConfigureAwait(false);
        _http.Dispose();
        lock (_cancelGate) _lifetime.Dispose();
    }
}
