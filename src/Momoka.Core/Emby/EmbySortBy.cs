namespace Momoka.Emby;

/// <summary>
/// Which kind of thing a grid is listing. Emby calls this the display preset, and uses it to drop
/// sort keys that cannot mean anything for the rows on screen: a bitrate for a series, a resolution
/// for an album, a critic rating for a photo.
/// <para>
/// The values are Emby's own item type names so the exclusion lists in <see cref="EmbySortBy"/> can
/// be read straight across from its list controller.
/// </para>
/// </summary>
public static class EmbySortPreset
{
    public const string Movie = "Movie";
    public const string Series = "Series";
    public const string Season = "Season";
    public const string Episode = "Episode";
    public const string Album = "MusicAlbum";
    public const string Song = "Audio";
    public const string Photo = "Photo";
    public const string Video = "Video";
    public const string MusicVideo = "MusicVideo";
    public const string Trailer = "Trailer";
    public const string BoxSet = "BoxSet";

    /// <summary>
    /// 混合内容库的筛选预设。排序菜单不用它：那一页在网页端给的是文件夹那整份菜单；
    /// 这个预设只回答「混合条目能筛哪些字段」，别拿它去顶电影或电视剧的名单。
    /// </summary>
    public const string Mixed = "Mixed";

    /// <summary>
    /// The preset for a library root, or null when the collection type says nothing useful and every
    /// general key should stay on offer.
    /// <para>
    /// "mixed" is deliberately absent: its rows are both films and shows, so either single preset would
    /// hide keys the other half needs. Null keeps the general catalogue instead of borrowing one of them.
    /// </para>
    /// </summary>
    public static string? ForCollection(string? collectionType) => collectionType switch
    {
        "movies" => Movie,
        "tvshows" => Series,
        "music" => Album,
        "musicvideos" => MusicVideo,
        "trailers" => Trailer,
        "photos" => Photo,
        "homevideos" => Video,
        "boxsets" => BoxSet,
        _ => null
    };

    /// <summary>
    /// The preset for the children of an item. A series lists seasons and a season lists episodes,
    /// and both want keys a library root does not; anything else is a mixed folder, where null keeps
    /// the whole general set on offer.
    /// </summary>
    public static string? ForChildrenOf(string? parentType) => parentType switch
    {
        EmbyItemType.Series => Season,
        EmbyItemType.Season => Episode,
        EmbyItemType.BoxSet => Movie,
        _ => null
    };
}

/// <summary>
/// One entry in the sort menu.
/// <para>
/// Transcribed from Emby's own list controller (<c>modules/common/itemmanager/baseitemcontroller.js</c>,
/// 4.10.1.0) rather than invented, so the composite tie-breakers (<c>DateCreated,SortName</c> and not a
/// bare <c>DateCreated</c>), the per-key default direction and the exclusions are the server's behaviour
/// instead of a guess at it. Three of them are worth knowing about: 媒体码率 sorts on
/// <c>TotalBitrate</c>, not on a video-only bitrate; 源分辨率 only exists from server 4.7.3; and the
/// two episode-date keys belong to 剧集 alone, which is why the 电视剧-节目 page's menu is exactly the
/// thirteen entries the web client shows there.
/// </para>
/// </summary>
public sealed record EmbySortOption
{
    /// <summary>Chinese label, taken from Emby's own zh-CN string table.</summary>
    public required string Label { get; init; }

    /// <summary>The <c>SortBy</c> value, commas and all.</summary>
    public required string Value { get; init; }

    /// <summary>
    /// The direction to switch to when this key is chosen. Newest first for a date, A to Z for a
    /// name: picking 加入日期 and getting the oldest thing in the library is never what was meant.
    /// </summary>
    public bool DescendingByDefault { get; init; }

    /// <summary>Presets this key is hidden for, because Emby returns null for them.</summary>
    public IReadOnlyList<string> NotFor { get; init; } = [];

    /// <summary>When non-empty, the only presets this key is offered for.</summary>
    public IReadOnlyList<string> OnlyFor { get; init; } = [];

