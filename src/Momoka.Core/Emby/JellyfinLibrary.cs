namespace Momoka.Emby;

/// <summary>
/// The library browser as Jellyfin Web 10.10 draws it: tabs, sort menus, filter dialog, layout menu and
/// empty-state wording.
/// <para>
/// This is deliberately not a variant of <see cref="LibraryBrowse"/> / <see cref="LibrarySortCatalog"/> /
/// <see cref="EmbyFilterBy"/> switched on with a few conditions. Jellyfin's own lists differ in more than
/// labels — its movie tab has Collections and no Folders, its series tab has no Favorites, its filter
/// dialog is a set of independent checkboxes rather than Emby's selects, and its page defaults sort by
/// name instead of by date added. Keeping the two catalogues apart also keeps Emby's screenshots and
/// options exactly where they were.
/// </para>
/// <para>
/// Transcribed from the 10.10.z sources: <c>controllers/movies/movies.js</c>,
/// <c>controllers/shows/tvshows.js</c>, <c>controllers/shows/episodes.js</c>,
/// <c>controllers/movies/movietrailers.js</c>, <c>controllers/movies/moviecollections.js</c>,
/// <c>components/filterdialog/filterdialog.js</c> and its template, plus
/// <c>strings/zh-CN.json</c> for every label.
/// </para>
/// </summary>
public static class JellyfinLibrary
{
    // ---------------------------------------------------------------- 库分类

    /// <summary>
    /// Jellyfin Web's library tabs, in its own order. Movies have Collections and no Folders; series have
    /// no Favorites and no Folders.
    /// <para>
    /// Mixed-content libraries have no tabs at all. Jellyfin stores them with no collection type, and its
    /// web client routes that root to <c>controllers/list.html</c> — the same single list page a physical
    /// folder gets (see <c>controllers/list.js</c> and the sidebar link without a <c>collectionType</c>
    /// parameter). A "mixed" entry here would invent three tabs the web client never draws.
    /// </para>
    /// </summary>
    public static IReadOnlyList<LibrarySectionEntry> Sections(string? collectionType) => collectionType switch
    {
        "movies" =>
        [
            new(LibrarySection.Items, "电影"),
            new(LibrarySection.Recommended, "建议"),
            new(LibrarySection.Trailers, "预告片"),
            new(LibrarySection.Favorites, "我的最爱"),
            new(LibrarySection.Collections, "合集"),
            new(LibrarySection.Genres, "类型")
        ],
        "tvshows" =>
        [
            new(LibrarySection.Items, "节目"),
            new(LibrarySection.Recommended, "建议"),
            new(LibrarySection.Upcoming, "即将播出"),
            new(LibrarySection.Genres, "类型"),
            new(LibrarySection.Studios, "制片发行商"),
            new(LibrarySection.Episodes, "剧集")
        ],
        _ => []
    };

    // ---------------------------------------------------------------- 排序

    private static readonly IReadOnlyList<EmbySortOption> MovieSorts =
    [
        Option("名称", "SortName,ProductionYear"),
        Option("随机", "Random"),
        Option("公众评分", "CommunityRating,SortName,ProductionYear", descending: true),
        Option("影评人评分", "CriticRating,SortName,ProductionYear", descending: true),
        Option("加入日期", "DateCreated,SortName,ProductionYear", descending: true),
        Option("播放日期", "DatePlayed,SortName,ProductionYear", descending: true),
        Option("家长分级", "OfficialRating,SortName,ProductionYear", descending: true),
        Option("播放次数", "PlayCount,SortName,ProductionYear", descending: true),
        Option("发行日期", "PremiereDate,SortName,ProductionYear", descending: true),
        Option("播放时长", "Runtime,SortName,ProductionYear")
    ];

    private static readonly IReadOnlyList<EmbySortOption> SeriesSorts =
    [
        Option("名称", "SortName"),
        Option("随机", "Random"),
        Option("公众评分", "CommunityRating,SortName", descending: true),
        Option("节目添加日期", "DateCreated,SortName", descending: true),
        Option("剧集添加日期", "DateLastContentAdded,SortName", descending: true),
        Option("播放日期", "SeriesDatePlayed,SortName", descending: true),
        Option("家长分级", "OfficialRating,SortName", descending: true),
        Option("发行日期", "PremiereDate,SortName", descending: true)
    ];

