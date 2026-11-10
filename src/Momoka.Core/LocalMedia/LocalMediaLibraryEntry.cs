namespace Momoka.LocalMedia;

public sealed record LocalMediaLibraryEntry(
    string Id,
    string LibraryId,
    LocalMediaSource Source,
    string TargetPath,
    LocalMediaMetadata Metadata,
    LocalMediaLinkMode Mode,
    IReadOnlyList<LocalMediaItemResult> Outputs,
    DateTimeOffset AddedAt,
    bool IsInLibrary,
    bool Complete);

public sealed record LocalMediaLibraryIssue(string LibraryId, string SourcePath, string Message, bool WriteAttempted, DateTimeOffset UpdatedAt);

public sealed record LocalMediaMonitorOptions(string LibraryId, string SourceRoot, string TargetRoot, LocalMediaLinkMode Mode)
{
    public bool Recursive { get; init; } = true;
    public bool IncludeExisting { get; init; } = true;
    public string ProviderKey { get; init; } = "";
    public TimeSpan SettleTime { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan RescanInterval { get; init; } = TimeSpan.FromSeconds(30);
}

public sealed record LocalMediaMonitorStatus(bool Running, bool Busy, int PendingCount, string Message, string CurrentPath = "");