    /// <summary>Presets that sort on something else, keyed by preset.</summary>
    public IReadOnlyDictionary<string, string> ValueFor { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Lowest server version that understands <see cref="Value"/>, or null for all of them.</summary>
    public string? MinServerVersion { get; init; }

    /// <summary>The value to send for a given preset.</summary>
    public string ValueOf(string? preset) =>
        preset is not null && ValueFor.TryGetValue(preset, out var special) ? special : Value;

    /// <summary>
    /// Whether this key can be answered for a preset at all, ignoring the server version.
    /// </summary>
    public bool OfferedFor(string? preset) =>
        preset is null ? OnlyFor.Count == 0 : (OnlyFor.Count == 0 || OnlyFor.Contains(preset)) && !NotFor.Contains(preset);

    /// <summary>
    /// Whether to offer this key at all.
    /// <para>
    /// Both unknowns fail open. An unrecognised preset means a mixed folder or a search result, where
    /// hiding keys would be guessing at what the rows are; an unknown server version means the probe
    /// has not answered yet, and offering a key the server ignores is a smaller failure than hiding
    /// one it supports.
    /// </para>
    /// </summary>
    public bool AppliesTo(string? preset, Version? serverVersion)
    {
        if (MinServerVersion is { } minimum
            && serverVersion is not null
            && Version.TryParse(minimum, out var required)
            && serverVersion < required)
            return false;

        return OfferedFor(preset);
    }
}

/// <summary>
/// The sort catalogue. <see cref="All"/> is the whole of it; <see cref="For"/> is what a particular
/// grid should show.
/// <para>
/// <b>The menu is sorted by label, ordinally — not by this file's order.</b> Emby builds its own menu
/// and then calls <c>sortBy.sort(compareByName)</c>, which is a plain <c>a &lt; b</c> on the localized
/// string; in Chinese that puts 标题 between 播放时长 and 随机 and nobody would write that order by
/// hand. Copying the sort instead of the list is what makes the menu read like the web client's, and it
/// is why <see cref="All"/> below is grouped by subject only for the reader's benefit.
/// </para>
/// </summary>
public static class EmbySortBy
{
    // Kept as constants because they are referenced directly as defaults, and pointed at Emby's real
    // composite values: a bare DateCreated leaves items added in the same scan in arbitrary order.
    public const string Name = "SortName";
    public const string Number = "ParentIndexNumber,IndexNumber,SortName";
    public const string DateAdded = "DateCreated,SortName";
    public const string ReleaseDate = "ProductionYear,PremiereDate,SortName";
    public const string Rating = "CommunityRating,SortName";
    public const string PlayCount = "PlayCount,SortName";
    public const string Random = "Random";

