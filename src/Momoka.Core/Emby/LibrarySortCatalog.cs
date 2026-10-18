namespace Momoka.Emby;

public static class LibrarySortCatalog
{
    public const string EpisodeOrder = "SeriesSortName,ParentIndexNumber,IndexNumber,SortName";
    public const string Filename = "IsFolder,Filename";

    private static readonly IReadOnlyList<EmbySortOption> FolderOnly =
    [
        new() { Label = "上次添加内容日期", Value = "DateLastContentAdded,SortName", DescendingByDefault = true },
        new() { Label = "专辑", Value = "Album,ParentIndexNumber,IndexNumber" },
        new() { Label = "专辑艺人", Value = "AlbumArtist,Album,ParentIndexNumber,IndexNumber,SortName" },
        new() { Label = "作曲家", Value = "Composer,Album,ParentIndexNumber,IndexNumber,SortName" },
        new() { Label = "艺人", Value = "Artist,Album,ParentIndexNumber,IndexNumber,SortName" }
    ];

    private static readonly IReadOnlyList<EmbySortOption> MediaSorts =
    [
        new() { Label = "帧率", Value = "Framerate,SortName", DescendingByDefault = true },
        new() { Label = "视频编解码器", Value = "VideoCodec,SortName" }
    ];

    public static IReadOnlyList<EmbySortOption> For(string? collectionType, LibrarySection section,
        Version? serverVersion = null)
    {
        var preset = LibraryBrowse.SortPreset(collectionType, section);
        // 混合内容库的「电影和节目」在网页端给的是文件夹那一整份菜单（专辑、艺人、编号都在），
        // 不是电影那份十九条；两者都有的键在两张截图里的叫法也不同。
        // 类型／播出平台只在没下钻时是索引页；下钻之后装的是这一库的条目，菜单要跟着这一库走。
        // 索引页本身不显示排序，所以把这两个分区一起按内容页算不会多出一颗键。
        var folderLike = section == LibrarySection.Folders
            || (collectionType == "mixed" && section is
                LibrarySection.Items or LibrarySection.Genres or LibrarySection.Studios);
        // 电影库的「影片」「预告片」「收藏」与类型下钻共用网页端电影那份菜单：电影本来就有流，
        // 所以帧率与视频编解码器也在里面。
        var movieLike = collectionType == "movies" && section is
            LibrarySection.Items or LibrarySection.Trailers or LibrarySection.Favorites
                or LibrarySection.Genres or LibrarySection.Studios;
        var episode = section == LibrarySection.Episodes;

        var options = EmbySortBy.For(preset, serverVersion)
            .Where(option => !episode || option.Value != EmbySortBy.Number)
            .Select(option => option with { Label = Label(option, folderLike || episode) });
        if (folderLike) options = options.Concat(FolderOnly);
        if (folderLike || episode || movieLike) options = options.Concat(MediaSorts);
        return [.. options.OrderBy(option => option.Label, StringComparer.Ordinal)];
    }

    public static EmbySortOption Default(string? collectionType, LibrarySection section) =>
        section is LibrarySection.Episodes or LibrarySection.Folders
            ? For(collectionType, section).First(option => option.Value ==
                (section == LibrarySection.Episodes ? EpisodeOrder : Filename))
            // 网页端电影库的「影片」与「收藏」都开在加入日期降序；「预告片」开在标题升序。
            // 混合内容库没有电影或电视剧预设，标题升序就是 null 预设下的默认。
            : collectionType == "movies" && section is LibrarySection.Items or LibrarySection.Favorites
                ? EmbySortBy.Find(EmbySortBy.DateAdded) ?? EmbySortBy.Default(EmbySortPreset.Movie)
                : EmbySortBy.Default(LibraryBrowse.SortPreset(collectionType, section));

    private static string Label(EmbySortOption option, bool communityRating) => option.Value switch
    {
        EpisodeOrder => "剧集",
        Filename => "文件名",
        EmbySortBy.Number => "编号",
        EmbySortBy.Rating when communityRating => "社区评分",
        "Resolution,SortName" => "分辨率",
        "TotalBitrate,SortName" => "比特率",
        "Size,SortName" => "文件尺寸",
        _ => option.Label
    };
}