    private static readonly IReadOnlyList<EmbySortOption> EpisodeSorts =
    [
        Option("名称", "SeriesSortName,SortName"),
        Option("TVDB 评分", "CommunityRating,SeriesSortName,SortName", descending: true),
        Option("加入日期", "DateCreated,SeriesSortName,SortName", descending: true),
        Option("首映日期", "PremiereDate,SeriesSortName,SortName", descending: true),
        Option("播放日期", "DatePlayed,SeriesSortName,SortName", descending: true),
        Option("家长分级", "OfficialRating,SeriesSortName,SortName", descending: true),
        Option("播放次数", "PlayCount,SeriesSortName,SortName", descending: true),
        Option("播放时长", "Runtime,SeriesSortName,SortName")
    ];

    private static readonly IReadOnlyList<EmbySortOption> TrailerSorts =
    [
        Option("名称", "SortName"),
        Option("公众评分", "CommunityRating,SortName", descending: true),
        Option("加入日期", "DateCreated,SortName", descending: true),
        Option("播放日期", "DatePlayed,SortName", descending: true),
        Option("家长分级", "OfficialRating,SortName", descending: true),
        Option("播放次数", "PlayCount,SortName", descending: true),
        Option("发行日期", "PremiereDate,SortName", descending: true)
    ];

    private static readonly IReadOnlyList<EmbySortOption> CollectionSorts =
    [
        Option("名称", "SortName"),
        Option("公众评分", "CommunityRating,SortName", descending: true),
        Option("加入日期", "DateCreated,SortName", descending: true),
        Option("家长分级", "OfficialRating,SortName", descending: true),
        Option("发行日期", "PremiereDate,SortName", descending: true)
    ];

    /// <summary>
    /// The generic list page's sort menu, in <c>controllers/list.js</c>'s own push order
    /// (<c>getSortMenuOptions</c>): name, ratings, dates, then 文件夹, rating, play count, release and
    /// runtime. 文件夹 is sixth here, not second — this is the list page's menu, not the movie page's.
    /// The page opens on <c>IsFolder,SortName</c> because <c>getDefaultSortBy</c> prepends it when
    /// <c>params.type</c> is absent.
    /// </summary>
    private static readonly IReadOnlyList<EmbySortOption> GenericSorts =
    [
        Option("名称", "SortName"),
        Option("公众评分", "CommunityRating,SortName", descending: true),
        Option("影评人评分", "CriticRating,SortName", descending: true),
        Option("加入日期", "DateCreated,SortName", descending: true),
        Option("播放日期", "DatePlayed,SortName", descending: true),
        Option("文件夹", "IsFolder,SortName"),
        Option("家长分级", "OfficialRating,SortName", descending: true),
        Option("播放次数", "PlayCount,SortName", descending: true),
        Option("发行日期", "ProductionYear,PremiereDate,SortName", descending: true),
        Option("播放时长", "Runtime,SortName")
    ];

    /// <summary>
    /// The sort menu for one tab. Menu order is Jellyfin's own order, not an alphabetical sort of the
    /// labels — the web client pushes these entries in a fixed sequence.
    /// </summary>
    public static IReadOnlyList<EmbySortOption> SortOptions(string? collectionType, LibrarySection section) => section switch
    {
        LibrarySection.Episodes => EpisodeSorts,
        LibrarySection.Trailers => TrailerSorts,
        LibrarySection.Collections => CollectionSorts,
        LibrarySection.Items or LibrarySection.Favorites when collectionType == "movies" => MovieSorts,
        LibrarySection.Items or LibrarySection.Favorites when collectionType == "tvshows" => SeriesSorts,
        LibrarySection.Items or LibrarySection.Favorites or LibrarySection.Folders => GenericSorts,
        _ => []
    };

