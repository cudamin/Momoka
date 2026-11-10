using System.Globalization;
using System.Text.Json;

namespace Momoka.MoviePilot;

public sealed partial class MoviePilotService
{
    /// <summary>把当前连接的搜索候选及人工指定季集解析为可供本机整理使用的资料。</summary>
    public async Task<MoviePilotLocalRecognition> ResolveLocalMediaAsync(MoviePilotMedia media, int? season, int? episode, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var identity = RequireResultConnection(media.ConnectionStamp);
        if (!ValidLocalMedia(media)) throw new MoviePilotException("请选择具有有效身份的电影或电视剧候选");
        if (media.Type == "电影" ? season is not null || episode is not null : season is not (>= 0 and <= 10000) || episode is not (>= 1 and <= 10000))
            throw new MoviePilotException("电影不使用季集编号；电视剧必须填写明确的单季单集编号");
        var detail = await MediaDetailAsync(media, cancellationToken).ConfigureAwait(false);
        CheckIdentity(identity, cancellationToken);
        RequireResultConnection(media.ConnectionStamp);
        var result = new MoviePilotLocalRecognition { Detail = detail, Season = season, Episode = episode };
        if (media.Type != "电视剧" || !string.Equals(media.MediaSource, "themoviedb", StringComparison.OrdinalIgnoreCase)) return result;

        var episodePath = "tmdb/" + Uri.EscapeDataString(media.MediaId!) + "/" + season!.Value.ToString(CultureInfo.InvariantCulture);
        var episodes = await CallAsync((address, token, client) => client.GetAsync(address, token, episodePath, cancellationToken),
            cancellationToken, identity).ConfigureAwait(false);
        CheckIdentity(identity, cancellationToken);
        RequireResultConnection(media.ConnectionStamp);
        if (episodes.ValueKind != JsonValueKind.Array)
            throw new MoviePilotException("MoviePilot 未返回有效的单集资料，请重新识别");
        var matches = episodes.EnumerateArray().Where(item => MoviePilotSubscription.Number(item, "episode_number") == episode).ToArray();
        if (matches.Length != 1 || MoviePilotSubscription.Number(matches[0], "season_number") != season)
            throw new MoviePilotException("MoviePilot 未返回唯一且季集一致的单集资料，请核对季集编号后重新识别");
        var episodeData = matches[0];
        var episodeId = MoviePilotSubscription.Text(episodeData, "id");
        if (!LocalNumericId(episodeId))
            throw new MoviePilotException("MoviePilot 单集资料缺少有效的单集 ID，不能生成可信的单集 NFO");
        return result with
        {
            EpisodeId = episodeId,
            EpisodeTitle = LocalOptionalText(episodeData, "name"),
            EpisodeOverview = LocalOptionalText(episodeData, "overview"),
            EpisodeAirDate = LocalOptionalText(episodeData, "air_date"),
            EpisodeImageUrl = LocalEpisodeImage(LocalOptionalText(episodeData, "still_path"), detail)
        };
    }

    /// <summary>
    /// 仅把文件名及相对目录文本交给 MP 识别，不要求 MP 可见文件，不调用整理或刮削写入接口。
    /// </summary>
    public async Task<MoviePilotLocalRecognition> RecognizeLocalFileAsync(string fileNameOrContext, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = fileNameOrContext?.Trim() ?? "";
        if (context.Length == 0) throw new MoviePilotException("请先选择要识别的影视文件");
        var identity = CaptureIdentity();
        var stamp = TransferConnectionStamp(identity);
        var path = "media/recognize?title=" + Uri.EscapeDataString(context);
        var data = await CallAsync((address, token, client) => client.GetAsync(address, token, path, cancellationToken),
            cancellationToken, identity).ConfigureAwait(false);
        CheckIdentity(identity, cancellationToken);
        var meta = MoviePilotSubscription.Member(data, "meta_info");
        var info = MoviePilotSubscription.Member(data, "media_info");
        if (meta.ValueKind != JsonValueKind.Object || info.ValueKind != JsonValueKind.Object)
            throw new MoviePilotException("MoviePilot 未识别到有效的电影或电视剧，请核对文件名后重新识别");
        var media = MoviePilotMediaParser.Parse(JsonSerializer.SerializeToElement(new[] { info })).SingleOrDefault();
        if (media is null || !ValidLocalMedia(media))
            throw new MoviePilotException("MoviePilot 未识别到有效的电影或电视剧，请核对文件名后重新识别");
        ValidateLocalParsedIdentity(meta, media);
        media = media with { ConnectionStamp = stamp };
        int? season = null;
        int? episode = null;
        if (media.Type == "电视剧")
        {
            season = LocalSingleNumber(meta, "begin_season", "end_season", "season_list", "total_season", 0, "季");
            episode = LocalSingleNumber(meta, "begin_episode", "end_episode", "episode_list", "total_episode", 1, "集");
            if (MoviePilotSubscription.Text(meta, "episode_group").Length > 0)
                throw new MoviePilotException("本机整理暂不支持剧集组编号，请使用明确的原始季集编号");
        }
        RequireResultConnection(stamp);
        var result = await ResolveLocalMediaAsync(media, season, episode, cancellationToken).ConfigureAwait(false);
        CheckIdentity(identity, cancellationToken);
        RequireResultConnection(stamp);
        return result;
    }

