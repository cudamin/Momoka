namespace Momoka.Emby;

/// <summary>
/// One on/off entry in the filter panel — the thing a tick in the panel actually writes.
/// <para>
/// Every <see cref="Key"/> and <see cref="Value"/> here is transcribed from the server's own OpenAPI
/// document (<c>/emby/openapi.json</c>, which 4.10 serves without a token) or from the web client's
/// filter template, not from Jellyfin's documentation. That matters more than it sounds: Emby ignores a
/// query parameter it does not recognise, so a filter with a misspelled or borrowed key does not fail —
/// it silently returns the unfiltered library, which looks like a bug in the panel.
/// </para>
/// </summary>
public sealed record EmbyFilterOption
{
    /// <summary>
    /// Stable identity, used in the selection set and written to settings.json. Deliberately not the
    /// <see cref="Value"/>: two options can share a key with opposite values (高清 and 标清 are both
    /// <c>IsHD</c>), and a stored selection has to survive a relabelling.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>The query parameter name.</summary>
    public required string Key { get; init; }

    /// <summary>The value to send for that parameter.</summary>
    public required string Value { get; init; }

    /// <summary>
    /// Options this one turns off when it is turned on. 已播放 and 未播放 are not a contradiction the
    /// server rejects — it answers with nothing at all, which reads as an empty library.
    /// </summary>
    public IReadOnlyList<string> Excludes { get; init; } = [];
}

/// <summary>
/// One choice inside a <see cref="EmbyFilterField"/>: 全部, 是, 否, 已播放, 连载中 — the rows a select in
/// the panel offers.
/// </summary>
public sealed record FilterChoice
{
    /// <summary>What the select shows.</summary>
    public required string Label { get; init; }

    /// <summary>
    /// The option this choice turns on; null for 全部, which turns the whole field off.
    /// </summary>
    public string? OptionId { get; init; }

    /// <summary>
    /// What the toolbar's chip says once this choice is on. 是/否 read fine inside a labelled select and
    /// terribly on a chip, so a yes/no field says 有预告片 / 无预告片 there.
    /// </summary>
    public string? ChipLabel { get; init; }

    /// <summary>Presets this choice is hidden for; see <see cref="EmbyFilterField.ChoicesFor"/>.</summary>
    public IReadOnlyList<string> NotFor { get; init; } = [];
}

/// <summary>How a field is drawn.</summary>
public enum FilterKind
{
    /// <summary>A dropdown of mutually exclusive choices — 播放状态, 预告片, 已锁定.</summary>
    Select,

    /// <summary>A switch — 收藏, 重制, and the three 缺少元数据 rows (where on means missing).</summary>
    Switch,

    /// <summary>A dropdown of checkboxes whose values come from the server — 类型, 家长评分, 工作室, 年份.</summary>
    Values
}

/// <summary>
/// One row of the filter panel, in the shape Emby's own filter dialog draws it: a label with a control
/// under it, grouped into 功能 / 元数据 / 缺少元数据.
/// <para>
/// The structure and the order are transcribed from <c>modules/filtermenu/filtermenu.template.html</c>
/// and the per-type visibility rules from <c>getVisibleFilters</c> in
/// <c>modules/tabbedview/listcontroller.js</c> (both 4.10.1.0). That is what makes the 电视剧-节目 page's
/// panel read 播放状态 · 收藏 · 重制 · 类型 · 家长评分 · 工作室 · 年份 · 状态 / 功能 ×4 / 元数据 ×2 /
/// 缺少元数据 ×3 with no 媒体信息 group at all: a Series row has no streams of its own, so Emby offers
/// it none of the codec, container or subtitle rows.
/// </para>
/// </summary>
public sealed record EmbyFilterField
{
    /// <summary>Stable identity, used by the panel to tell one row from another.</summary>
    public required string Id { get; init; }

    /// <summary>Chinese label, matching Emby's own zh-CN wording.</summary>
    public required string Label { get; init; }

    public required FilterKind Kind { get; init; }