    /// <summary>The sort each Jellyfin tab opens on; see the query defaults in the controllers above.</summary>
    public static EmbySortOption DefaultSort(string? collectionType, LibrarySection section)
    {
        var options = SortOptions(collectionType, section);
        var value = section switch
        {
            LibrarySection.Episodes => "SeriesSortName,SortName",
            LibrarySection.Trailers or LibrarySection.Collections => "SortName",
            LibrarySection.Items or LibrarySection.Favorites when collectionType == "movies" => "SortName,ProductionYear",
            LibrarySection.Items or LibrarySection.Favorites when collectionType == "tvshows" => "SortName",
            LibrarySection.Items or LibrarySection.Folders => "IsFolder,SortName",
            _ => "SortName"
        };
        return options.FirstOrDefault(option => option.Value == value) ?? options.FirstOrDefault() ?? Option("名称", "SortName");
    }

    /// <summary>
    /// Whether the sort in force is a name ordering, which is what the letter bar needs. A date sort that
    /// happens to use <c>SortName</c> as a tie-breaker is not a name sort.
    /// </summary>
    public static bool IsNameSort(string? sort) => sort is "SortName" or "SortName,ProductionYear"
        or "SeriesSortName,SortName" or "IsFolder,SortName";

    private static EmbySortOption Option(string label, string value, bool descending = false) =>
        new() { Label = label, Value = value, DescendingByDefault = descending };

    // ---------------------------------------------------------------- 筛选

    private static readonly EmbyFilterField StatusField = new()
    {
        Id = "jellyfinStatus",
        Label = "播放状态",
        Kind = FilterKind.Checks,
        Choices =
        [
            new() { Label = "已播放", OptionId = "played", ChipLabel = "已播放" },
            new() { Label = "未播放", OptionId = "unplayed", ChipLabel = "未播放" },
            new() { Label = "可恢复播放", OptionId = "resumable", ChipLabel = "可恢复播放" },
            new() { Label = "我的最爱", OptionId = "favorite", ChipLabel = "我的最爱" }
        ]
    };

    private static readonly EmbyFilterField SeriesStatusField = new()
    {
        Id = "jellyfinSeriesStatus",
        Label = "状态",
        Kind = FilterKind.Checks,
        Choices =
        [
            new() { Label = "连载中", OptionId = "continuing", ChipLabel = "连载中" },
            new() { Label = "完结", OptionId = "ended", ChipLabel = "完结" },
            new() { Label = "暂未发布", OptionId = "unreleased", ChipLabel = "暂未发布" }
        ]
    };

    private static readonly EmbyFilterField EpisodeField = new()
    {
        Id = "jellyfinEpisodes",
        Label = "剧集",
        Kind = FilterKind.Checks,
        Choices =
        [
            new() { Label = "特集", OptionId = "specialEpisode", ChipLabel = "特集" },
            new() { Label = "缺少的剧集", OptionId = "missingEpisode", ChipLabel = "缺少的剧集" },
            new() { Label = "未播出的剧集", OptionId = "futureEpisode", ChipLabel = "未播出的剧集" }
        ]
    };

    private static readonly EmbyFilterField FeaturesField = new()
    {
        Id = "jellyfinFeatures",
        Label = "功能",
        Kind = FilterKind.Checks,
        Choices =
        [
            new() { Label = "字幕", OptionId = "subtitles", ChipLabel = "字幕" },
            new() { Label = "预告片", OptionId = "trailer", ChipLabel = "预告片" },
            new() { Label = "花絮", OptionId = "special", ChipLabel = "花絮" },
            new() { Label = "主题曲", OptionId = "themeSong", ChipLabel = "主题曲" },
            new() { Label = "主题视频", OptionId = "themeVideo", ChipLabel = "主题视频" }
        ]
    };

    private static readonly EmbyFilterField VideoTypesField = new()
    {
        Id = "jellyfinVideoTypes",
        Label = "视频类型",
        Kind = FilterKind.Checks,
        Choices =
        [
            new() { Label = "蓝光", OptionId = "bluray", ChipLabel = "蓝光" },
            new() { Label = "DVD", OptionId = "dvd", ChipLabel = "DVD" },
            new() { Label = "HD高清", OptionId = "hd", ChipLabel = "HD高清" },
            new() { Label = "4K", OptionId = "is4k", ChipLabel = "4K" },
            new() { Label = "SD标清", OptionId = "sd", ChipLabel = "SD标清" },
            new() { Label = "三维", OptionId = "3d", ChipLabel = "三维" }
        ]
    };

