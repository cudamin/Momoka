using Momoka.Emby;
using Momoka.LocalMedia;

namespace Momoka.Shell.Media;

/// <summary>
/// 把本机媒体库条目折成详情页认得的 <see cref="EmbyItem"/>：不连任何服务器，播放直接落在源文件上。
/// 详情页因此可以原样复用（电影/剧集/单集三种形态），只是取图和取集列表改读本机索引。
/// </summary>
internal static class LocalMediaItems
{
    /// <summary>一部作品在本地索引里的稳定身份：库 + 数据来源 + 数据来源编号。</summary>
    internal static string SeriesKey(string libraryId, LocalMediaMetadata metadata) =>
        $"local-series:{libraryId}:{metadata.MediaSource}:{metadata.MediaId}";

    internal static EmbyItem Movie(LocalMediaLibraryEntry entry)
    {
        var metadata = entry.Metadata;
        var item = new EmbyItem
        {
            Id = entry.Id,
            Name = metadata.Title,
            Type = EmbyItemType.Movie,
            ProductionYear = metadata.Year,
            Overview = metadata.Overview,
            PremiereDate = metadata.Premiered is { } premiered ? new DateTimeOffset(premiered.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null,
            IsLocal = true,
            LocalLibraryId = entry.LibraryId,
            LocalGroupKey = LocalMediaEntryArtwork.GroupKey(entry),
            LocalArtworkPath = LocalMediaEntryArtwork.PosterPath(entry),
            LocalBackdropPath = LocalMediaEntryArtwork.BackdropPath(entry),
            MediaSources = [Source(entry)],
            UserData = UserState(entry)
        };
        Apply(item, metadata);
        return item;
    }

    internal static EmbyItem Episode(LocalMediaLibraryEntry entry)
    {
        var metadata = entry.Metadata;
        var episode = metadata.Episode ?? 0;
        var item = new EmbyItem
        {
            Id = entry.Id,
            Name = metadata.EpisodeTitle.Length > 0 ? metadata.EpisodeTitle : $"第 {episode} 集",
            Type = EmbyItemType.Episode,
            ProductionYear = metadata.Year,
            Overview = metadata.EpisodeOverview.Length > 0 ? metadata.EpisodeOverview : metadata.Overview,
            ParentIndexNumber = metadata.Season,
            IndexNumber = episode,
            SeriesName = metadata.Title,
            SeasonName = SeasonName(metadata.Season),
            PremiereDate = metadata.EpisodeAired is { } aired ? new DateTimeOffset(aired.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null,
            IsLocal = true,
            LocalLibraryId = entry.LibraryId,
            LocalGroupKey = LocalMediaEntryArtwork.GroupKey(entry),
            LocalArtworkPath = LocalMediaEntryArtwork.PosterPath(entry),
            LocalBackdropPath = LocalMediaEntryArtwork.BackdropPath(entry),
            LocalThumbPath = LocalMediaEntryArtwork.ThumbPath(entry),
            MediaSources = [Source(entry)],
            UserData = UserState(entry)
        };
        Apply(item, metadata);
        return item;
    }

    /// <summary>剧集这一层：海报、背景与简介取组里第一条，集列表由调用方另给。</summary>
    internal static EmbyItem Series(string libraryId, IReadOnlyList<LocalMediaLibraryEntry> entries)
    {
        var first = entries[0];
        var metadata = first.Metadata;
        var seasons = entries.Select(entry => entry.Metadata.Season ?? 0).Distinct().Count();
        var item = new EmbyItem
        {
            Id = SeriesKey(libraryId, metadata),
            Name = metadata.Title,
            Type = EmbyItemType.Series,
            ProductionYear = metadata.Year,
            Overview = entries.Select(entry => entry.Metadata.Overview).FirstOrDefault(text => text.Length > 0) ?? "",
            PremiereDate = metadata.Premiered is { } premiered ? new DateTimeOffset(premiered.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null,
            // 详情页的「共 N 季」读的是剧这一层的 ChildCount；本机索引里它就是实际出现的季数，不是集数。
            ChildCount = seasons,
            IsLocal = true,
            LocalLibraryId = libraryId,
            LocalGroupKey = LocalMediaEntryArtwork.GroupKey(first),
            LocalArtworkPath = entries.Select(LocalMediaEntryArtwork.PosterPath).FirstOrDefault(path => path.Length > 0) ?? "",
            LocalBackdropPath = entries.Select(LocalMediaEntryArtwork.BackdropPath).FirstOrDefault(path => path.Length > 0) ?? "",
            ProviderIds = Provider(metadata),
            UserData = UserState(first)
        };
        Apply(item, metadata);
        return item;
    }

    /// <summary>详情页的评分、类型、片长、宣传语、演职人员与已看收藏；每一组只保留本机索引里有的那份。</summary>
    private static void Apply(EmbyItem item, LocalMediaMetadata metadata)
    {
        item.Genres = metadata.Genres.Where(name => name.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        item.CommunityRating = metadata.CommunityRating is { } rating ? (float)rating : null;
        item.RunTimeTicks = metadata.RuntimeMinutes is > 0 and var minutes ? minutes * 60L * TimeSpan.TicksPerSecond : null;
        item.Taglines = metadata.Tagline.Length > 0 ? [metadata.Tagline] : [];
        item.Studios = metadata.Studios.Where(name => name.Length > 0).Select(name => new EmbyStudio { Name = name }).ToList();
        item.People = metadata.People.Where(person => person.Name.Length > 0)
            .Select(person => new EmbyPerson
            {
                // 本机人物没有服务器账号：编号只在本页去重用，人像直接读元数据目录。
                Id = "local-person:" + (person.Id.Length > 0 ? person.Id : person.Name),
                Name = person.Name,
                Role = person.Role,
                Type = person.Director ? "Director" : "Actor",
                PrimaryImageTag = person.ImagePath.Length > 0 ? "local" : null,
                LocalArtworkPath = person.ImagePath
            })
            .ToList();
    }

    /// <summary>详情页季清单：由本机条目里实际出现的季号生成，不向服务器要。</summary>
    internal static IReadOnlyList<EmbyItem> Seasons(EmbyItem series, IReadOnlyList<EmbyItem> episodes) => episodes
        .Select(episode => episode.ParentIndexNumber ?? 0)
        .Distinct()
        .Order()
        .Select(number => new EmbyItem
        {
            Id = $"{series.Id}:S{number:00}",
            Name = SeasonName(number),
            Type = EmbyItemType.Season,
            SeriesId = series.Id,
            SeriesName = series.Name,
            IndexNumber = number
        })
        .ToArray();

    internal static IReadOnlyList<EmbyItem> Sort(IEnumerable<EmbyItem> episodes) => episodes
        .OrderBy(episode => episode.ParentIndexNumber ?? 0)
        .ThenBy(episode => episode.IndexNumber ?? 0)
        .ToArray();

    private static EmbyUserData UserState(LocalMediaLibraryEntry entry) => new()
    {
        Played = entry.Watched,
        IsFavorite = entry.Favorite
    };

    private static string SeasonName(int? season) => season is null ? "" : season == 0 ? "特别篇" : $"第 {season} 季";

    private static MediaSource Source(LocalMediaLibraryEntry entry)
    {
        var path = entry.TargetPath;
        var size = entry.Source.Length > 0 ? entry.Source.Length : (long?)null;
        return new MediaSource
        {
            Id = "local",
            Name = Path.GetFileName(path),
            Path = path,
            Container = Path.GetExtension(path).TrimStart('.'),
            Size = size,
            // 本机视频没有服务器媒体流：读数从文件名与索引里记录的长度来，只用于详情页与媒体信息表。
            LocalFileInfoLabel = LocalMediaFileInfo.Label(path, size)
        };
    }

    private static Dictionary<string, string> Provider(LocalMediaMetadata metadata) => metadata.MediaSource switch
    {
        "themoviedb" or "tmdb" => new() { ["Tmdb"] = metadata.MediaId },
        _ => []
    };
}
