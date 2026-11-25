namespace Momoka.LocalMedia;

public sealed record LocalMediaLibraryEntry(
    string Id,
    string LibraryId,
    LocalMediaSource Source,
    // 名字沿用旧索引的 TargetPath；原地入库后它就是源视频路径。
    string TargetPath,
    LocalMediaMetadata Metadata,
    IReadOnlyList<LocalMediaItemResult> Outputs,
    DateTimeOffset AddedAt,
    bool IsInLibrary,
    bool Complete)
{
    /// <summary>本机观看状态：详情页的已看按钮直接落在这条索引上，不向任何服务器上报。</summary>
    public bool Watched { get; init; }

    /// <summary>本机收藏状态：详情页的收藏按钮直接落在这条索引上。</summary>
    public bool Favorite { get; init; }
}

public sealed record LocalMediaLibraryIssue(string LibraryId, string SourcePath, string Message, bool WriteAttempted, DateTimeOffset UpdatedAt);

/// <summary>从实际写入回执里取这一条已完成的图片路径；失败或没有写的图种返回空串。</summary>
public static class LocalMediaEntryArtwork
{
    public static string PathFor(LocalMediaLibraryEntry entry, LocalMediaOutputKind kind) => entry.Outputs
        .Where(output => output.Success && output.Kind == kind)
        .Select(output => output.Path)
        .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path)) ?? "";

    public static string PosterPath(LocalMediaLibraryEntry entry) => PathFor(entry, LocalMediaOutputKind.Poster);

    public static string BackdropPath(LocalMediaLibraryEntry entry) => PathFor(entry, LocalMediaOutputKind.Backdrop);

    public static string ThumbPath(LocalMediaLibraryEntry entry) => PathFor(entry, LocalMediaOutputKind.EpisodeImage);

    /// <summary>
    /// 同一部作品的条目归到一张卡上：类型 + 数据来源 + 来源编号。缺来源编号时不合并，一条文件一张卡。
    /// </summary>
    public static string GroupKey(LocalMediaLibraryEntry entry)
    {
        var metadata = entry.Metadata;
        if (string.IsNullOrWhiteSpace(metadata.MediaSource) || string.IsNullOrWhiteSpace(metadata.MediaId)) return entry.Id;
        return $"{metadata.MediaType}|{metadata.MediaSource}|{metadata.MediaId}";
    }
}

public sealed record LocalMediaMonitorOptions(string LibraryId, string SourceRoot, string MetadataRoot, string LibraryName)
{
    public bool Recursive { get; init; } = true;
    public bool IncludeExisting { get; init; } = true;
    public string ProviderKey { get; init; } = "";
    public TimeSpan SettleTime { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan RescanInterval { get; init; } = TimeSpan.FromSeconds(30);
}

public sealed record LocalMediaMonitorStatus(bool Running, bool Busy, int PendingCount, string Message, string CurrentPath = "");