    // The list page's filter dialog is filtermenu.js, not the movie page's filterdialog.js. Its basic
    // section has four independent checkboxes in this order; its video row shows HD/4K/SD/3D/Blu-ray/DVD
    // (the last two are the old VideoTypes list), and its feature row calls extras 额外 rather than 花絮.

    private static readonly EmbyFilterField ListStatusField = new()
    {
        Id = "jellyfinListStatus",
        Label = "播放状态",
        Kind = FilterKind.Checks,
        Choices =
        [
            new() { Label = "未播放", OptionId = "unplayed", ChipLabel = "未播放" },
            new() { Label = "已播放", OptionId = "played", ChipLabel = "已播放" },
            new() { Label = "我的最爱", OptionId = "favorite", ChipLabel = "我的最爱" },
            new() { Label = "继续观看", OptionId = "resumable", ChipLabel = "继续观看" }
        ]
    };

    private static readonly EmbyFilterField ListVideoTypesField = new()
    {
        Id = "jellyfinListVideoTypes",
        Label = "视频类型",
        Kind = FilterKind.Checks,
        Choices =
        [
            new() { Label = "HD", OptionId = "hd", ChipLabel = "HD" },
            new() { Label = "4K", OptionId = "is4k", ChipLabel = "4K" },
            new() { Label = "SD", OptionId = "sd", ChipLabel = "SD" },
            new() { Label = "3D", OptionId = "3d", ChipLabel = "3D" },
            new() { Label = "Blu-ray", OptionId = "bluray", ChipLabel = "Blu-ray" },
            new() { Label = "DVD", OptionId = "dvd", ChipLabel = "DVD" }
        ]
    };

    private static readonly EmbyFilterField ListFeaturesField = new()
    {
        Id = "jellyfinListFeatures",
        Label = "功能",
        Kind = FilterKind.Checks,
        Choices =
        [
            new() { Label = "字幕", OptionId = "subtitles", ChipLabel = "字幕" },
            new() { Label = "预告片", OptionId = "trailer", ChipLabel = "预告片" },
            new() { Label = "额外", OptionId = "special", ChipLabel = "额外" },
            new() { Label = "主题曲", OptionId = "themeSong", ChipLabel = "主题曲" },
            new() { Label = "主题视频", OptionId = "themeVideo", ChipLabel = "主题视频" }
        ]
    };

    private static readonly EmbyFilterField GenresField = new()
    {
        Id = "jellyfinGenres",
        Label = "类型",
        Kind = FilterKind.Values,
        ListKey = EmbyFilterBy.Keys.Genres
    };

    private static readonly EmbyFilterField RatingsField = new()
    {
        Id = "jellyfinRatings",
        Label = "家长分级",
        Kind = FilterKind.Values,
        ListKey = EmbyFilterBy.Keys.OfficialRatings
    };

    private static readonly EmbyFilterField TagsField = new()
    {
        Id = "jellyfinTags",
        Label = "标签",
        Kind = FilterKind.Values,
        ListKey = EmbyFilterBy.Keys.Tags
    };

    private static readonly EmbyFilterField YearsField = new()
    {
        Id = "jellyfinYears",
        Label = "年份",
        Kind = FilterKind.Values,
        ListKey = EmbyFilterBy.Keys.Years
    };

    /// <summary>
    /// Every Jellyfin filter row, regardless of tab. Used by the catalogue self-check to prove each
    /// option has a row that can draw and clear it, since these rows are not in <see cref="EmbyFilterBy.Fields"/>.
    /// </summary>
    public static IReadOnlyList<EmbyFilterField> AllFields { get; } =
    [
        StatusField, SeriesStatusField, EpisodeField, FeaturesField, VideoTypesField,
        ListStatusField, ListVideoTypesField, ListFeaturesField,
        GenresField, RatingsField, TagsField, YearsField
    ];

