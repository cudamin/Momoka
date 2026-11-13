using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Momoka.Emby;
using Momoka.Music;

namespace Momoka.Shell;

/// <summary>Anonymous paging transport shared by the native probe and its offline success/failure tests.</summary>
internal sealed class MusicPagingProbeServer
{
    internal int Total { get; set; } = 81;
    internal bool FailNextMore { get; set; }
    internal List<(string Type, int Offset, int Limit)> Requests { get; } = [];

    internal Task<HttpResponseMessage> ReplyAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var uri = request.RequestUri ?? throw new InvalidOperationException("分页夹具缺少URI");
        if (!uri.Host.EndsWith(".invalid", StringComparison.Ordinal) || request.Method != HttpMethod.Get)
            throw new InvalidOperationException("音乐分页探针只允许匿名GET");
        if (uri.AbsolutePath.Contains("/Images/", StringComparison.Ordinal))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        if (!uri.AbsolutePath.EndsWith("/Artists", StringComparison.Ordinal) && !uri.AbsolutePath.EndsWith("/Items", StringComparison.Ordinal))
            throw new InvalidOperationException("音乐分页探针拒绝未知路由");
        var type = uri.AbsolutePath.EndsWith("/Artists", StringComparison.Ordinal) ? EmbyItemType.MusicArtist
            : Parameter(uri, "IncludeItemTypes") ?? EmbyItemType.Audio;
        if (type is not (EmbyItemType.Audio or EmbyItemType.MusicAlbum or EmbyItemType.MusicArtist or EmbyItemType.Playlist))
            throw new InvalidOperationException("音乐分页探针拒绝非音乐类型");
        var offset = Number(uri, "StartIndex", 0);
        var limit = Number(uri, "Limit", MusicCatalog.PageSize);
        Requests.Add((type, offset, limit));
        if (offset > 0 && FailNextMore)
        {
            FailNextMore = false;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            { Content = new StringContent("anonymous music second-page failure") });
        }
        var items = Enumerable.Range(offset, Math.Clamp(Total - offset, 0, limit)).Select(index => new EmbyItem
        {
            Id = ItemId(type, index),
            Name = "匿名音乐 " + (index + 1),
            Type = type,
            AlbumArtists = type == EmbyItemType.MusicAlbum ? [new EmbyNamedId { Id = "artist", Name = "匿名艺术家" }] : [],
            RunTimeTicks = type == EmbyItemType.Audio ? TimeSpan.FromSeconds(180).Ticks : null
        }).ToList();
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(new ItemsResult { Items = items, TotalRecordCount = Total }), Encoding.UTF8, "application/json") });
    }

    internal static string ItemId(string type, int index) => "music-paging-" + type + "-" + index;

    internal static void ValidateLoaded(int total, IReadOnlyList<EmbyItem> items, bool moreAvailable, string type)
    {
        if (items.Count != total || moreAvailable || items.Count <= MusicCatalog.PageSize
            || items[MusicCatalog.PageSize].Id != ItemId(type, MusicCatalog.PageSize)
            || items[^1].Id != ItemId(type, total - 1)
            || items.Where((item, index) => item.Type != type || item.Id != ItemId(type, index)).Any())
            throw new InvalidOperationException("音乐分页真实读取与末页回执不一致");
    }

    private static string? Parameter(Uri uri, string name) => uri.Query.TrimStart('?').Split('&')
        .Select(value => value.Split('=', 2)).Where(pair => pair.Length == 2 && pair[0] == name)
        .Select(pair => Uri.UnescapeDataString(pair[1])).FirstOrDefault();

    private static int Number(Uri uri, string name, int fallback)
    {
        var value = Parameter(uri, name);
        if (value is null) return fallback;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || number < 0 || name == "Limit" && number is < 1 or > 1000)
            throw new InvalidOperationException("音乐分页探针拒绝非法分页参数");
        return number;
    }
}
