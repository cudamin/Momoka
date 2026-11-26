using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Momoka.Scrobbling;

/// <summary>Bangumi v0 collection API; bearer credentials stay on the fixed API origin.</summary>
public sealed class BangumiClient : IDisposable
{
    private const int MaxResponseBytes = 2 * 1024 * 1024;
    private static readonly Uri ApiRoot = new("https://api.bgm.tv/");
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public BangumiClient(HttpClient? http = null)
    {
        _ownsHttp = http is null;
        _http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(20) };
    }

    public async Task<ScrobblingSubject> SubjectAsync(long id, string token, CancellationToken cancellationToken)
    {
        ValidateId(id);
        using var document = await SendAsync(HttpMethod.Get, $"v0/subjects/{id}", token, null, null, false, cancellationToken).ConfigureAwait(false)
            ?? throw InvalidResponse();
        var root = document.RootElement;
        if (root.GetProperty("id").GetInt64() != id) throw InvalidResponse();
        var title = Text(root, "name_cn");
        if (string.IsNullOrWhiteSpace(title)) title = Text(root, "name");
        if (string.IsNullOrWhiteSpace(title)) throw InvalidResponse();
        int? count = root.TryGetProperty("eps", out var eps) && eps.TryGetInt32(out var value) && value > 0 ? value : null;
        return new(id, title, count);
    }

    /// <summary>Read-only credential check. A successful identity read does not prove collection write permission.</summary>
    public async Task CheckAccessAsync(string token, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Get, "v0/me", token, null, null, false, cancellationToken).ConfigureAwait(false)
            ?? throw InvalidResponse();
        if (string.IsNullOrWhiteSpace(Text(document.RootElement, "username"))) throw InvalidResponse();
    }

    public async Task<IReadOnlyList<ScrobblingCandidate>> SearchSubjectsAsync(string keyword, string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(keyword) || keyword.Trim().Length > 200) throw new ArgumentException("搜索标题长度无效", nameof(keyword));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        using var document = await SendAsync(HttpMethod.Post, "v0/search/subjects?limit=20", token,
            new { keyword = keyword.Trim(), filter = new { type = new[] { 2 } } }, null, false, deadline.Token, expectJson: true).ConfigureAwait(false)
            ?? throw InvalidResponse();
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) throw InvalidResponse();
        var results = new List<ScrobblingCandidate>();
        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var idValue) || !idValue.TryGetInt64(out var id) || id <= 0) continue;
            var title = Text(item, "name_cn");
            if (string.IsNullOrWhiteSpace(title)) title = Text(item, "name");
            if (string.IsNullOrWhiteSpace(title)) continue;
            var year = Year(item, "date");
            int? count = item.TryGetProperty("eps", out var eps) && eps.TryGetInt32(out var episodes) && episodes > 0 ? episodes : null;
            var detail = Text(item, "name");
            results.Add(new(id, title, year, count, detail.Length == 0 || detail == title ? null : detail));
        }
        return results;
    }

    public async Task<IReadOnlyList<BangumiEpisode>> GetEpisodesAsync(long subjectId, string token, CancellationToken cancellationToken)
    {
        ValidateId(subjectId);
        var episodes = new List<BangumiEpisode>();
        var ids = new HashSet<long>();
        var numbers = new HashSet<int>();
        for (var page = 0; page < 100; page++)
        {
            var offset = page * 200;
            using var document = await SendAsync(HttpMethod.Get,
                $"v0/episodes?subject_id={subjectId}&type=0&limit=200&offset={offset}", token, null, null, false, cancellationToken).ConfigureAwait(false)
                ?? throw InvalidResponse();
            var root = document.RootElement;
            var data = root.GetProperty("data");
            foreach (var episode in data.EnumerateArray())
            {
                if (episode.GetProperty("type").GetInt32() != 0) continue;
                var id = episode.GetProperty("id").GetInt64();
                var sort = episode.GetProperty("sort").GetDecimal();
                if (id <= 0 || sort <= 0 || sort != decimal.Truncate(sort) || sort > int.MaxValue) continue;
                var number = (int)sort;
                if (!ids.Add(id) || !numbers.Add(number)) throw InvalidResponse();
                episodes.Add(new(id, number));
            }
            if (data.GetArrayLength() < 200 || root.TryGetProperty("total", out var total)
                && total.TryGetInt32(out var count) && offset + data.GetArrayLength() >= count)
                return episodes.OrderBy(episode => episode.Number).ToArray();
        }
        throw InvalidResponse();
    }

    public async Task MarkEpisodeAsync(long subjectId, long episodeId, string token, Func<bool> stillAllowed, CancellationToken cancellationToken)
    {
        ValidateId(subjectId);
        ValidateId(episodeId);
        Allowed(stillAllowed, cancellationToken);
        using var me = await SendAsync(HttpMethod.Get, "v0/me", token, null, null, false, cancellationToken).ConfigureAwait(false)
            ?? throw InvalidResponse();
        var username = Text(me.RootElement, "username");
        if (string.IsNullOrWhiteSpace(username)) throw InvalidResponse();
        using var collection = await SendAsync(HttpMethod.Get, $"v0/users/{Uri.EscapeDataString(username)}/collections/{subjectId}",
            token, null, null, true, cancellationToken).ConfigureAwait(false);
        // Existing subject state, score, tags and comments must survive an episode update.
        if (collection is null)
        {
            using var created = await SendAsync(HttpMethod.Post, $"v0/users/-/collections/{subjectId}",
                token, new { type = 3 }, stillAllowed, false, cancellationToken).ConfigureAwait(false);
        }
        using var existing = await SendAsync(HttpMethod.Get, $"v0/users/-/collections/-/episodes/{episodeId}",
            token, null, null, false, cancellationToken).ConfigureAwait(false) ?? throw InvalidResponse();
        if (existing.RootElement.GetProperty("episode").GetProperty("id").GetInt64() != episodeId) throw InvalidResponse();
        if (existing.RootElement.GetProperty("type").GetInt32() == 2) return;
        using var updated = await SendAsync(HttpMethod.Patch, $"v0/users/-/collections/{subjectId}/episodes",
            token, new { episode_id = new[] { episodeId }, type = 2 }, stillAllowed, false, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonDocument?> SendAsync(HttpMethod method, string path, string token, object? payload,
        Func<bool>? stillAllowed, bool allowNotFound, CancellationToken cancellationToken, bool expectJson = false)
    {
        if (string.IsNullOrWhiteSpace(token) || token.IndexOfAny(['\r', '\n']) >= 0)
            throw new InvalidOperationException("请配置有效的 Bangumi Access Token");
        cancellationToken.ThrowIfCancellationRequested();
        if (stillAllowed is not null) Allowed(stillAllowed, cancellationToken);
        using var request = new HttpRequestMessage(method, new Uri(ApiRoot, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        request.Headers.UserAgent.ParseAdd("Momoka/1.0 (+https://github.com/cudamin/Momoka)");
        if (payload is not null) request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Bangumi 请求失败（HTTP {(int)response.StatusCode}）", null, response.StatusCode);
        if (method != HttpMethod.Get && !expectJson) return null;
        if (response.Content.Headers.ContentLength > MaxResponseBytes) throw InvalidResponse();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (buffer.Length + read > MaxResponseBytes) throw InvalidResponse();
            buffer.Write(chunk, 0, read);
        }
        try { return JsonDocument.Parse(buffer.ToArray()); }
        catch (JsonException) { throw InvalidResponse(); }
    }

    private static string Text(JsonElement value, string key) => value.TryGetProperty(key, out var property)
        && property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : "";
    private static int? Year(JsonElement value, string key)
    {
        var date = Text(value, key);
        return date.Length >= 4 && int.TryParse(date.AsSpan(0, 4), out var year) && year is >= 1900 and <= 2200 ? year : null;
    }
    private static InvalidOperationException InvalidResponse() => new("Bangumi 条目或章节信息无法识别");
    private static void ValidateId(long id) { if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id)); }
    private static void Allowed(Func<bool> allowed, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!allowed()) throw new OperationCanceledException("同步配置已改变", token);
    }
    public void Dispose() { if (_ownsHttp) _http.Dispose(); }
}
