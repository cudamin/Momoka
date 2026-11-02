using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Momoka.Danmaku;

public sealed partial class DandanPlayClient
{
    /// <summary>
    /// danmu_api 的 /api/v2/match：用片名、年份和集号换取单一条目，避免多源搜索结果让自动匹配失效。
    /// 只走用户自部署的来源，不签名，也不跟随跳转。
    /// </summary>
    public async Task<DandanPlayEpisode?> MatchAsync(string fileName, CancellationToken cancellationToken = default)
    {
        fileName = (fileName ?? "").Trim();
        if (fileName.Length is < 2 or > 200 || fileName.Any(char.IsControl))
            throw new DandanPlayException("自动匹配需要一个 2～200 字的片名。");

        using var operation = BeginOperation(cancellationToken);
        cancellationToken = operation.Token;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var entered = false;
        try
        {
            await _requests.WaitAsync(deadline.Token).ConfigureAwait(false);
            entered = true;
            var connection = await ResolveAsync(DanmakuSource.DanmuApi, deadline.Token).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(connection.BaseUri, "api/v2/match"));
            request.Headers.Accept.ParseAdd("application/json");
            request.Content = new StringContent(JsonSerializer.Serialize(new { fileName }), Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                .ConfigureAwait(false);
            if (IsRedirect(response.StatusCode)) throw new DandanPlayException("danmu_api 返回了跳转，已阻止。");
            if (!response.IsSuccessStatusCode) throw Failure(connection, response);
            var bytes = await ReadBoundedAsync(response.Content, deadline.Token).ConfigureAwait(false);
            return ParseMatch(bytes);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DandanPlayException("danmu_api 自动匹配超时，请手动搜索。");
        }
        catch (HttpRequestException) { throw new DandanPlayException("无法连接 danmu_api，请检查地址与网络。"); }
        catch (IOException) { throw new DandanPlayException("读取 danmu_api 自动匹配结果失败，请稍后重试。"); }
        finally { if (entered) _requests.Release(); }
    }

    private static DandanPlayEpisode? ParseMatch(byte[] bytes)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(bytes); }
        catch (JsonException) { throw new DandanPlayException("danmu_api 返回了无法识别的自动匹配结果。"); }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False
                || root.TryGetProperty("errorCode", out var code)
                    && (code.ValueKind != JsonValueKind.Number || !code.TryGetInt32(out var number) || number != 0))
                throw new DandanPlayException("danmu_api 拒绝了自动匹配请求。");
            if (!root.TryGetProperty("isMatched", out var matched) || matched.ValueKind != JsonValueKind.True)
                return null;
            if (!root.TryGetProperty("matches", out var matches) || matches.ValueKind != JsonValueKind.Array
                || matches.GetArrayLength() == 0) return null;
            var first = matches[0];
            if (first.ValueKind != JsonValueKind.Object
                || !first.TryGetProperty("episodeId", out var id) || id.ValueKind != JsonValueKind.Number
                || !id.TryGetInt64(out var episodeId) || episodeId <= 0) return null;
            var animeTitle = Text(first, "animeTitle");
            if (animeTitle.Length == 0) return null;
            return new DandanPlayEpisode(episodeId, animeTitle, Text(first, "episodeTitle"));
        }
    }
}
