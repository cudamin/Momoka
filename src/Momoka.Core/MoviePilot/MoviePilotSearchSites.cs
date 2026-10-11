using System.Globalization;
using System.Text.Json;

namespace Momoka.MoviePilot;

/// <summary>站点选择只保留搜索需要的编号和名称，不保留 Cookie、地址或下载凭据。</summary>
public sealed record MoviePilotSearchSite(int Id, string Name);

public sealed record MoviePilotSiteSelection(string ConnectionStamp, IReadOnlyList<int> Ids)
{
    public string QueryValue()
    {
        if (Ids.Count == 0 || Ids.Any(id => id <= 0))
            throw new MoviePilotException("请至少选择一个搜索站点，或使用 MoviePilot 默认站点");
        return string.Join(',', Ids.Distinct().Select(id => id.ToString(CultureInfo.InvariantCulture)));
    }
}

public sealed partial class MoviePilotService
{
    public async Task<IReadOnlyList<MoviePilotSearchSite>> SearchSitesAsync(string? mediaType, CancellationToken cancellationToken)
    {
        var path = mediaType switch
        {
            "电影" => "site/media/movie",
            "电视剧" => "site/media/tv",
            _ => "site/"
        };
        var identity = CaptureIdentity();
        var data = await CallAsync((address, token) => _client.GetAsync(address, token, path, cancellationToken),
            cancellationToken, identity).ConfigureAwait(false);
        return ParseSearchSites(data);
    }

    public static IReadOnlyList<MoviePilotSearchSite> ParseSearchSites(JsonElement data) => MoviePilotTransfer.Array(data)
        .Where(item => MoviePilotTransfer.Bool(item, "is_active"))
        .Select(item => new MoviePilotSearchSite(
            item.TryGetProperty("id", out var value) && int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0,
            MoviePilotTransfer.Text(item, "name").Trim()))
        .Where(site => site.Id > 0 && site.Name.Length > 0)
        .DistinctBy(site => site.Id).ToArray();

    private string SearchSitesQuery(MoviePilotSiteSelection? sites)
    {
        if (sites is null) return ""; // 省略参数表示使用服务器的 IndexerSites，未必是全部站点。
        RequireResultConnection(sites.ConnectionStamp);
        return "&sites=" + Uri.EscapeDataString(sites.QueryValue());
    }
}
