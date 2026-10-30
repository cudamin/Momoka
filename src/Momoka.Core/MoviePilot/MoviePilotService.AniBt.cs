using System.Globalization;
using System.Text.Json;
using Momoka.AniBt;

namespace Momoka.MoviePilot;

internal sealed record MoviePilotAniBtConnection(string Stamp, long Generation);

internal sealed record MoviePilotAniBtTransfer(MoviePilotTransferRequest Request, JsonElement File);

/// <summary>MoviePilot 媒体详情里 AniBT 整理要用的那几项；不把原始回话交给界面。</summary>
internal sealed record MoviePilotAniBtMediaInfo(string MediaSource, string MediaId, string Title, string OriginalTitle,
    IReadOnlyList<string> Names, string Type);

/// <summary>整理时交给 MoviePilot 的媒体身份；同一部主剧集用库内已有的 TMDB 编号，否则用 AniBT 的 Bangumi 编号。</summary>
internal sealed record MoviePilotAniBtTarget(string MediaSource, string MediaId, string Title);

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
            media = await MediaAsync("bangumi", anime.BgmId.ToString(CultureInfo.InvariantCulture), type, connection,
                cancellationToken, requireType: false).ConfigureAwait(false);
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

    private async Task<JsonElement> MediaAsync(string mediaSource, string mediaId, string expectedType,
        MoviePilotAniBtConnection connection, CancellationToken cancellationToken, bool requireType = true)
    {
        CheckAniBtConnection(connection, cancellationToken);
        if (mediaSource is not ("bangumi" or "themoviedb"))
            throw new MoviePilotException("AniBT 没有提供可用的媒体来源");
        if (!long.TryParse(mediaId, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number <= 0)
            throw new MoviePilotException("AniBT 没有提供有效的媒体编号");
        var identity = RequireResultConnection(connection.Stamp);
        var id = number.ToString(CultureInfo.InvariantCulture);
        var path = $"media/{id}?media_source={Uri.EscapeDataString(mediaSource)}&type_name={Uri.EscapeDataString(expectedType)}";
        var media = await ReadAsync().ConfigureAwait(false);
        // 来源目录可用不等于一次详情查询必有结果。仅对完全空的只读回话重读一次，
        // 不重试身份/类型不匹配，更不重试整理写入。
        if (EmptyMedia(media)) media = await ReadAsync().ConfigureAwait(false);
        if (EmptyMedia(media))
            throw new MoviePilotException("MoviePilot 暂时没有返回此作品的媒体信息，请重新读取整理目标");
        if (media.ValueKind != JsonValueKind.Object ||
            MoviePilotTransfer.Text(media, "media_source") != mediaSource ||
            MoviePilotTransferHistory.Scalar(media, "media_id") != id ||
            MoviePilotTransfer.Text(media, "type") is not (MoviePilotTransferRequest.TypeMovie or MoviePilotTransferRequest.TypeSeries) ||
            requireType && MoviePilotTransfer.Text(media, "type") != expectedType ||
            string.IsNullOrWhiteSpace(MoviePilotTransfer.Text(media, "title")))
            throw new MoviePilotOperationBlockedException("MoviePilot 返回的媒体身份或类型与 AniBT 结果不一致，已停止操作");
        return media.Clone();

        async Task<JsonElement> ReadAsync()
        {
            var result = await CallAsync((address, token) => _client.GetAsync(address, token, path, cancellationToken),
                cancellationToken, identity).ConfigureAwait(false);
            CheckAniBtConnection(connection, cancellationToken);
            return result;
        }
    }

    /// <summary>主剧集沿用也不能绕过所选 Bangumi 候选的来源、编号与类型校验。</summary>
    internal async Task<MoviePilotAniBtMediaInfo> VerifyAniBtCandidateAsync(AniBtAnime anime, string expectedType,
        MoviePilotAniBtConnection connection, CancellationToken cancellationToken)
    {
        CheckAniBtConnection(connection, cancellationToken);
        var sources = await MediaSourcesAsync(cancellationToken).ConfigureAwait(false);
        CheckAniBtConnection(connection, cancellationToken);
        if (!sources.Any(source => source.Id == "bangumi" && source.IsVideo))
            throw new MoviePilotOperationBlockedException("此 MoviePilot 没有启用 Bangumi 媒体来源，暂时不能核验 AniBT 候选");
        return await ReadAniBtMediaAsync("bangumi", anime.BgmId.ToString(CultureInfo.InvariantCulture), expectedType,
            connection, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>读一条媒体详情并按白名单投影；AniBT 用它判断候选与库内主剧集是不是同一部作品。</summary>
    internal async Task<MoviePilotAniBtMediaInfo> ReadAniBtMediaAsync(string mediaSource, string mediaId,
        string expectedType, MoviePilotAniBtConnection connection, CancellationToken cancellationToken)
    {
        var media = await MediaAsync(mediaSource, mediaId, expectedType, connection, cancellationToken).ConfigureAwait(false);
        var names = new List<string>();
        if (media.TryGetProperty("names", out var values) && values.ValueKind == JsonValueKind.Array)
            foreach (var value in values.EnumerateArray())
                if (value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } name)
                    names.Add(name);
        return new(mediaSource, mediaId, MoviePilotTransfer.Text(media, "title"),
            MoviePilotTransfer.Text(media, "original_title"), names, MoviePilotTransfer.Text(media, "type"));
    }

    private static bool EmptyMedia(JsonElement media) => media.ValueKind == JsonValueKind.Object
        && MoviePilotTransfer.Text(media, "media_source").Length == 0
        && MoviePilotTransferHistory.Scalar(media, "media_id").Length == 0
        && MoviePilotTransfer.Text(media, "type").Length == 0
        && MoviePilotTransfer.Text(media, "title").Length == 0;

    /// <summary>
    /// 按原整理记录和 AniBT 选中的 Bangumi 身份准备一次真正的 MoviePilot 手动整理（重新整理）。
    /// <para>
    /// 源文件必须走 <see cref="MoviePilotTransferHistory.TransferPath"/>：成功 move 的记录指向 Emby 现在看到的
    /// 目标，复制/软链接记录则指向仍存在的原下载源；直接拿 Emby 路径调用 <c>storage/list</c> 会在后两种记录上
    /// 问错文件。目的目录、整理方式和分类开关整组取自该记录的 <c>transfer/manual/target-path</c>，不按默认值另拼。
    /// </para>
    /// </summary>
    internal async Task<MoviePilotAniBtTransfer> PrepareAniBtTransferAsync(string targetPath, AniBtAnime anime,
        MoviePilotAniBtMediaInfo candidate, MoviePilotAniBtTarget target, string expectedType,
        MoviePilotTransferHistory history, int? season, string episode,
        MoviePilotAniBtConnection connection, CancellationToken cancellationToken)
    {
        CheckAniBtConnection(connection, cancellationToken);
        if (target.MediaSource is not ("bangumi" or "themoviedb") || target.MediaId.Length == 0)
            throw new MoviePilotOperationBlockedException("AniBT 没有给出可用的整理身份，请重新识别");
        var sources = await MediaSourcesAsync(cancellationToken).ConfigureAwait(false);
        CheckAniBtConnection(connection, cancellationToken);
        if (!sources.Any(source => source.Id == target.MediaSource && source.IsVideo))
            throw new MoviePilotOperationBlockedException(target.MediaSource == "bangumi"
                ? "此 MoviePilot 没有启用 Bangumi 媒体来源，暂时不能按 AniBT 结果重新整理"
                : "此 MoviePilot 没有启用 TheMovieDb 媒体来源，暂时不能沿用主剧集身份整理");
        var bangumiId = anime.BgmId.ToString(CultureInfo.InvariantCulture);
        if (candidate.MediaSource != "bangumi" || candidate.MediaId != bangumiId || candidate.Type != expectedType
            || target.MediaSource == "bangumi" && target.MediaId != bangumiId
            || target.MediaSource == "themoviedb" && expectedType != MoviePilotTransferRequest.TypeSeries)
            throw new MoviePilotOperationBlockedException("所选媒体不属于此文件的 AniBT 候选结果或媒体类型，请重新识别");
        // candidate 是本批次经 MediaAsync 核验的 Bangumi 详情；TMDB 目标也已核验详情及主剧集归属。
        // 预览只核对实际文件目标，不能替代这些身份校验。
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
            MediaSource = target.MediaSource,
            MediaId = target.MediaId,
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