    private static bool ValidLocalMedia(MoviePilotMedia media)
    {
        if (media.Type is not ("电影" or "电视剧") || !media.CanSubscribe) return false;
        var source = media.MediaSource!;
        var id = media.MediaId!;
        if (source.Length > 64 || !source.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-')) return false;
        return source.ToLowerInvariant() switch
        {
            "musicbrainz" or "theaudiodb" or "doubanmusic" => false,
            "themoviedb" or "douban" or "bangumi" or "anilist" or "tvdb" => LocalNumericId(id),
            "imdb" => id.StartsWith("tt", StringComparison.Ordinal) && LocalNumericId(id[2..]),
            _ => id.Length is > 0 and <= 256 && !id.Any(char.IsWhiteSpace) && !id.Any(char.IsControl)
        };
    }

    private static bool LocalNumericId(string id) => id.Length is > 0 and <= 20 && id.All(char.IsAsciiDigit) && id.Any(character => character != '0');

    private static void ValidateLocalParsedIdentity(JsonElement meta, MoviePilotMedia media)
    {
        var source = MoviePilotSubscription.Text(meta, "media_source");
        var id = MoviePilotSubscription.Text(meta, "media_id");
        var type = MoviePilotSubscription.Text(meta, "type");
        if ((source.Length > 0 && !string.Equals(source, media.MediaSource, StringComparison.OrdinalIgnoreCase))
            || (id.Length > 0 && id != media.MediaId) || (type.Length > 0 && type != "未知" && type != media.Type))
            throw new MoviePilotException("文件解析信息与 MoviePilot 媒体身份不匹配，请核对文件名后重新识别");
    }

    private static int LocalSingleNumber(JsonElement meta, string beginName, string endName, string listName, string totalName, int minimum, string label)
    {
        var numbers = new List<int>();
        var begin = MoviePilotSubscription.Member(meta, beginName);
        Add(begin);
        Add(MoviePilotSubscription.Member(meta, endName));
        var list = MoviePilotSubscription.Member(meta, listName);
        if (list.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
        {
            if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() > 1) Reject();
            foreach (var value in list.EnumerateArray())
            {
                if (value.ValueKind == JsonValueKind.Null) Reject();
                Add(value);
            }
        }
        var total = MoviePilotSubscription.Member(meta, totalName);
        if (total.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null)
            && (!int.TryParse(MoviePilotSubscription.Scalar(total), NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count is < 0 or > 1)) Reject();
        if (numbers.Count == 0 || numbers.Distinct().Count() != 1) Reject();
        if (begin.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null && (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)) Reject();
        return numbers[0];

        void Add(JsonElement value)
        {
            if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return;
            if (!int.TryParse(MoviePilotSubscription.Scalar(value), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                || number < minimum || number > 10000) Reject();
            numbers.Add(number);
        }
        void Reject() => throw new MoviePilotException($"文件的{label}编号缺失、冲突或包含范围；首版仅支持明确的单季单集，请核对文件名后重新识别");
    }

    private static string? LocalOptionalText(JsonElement data, string name) =>
        MoviePilotSubscription.Text(data, name).Trim() is { Length: > 0 } value ? value : null;

    private static string? LocalEpisodeImage(string? image, MoviePilotMediaDetail detail)
    {
        if (image is null || !image.StartsWith('/')) return image;
        // 上游 TMDB 图片域名可配置；优先沿用已核对作品图片的域名及大小路径。
        foreach (var artwork in new[] { detail.Media.PosterUrl, detail.Media.BackdropUrl })
        {
            if (!Uri.TryCreate(artwork, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) continue;
            var marker = uri.AbsolutePath.IndexOf("/t/p/", StringComparison.Ordinal);
            if (marker < 0) continue;
            var sizeEnd = uri.AbsolutePath.IndexOf('/', marker + 5);
            if (sizeEnd >= 0) return uri.GetLeftPart(UriPartial.Authority) + uri.AbsolutePath[..sizeEnd] + image;
        }
        return MoviePilotMediaDetail.Image(image);
    }
}