    /// <summary>
    /// The heading this row sits under — 功能, 元数据, 缺少元数据 — or null for the rows above every
    /// heading (播放状态 through 状态).
    /// </summary>
    public string? Group { get; init; }

    /// <summary>The rows a <see cref="FilterKind.Select"/> offers.</summary>
    public IReadOnlyList<FilterChoice> Choices { get; init; } = [];

    /// <summary>The <see cref="EmbyFilterList.Key"/> a <see cref="FilterKind.Values"/> row reads.</summary>
    public string? ListKey { get; init; }

    /// <summary>Presets this row is hidden for.</summary>
    public IReadOnlyList<string> NotFor { get; init; } = [];

    /// <summary>When non-empty, the only presets this row is offered for.</summary>
    public IReadOnlyList<string> OnlyFor { get; init; } = [];

    /// <summary>Lowest server version that understands this row's key, or null for all of them.</summary>
    public string? MinServerVersion { get; init; }

    /// <summary>
    /// Whether to offer this row at all. Both unknowns fail open, for the same reasons as
    /// <see cref="EmbySortOption.AppliesTo"/>: an unrecognised preset is a mixed folder or a search
    /// result, and an unknown version means the probe has not answered yet.
    /// </summary>
    public bool AppliesTo(string? preset, Version? serverVersion)
    {
        if (MinServerVersion is { } minimum
            && serverVersion is not null
            && Version.TryParse(minimum, out var required)
            && serverVersion < required)
            return false;

        if (preset is null) return OnlyFor.Count == 0;

        if (OnlyFor.Count > 0 && !OnlyFor.Contains(preset)) return false;

        return !NotFor.Contains(preset);
    }

    /// <summary>
    /// The choices to draw for a preset. Only 播放状态 varies: 可继续播放 asks for a resume position,
    /// and a series, a box set, an album or a photo has none of its own.
    /// </summary>
    public IReadOnlyList<FilterChoice> ChoicesFor(string? preset) =>
        preset is null ? Choices : [.. Choices.Where(choice => !choice.NotFor.Contains(preset))];
}

/// <summary>
/// A filter section whose choices come from the server rather than from this file: which genres,
/// studios, parental ratings, tags and years the rows on screen actually have.
/// <para>
/// <see cref="Key"/> doubles as the endpoint name — <c>/Genres</c>, <c>/Studios</c>,
/// <c>/OfficialRatings</c>, <c>/Tags</c> and <c>/Years</c> each take the whole of <c>/Items</c>'s query
/// and answer with the distinct values inside it, so one method fetches all five and the panel stays a
/// loop.
/// </para>
/// </summary>
public sealed record EmbyFilterList
{
    public required string Label { get; init; }

    public required string Key { get; init; }
}

/// <summary>
/// The filter catalogue. <see cref="Fields"/> is the panel, in order; <see cref="All"/> is every
/// option those fields can turn on, and is what a stored selection is read back against.
/// <para>
/// Five things Emby's web client offers are deliberately absent, each for a reason worth keeping:
/// </para>
/// <list type="bullet">
///   <item>
///     喜欢 / 不喜欢 (<c>Filters=Likes,Dislikes</c>) — nothing in this client can set a thumb rating, and
///     a filter for a state the user cannot reach is a filter that always comes back empty.
///   </item>
///   <item>
///     缺失剧集 (<c>IsMissing</c>) — Jellyfin has it, Emby 4.10 does not. It is not in the server's
///     parameter list, so it would be silently dropped.
///   </item>
///   <item>
///     容器 / 视频编码 / 音频编码 / 音频布局 / 音频语言 / 字幕编码 / 字幕语言 — the parameters exist, but
///     the lists they filter on come from endpoints this client does not call yet. A select with no
///     options is worse than no select.
///   </item>
///   <item>
///     分辨率 (<c>Resolution</c>: 4K/1080p/720p/SD) — same endpoint problem, and 画质 below covers the
///     one distinction the server can answer without a list.
///   </item>
///   <item>
///     频道映射状态 — live TV only; no grid here lists channels.
///   </item>
/// </list>
/// </summary>
public static class EmbyFilterBy
{
    /// <summary>Section headings, in the order they appear.</summary>
    public static class Groups
    {
        public const string Media = "媒体信息";
        public const string Features = "功能";
        public const string Metadata = "元数据";
        public const string Missing = "缺少元数据";
    }

