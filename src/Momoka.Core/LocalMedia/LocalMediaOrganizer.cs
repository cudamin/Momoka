namespace Momoka.LocalMedia;

public sealed record LocalMediaSource(string Path, long Length, DateTime LastWriteTimeUtc)
{
    public string RootPath { get; init; } = "";
}

public static partial class LocalMediaOrganizer
{
    /// <summary>UNC 共享或映射的网络盘。网络目录不支持硬链接，用于界面提示和保存校验。</summary>
    public static bool IsNetworkPath(string path) => LocalMediaFiles.IsNetwork(path);

    public static LocalMediaPlan Prepare(LocalMediaSource source, string targetRoot, LocalMediaMetadata metadata,
        LocalMediaLinkMode mode, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(metadata);
        ValidateMetadata(metadata);
        if (!Enum.IsDefined(mode)) throw new ArgumentException("请选择有效整理方式", nameof(mode));
        var path = LocalMediaFiles.FullPath(source.Path);
        var sourceRoot = LocalMediaFiles.FullPath(source.RootPath.Length > 0 ? source.RootPath : Path.GetDirectoryName(path)!);
        if (!Inside(path, sourceRoot)) throw new IOException("源文件不属于扫描目录");
        var rename = mode == LocalMediaLinkMode.Rename;
        targetRoot = rename ? Path.GetDirectoryName(path)! : LocalMediaFiles.FullPath(targetRoot);
        if (mode == LocalMediaLinkMode.HardLink && (LocalMediaFiles.IsNetwork(path) || LocalMediaFiles.IsNetwork(targetRoot)))
            throw new IOException("网络目录不支持硬链接，请选择软链接或直接改名");
        if (!rename && Inside(targetRoot, sourceRoot)) throw new IOException("目标目录不能位于源扫描目录内");
        using var leases = new LocalMediaFiles();
        leases.Directories(sourceRoot, create: false);
        leases.Directories(targetRoot, create: false);
        var snapshot = LocalMediaFiles.Snapshot(path, leases.Read(path), cancellationToken);
        if (snapshot.Length != source.Length || snapshot.LastWriteTimeUtc != source.LastWriteTimeUtc)
            throw new IOException("源文件在扫描后发生变化，请重新扫描");
        var title = SafeName(metadata.Title);
        var yearTitle = title + (metadata.Year is { } year ? $" ({year})" : "");
        var name = metadata.MediaType == LocalMediaType.Movie ? yearTitle
            : $"{title} - S{metadata.Season:00}E{metadata.Episode:00}";
        var directory = rename ? targetRoot : Path.Combine(targetRoot, yearTitle);
        var seriesDirectory = directory;
        if (!rename && metadata.MediaType == LocalMediaType.Episode) directory = Path.Combine(directory, $"Season {metadata.Season:00}");
        if (!rename && Inside(directory, sourceRoot)) throw new IOException("实际目标作品目录不能位于源扫描目录内");
        var target = Path.Combine(directory, name + Path.GetExtension(path));
        var sidecars = new List<LocalMediaSidecar>();
        var snapshots = new List<LocalMediaSnapshot> { snapshot };
        var sourceStem = Path.GetFileNameWithoutExtension(path);
        foreach (var subtitle in Directory.EnumerateFiles(Path.GetDirectoryName(path)!).Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!SubtitleExtensions.Contains(Path.GetExtension(subtitle))) continue;
            var subtitleStem = Path.GetFileNameWithoutExtension(subtitle);
            if (!subtitleStem.Equals(sourceStem, StringComparison.OrdinalIgnoreCase) &&
                !subtitleStem.StartsWith(sourceStem + ".", StringComparison.OrdinalIgnoreCase)) continue;
            var suffix = subtitleStem[sourceStem.Length..] + Path.GetExtension(subtitle);
            sidecars.Add(new(Path.Combine(directory, name + suffix), LocalMediaOutputKind.SubtitleLink, subtitle));
            snapshots.Add(LocalMediaFiles.Snapshot(subtitle, leases.Read(subtitle), cancellationToken));
        }
        sidecars.Add(new(Path.Combine(directory, name + ".nfo"), metadata.MediaType == LocalMediaType.Movie
            ? LocalMediaOutputKind.MovieNfo : LocalMediaOutputKind.EpisodeNfo));
        if (!rename && metadata.MediaType == LocalMediaType.Episode)
            sidecars.Add(new(Path.Combine(seriesDirectory, "tvshow.nfo"), LocalMediaOutputKind.SeriesNfo));
        var imageDirectory = !rename && metadata.MediaType == LocalMediaType.Episode ? seriesDirectory : directory;
        if (metadata.PosterUrl.Length > 0) sidecars.Add(new(Path.Combine(imageDirectory, (rename ? name + "-poster" : "poster") + ImageExtension(metadata.PosterUrl)), LocalMediaOutputKind.Poster, ImageUrl: metadata.PosterUrl));
        if (metadata.BackdropUrl.Length > 0) sidecars.Add(new(Path.Combine(imageDirectory, (rename ? name + "-fanart" : "fanart") + ImageExtension(metadata.BackdropUrl)), LocalMediaOutputKind.Backdrop, ImageUrl: metadata.BackdropUrl));
        if (metadata.MediaType == LocalMediaType.Episode && metadata.EpisodeImageUrl.Length > 0)
            sidecars.Add(new(Path.Combine(directory, name + "-thumb" + ImageExtension(metadata.EpisodeImageUrl)), LocalMediaOutputKind.EpisodeImage, ImageUrl: metadata.EpisodeImageUrl));
        var plan = new LocalMediaPlan(source with { Path = path, RootPath = sourceRoot }, targetRoot, target, metadata, mode,
            Array.AsReadOnly(sidecars.ToArray()))
        { Snapshots = Array.AsReadOnly(snapshots.ToArray()) };
        cancellationToken.ThrowIfCancellationRequested();
        RequireTargets(plan, leases);
        return plan;
    }

    public static IReadOnlyList<LocalMediaSource> Scan(string root, bool recursive, CancellationToken cancellationToken)
    {
        var files = new List<LocalMediaSource>();
        var pending = new Stack<string>();
        root = LocalMediaFiles.FullPath(root);
        using var leases = new LocalMediaFiles();
        leases.Directories(root, create: false);
        LocalMediaFiles.RequirePlain(root, directory: true);
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            leases.Directories(directory, create: false);
            foreach (var path in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (recursive) pending.Push(path);
                    continue;
                }
                if (!VideoExtensions.Contains(Path.GetExtension(path))) continue;
                var file = new FileInfo(path);
                files.Add(new(path, file.Length, file.LastWriteTimeUtc) { RootPath = root });
            }
        }
        return Array.AsReadOnly(files.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".m4v", ".ts", ".m2ts", ".webm", ".mpg", ".mpeg", ".flv" };
    private static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".srt", ".ass", ".ssa", ".vtt", ".sub", ".idx", ".sup" };

    private static bool Inside(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string ImageExtension(string url)
    {
        var extension = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? Path.GetExtension(uri.AbsolutePath).ToLowerInvariant() : "";
        return extension is ".png" or ".webp" ? extension : ".jpg";
    }

    private static string SafeName(string text)
    {
        var invalid = "<>:\"/\\|?*";
        var name = string.Concat(text.Trim().Select(character => char.IsControl(character) || invalid.Contains(character) ? '_' : character)).TrimEnd(' ', '.');
        if (name.Length > 100) name = name[..100].TrimEnd(' ', '.');
        if (name.Length == 0) throw new ArgumentException("作品名称不能为空");
        var first = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        if (first is "CON" or "PRN" or "AUX" or "NUL" or "CLOCK$" or "CONIN$" or "CONOUT$" ||
            first.Length == 4 && (first.StartsWith("COM", StringComparison.Ordinal) || first.StartsWith("LPT", StringComparison.Ordinal)) &&
            "123456789¹²³".Contains(first[3])) name = "_" + name;
        return name;
    }

    private static void ValidateMetadata(LocalMediaMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.Title) || string.IsNullOrWhiteSpace(metadata.MediaSource) || string.IsNullOrWhiteSpace(metadata.MediaId))
            throw new ArgumentException("缺少作品名称、数据来源或可信编号，不能整理");
        if (!Enum.IsDefined(metadata.MediaType) || metadata.Year is < 1 or > 9999)
            throw new ArgumentException("作品类型或年份无效");
        if (metadata.MediaType == LocalMediaType.Episode && (metadata.Season is null or < 0 or > 999 || metadata.Episode is null or < 1 or > 9999))
            throw new ArgumentException("单集必须有明确季号和单一集号，特别篇可使用第0季");
    }
}
