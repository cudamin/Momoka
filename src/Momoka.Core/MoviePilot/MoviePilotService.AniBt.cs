using System.Globalization;
using System.Text.Json;
using Momoka.AniBt;

namespace Momoka.MoviePilot;

internal sealed record MoviePilotAniBtConnection(string Stamp, long Generation);

internal sealed record MoviePilotAniBtScrapeTarget(string Path, string MediaId, string Type, JsonElement File);

public sealed partial class MoviePilotService
{
    internal MoviePilotAniBtConnection CaptureAniBtConnection()
    {
        var identity = CaptureIdentity();
        CheckIdentity(identity, CancellationToken.None);
        if (!Available) throw new MoviePilotOperationBlockedException("MoviePilot 尚未连接，请先在设置中测试连接");
        return new(TransferConnectionStamp(identity), identity.Generation);
    }

    internal void CheckAniBtConnection(MoviePilotAniBtConnection connection, CancellationToken cancellationToken)
    {
        var identity = RequireResultConnection(connection.Stamp);
        CheckIdentity(identity, cancellationToken);
        if (identity.Generation != connection.Generation)
            throw new MoviePilotOperationBlockedException("MoviePilot 连接已改变，请重新搜索或识别");
        if (!Available) throw new MoviePilotOperationBlockedException("MoviePilot 已断开连接，请重新连接后操作");
    }