    /// <summary>The query parameter names this file uses, so a typo is a compile error once rather than a silent no-op.</summary>
    public static class Keys
    {
        public const string Filters = "Filters";
        public const string Genres = "Genres";
        public const string Tags = "Tags";
        public const string Years = "Years";
        public const string Studios = "Studios";
        public const string OfficialRatings = "OfficialRatings";
        public const string VideoTypes = "VideoTypes";
        public const string SeriesStatus = "SeriesStatus";
        public const string HasSubtitles = "HasSubtitles";
        public const string HasTrailer = "HasTrailer";
        public const string HasSpecialFeature = "HasSpecialFeature";
        public const string HasThemeSong = "HasThemeSong";
        public const string HasThemeVideo = "HasThemeVideo";
        public const string HasOverview = "HasOverview";
        public const string HasImdbId = "HasImdbId";
        public const string HasTvdbId = "HasTvdbId";
        public const string HasTmdbId = "HasTmdbId";
        public const string IsLocked = "IsLocked";
        public const string IsDuplicate = "IsDuplicate";
        public const string IsHD = "IsHD";
        public const string Is3D = "Is3D";
    }

    /// <summary>
    /// How several values for one key are joined, or null when the key takes exactly one.
    /// <para>
    /// Emby is not consistent about this and the difference is not cosmetic: a pipe sent where a comma
    /// belongs is read as part of the value, so <c>Genres=动画,科幻</c> looks for one genre literally
    /// named 「动画,科幻」 and finds nothing. The split below is quoted from the parameter descriptions in
    /// the server's own OpenAPI document — 「pipe delimeted」 for the ones that name things, 「comma
    /// delimeted」 for the ones that name enum members.
    /// </para>
    /// </summary>
    public static string? Delimiter(string key) => key switch
    {
        Keys.Genres or Keys.Tags or Keys.OfficialRatings or Keys.Studios => "|",
        Keys.Filters or Keys.Years or Keys.VideoTypes or Keys.SeriesStatus => ",",
        _ => null
    };

    // ---------------------------------------------------------------- 选项目录