    /// <summary>
    /// The filter panel for one tab, in Jellyfin's order: 筛选 (播放状态), status/episode rows when they
    /// apply, 功能, then the dynamic lists and 视频类型. The web client's rows are groups of checkboxes;
    /// <see cref="FilterKind.Checks"/> is the shape that carries them.
    /// </summary>
    public static IReadOnlyList<EmbyFilterField> FilterFields(string? collectionType, LibrarySection section)
    {
        // A Jellyfin list page is a mixed-content library root or a folder below one (list.js). Its
        // filter menu is filtermenu.js: four status checks, the server's genres, the six video types,
        // then features. It has no ratings, tags or years rows at all.
        if (collectionType is null or "mixed" or "folders")
            return section is LibrarySection.Items or LibrarySection.Favorites
                ? [ListStatusField, GenresField, ListVideoTypesField, ListFeaturesField]
                : [];

        if (!HasFilter(collectionType, section)) return [];

        List<EmbyFilterField> fields = [StatusField];
        if (section == LibrarySection.Episodes) fields.Add(EpisodeField);
        else if (collectionType == "tvshows") fields.Add(SeriesStatusField);

        fields.Add(FeaturesField);
        fields.Add(GenresField);
        fields.Add(RatingsField);
        fields.Add(TagsField);
        if (section == LibrarySection.Episodes) fields.Add(VideoTypesField);
        else if (collectionType == "movies") fields.Add(VideoTypesField);
        fields.Add(YearsField);
        return fields;
    }

    /// <summary>
    /// Which tabs have a filter button. Jellyfin's collections, index pages and upcoming calendar do not;
    /// its movie, series and episode grids do.
    /// </summary>
    public static bool HasFilter(string? collectionType, LibrarySection section) => section switch
    {
        LibrarySection.Items or LibrarySection.Favorites
            => collectionType is "movies" or "tvshows" or "mixed" or "folders" or null,
        LibrarySection.Trailers or LibrarySection.Episodes => collectionType == "movies" || collectionType == "tvshows",
        LibrarySection.Folders => collectionType is "mixed",
        _ => false
    };

    /// <summary>
    /// Every option id this panel can tick. A selection restored from settings is pruned to this set so an
    /// Emby-only id cannot sit invisibly in the badge on a Jellyfin library.
    /// </summary>
    private static readonly HashSet<string> FilterIds =
    [
        .. new[] { StatusField, SeriesStatusField, EpisodeField, FeaturesField, VideoTypesField,
                ListStatusField, ListVideoTypesField, ListFeaturesField }
            .SelectMany(field => field.Choices)
            .Select(choice => choice.OptionId)
            .Where(id => id is not null)
            .Select(id => id!)
    ];

    /// <summary>A copy of <paramref name="filters"/> with options and lists Jellyfin does not offer removed.</summary>
    public static ItemFilters Prune(ItemFilters filters) => new()
    {
        Toggles = [.. filters.Toggles.Where(FilterIds.Contains)],
        Genres = [.. filters.Genres],
        OfficialRatings = [.. filters.OfficialRatings],
        Tags = [.. filters.Tags],
        Years = [.. filters.Years]
    };

    // ---------------------------------------------------------------- 布局

    private static readonly IReadOnlyList<LibraryView> MovieViews =
        [LibraryView.Banner, LibraryView.List, LibraryView.Poster, LibraryView.PosterCard, LibraryView.Thumb, LibraryView.ThumbCard];

    /// <summary>
    /// The list page's 图片类型 select, in <c>viewSettings.template.html</c>'s option order: primary,
    /// banner, disc, logo, thumb, list. The web names them 封面图 / 横幅图 / 光盘封面图 / 徽标 / 缩略图 /
    /// 列表; <see cref="ViewLabel"/> is that same list for the view-settings menu.
    /// </summary>
    private static readonly IReadOnlyList<LibraryView> ListViews =
    [
        LibraryView.Poster, LibraryView.Banner, LibraryView.Disc,
        LibraryView.Logo, LibraryView.Thumb, LibraryView.List
    ];

