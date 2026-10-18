using System.Globalization;
using System.Text.Json;
using Momoka.AniBt;

namespace Momoka.MoviePilot;

internal sealed record MoviePilotAniBtConnection(string Stamp, long Generation);

internal sealed record MoviePilotAniBtTransfer(MoviePilotTransferRequest Request, JsonElement File);

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

    /// <summary>
    /// 按原整理记录和 AniBT 选中的 Bangumi 身份准备一次真正的 MoviePilot 手动整理（重新整理）。
    /// <para>
    /// 源文件必须走 <see cref="MoviePilotTransferHistory.TransferPath"/>：成功 move 的记录指向 Emby 现在看到的
    /// 目标，复制/软链接记录则指向仍存在的原下载源；直接拿 Emby 路径调用 <c>storage/list</c> 会在后两种记录上
    /// 问错文件。目的目录、整理方式和分类开关整组取自该记录的 <c>transfer/manual/target-path</c>，不按默认值另拼。
    /// </para>
    /// </summary>
    internal async Task<MoviePilotAniBtTransfer> PrepareAniBtTransferAsync(string targetPath, AniBtAnime anime,
        string expectedType, MoviePilotTransferHistory history, int? season, string episode,
        MoviePilotAniBtConnection connection, CancellationToken cancellationToken)
    {
        CheckAniBtConnection(connection, cancellationToken);
        var sources = await MediaSourcesAsync(cancellationToken).ConfigureAwait(false);
        CheckAniBtConnection(connection, cancellationToken);
        if (!sources.Any(source => source.Id == "bangumi" && source.IsVideo))
            throw new MoviePilotOperationBlockedException("此 MoviePilot 没有启用 Bangumi 媒体来源，暂时不能按 AniBT 结果重新整理");
        await AniBtMediaAsync(anime.BgmId, expectedType, connection, cancellationToken).ConfigureAwait(false);
        var transferPath = history.TransferPath;
        if (transferPath.Length == 0)
            throw new MoviePilotOperationBlockedException($"原整理记录 #{history.Id} 没有可用的文件路径，请先在 MoviePilot 核对");
        var file = await TransferFileAsync(transferPath, cancellationToken).ConfigureAwait(false);
        CheckAniBtConnection(connection, cancellationToken);
        MoviePilotTransfer.RequireFile(file, transferPath);
        var matched = await TransferHistoryTargetAsync([history], cancellationToken).ConfigureAwait(false);
        CheckAniBtConnection(connection, cancellationToken);
        if (matched.Directories.Count != 1)
            throw new MoviePilotOperationBlockedException("MoviePilot 没有为原整理记录返回目的目录，请先在 MoviePilot 核对目录配置");
        var directory = matched.Directories[0];
        var request = new MoviePilotTransferRequest
        {
            Files = [new MoviePilotTransferFile(MoviePilotTransfer.PathName(targetPath), targetPath)],
            Histories = [history],
            TargetStorage = directory.Storage,
            TargetPath = directory.Path,
            TransferType = directory.TransferType ?? "",
            Type = expectedType,
            MediaSource = "bangumi",
            MediaId = anime.BgmId.ToString(CultureInfo.InvariantCulture),
            Season = season?.ToString(CultureInfo.InvariantCulture) ?? "",
            Episode = episode,
            TypeFolder = directory.TypeFolder,
            CategoryFolder = directory.CategoryFolder,
            // AniBT 识别的目的就是换识别并重写元数据，不能沿用目录里关闭刮削的默认值。
            Scrape = true,
            Reorganize = true
        };
        if (request.Problem is { } problem) throw new MoviePilotException(problem);
        return new(request, file.Clone());
    }
}