    /// <summary>
    /// Every option a field can turn on. Kept as a flat list because this is what
    /// <see cref="ItemFilters.Toggles"/> stores ids from and what <see cref="ItemFilters.Contributions"/>
    /// turns back into query parameters.
    /// </summary>
    public static readonly IReadOnlyList<EmbyFilterOption> All =
    [
        new() { Id = "played", Key = Keys.Filters, Value = "IsPlayed", Excludes = ["unplayed"] },
        new() { Id = "unplayed", Key = Keys.Filters, Value = "IsUnplayed", Excludes = ["played"] },
        new() { Id = "resumable", Key = Keys.Filters, Value = "IsResumable" },
        new() { Id = "favorite", Key = Keys.Filters, Value = "IsFavorite" },
        new() { Id = "duplicate", Key = Keys.IsDuplicate, Value = "true" },
        new() { Id = "hd", Key = Keys.IsHD, Value = "true", Excludes = ["sd"] },
        new() { Id = "sd", Key = Keys.IsHD, Value = "false", Excludes = ["hd"] },
        new() { Id = "3d", Key = Keys.Is3D, Value = "true" },
        new() { Id = "subtitles", Key = Keys.HasSubtitles, Value = "true", Excludes = ["noSubtitles"] },
        new() { Id = "noSubtitles", Key = Keys.HasSubtitles, Value = "false", Excludes = ["subtitles"] },
        new() { Id = "trailer", Key = Keys.HasTrailer, Value = "true", Excludes = ["noTrailer"] },
        new() { Id = "noTrailer", Key = Keys.HasTrailer, Value = "false", Excludes = ["trailer"] },
        new() { Id = "special", Key = Keys.HasSpecialFeature, Value = "true", Excludes = ["noSpecial"] },
        new() { Id = "noSpecial", Key = Keys.HasSpecialFeature, Value = "false", Excludes = ["special"] },
        new() { Id = "themeSong", Key = Keys.HasThemeSong, Value = "true", Excludes = ["noThemeSong"] },
        new() { Id = "noThemeSong", Key = Keys.HasThemeSong, Value = "false", Excludes = ["themeSong"] },
        new() { Id = "themeVideo", Key = Keys.HasThemeVideo, Value = "true", Excludes = ["noThemeVideo"] },
        new() { Id = "noThemeVideo", Key = Keys.HasThemeVideo, Value = "false", Excludes = ["themeVideo"] },
        new() { Id = "locked", Key = Keys.IsLocked, Value = "true", Excludes = ["unlocked"] },
        new() { Id = "unlocked", Key = Keys.IsLocked, Value = "false", Excludes = ["locked"] },
        new() { Id = "overview", Key = Keys.HasOverview, Value = "true", Excludes = ["noOverview"] },
        new() { Id = "noOverview", Key = Keys.HasOverview, Value = "false", Excludes = ["overview"] },
        // 缺少元数据: the web client inverts these (`data-invert="true"`), so a switch that is ON sends
        // false. 缺少 IMDb Id on means HasImdbId=false, and that is what these ids say.
        new() { Id = "missingImdbId", Key = Keys.HasImdbId, Value = "false" },
        new() { Id = "missingTvdbId", Key = Keys.HasTvdbId, Value = "false" },
        new() { Id = "missingTmdbId", Key = Keys.HasTmdbId, Value = "false" },
        new() { Id = "continuing", Key = Keys.SeriesStatus, Value = "Continuing" },
        new() { Id = "ended", Key = Keys.SeriesStatus, Value = "Ended" }
    ];

    /// <summary>
    /// The sections whose choices are read off the server, in panel order. 类型 first because it is the
    /// one anybody uses; 年份 last because it is the longest.
    /// </summary>
    public static readonly IReadOnlyList<EmbyFilterList> Lists =
    [
        new() { Label = "类型", Key = Keys.Genres },
        new() { Label = "家长评分", Key = Keys.OfficialRatings },
        new() { Label = "工作室", Key = Keys.Studios },
        new() { Label = "标签", Key = Keys.Tags },
        new() { Label = "年份", Key = Keys.Years }
    ];

    // Presets that have no media streams of their own. Emby offers them none of the codec, container or
    // subtitle rows, and 媒体信息 therefore disappears whole — which is exactly what the 电视剧-节目
    // page's panel looks like.
    private static readonly string[] NoStreams =
    [
        EmbySortPreset.Series, EmbySortPreset.Season, EmbySortPreset.BoxSet,
        EmbySortPreset.Album, EmbySortPreset.Song, EmbySortPreset.Photo
    ];

    // Presets that can carry a trailer, an extra, a theme song or a theme video.
    private static readonly string[] FeaturePresets =
    [
        EmbySortPreset.Movie, EmbySortPreset.Series, EmbySortPreset.Season, EmbySortPreset.Episode,
        EmbySortPreset.BoxSet, EmbySortPreset.MusicVideo, EmbySortPreset.Video
    ];

    // Presets that carry a lock flag and an overview.
    private static readonly string[] MetadataPresets =
    [
        EmbySortPreset.Movie, EmbySortPreset.Series, EmbySortPreset.Season, EmbySortPreset.Episode,
        EmbySortPreset.BoxSet, EmbySortPreset.Trailer, EmbySortPreset.Song, EmbySortPreset.Album,
        EmbySortPreset.MusicVideo, EmbySortPreset.Video, EmbySortPreset.Photo
    ];

