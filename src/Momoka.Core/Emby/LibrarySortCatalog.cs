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
        if (section is not (LibrarySection.Episodes or LibrarySection.Folders))
            return EmbySortBy.For(preset, serverVersion);

        var options = EmbySortBy.For(preset, serverVersion)
            .Where(option => section != LibrarySection.Episodes || option.Value != EmbySortBy.Number)
            .Select(option => option with { Label = Label(option) });
        if (section == LibrarySection.Folders) options = options.Concat(FolderOnly);
        return [.. options.Concat(MediaSorts).OrderBy(option => option.Label, StringComparer.Ordinal)];
    }

    public static EmbySortOption Default(string? collectionType, LibrarySection section) =>
        section is LibrarySection.Episodes or LibrarySection.Folders
            ? For(collectionType, section).First(option => option.Value ==
                (section == LibrarySection.Episodes ? EpisodeOrder : Filename))
            // 网页端电影库的「影片」开在加入日期降序；混合内容库没有电影或电视剧预设，标题升序就是
            // null 预设下的默认，两张网页截图里的排序键正是这两条。
            : collectionType == "movies" && section == LibrarySection.Items
                ? EmbySortBy.Find(EmbySortBy.DateAdded) ?? EmbySortBy.Default(EmbySortPreset.Movie)
                : EmbySortBy.Default(LibraryBrowse.SortPreset(collectionType, section));

    private static string Label(EmbySortOption option) => option.Value switch
    {
        EpisodeOrder => "剧集",
        Filename => "文件名",
        EmbySortBy.Number => "编号",
        EmbySortBy.Rating => "社区评分",
        "Resolution,SortName" => "分辨率",
        "TotalBitrate,SortName" => "比特率",
        "Size,SortName" => "文件尺寸",
        _ => option.Label
    };
}
