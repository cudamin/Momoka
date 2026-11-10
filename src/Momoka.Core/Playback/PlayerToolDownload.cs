using Momoka.Emby;

namespace Momoka.Playback;

public sealed class PlayerToolDownloadException(string message) : Exception(message);

/// <summary>将受认证媒体交给本地工具之前，先在进程内下载；任何请求信息都不进入子进程。</summary>
public static class PlayerToolDownload
{
    public static HttpClient CreateClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(20)
    }) { Timeout = Timeout.InfiniteTimeSpan };

    public static async Task CopyAsync(HttpClient client, Uri source,
        IReadOnlyList<KeyValuePair<string, string>> headers, string destination,
        IProgress<(long Bytes, long? Total)>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(destination) || File.Exists(destination))
            throw new PlayerToolDownloadException("输出路径无效或文件已存在。");
        var created = false;
        try
        {
            EmbyHttpRedirect.Validate(source);
            var current = source;
            var authenticated = true;
            for (var redirects = 0; ; redirects++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                if (authenticated)
                    foreach (var header in headers)
                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (EmbyHttpRedirect.IsRedirect(response.StatusCode))
                {
                    if (redirects >= EmbyHttpRedirect.Limit) throw new PlayerToolDownloadException("媒体下载重定向次数过多。");
                    var next = EmbyHttpRedirect.Next(current, response.Headers.Location, HttpMethod.Get, response.StatusCode, false);
                    authenticated &= EmbyHttpRedirect.SameOrigin(source, next);
                    current = next;
                    continue;
                }
                if (!response.IsSuccessStatusCode) throw new PlayerToolDownloadException($"媒体下载失败（HTTP {(int)response.StatusCode}）。");
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, useAsync: true);
                created = true;
                var buffer = new byte[131072];
                long written = 0;
                while (await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false) is var count && count > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    written += count;
                    progress?.Report((written, response.Content.Headers.ContentLength));
                }
                if (written == 0 || response.Content.Headers.ContentLength is { } expected && written != expected)
                    throw new PlayerToolDownloadException("媒体下载不完整。");
                break;
            }
        }
        catch (Exception error)
        {
            if (created) { try { File.Delete(destination); } catch (IOException) { } }
            if (error is OperationCanceledException) throw;
            // HttpClient/IO exceptions can include the authenticated URL. Keep only a safe description.
            throw new PlayerToolDownloadException(error is PlayerToolDownloadException known ? known.Message : "无法准备本地媒体文件。");
        }
    }
}
