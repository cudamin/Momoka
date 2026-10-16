using System.Net;
using System.Net.Http.Headers;
using System.Xml;
using System.Xml.Linq;
using Momoka.Diagnostics;

namespace Momoka.Updates;

/// <summary>
/// 去发布页问一句「有没有比手上这一版新的」。
/// <para>
/// 只问公开的发布源，不带任何身份：没有令牌、没有设备 id、没有机器名，请求头里只有客户端名与版本号。这是本
/// 程序唯一一个既不由用户配置、也不由他自己的服务器给出的外部地址 —— 隐私政策第 4 节为此单列一条，改这里的地址
/// 就得同步改它。
/// </para>
/// <para>
/// <b>读 Atom 源，不读 REST 接口。</b>2026-10-14 到 10-16 这三天，每一次启动都答「发布页返回 HTTP 403」——
/// 一次都没成过。实测的结论是：<c>api.github.com</c> 对匿名请求只给每个 IP 每小时 60 次，而本机走系统代理
/// （<c>127.0.0.1:8686</c>）出去，那个出口 IP 是共享的、额度被别人用光了；同一时刻绕开代理直连就是 200、还剩
/// 55 次，正文写着 <c>API rate limit exceeded for …</c>。把地址换成 <c>github.com/…/releases.atom</c> 之后，
/// 同一个代理下答 200 —— 这个源由网页那头给，不受接口的匿名配额管。对挂在共享代理后面的用户，这一条同样成立，
/// 不是本机一次性的绕路。
/// </para>
/// <para>
/// <b>列清单，不用 <c>/releases/latest</c>。</b>那个接口跳过 pre-release，而本项目的 Release 全被标成了
/// pre-release，用它只会拿到 404（实测），也就是永远答「没有新版本」。Atom 源按发布时间倒序列出最近十条，含
/// pre-release、不含草稿，取其中最高的一版，判据在 <see cref="UpdateVersion.NewestNewer"/>。
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

    /// <summary>Atom 正文里那个命名空间；不带它一个 <c>entry</c> 都找不着。</summary>
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    /// <summary>
    /// 发布页的 Atom 源。<c>releases.atom</c> 由 github.com 网页那头给，是最近十版、按发布时间倒序、含
    /// pre-release；十条够覆盖「取最高的一版」这件事，又不必把整份历史拉回来。
    /// </summary>
    public static Uri ReleasesEndpoint { get; } = new($"https://github.com/{Repository}/releases.atom");

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
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/atom+xml"));
    }

    /// <summary>
    /// 比 <paramref name="current"/> 新的最高一版；没有就是 null（含「清单读不出来」以外的所有正常答案）。
    /// 连不上、答非所问、正文读不出来都抛 <see cref="UpdateException"/> —— 由调用方决定这一趟要不要惊动用户
    /// （启动时那一趟不惊动，手动那一趟要说一句）。
    /// </summary>
    public async Task<AppRelease?> FindNewerAsync(Version current, CancellationToken cancellationToken)
    {
        string xml;
        try
        {
            using var response = await _http.GetAsync(ReleasesEndpoint, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new UpdateException($"发布页返回 HTTP {(int)response.StatusCode}", response.StatusCode);

            xml = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
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

        if (Parse(xml) is not { } releases) throw new UpdateException("发布页的响应读不出来");

        var release = UpdateVersion.NewestNewer(releases, current);
        Log.Info(Category, release is null
            ? $"检查更新：{current.ToString(3)} 已是最新"
            : $"检查更新：发现新版本 {release.VersionText}（当前 {current.ToString(3)}）");

        return release;
    }

    /// <summary>
    /// 把 Atom 源读成一份发布清单；<b>正文不是一个 feed 时返回 null</b>（而不是空清单）——「读不出来」和
    /// 「一版都没有」是两件事，混成一个的话，发布页抽风时手动检查会答「已是最新版本」，那是句谎话。仓库改名后
    /// 那一路正是这样：答 404 加一段 <c>Not Found</c> 纯文本，连 XML 都不是。
    /// <para>
    /// 单条读不懂的（没有页面地址、tag 不是版本号）跳过而不是整份作废：一份清单里有一条坏的不该让整趟白跑。
    /// </para>
    /// <para>
    /// <b>tag 从页面地址里取，不取 <c>&lt;title&gt;</c>。</b>那一条是人写的发布标题，实测同一份源里
    /// <c>v0.1.5</c> 的标题是 <c>v0.1.5</c>、<c>v0.1.4</c> 的标题却是 <c>Momoka 0.1.4</c>；按标题认版本号会漏掉
    /// 后一版，而屏上一个字都不说。页面地址（<c>…/releases/tag/v0.1.4</c>）和 <c>&lt;id&gt;</c> 都由 GitHub 按
    /// tag 生成，取它同时也就拿到了「打开下载页」要用的地址。
    /// </para>
    /// </summary>
    internal static IReadOnlyList<AppRelease>? Parse(string xml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (XmlException error)
        {
            Log.Warn(Category, $"发布页的响应读不出来：{error.Message}");
            return null;
        }

        // 根元素不是 feed 就当作读不出来：GitHub 出错时回的是 HTML 或纯文本，空清单会被上面那句「已是最新版本」说成谎话。
        if (document.Root is not { } feed || feed.Name != Atom + "feed") return null;

        var releases = new List<AppRelease>();
        foreach (var entry in feed.Elements(Atom + "entry"))
        {
            if (PageOf(entry) is not { } page) continue;
            if (page.Scheme != Uri.UriSchemeHttp && page.Scheme != Uri.UriSchemeHttps) continue;
            if (TagOf(page) is not { } tag) continue;
            if (!UpdateVersion.TryParseTag(tag, out var version)) continue;

            releases.Add(new AppRelease(version, tag, page.AbsoluteUri));
        }

        return releases;
    }

    /// <summary>这一条发布页的地址，取 <c>rel="alternate"</c> 那个 <c>link</c>；没有就是读不懂，跳过。</summary>
    private static Uri? PageOf(XElement entry)
    {
        foreach (var link in entry.Elements(Atom + "link"))
        {
            if (!string.Equals((string?)link.Attribute("rel"), "alternate", StringComparison.Ordinal)) continue;
            if (Uri.TryCreate((string?)link.Attribute("href"), UriKind.Absolute, out var page)) return page;
        }

        return null;
    }

    /// <summary>从 <c>…/releases/tag/&lt;tag&gt;</c> 里把 tag 抠出来；地址长得不像就是 null。</summary>
    private static string? TagOf(Uri page)
    {
        const string marker = "/releases/tag/";

        var url = page.AbsoluteUri;
        var index = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return null;

        var tag = Uri.UnescapeDataString(url[(index + marker.Length)..]).Trim('/');
        return tag.Length == 0 ? null : tag;
    }

    public void Dispose()
    {
        // 客户端不持有 handler（disposeHandler: false），所以它在这里放，而且只放一次。
        _http.Dispose();
        _handler.Dispose();
    }
}
