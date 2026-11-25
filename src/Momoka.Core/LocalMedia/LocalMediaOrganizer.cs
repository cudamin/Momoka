namespace Momoka.LocalMedia;

public sealed record LocalMediaSource(string Path, long Length, DateTime LastWriteTimeUtc)
{
    public string RootPath { get; init; } = "";
}

public static partial class LocalMediaOrganizer
{
    /// <summary>UNC 共享或映射的网络盘。网络目录可以入库，只是扫描与读取速度取决于共享。</summary>
    public static bool IsNetworkPath(string path) => LocalMediaFiles.IsNetwork(path);

    /// <summary>
    /// 直接把源文件登记为库内容：视频不移动、不改名、不建链接，只把 NFO、海报与剧照写进
    /// <paramref name="metadataRoot"/> 下按媒体库名称分开的目录。
    /// </summary>
    public static LocalMediaPlan Prepare(LocalMediaSource source, string metadataRoot, string libraryName,
        LocalMediaMetadata metadata, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataRoot);
        ValidateMetadata(metadata);
        var path = LocalMediaFiles.FullPath(source.Path);
        var sourceRoot = LocalMediaFiles.FullPath(source.RootPath.Length > 0 ? source.RootPath : Path.GetDirectoryName(path)!);
        if (!Inside(path, sourceRoot)) throw new IOException("源文件不属于扫描目录");
        using var leases = new LocalMediaFiles();
        leases.Directories(sourceRoot, create: false);
        var snapshot = LocalMediaFiles.Snapshot(path, leases.Read(path), cancellationToken);
        if (snapshot.Length != source.Length || snapshot.LastWriteTimeUtc != source.LastWriteTimeUtc)
            throw new IOException("源文件在扫描后发生变化，请重新扫描");

        var library = Path.Combine(LocalMediaFiles.FullPath(metadataRoot), SafeName(libraryName));
        var title = SafeName(metadata.Title);
        var yearTitle = title + (metadata.Year is { } year ? $" ({year})" : "");
        var name = metadata.MediaType == LocalMediaType.Movie ? yearTitle
            : $"{title} - S{metadata.Season:00}E{metadata.Episode:00}";
        var seriesDirectory = Path.Combine(library, yearTitle);
        var directory = metadata.MediaType == LocalMediaType.Episode
            ? Path.Combine(seriesDirectory, $"Season {metadata.Season:00}") : seriesDirectory;
        var sidecars = new List<LocalMediaSidecar>
        {
            new(Path.Combine(directory, name + ".nfo"),
                metadata.MediaType == LocalMediaType.Movie ? LocalMediaOutputKind.MovieNfo : LocalMediaOutputKind.EpisodeNfo)
        };
        if (metadata.MediaType == LocalMediaType.Episode)
            sidecars.Add(new(Path.Combine(seriesDirectory, "tvshow.nfo"), LocalMediaOutputKind.SeriesNfo));
        if (metadata.PosterUrl.Length > 0)
            sidecars.Add(new(Path.Combine(seriesDirectory, "poster" + ImageExtension(metadata.PosterUrl)),
                LocalMediaOutputKind.Poster, metadata.PosterUrl));
        if (metadata.BackdropUrl.Length > 0)
            sidecars.Add(new(Path.Combine(seriesDirectory, "fanart" + ImageExtension(metadata.BackdropUrl)),
                LocalMediaOutputKind.Backdrop, metadata.BackdropUrl));
        if (metadata.MediaType == LocalMediaType.Episode && metadata.EpisodeImageUrl.Length > 0)
            sidecars.Add(new(Path.Combine(directory, name + "-thumb" + ImageExtension(metadata.EpisodeImageUrl)),
                LocalMediaOutputKind.EpisodeImage, metadata.EpisodeImageUrl));
        // 演职人员头像按 TMDB 人物编号落在作品目录的 people 子目录：同一部剧的每一集共用同一批文件，
        // 头像缺席只是少一张脸，不让整条入库变成「未完成」。
        var people = new List<LocalMediaPerson>(metadata.People.Length);
        foreach (var person in metadata.People)
        {
            var imagePath = "";
            if (person.Id.Length > 0 && person.ImageUrl.Length > 0)
            {
                imagePath = Path.Combine(seriesDirectory, "people", SafeName(person.Id) + ImageExtension(person.ImageUrl));
                if (!sidecars.Any(item => string.Equals(item.Path, imagePath, StringComparison.OrdinalIgnoreCase)))
                    sidecars.Add(new(imagePath, LocalMediaOutputKind.PersonImage, person.ImageUrl, Optional: true));
            }
            people.Add(person with { ImagePath = imagePath });
        }
        var planned = people.Count == 0 ? metadata : metadata with { People = people.ToArray() };
        var plan = new LocalMediaPlan(source with { Path = path, RootPath = sourceRoot }, path, planned,
            Array.AsReadOnly(sidecars.ToArray()))
        { Snapshots = Array.AsReadOnly(new[] { snapshot }) };
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
            throw new ArgumentException("缺少作品名称、数据来源或可信编号，不能入库");
        if (!Enum.IsDefined(metadata.MediaType) || metadata.Year is < 1 or > 9999)
            throw new ArgumentException("作品类型或年份无效");
        if (metadata.MediaType == LocalMediaType.Episode && (metadata.Season is null or < 0 or > 999 || metadata.Episode is null or < 1 or > 9999))
            throw new ArgumentException("单集必须有明确季号和单一集号，特别篇可使用第0季");
    }
}