    internal async Task<IReadOnlyList<MoviePilotResource>> AniBtResourcesAsync(AniBtAnime anime,
        IReadOnlyList<AniBtRelease> releases, MoviePilotAniBtConnection connection, CancellationToken cancellationToken)
    {
        CheckAniBtConnection(connection, cancellationToken);
        var sources = await MediaSourcesAsync(cancellationToken).ConfigureAwait(false);
        CheckAniBtConnection(connection, cancellationToken);
        JsonElement media = default;
        var type = anime.Format.Equals("movie", StringComparison.OrdinalIgnoreCase)
            ? MoviePilotTransferRequest.TypeMovie : MoviePilotTransferRequest.TypeSeries;
        if (anime.BgmId > 0 && sources.Any(source => source.Id == "bangumi" && source.IsVideo))
        {
            media = await AniBtMediaAsync(anime.BgmId, type, connection, cancellationToken, requireType: false).ConfigureAwait(false);
            type = MoviePilotTransfer.Text(media, "type");
        }
        var result = new List<MoviePilotResource>(releases.Count);
        foreach (var release in releases)
        {
            if (!release.DownloadUri.IsAbsoluteUri || release.DownloadUri.Scheme is not ("http" or "https") ||
                release.DownloadUri.UserInfo.Length > 0 || !release.PageUri.IsAbsoluteUri ||
                release.PageUri.Scheme is not ("http" or "https") || release.PageUri.UserInfo.Length > 0)
                throw new MoviePilotException("AniBT 返回了无效的种子地址");
            var torrent = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["title"] = release.Title,
                ["description"] = release.GroupName,
                ["site_name"] = "AniBT",
                ["enclosure"] = release.DownloadUri.AbsoluteUri,
                ["page_url"] = release.PageUri.AbsoluteUri,
                ["size"] = release.Size ?? 0,
                ["pubdate"] = release.PublishedAt?.ToString("O", CultureInfo.InvariantCulture),
                ["category"] = type
            });
            result.Add(new MoviePilotResource
            {
                Title = release.Title,
                SiteName = "AniBT",
                Description = release.GroupName,
                Size = release.Size ?? 0,
                Resolution = release.Resolution,
                PageUrl = release.PageUri.AbsoluteUri,
                PublishedAt = release.PublishedAt,
                TorrentInfo = torrent,
                MediaSource = media.ValueKind == JsonValueKind.Object ? "bangumi" : null,
                MediaId = media.ValueKind == JsonValueKind.Object ? anime.BgmId.ToString(CultureInfo.InvariantCulture) : null,
                MediaType = media.ValueKind == JsonValueKind.Object ? type : null,
                MediaInfo = media,
                ConnectionStamp = connection.Stamp,
                ConnectionGeneration = connection.Generation
            });
        }
        CheckAniBtConnection(connection, cancellationToken);
        return result;
    }

    private async Task<JsonElement> AniBtMediaAsync(long bangumiId, string expectedType,
        MoviePilotAniBtConnection connection, CancellationToken cancellationToken, bool requireType = true)
    {
        CheckAniBtConnection(connection, cancellationToken);
        if (bangumiId <= 0) throw new MoviePilotException("AniBT 没有提供有效的 Bangumi 编号");
        var identity = RequireResultConnection(connection.Stamp);
        var id = bangumiId.ToString(CultureInfo.InvariantCulture);
        var path = $"media/{id}?media_source=bangumi&type_name={Uri.EscapeDataString(expectedType)}";
        var media = await CallAsync((address, token) => _client.GetAsync(address, token, path, cancellationToken),
            cancellationToken, identity).ConfigureAwait(false);
        CheckAniBtConnection(connection, cancellationToken);
        if (media.ValueKind != JsonValueKind.Object ||
            MoviePilotTransfer.Text(media, "media_source") != "bangumi" ||
            MoviePilotTransferHistory.Scalar(media, "media_id") != id ||
            MoviePilotTransfer.Text(media, "type") is not (MoviePilotTransferRequest.TypeMovie or MoviePilotTransferRequest.TypeSeries) ||
            requireType && MoviePilotTransfer.Text(media, "type") != expectedType ||
            string.IsNullOrWhiteSpace(MoviePilotTransfer.Text(media, "title")))
            throw new MoviePilotException("MoviePilot 返回的媒体身份或类型与 AniBT 结果不一致，已停止操作");
        return media.Clone();
    }

    internal async Task<MoviePilotAniBtScrapeTarget> PrepareAniBtScrapeAsync(string targetPath, AniBtAnime anime,
        string expectedType, MoviePilotAniBtConnection connection, CancellationToken cancellationToken)
    {
        CheckAniBtConnection(connection, cancellationToken);
        var sources = await MediaSourcesAsync(cancellationToken).ConfigureAwait(false);
        CheckAniBtConnection(connection, cancellationToken);
        if (!sources.Any(source => source.Id == "bangumi" && source.IsVideo))
            throw new MoviePilotOperationBlockedException("此 MoviePilot 没有启用 Bangumi 媒体来源，暂时不能按 AniBT 结果刮削");
        await AniBtMediaAsync(anime.BgmId, expectedType, connection, cancellationToken).ConfigureAwait(false);
        var file = await TransferFileAsync(targetPath, cancellationToken).ConfigureAwait(false);
        CheckAniBtConnection(connection, cancellationToken);
        MoviePilotTransfer.RequireFile(file, targetPath);
        if (expectedType == MoviePilotTransferRequest.TypeSeries)
        {
            var identity = RequireResultConnection(connection.Stamp);
            var recognition = await CallAsync((address, token) => _client.GetAsync(address, token,
                $"media/recognize_file?path={Uri.EscapeDataString(targetPath)}&media_source=bangumi", cancellationToken),
                cancellationToken, identity).ConfigureAwait(false);
            CheckAniBtConnection(connection, cancellationToken);
            var meta = MoviePilotTransferHistory.Member(recognition, "meta_info");
            if (!int.TryParse(MoviePilotTransferHistory.Scalar(meta, "begin_episode"), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var episode) || episode <= 0)
                throw new MoviePilotOperationBlockedException($"MoviePilot 无法从当前文件名解析集号：{targetPath}。刮削接口不能改写季集号，请先在 MoviePilot 核对命名");
        }
        return new(targetPath, anime.BgmId.ToString(CultureInfo.InvariantCulture), expectedType, file.Clone());
    }

    internal async Task<AniBtScrapeResult> SubmitAniBtScrapeAsync(MoviePilotAniBtScrapeTarget target,
        MoviePilotAniBtConnection connection, CancellationToken cancellationToken)
    {
        CheckAniBtConnection(connection, cancellationToken);
        var currentFile = await TransferFileAsync(target.Path, cancellationToken).ConfigureAwait(false);
        CheckAniBtConnection(connection, cancellationToken);
        if (!JsonElement.DeepEquals(currentFile, target.File))
            throw new MoviePilotOperationBlockedException("待刮削文件在核对后发生变化，本次没有提交");
        var identity = RequireResultConnection(connection.Stamp);
        // 防重按账号和实际目标路径，不按 AniBT 选择或这次计划：改选候选不能绕过未知结果锁。
        var pathKey = target.Path.Replace('\\', '/').TrimEnd('/');
        if (pathKey.Length > 1 && pathKey[1] == ':' || pathKey.StartsWith("//", StringComparison.Ordinal))
            pathKey = pathKey.ToUpperInvariant();
        var path = $"media/scrape/local?media_source=bangumi&media_id={Uri.EscapeDataString(target.MediaId)}" +
            $"&type_name={Uri.EscapeDataString(target.Type)}";
        var reply = await WriteAsync(identity, [OperationKey(connection.Stamp, "anibt-scrape", pathKey)],
            (address, token) => _client.PostReplyAsync(address, token, path, currentFile, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        if (!reply.Success)
            throw new MoviePilotException(reply.Message.Length > 0 ? reply.Message : "MoviePilot 拒绝了刮削请求");
        // v3.1.3 的 success 不包含逐个 NFO/图片写入状态，不能把 HTTP 成功包装成文件已验证。
        return new(target.Path, true, "MoviePilot 已处理刮削请求，NFO 和图片结果请在 MoviePilot 核对");
    }
}