    /// <summary>
    /// The panel, in order. Every row is one of <see cref="FilterKind"/>; the headings come from
    /// <see cref="Groups"/> and a row with a null <see cref="EmbyFilterField.Group"/> sits above all of
    /// them, which is how the web client draws 播放状态 through 状态 with no heading at all.
    /// </summary>
    public static readonly IReadOnlyList<EmbyFilterField> Fields =
    [
        new()
        {
            Id = "playstate",
            Label = "播放状态",
            Kind = FilterKind.Select,
            Choices =
            [
                new() { Label = "全部" },
                new() { Label = "已播放", OptionId = "played", ChipLabel = "已播放" },
                new() { Label = "未播放", OptionId = "unplayed", ChipLabel = "未播放" },
                // A resume position belongs to a file. A series has none of its own, and asking for one
                // returns nothing however many part-watched episodes it contains.
                new()
                {
                    Label = "可继续播放",
                    OptionId = "resumable",
                    ChipLabel = "可继续播放",
                    NotFor = [EmbySortPreset.Series, EmbySortPreset.BoxSet, EmbySortPreset.Album, EmbySortPreset.Photo]
                }
            ]
        },
        new()
        {
            Id = "favorite",
            Label = "收藏",
            Kind = FilterKind.Switch,
            Choices = [new() { Label = "收藏", OptionId = "favorite", ChipLabel = "收藏" }]
        },
        new()
        {
            Id = "duplicate",
            Label = "重制",
            Kind = FilterKind.Switch,
            MinServerVersion = "4.9.0.42",
            OnlyFor = [EmbySortPreset.Movie, EmbySortPreset.Series, EmbySortPreset.Episode, EmbySortPreset.Video],
            Choices = [new() { Label = "重制", OptionId = "duplicate", ChipLabel = "重制" }]
        },
        new()
        {
            Id = "genre",
            Label = "类型",
            Kind = FilterKind.Values,
            ListKey = Keys.Genres
        },
        new()
        {
            Id = "officialRating",
            Label = "家长评分",
            Kind = FilterKind.Values,
            ListKey = Keys.OfficialRatings
        },
        new()
        {
            Id = "studio",
            Label = "工作室",
            Kind = FilterKind.Values,
            ListKey = Keys.Studios
        },
        new()
        {
            Id = "tag",
            Label = "标签",
            Kind = FilterKind.Values,
            ListKey = Keys.Tags,
            // Emby's field map for a Series row has no tags to offer, and the web panel shows no 标签
            // row on the 节目 page at all.
            NotFor = [EmbySortPreset.Series, EmbySortPreset.Season, EmbySortPreset.Episode]
        },
        new()
        {
            Id = "year",
            Label = "年份",
            Kind = FilterKind.Values,
            ListKey = Keys.Years
        },
        new()
        {
            Id = "seriesStatus",
            Label = "状态",
            Kind = FilterKind.Select,
            OnlyFor = [EmbySortPreset.Series],
            Choices =
            [
                new() { Label = "全部" },
                new() { Label = "连载中", OptionId = "continuing", ChipLabel = "连载中" },
                new() { Label = "已完结", OptionId = "ended", ChipLabel = "已完结" }
            ]
        },

        // ------------------------------------------------------------ 媒体信息
        new()
        {
            Id = "quality",
            Label = "画质",
            Kind = FilterKind.Select,
            Group = Groups.Media,
            NotFor = NoStreams,
            Choices =
            [
                new() { Label = "全部" },
                new() { Label = "高清", OptionId = "hd", ChipLabel = "高清" },
                new() { Label = "标清", OptionId = "sd", ChipLabel = "标清" }
            ]
        },
        new()
        {
            Id = "threeD",
            Label = "3D",
            Kind = FilterKind.Switch,
            Group = Groups.Media,
            NotFor = NoStreams,
            Choices = [new() { Label = "3D", OptionId = "3d", ChipLabel = "3D" }]
        },
        new()
        {
            Id = "subtitles",
            Label = "字幕",
            Kind = FilterKind.Select,
            Group = Groups.Media,
            NotFor = NoStreams,
            Choices =
            [
                new() { Label = "全部" },
                new() { Label = "有", OptionId = "subtitles", ChipLabel = "有字幕" },
                new() { Label = "无", OptionId = "noSubtitles", ChipLabel = "无字幕" }
            ]
        },

        // ------------------------------------------------------------ 功能
        new()
        {
            Id = "trailer",
            Label = "预告片",
            Kind = FilterKind.Select,
            Group = Groups.Features,
            OnlyFor = FeaturePresets,
            Choices =
            [
                new() { Label = "全部" },
                new() { Label = "有", OptionId = "trailer", ChipLabel = "有预告片" },
                new() { Label = "无", OptionId = "noTrailer", ChipLabel = "无预告片" }
            ]
        },
        new()
        {
            Id = "special",
            Label = "附加篇",
            Kind = FilterKind.Select,
            Group = Groups.Features,
            OnlyFor = FeaturePresets,
            Choices =
            [
                new() { Label = "全部" },
                new() { Label = "有", OptionId = "special", ChipLabel = "有附加篇" },
                new() { Label = "无", OptionId = "noSpecial", ChipLabel = "无附加篇" }
            ]
        },
        new()
        {
            Id = "themeSong",
            Label = "主题曲",
            Kind = FilterKind.Select,
            Group = Groups.Features,
            OnlyFor = FeaturePresets,
            Choices =
            [
                new() { Label = "全部" },
                new() { Label = "有", OptionId = "themeSong", ChipLabel = "有主题曲" },
                new() { Label = "无", OptionId = "noThemeSong", ChipLabel = "无主题曲" }
            ]
        },
        new()
        {
            Id = "themeVideo",
            Label = "主题视频",
            Kind = FilterKind.Select,
            Group = Groups.Features,
            OnlyFor = FeaturePresets,
            Choices =
            [
                new() { Label = "全部" },
                new() { Label = "有", OptionId = "themeVideo", ChipLabel = "有主题视频" },
                new() { Label = "无", OptionId = "noThemeVideo", ChipLabel = "无主题视频" }
            ]
        },

        // ------------------------------------------------------------ 元数据
        new()
        {
            Id = "locked",
            Label = "已锁定",
            Kind = FilterKind.Select,
            Group = Groups.Metadata,
            OnlyFor = MetadataPresets,
            Choices =
            [
                new() { Label = "全部" },
                new() { Label = "是", OptionId = "locked", ChipLabel = "已锁定" },
                new() { Label = "否", OptionId = "unlocked", ChipLabel = "未锁定" }
            ]
        },
        new()
        {
            Id = "overview",
            Label = "概要",
            Kind = FilterKind.Select,
            Group = Groups.Metadata,
            OnlyFor = MetadataPresets,
            Choices =
            [
                new() { Label = "全部" },
                new() { Label = "有", OptionId = "overview", ChipLabel = "有概要" },
                new() { Label = "无", OptionId = "noOverview", ChipLabel = "无概要" }
            ]
        },

        // ------------------------------------------------------------ 缺少元数据
        new()
        {
            Id = "missingImdb",
            Label = "IMDb Id",
            Kind = FilterKind.Switch,
            Group = Groups.Missing,
            OnlyFor = [EmbySortPreset.Movie, EmbySortPreset.Series, EmbySortPreset.Season, EmbySortPreset.Episode,
                EmbySortPreset.MusicVideo, EmbySortPreset.Video, EmbySortPreset.Trailer, EmbySortPreset.BoxSet],
            Choices = [new() { Label = "IMDb Id", OptionId = "missingImdbId", ChipLabel = "缺少 IMDb Id" }]
        },
        new()
        {
            Id = "missingTvdb",
            Label = "Tvdb Id",
            Kind = FilterKind.Switch,
            Group = Groups.Missing,
            OnlyFor = [EmbySortPreset.Movie, EmbySortPreset.Series, EmbySortPreset.Season, EmbySortPreset.Episode,
                EmbySortPreset.MusicVideo, EmbySortPreset.Video, EmbySortPreset.Trailer],
            Choices = [new() { Label = "Tvdb Id", OptionId = "missingTvdbId", ChipLabel = "缺少 Tvdb Id" }]
        },
        new()
        {
            Id = "missingTmdb",
            Label = "MovieDb Id",
            Kind = FilterKind.Switch,
            Group = Groups.Missing,
            OnlyFor = [EmbySortPreset.Movie, EmbySortPreset.Series, EmbySortPreset.Season, EmbySortPreset.Episode,
                EmbySortPreset.MusicVideo, EmbySortPreset.Video, EmbySortPreset.Trailer],
            Choices = [new() { Label = "MovieDb Id", OptionId = "missingTmdbId", ChipLabel = "缺少 MovieDb Id" }]
        }
    ];