    /// <summary>The label the web list page's view-settings menu puts on one image type.</summary>
    public static string ViewLabel(LibraryView view) => view switch
    {
        LibraryView.Poster => "封面图",
        LibraryView.Banner => "横幅图",
        LibraryView.Disc => "光盘封面图",
        LibraryView.Logo => "徽标",
        LibraryView.Thumb => "缩略图",
        LibraryView.List => "列表",
        _ => LibraryDisplayCatalog.LabelOf(view)
    };

    private static readonly IReadOnlyList<LibraryView> CollectionViews =
        [LibraryView.List, LibraryView.Poster, LibraryView.PosterCard, LibraryView.Thumb, LibraryView.ThumbCard];

    private static readonly IReadOnlyList<LibraryView> EpisodeViews =
        [LibraryView.List, LibraryView.Poster, LibraryView.PosterCard];

    /// <summary>
    /// The layout menu Jellyfin Web offers on this tab. The menu itself is only drawn on tabs where the
    /// web client has a 视图 button; <see cref="HasLayout"/> says which those are.
    /// </summary>
    public static IReadOnlyList<LibraryView> Views(string? collectionType, LibrarySection section) => section switch
    {
        LibrarySection.Collections => CollectionViews,
        LibrarySection.Episodes => EpisodeViews,
        LibrarySection.Items or LibrarySection.Favorites when collectionType is "movies" or "tvshows" => MovieViews,
        // A mixed-content root and a folder below it are the same list page; Jellyfin stores the root
        // without a collection type (see Sections). Its view select is the six image types above.
        LibrarySection.Items or LibrarySection.Favorites or LibrarySection.Folders
            when collectionType is null or "mixed" or "folders" => ListViews,
        _ => [LibraryView.Poster]
    };

    /// <summary>Which tabs get the 布局 button; Jellyfin's trailers and index pages have none.</summary>
    public static bool HasLayout(string? collectionType, LibrarySection section) => section switch
    {
        LibrarySection.Collections or LibrarySection.Episodes => true,
        LibrarySection.Items or LibrarySection.Favorites or LibrarySection.Folders
            => collectionType is "movies" or "tvshows" or "mixed" or "folders" or null,
        _ => false
    };

    /// <summary>
    /// The letter bar's rule: Jellyfin offers it on the movie, series, trailer and episode grids, and on
    /// the list page (list.js calls <c>initAlphaPicker</c> for every item view). The count threshold and
    /// the name-sort condition stay in the view model.
    /// </summary>
    public static bool HasAlphabetPicker(string? collectionType, LibrarySection section) => section switch
    {
        LibrarySection.Items or LibrarySection.Favorites
            => collectionType is "movies" or "tvshows" or "mixed" or "folders" or null,
        LibrarySection.Trailers or LibrarySection.Episodes => collectionType is "movies" or "tvshows",
        _ => false
    };

    /// <summary>
    /// Applies Jellyfin's own defaults to a restored display: an unsupported shape (an Emby 表格 or 光盘
    /// saved earlier) becomes a poster, and the grouping switch, which Jellyfin's API does not have, is
    /// left off. Everything the user can still choose is carried over.
    /// </summary>
    public static LibraryDisplay Fit(LibraryDisplay display, string? collectionType, LibrarySection section)
    {
        var views = Views(collectionType, section);
        return new LibraryDisplay
        {
            View = views.Contains(display.View) ? display.View : LibraryView.Poster,
            CardSize = display.CardSize,
            GroupIntoCollections = false,
            Fields = [.. display.Fields]
        };
    }

    // ---------------------------------------------------------------- 空态

    /// <summary>What Jellyfin says when a tab has nothing in it.</summary>
    public static string EmptyNotice(string? collectionType, LibrarySection section) => section switch
    {
        LibrarySection.Collections => "还没有合集。点击 + 按钮开始创建合集。",
        LibrarySection.Trailers when collectionType == "movies" =>
            "安装 Trailer channel 以通过添加一个网络预告片媒体库来增强你的电影体验。",
        LibrarySection.Recommended when collectionType == "movies" =>
            "没有可用的电影建议。开始观看你的电影并进行评分，再回过头来查看你的建议。",
        LibrarySection.Recommended => "没有可用的建议。",
        _ => "这里没有内容"
    };
}
