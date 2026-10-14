using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Momoka.Diagnostics;

namespace Momoka.Updates;

/// <summary>
/// 去发布页问一句「有没有比手上这一版新的」。
/// <para>
/// 只问公开的发布接口，不带任何身份：没有令牌、没有设备 id、没有机器名，请求头里只有客户端名与版本号。这是本
/// 程序唯一一个既不由用户配置、也不由他自己的服务器给出的外部地址 —— 隐私政策第 4 节为此单列一条，改这里的地址
/// 就得同步改它。
/// </para>
/// <para>
/// <b>列清单，不用 <c>/releases/latest</c>。</b>那个接口跳过 pre-release，而本项目的 Release 全被标成了
/// pre-release，用它只会拿到 404（实测），也就是永远答「没有新版本」。清单接口一次能拿最近若干版，取其中最高的
/// 一版，判据在 <see cref="UpdateVersion.NewestNewer"/>。
/// </para>
/// <para>
/// 和 <see cref="Emby.EmbyHttp"/> 一样是自带 <see cref="HttpMessageHandler"/> 的客户端，不共用那个：那个客户端
/// 的每一行都在回答「哪台 Emby 服务器」的问题（逐跳认证、重定向校验、Emby 的请求头），而这里是一个匿名 GET。
/// </para>
/// </summary>
public sealed class UpdateClient : IDisposable
{
    /// <summary>发布页所在的仓库，改这一处就够了。</summary>
    public const string Repository = "cudamin/Momoka";

    private const string Category = "更新";

    /// <summary>一次取多少条：够覆盖「跳过草稿、取最高」这件事，又不必把整份历史拉回来。</summary>
    private const int PageSize = 20;

    public static Uri ReleasesEndpoint { get; } =
        new($"https://api.github.com/repos/{Repository}/releases?per_page={PageSize}");

    private readonly HttpClient _http;
    private readonly HttpMessageHandler _handler;

    public UpdateClient(HttpMessageHandler? handler = null)
    {
        handler ??= new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(8),
            AutomaticDecompression = DecompressionMethods.All
        };

        _handler = handler;

        // 15 秒：这一趟是启动路径上的一个附带动作，用户没有在等它；连不上由上面的 8 秒兜底。
        _http = new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(15) };

        // GitHub 的接口没有 User-Agent 就答 403，所以这个头不是可选的。构造时设一次、此后只读 —— 与
        // EmbyHttp 里「不要在每个请求之前改 DefaultRequestHeaders」那句不冲突，那一句说的是并发写同一份头。
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppIdentity.Title}/{AppIdentity.Version}");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    /// <summary>
    /// 比 <paramref name="current"/> 新的最高一版；没有就是 null（含「清单读不出来」以外的所有正常答案）。
    /// 连不上、答非所问、正文读不出来都抛 <see cref="UpdateException"/> —— 由调用方决定这一趟要不要惊动用户
    /// （启动时那一趟不惊动，手动那一趟要说一句）。
    /// </summary>
    public async Task<AppRelease?> FindNewerAsync(Version current, CancellationToken cancellationToken)
    {
        string json;
        try
        {
            using var response = await _http.GetAsync(ReleasesEndpoint, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new UpdateException($"发布页返回 HTTP {(int)response.StatusCode}", response.StatusCode);

            json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (UpdateException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 调用方自己取消的（关窗口、离开设置页）不算失败，照原样抛，别包成「超时」。
            throw new UpdateException("连接发布页超时");
        }
        catch (HttpRequestException error)
        {
            throw new UpdateException("无法连接到发布页，请检查网络", inner: error);
        }

        if (Parse(json) is not { } releases) throw new UpdateException("发布页的响应读不出来");

        var release = UpdateVersion.NewestNewer(releases, current);
        Log.Info(Category, release is null
            ? $"检查更新：{current.ToString(3)} 已是最新"
            : $"检查更新：发现新版本 {release.VersionText}（当前 {current.ToString(3)}）");

        return release;
    }

    /// <summary>
    /// 把接口回来的 JSON 读成一份发布清单；<b>正文不是一个发布数组时返回 null</b>（而不是空清单）——
    /// 「读不出来」和「一版都没有」是两件事，混成一个的话，发布页抽风时手动检查会答「已是最新版本」，那是句谎话。
    /// 单条读不懂的（tag 不是版本号、没有页面地址、是草稿）跳过而不是整份作废：一份清单里有一条坏的不该让整趟白跑。
    /// </summary>
    internal static IReadOnlyList<AppRelease>? Parse(string json)
    {
        ReleasePayload[]? payloads;
        try
        {
            payloads = JsonSerializer.Deserialize<ReleasePayload[]>(json, Json);
        }
        catch (JsonException error)
        {
            Log.Warn(Category, $"发布页的响应读不出来：{error.Message}");
            return null;
        }

        if (payloads is null) return null;

        var releases = new List<AppRelease>(payloads.Length);
        foreach (var payload in payloads)
        {
            if (payload.Draft || payload.TagName is null || payload.HtmlUrl is null) continue;
            if (!UpdateVersion.TryParseTag(payload.TagName, out var version)) continue;
            if (!Uri.TryCreate(payload.HtmlUrl, UriKind.Absolute, out var page)) continue;
            if (page.Scheme != Uri.UriSchemeHttp && page.Scheme != Uri.UriSchemeHttps) continue;

            releases.Add(new AppRelease(version, payload.TagName, payload.HtmlUrl));
        }

        return releases;
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>接口回来的那几栏，只读得着的就这几栏 —— 不建一个几十属性的模型。</summary>
    private sealed record ReleasePayload(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("draft")] bool Draft);

    public void Dispose()
    {
        // 客户端不持有 handler（disposeHandler: false），所以它在这里放，而且只放一次。
        _http.Dispose();
        _handler.Dispose();
    }
}