    /// <summary>The rows a grid should offer, in panel order.</summary>
    public static IReadOnlyList<EmbyFilterField> For(string? preset, Version? serverVersion = null) =>
        [.. Fields.Where(field => field.AppliesTo(preset, serverVersion))];

    /// <summary>The catalogue entry for a stored id, or null if it is no longer offered.</summary>
    public static EmbyFilterOption? Find(string? id) =>
        id is null ? null : All.FirstOrDefault(option => option.Id == id);

    /// <summary>The field that owns a stored id, or null when the id is unknown to this build.</summary>
    public static EmbyFilterField? FieldOf(string? id) => id is null
        ? null
        : Fields.FirstOrDefault(field => field.Choices.Any(choice => choice.OptionId == id));

    /// <summary>
    /// The chip text for a stored id — 未播放, 有预告片, 缺少 IMDb Id — falling back to the id itself.
    /// </summary>
    public static string LabelOf(string? id)
    {
        if (id is null) return "";

        foreach (var field in Fields)
            foreach (var choice in field.Choices)
                if (choice.OptionId == id)
                    return choice.ChipLabel ?? choice.Label;

        return id;
    }

    /// <summary>
    /// 当前生效的每一条筛选，一条一格 —— 工具栏底下那一行可以逐个点掉的筛选条。
    /// <para>
    /// 顺序和面板一致：先按 <see cref="Fields"/> 的顺序走一遍开关与下拉，再走四张按值多选的表。
    /// 存的顺序是点的顺序，一行会跟着上次怎么点而重排，所以这里不读它。认不出来的开关 id 不出格，
    /// 和那个数字一致：那个数也不数它们，一格点不掉又说不出名字的筛选条比不出更糟。
    /// </para>
    /// </summary>
    public static IReadOnlyList<FilterChip> Chips(ItemFilters filters)
    {
        ArgumentNullException.ThrowIfNull(filters);

        var chips = new List<FilterChip>(filters.Count);

        foreach (var field in Fields)
        {
            foreach (var choice in field.Choices)
            {
                if (choice.OptionId is { } id && filters.Has(id))
                    chips.Add(new FilterChip { Label = choice.ChipLabel ?? choice.Label, Key = "", Value = id });
            }

            if (field.Kind != FilterKind.Values || field.ListKey is not { } listKey) continue;

            foreach (var value in filters.Values(listKey))
                chips.Add(new FilterChip { Label = $"{field.Label}：{value}", Key = listKey, Value = value });
        }

        return chips;
    }
}

/// <summary>
/// 一格筛选条：屏上那句话，加上「点掉它」要动的那一处。见 <see cref="EmbyFilterBy.Chips"/>。
/// </summary>
public sealed record FilterChip
{
    /// <summary>屏上那句话：开关是它自己的标签（「未播放」），列表是「类型：动画」。</summary>
    public required string Label { get; init; }

    /// <summary>
    /// 空的时候这一格是个开关，<see cref="Value"/> 是它的 <see cref="EmbyFilterOption.Id"/>；否则是
    /// <see cref="EmbyFilterBy.Lists"/> 里那个 key，<see cref="Value"/> 是那张列表里的一个值。
    /// </summary>
    public required string Key { get; init; }

    /// <inheritdoc cref="Key"/>
    public required string Value { get; init; }

    /// <summary>把这一格从选择里去掉。改的是传进来那一份，和面板上打勾去勾走的是同一条路。</summary>
    public void Remove(ItemFilters filters)
    {
        ArgumentNullException.ThrowIfNull(filters);

        if (Key.Length == 0) filters.Toggles.Remove(Value);
        else filters.Values(Key).Remove(Value);
    }
}
