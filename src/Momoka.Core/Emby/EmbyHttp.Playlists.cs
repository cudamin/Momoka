using System.Net;
using System.Net.Http.Json;
using Momoka.Diagnostics;

namespace Momoka.Emby;

/// <summary>A sent playlist write has no confirmed response. Read back before offering another write.</summary>
public sealed class PlaylistWriteUncertainException : Exception
{
    public PlaylistWriteUncertainException() : base("服务器播放列表操作的结果未能确认，请先刷新列表核对，避免重复提交。") { }
}

public sealed partial class EmbyHttp
{
    internal async Task WritePlaylistAsync(HttpMethod method, Uri url, object? body, RequestContext context,
        CancellationToken cancellationToken, Action? confirmed) =>
        await WritePlaylistForJsonAsync<object>(method, url, body, context, cancellationToken, confirmed,
            readReceipt: false).ConfigureAwait(false);

    /// <summary>Success headers confirm the write; optional receipt IO never makes it retryable.</summary>
    internal async Task<T?> WritePlaylistForJsonAsync<T>(HttpMethod method, Uri url, object? body, RequestContext context,
        CancellationToken cancellationToken, Action? confirmed, bool readReceipt = true) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_http.Timeout);
        var accepted = false;
        T? receipt = null;
        try
        {
            using var response = await SendAsync(method, url, body, context, deadline.Token,
                retainErrorStatus: true).ConfigureAwait(false);
            accepted = true;
            try { confirmed?.Invoke(); }
            catch (Exception error) { Log.Debug(Category, $"播放列表回执通知失败（{error.GetType().Name}）"); }
            if (!readReceipt || response.StatusCode == HttpStatusCode.NoContent) return null;
            try
            {
                receipt = await response.Content.ReadFromJsonAsync<T>(Json, deadline.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is System.Text.Json.JsonException or NotSupportedException
                or OperationCanceledException or HttpRequestException or IOException)
            {
                Log.Debug(Category, $"播放列表写入已接受，附属回执不可读（{error.GetType().Name}）");
            }
            return receipt;
        }
        catch (Exception) when (accepted) { return receipt; }
        // An explicit 4xx rejected the request; only its authenticated 401 can safely trigger retry.
        catch (EmbyApiException error) when (error.StatusCode is { } status && (int)status is >= 400 and < 500) { throw; }
        catch (Exception error) when (error is EmbyApiException or OperationCanceledException
            or HttpRequestException or IOException)
        {
            throw new PlaylistWriteUncertainException();
        }
    }
}
