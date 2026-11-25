using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Momoka.Metadata;

/// <summary>独立的TMDB v3只读客户端，采用用户提供的API Read Access Token。</summary>
public sealed class TmdbClient : IDisposable
{
    private const string ApiBase = "https://api.themoviedb.org/3/";
    private const string ImageBase = "https://image.tmdb.org/t/p/original";
    private const int MaxJsonBytes = 2 * 1024 * 1024;
    private const int MaxImageBytes = 20 * 1024 * 1024;
    private static readonly Regex ImagePath = new(@"^/[A-Za-z0-9_-]+\.(?:jpg|jpeg|png|webp)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private readonly HttpClient _http;
    private readonly string _token;

    public TmdbClient(string readAccessToken, HttpMessageHandler? handler = null, IWebProxy? proxy = null)
    {
        if (string.IsNullOrWhiteSpace(readAccessToken) || readAccessToken.Any(character => character <= ' ' || character >= 127))
            throw new TmdbException("请提供有效的TMDB API Read Access Token。");
        _token = readAccessToken;
        // proxy 为 null 时保持 HttpClientHandler 的默认行为：跟随系统代理 —— 与这一行出现之前一样。
        handler ??= new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, Proxy = proxy };
        // 实际HTTP传输也不追随重定向；测试注入的匿名handler只替代网络边界。
        if (handler is HttpClientHandler network) { network.AllowAutoRedirect = false; network.UseCookies = false; }
        if (handler is SocketsHttpHandler sockets) { sockets.AllowAutoRedirect = false; sockets.UseCookies = false; }
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(20) };
    }

    public async Task<IReadOnlyList<TmdbMediaCandidate>> SearchAsync(string query, bool series, int? year, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 500)
            throw new ArgumentException("请输入标题（不超过500字）。", nameof(query));
        if (year.HasValue && year.Value is < 1000 or > 9999)
            throw new ArgumentException("年份须是四位数字。", nameof(year));
        var route = $"search/{(series ? "tv" : "movie")}?query={Uri.EscapeDataString(query.Trim())}&language=zh-CN&include_adult=false&page=1";
        if (year.HasValue) route += $"&{(series ? "first_air_date_year" : "year")}={year.Value.ToString(CultureInfo.InvariantCulture)}";
        using var json = await ApiAsync(route, ct).ConfigureAwait(false);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            throw InvalidResponse();
        var complete = !root.TryGetProperty("total_pages", out var pages) || (pages.TryGetInt32(out var total) && total <= 1);
        var found = new List<TmdbMediaCandidate>();
        foreach (var result in results.EnumerateArray())
        {
            ValidateMedia(result, series);
            var date = Date(result, series ? "first_air_date" : "release_date");
            found.Add(new()
            {
                Id = Id(result),
                Title = Title(result, series),
                OriginalTitle = Text(result, series ? "original_name" : "original_title"),
                Year = Year(date),
                IsSeries = series,
                AutoMatchEligible = complete,
                Overview = Text(result, "overview"),
                PosterUrl = ImageUrl(result, "poster_path"),
                BackdropUrl = ImageUrl(result, "backdrop_path")
            });
        }
        if (found.GroupBy(item => item.Id, StringComparer.Ordinal).Any(group => group.Distinct().Count() > 1)) throw InvalidResponse();
        return found.DistinctBy(item => item.Id, StringComparer.Ordinal).ToArray();
    }

    public async Task<TmdbLocalMetadata> DetailsAsync(string id, bool series, int? season, int? episode, CancellationToken ct)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number <= 0)
            throw new ArgumentException("TMDB ID须是正整数。", nameof(id));
        if ((!series && (season.HasValue || episode.HasValue)) || season < 0 || episode <= 0 || (episode.HasValue && !season.HasValue))
            throw new ArgumentException("请核对媒体类型、季号和集号。特别篇可使用季号0。", nameof(season));
        var canonicalId = number.ToString(CultureInfo.InvariantCulture);
        using var json = await ApiAsync($"{(series ? "tv" : "movie")}/{canonicalId}?language=zh-CN&append_to_response=credits,aggregate_credits", ct).ConfigureAwait(false);
        var root = json.RootElement;
        ValidateMedia(root, series);
        if (Id(root) != canonicalId) throw InvalidResponse();
        var premiered = Date(root, series ? "first_air_date" : "release_date");
        var details = new TmdbLocalMetadata
        {
            Id = canonicalId,
            Title = Title(root, series),
            OriginalTitle = Text(root, series ? "original_name" : "original_title"),
            IsSeries = series,
            Year = Year(premiered),
            Season = season,
            Episode = episode,
            Premiered = premiered,
            Overview = Text(root, "overview"),
            PosterUrl = ImageUrl(root, "poster_path"),
            BackdropUrl = ImageUrl(root, "backdrop_path"),
            Tagline = OptionalText(root, "tagline"),
            Rating = OptionalNumber(root, "vote_average"),
            Runtime = RuntimeOf(root, series),
            Genres = Names(root, "genres", 12),
            Studios = Names(root, series ? "networks" : "production_companies", 2),
            People = PeopleOf(root, series)
        };
        if (!episode.HasValue) return details;

        var episodeRoute = $"tv/{canonicalId}/season/{season!.Value.ToString(CultureInfo.InvariantCulture)}/episode/{episode.Value.ToString(CultureInfo.InvariantCulture)}?language=zh-CN";
        using var episodeJson = await ApiAsync(episodeRoute, ct).ConfigureAwait(false);
        var one = episodeJson.RootElement;
        if (one.ValueKind != JsonValueKind.Object || Integer(one, "season_number") != season || Integer(one, "episode_number") != episode
            || (one.TryGetProperty("show_id", out _) && Id(one, "show_id") != canonicalId)) throw InvalidResponse();
        var episodeTitle = Text(one, "name");
        if (episodeTitle.Length == 0) throw InvalidResponse();
        return details with
        {
            EpisodeId = Id(one),
            EpisodeTitle = episodeTitle,
            EpisodeOverview = Text(one, "overview"),
            EpisodeImageUrl = ImageUrl(one, "still_path"),
            EpisodeAired = Date(one, "air_date"),
            Runtime = RuntimeOf(one, series: false) is > 0 and var minutes ? minutes : details.Runtime
        };
    }

    public async Task<byte[]> FetchImageAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "image.tmdb.org", StringComparison.OrdinalIgnoreCase) || uri.Port != 443
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || !uri.AbsolutePath.StartsWith("/t/p/", StringComparison.Ordinal))
            throw new ArgumentException("图片必须来自TMDB的HTTPS图片地址。", nameof(url));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        // 不索取 image/webp：TMDB 的图片 CDN 会按 Accept 协商格式，即使在 .jpg 地址上也会回 WebP。
        // 元数据目录里的图片按计划后缀落盘（poster.jpg 等），实际字节必须是同一种格式才不会被拒写。
        request.Headers.Accept.ParseAdd("image/jpeg,image/png");
        using var timeout = RequestTimeout(ct);
        try
        {
            using var response = await SendAsync(request, timeout.Token).ConfigureAwait(false);
            var type = response.Content.Headers.ContentType?.MediaType;
            if (type is not ("image/jpeg" or "image/png" or "image/webp")) throw new TmdbException("TMDB图片格式无效。");
            var bytes = await ReadBoundedAsync(response.Content, MaxImageBytes, timeout.Token).ConfigureAwait(false);
            if (!HasImageSignature(bytes, type)) throw new TmdbException("TMDB图片内容无效。");
            return bytes;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TmdbException("读取TMDB图片超时，请稍后重试。");
        }
    }

    private async Task<JsonDocument> ApiAsync(string route, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiBase + route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Headers.Accept.ParseAdd("application/json");
        using var timeout = RequestTimeout(ct);
        try
        {
            using var response = await SendAsync(request, timeout.Token).ConfigureAwait(false);
            var bytes = await ReadBoundedAsync(response.Content, MaxJsonBytes, timeout.Token).ConfigureAwait(false);
            try { return JsonDocument.Parse(bytes); }
            catch (JsonException) { throw InvalidResponse(); }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TmdbException("读取TMDB响应超时，请稍后重试。");
        }
    }

    private CancellationTokenSource RequestTimeout(CancellationToken ct)
    {
        // ResponseHeadersRead使HttpClient.Timeout只覆盖响应头；同一个预算必须继续约束响应体。
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_http.Timeout);
        return timeout;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        HttpResponseMessage response;
        try { response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TmdbException("连接TMDB超时，请稍后重试。"); }
        catch (HttpRequestException) { throw new TmdbException("无法连接TMDB，请检查网络后重试。"); }
        if (response.IsSuccessStatusCode) return response;
        var status = response.StatusCode;
        response.Dispose();
        throw new TmdbException(status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "TMDB令牌无效或没有读取权限。",
            HttpStatusCode.NotFound => "TMDB未找到指定媒体或季集。",
            HttpStatusCode.TooManyRequests => "TMDB请求过于频繁，请稍后重试。",
            >= HttpStatusCode.MultipleChoices and < HttpStatusCode.BadRequest => "TMDB返回了重定向，已停止请求。",
            _ => $"TMDB请求失败（HTTP {(int)status}）。"
        }, status);
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int limit, CancellationToken ct)
    {
        var promised = content.Headers.ContentLength;
        if (promised is 0 || promised > limit) throw new TmdbException("TMDB响应为空或超过大小限制。");
        try
        {
            await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int count;
            while ((count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                if (output.Length + count > limit) throw new TmdbException("TMDB响应超过大小限制。");
                output.Write(buffer, 0, count);
            }
            if (output.Length == 0 || (promised.HasValue && output.Length != promised.Value))
                throw new TmdbException("TMDB响应不完整，请重试。");
            return output.ToArray();
        }
        catch (IOException) { throw new TmdbException("读取TMDB响应失败，请重试。"); }
        catch (HttpRequestException) { throw new TmdbException("读取TMDB响应失败，请重试。"); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TmdbException("读取TMDB响应超时，请重试。"); }
    }

    private static bool HasImageSignature(byte[] bytes, string type) => type switch
    {
        "image/jpeg" => bytes.Length >= 4 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff,
        "image/png" => bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
        "image/webp" => bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8),
        _ => false
    };

    private static void ValidateMedia(JsonElement value, bool series)
    {
        if (value.ValueKind != JsonValueKind.Object || Title(value, series).Length == 0
            || (value.TryGetProperty("media_type", out _) && Text(value, "media_type") != (series ? "tv" : "movie"))) throw InvalidResponse();
        _ = Id(value);
    }

    private static string Title(JsonElement value, bool series)
    {
        var title = Text(value, series ? "name" : "title");
        return title.Length == 0 ? Text(value, series ? "original_name" : "original_title") : title;
    }

    private static string Id(JsonElement value, string field = "id")
    {
        var id = Integer(value, field);
        if (id <= 0) throw InvalidResponse();
        return id.ToString(CultureInfo.InvariantCulture);
    }

    private static int Integer(JsonElement value, string field) =>
        value.TryGetProperty(field, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number)
            ? number : throw InvalidResponse();

    private static string Text(JsonElement value, string field)
    {
        if (!value.TryGetProperty(field, out var property) || property.ValueKind == JsonValueKind.Null) return "";
        if (property.ValueKind != JsonValueKind.String) throw InvalidResponse();
        return property.GetString()?.Trim() ?? "";
    }

    private static string Date(JsonElement value, string field)
    {
        var date = Text(value, field);
        if (date.Length == 0) return "";
        return DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ? date : throw InvalidResponse();
    }

    private static int Year(string date) => date.Length >= 4 ? int.Parse(date[..4], CultureInfo.InvariantCulture) : 0;

    private static string ImageUrl(JsonElement value, string field)
    {
        var path = Text(value, field);
        if (path.Length == 0) return "";
        if (!ImagePath.IsMatch(path)) throw InvalidResponse();
        return ImageBase + path;
    }

    /// <summary>详情页可显示的补充资料：没有这一项就是空，不影响媒体身份校验。</summary>
    private static string OptionalText(JsonElement value, string field) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(field, out var property)
        && property.ValueKind == JsonValueKind.String ? property.GetString()?.Trim() ?? "" : "";

    private static double OptionalNumber(JsonElement value, string field) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(field, out var property)
        && property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var number) && number > 0 ? number : 0;

    private static IReadOnlyList<string> Names(JsonElement value, string field, int maximum)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(field, out var array)
            || array.ValueKind != JsonValueKind.Array) return [];
        return array.EnumerateArray().Take(maximum)
            .Select(item => item.ValueKind == JsonValueKind.Object ? OptionalText(item, "name") : "")
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static int RuntimeOf(JsonElement value, bool series)
    {
        if (!series) return (int)Math.Round(OptionalNumber(value, "runtime"));
        var runtime = FirstArrayNumber(value, "episode_run_time");
        if (runtime == 0 && value.TryGetProperty("last_episode_to_air", out var last) && last.ValueKind == JsonValueKind.Object)
            runtime = (int)Math.Round(OptionalNumber(last, "runtime"));
        return runtime;
    }

    private static int FirstArrayNumber(JsonElement value, string field)
    {
        if (!value.TryGetProperty(field, out var array) || array.ValueKind != JsonValueKind.Array) return 0;
        foreach (var item in array.EnumerateArray())
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var number) && number > 0) return number;
        return 0;
    }

    /// <summary>电影取 credits 里的演员与导演；电视剧优先取 aggregate_credits，并把 created_by 当导演。</summary>
    private static IReadOnlyList<TmdbLocalPerson> PeopleOf(JsonElement root, bool series)
    {
        var people = new List<TmdbLocalPerson>();
        if (series && root.TryGetProperty("created_by", out var created) && created.ValueKind == JsonValueKind.Array)
            foreach (var item in created.EnumerateArray().Take(2))
                Add(item, "", director: true);

        var credits = Member(root, "credits");
        var cast = series ? Member(Member(root, "aggregate_credits"), "cast") : Member(credits, "cast");
        if (cast.ValueKind == JsonValueKind.Array)
            foreach (var item in cast.EnumerateArray().Take(10))
            {
                var role = series
                    ? (Member(item, "roles") is { ValueKind: JsonValueKind.Array } roles
                        ? roles.EnumerateArray().Take(1).Select(value => OptionalText(value, "character")).FirstOrDefault() ?? ""
                        : "")
                    : OptionalText(item, "character");
                Add(item, role, director: false);
            }

        if (!series && Member(credits, "crew") is { ValueKind: JsonValueKind.Array } crew)
            foreach (var item in crew.EnumerateArray().Take(30).Where(item => OptionalText(item, "job") == "Director"))
                Add(item, "", director: true);

        return people.DistinctBy(person => (person.Name, person.Director)).Take(12).ToArray();

        void Add(JsonElement item, string role, bool director)
        {
            if (item.ValueKind != JsonValueKind.Object) return;
            var name = OptionalText(item, "name");
            if (name.Length == 0) return;
            var id = item.TryGetProperty("id", out var number) && number.ValueKind == JsonValueKind.Number
                && number.TryGetInt32(out var value) && value > 0 ? value.ToString(CultureInfo.InvariantCulture) : "";
            people.Add(new(name, role, director) { Id = id, ImageUrl = ImageUrl(item, "profile_path") });
        }
    }

    private static JsonElement Member(JsonElement value, string field) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(field, out var property) ? property : default;

    private static TmdbException InvalidResponse() => new("TMDB响应内容或媒体身份不符合预期。");
    public void Dispose() => _http.Dispose();
}