    /// <summary>
    /// Every key. The exclusion lists are Emby's, narrowed to the presets this application can actually
    /// produce; its Studio, Genre, Tag, MusicArtist, TvChannel, Program, Playlist, Game and GameSystem
    /// cases are dropped because no grid here ever lists those.
    /// </summary>
    public static readonly IReadOnlyList<EmbySortOption> All =
    [
        new()
        {
            Label = "标题",
            Value = Name
        },
        new()
        {
            Label = "节目名称",
            Value = "SeriesSortName,ParentIndexNumber,IndexNumber,SortName",
            OnlyFor = [EmbySortPreset.Episode]
        },
        new()
        {
            Label = "顺序编号",
            Value = Number,
            NotFor =
            [
                EmbySortPreset.Movie, EmbySortPreset.Video, EmbySortPreset.Trailer,
                EmbySortPreset.MusicVideo, EmbySortPreset.Photo, EmbySortPreset.Album,
                EmbySortPreset.Series, EmbySortPreset.BoxSet
            ]
        },
        new()
        {
            Label = "文件名称",
            // IsFolder first is Emby's: it keeps sub-directories together at one end instead of
            // interleaving them with the files by name.
            Value = "IsFolder,Filename",
            NotFor = [EmbySortPreset.Trailer, EmbySortPreset.Album, EmbySortPreset.Series, EmbySortPreset.BoxSet]
        },
        new()
        {
            Label = "加入日期",
            Value = DateAdded,
            DescendingByDefault = true
        },
        new()
        {
            // 剧集自己的两个日期键：Emby 的 switch 只给 Series 这一支，电影、合集和混合列表都没有。
            Label = "上次添加集日期",
            Value = "DateLastContentAdded,SortName",
            OnlyFor = [EmbySortPreset.Series],
            DescendingByDefault = true
        },
        new()
        {
            Label = "上次发布集日期",
            Value = "LastContentPremiereDate,SortName",
            OnlyFor = [EmbySortPreset.Series],
            DescendingByDefault = true,
            MinServerVersion = "4.9.0.17"
        },
        new()
        {
            Label = "播放日期",
            Value = "DatePlayed,SortName",
            DescendingByDefault = true,
            // A series has no play date of its own; the server exposes its episodes' latest as a
            // separate key.
            ValueFor = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [EmbySortPreset.Series] = "SeriesDatePlayed,SortName"
            },
            NotFor = [EmbySortPreset.BoxSet]
        },
        new()
        {
            Label = "发行日期",
            Value = ReleaseDate,
            DescendingByDefault = true,
            NotFor = [EmbySortPreset.BoxSet]
        },
        new()
        {
            Label = "年份",
            Value = "ProductionYear,SortName",
            DescendingByDefault = true,
            NotFor = [EmbySortPreset.BoxSet]
        },
        new()
        {
            Label = "播放时长",
            Value = "Runtime,SortName",
            NotFor = [EmbySortPreset.Photo]
        },
        new()
        {
            Label = "IMDb 评分",
            Value = Rating,
            DescendingByDefault = true,
            NotFor = [EmbySortPreset.Photo, EmbySortPreset.BoxSet]
        },
        new()
        {
            Label = "影评人评分",
            Value = "CriticRating,SortName",
            DescendingByDefault = true,
            NotFor = [EmbySortPreset.Photo, EmbySortPreset.BoxSet, EmbySortPreset.Album, EmbySortPreset.Song]
        },
        new()
        {
            Label = "家长评分",
            Value = "OfficialRating,SortName",
            DescendingByDefault = true
        },
        new()
        {
            Label = "导演",
            Value = "Director,SortName",
            DescendingByDefault = true,
            MinServerVersion = "4.9.0.48",
            NotFor = [EmbySortPreset.Photo, EmbySortPreset.BoxSet, EmbySortPreset.Album, EmbySortPreset.Song]
        },
        new()
        {
            Label = "播放次数",
            Value = PlayCount,
            DescendingByDefault = true,
            NotFor = [EmbySortPreset.Series, EmbySortPreset.BoxSet, EmbySortPreset.Photo]
        },
        new()
        {
            Label = "源分辨率",
            Value = "Resolution,SortName",
            DescendingByDefault = true,
            MinServerVersion = "4.7.3",
            NotFor = [EmbySortPreset.Series, EmbySortPreset.BoxSet, EmbySortPreset.Album, EmbySortPreset.Song]
        },
        new()
        {
            Label = "媒体码率",
            // TotalBitrate, not a video-only figure: this is the whole file's rate, which is what
            // Emby indexes and the only one it will sort on.
            Value = "TotalBitrate,SortName",
            DescendingByDefault = true,
            NotFor =
            [
                EmbySortPreset.Photo, EmbySortPreset.Series, EmbySortPreset.BoxSet,
                EmbySortPreset.Album, EmbySortPreset.Song
            ]
        },
        new()
        {
            Label = "媒体容器",
            Value = "Container,SortName",
            DescendingByDefault = true,
            NotFor = [EmbySortPreset.Series, EmbySortPreset.BoxSet, EmbySortPreset.Album]
        },
        new()
        {
            Label = "文件大小",
            Value = "Size,SortName",
            DescendingByDefault = true,
            NotFor = [EmbySortPreset.Series, EmbySortPreset.BoxSet, EmbySortPreset.Album]
        },
        new()
        {
            // Emby has had this since 4.8.0.11 for exactly these presets, and puts it last by name.
            Label = "随机",
            Value = Random
        }
    ];

    /// <summary>
    /// The keys a grid should offer, in the order the menu draws them: by label, ordinally. See the
    /// type's remarks for why this is a sort and not a hand-written order.
    /// </summary>
    public static IReadOnlyList<EmbySortOption> For(string? preset, Version? serverVersion = null) =>
        [.. All.Where(option => option.AppliesTo(preset, serverVersion))
            .OrderBy(option => option.Label, StringComparer.Ordinal)];

    /// <summary>
    /// What to sort by before anyone chooses. Episodes and seasons go in broadcast order, because a
    /// season listed alphabetically is the one ordering that is always wrong; everything else by name.
    /// </summary>
    public static EmbySortOption Default(string? preset) =>
        preset is EmbySortPreset.Episode or EmbySortPreset.Season
            ? Find(Number) ?? All[0]
            : All[0];

    /// <summary>
    /// The catalogue entry for a stored <c>SortBy</c> value, or null if it is no longer offered. The
    /// per-preset values count too: 播放日期 stores <c>SeriesDatePlayed,SortName</c> for a 剧集 grid, and
    /// that has to read back as the same menu entry rather than as an unknown key.
    /// </summary>
    public static EmbySortOption? Find(string? value)
    {
        if (value is null) return null;

        return All.FirstOrDefault(option => option.Value == value)
            ?? All.FirstOrDefault(option => option.ValueFor.Values.Contains(value, StringComparer.Ordinal));
    }

    /// <summary>The label to show for a value, falling back to the value itself.</summary>
    public static string LabelOf(string? value) => Find(value)?.Label ?? value ?? "";

    /// <summary>Label/value pairs for a plain sort dropdown, for the shell that has no grouped menu.</summary>
    public static (string Label, string Value)[] Choices =>
        [.. All.OrderBy(option => option.Label, StringComparer.Ordinal).Select(option => (option.Label, option.Value))];
}
