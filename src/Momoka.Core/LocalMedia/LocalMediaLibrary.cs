using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Momoka.Infrastructure;

namespace Momoka.LocalMedia;

/// <summary>只从实际整理回执登记媒体；写入前落盘的防重记录不因重启或重新识别而解除。</summary>
public sealed class LocalMediaLibrary
{
    private readonly object _gate = new();
    private readonly Dictionary<string, LocalMediaLibraryEntry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Attempt> _attempts = new(StringComparer.Ordinal);
    // 已在库里、只是缺人物头像的条目：补写失败不升级成「待校正」，那一条本来就能播。
    private readonly HashSet<string> _upgradeOnly = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public LocalMediaLibrary(string filePath)
    {
        FilePath = LocalMediaFiles.FullPath(filePath);
        using var files = new LocalMediaFiles();
        files.Directories(Path.GetDirectoryName(FilePath)!, create: false);
        if (!LocalMediaFiles.Exists(FilePath)) return;
        using var stream = files.Read(FilePath);
        var document = JsonSerializer.Deserialize<Document>(stream, JsonOptions)
            ?? throw new IOException("本地媒体库索引为空，未清空历史防重记录");
        if (document.Version != 1) throw new IOException("本地媒体库索引版本不受支持，未清空历史防重记录");
        foreach (var entry in document.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.LibraryId) || entry.Id != Key(entry.LibraryId, entry.Source.Path))
                throw new IOException("本地媒体库索引身份无效，未清空历史防重记录");
            _entries.Add(entry.Id, entry with { Outputs = Array.AsReadOnly(entry.Outputs.ToArray()) });
        }
        foreach (var attempt in document.Attempts) _attempts.Add(Key(attempt.LibraryId, attempt.SourcePath), attempt);
    }

    public string FilePath { get; }
    public IReadOnlyList<LocalMediaLibraryEntry> Entries
    {
        get { lock (_gate) return Array.AsReadOnly(_entries.Values.OrderByDescending(entry => entry.AddedAt).ToArray()); }
    }
    public IReadOnlyList<LocalMediaLibraryIssue> Issues
    {
        get
        {
            lock (_gate) return Array.AsReadOnly(_attempts.Values.Where(attempt => attempt.Message.Length > 0)
                .Select(attempt => new LocalMediaLibraryIssue(attempt.LibraryId, attempt.SourcePath, attempt.Message, attempt.WriteAttempted, attempt.UpdatedAt)).ToArray());
        }
    }
    public event EventHandler? Changed;

    public async Task<LocalMediaResult> ExecuteAsync(string libraryId, LocalMediaPlan plan,
        Func<string, CancellationToken, Task<byte[]>> imageFetch, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryId);
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        var key = Key(libraryId, plan.Source.Path);
        lock (_gate)
        {
            if (WasAttemptedLocked(libraryId, plan.Source.Path, allowRecognitionRetry: true))
                return new([new(plan.VideoPath, LocalMediaOutputKind.Video, false, "此媒体库已尝试入库该文件，请先核对原结果，未重复写入")], false);
            _attempts[key] = new(libraryId, plan.Source.Path, plan.VideoPath, true, "入库已提交，完成状态待核对", DateTimeOffset.UtcNow);
            SaveLocked();
        }
        LocalMediaResult result;
        try { result = await LocalMediaOrganizer.ExecuteAsync(plan, imageFetch, cancellationToken).ConfigureAwait(false); }
        catch (Exception error)
        {
            lock (_gate)
            {
                _attempts[key] = _attempts[key] with { Message = "整理结果不明，请核对目标后手动处理。" + SafeMessage(error.Message) };
                SaveLocked();
            }
            Notify();
            throw;
        }
        lock (_gate)
        {
            _upgradeOnly.Remove(key);
            if (result.Cancelled && result.Items.Count == 0) _attempts.Remove(key);
            else
            {
                var video = result.Items.Any(item => item.Kind == LocalMediaOutputKind.Video && item.Success &&
                    PathEquals(item.Path, plan.VideoPath)) && File.Exists(plan.VideoPath);
                var nfoKind = plan.Metadata.MediaType == LocalMediaType.Movie ? LocalMediaOutputKind.MovieNfo : LocalMediaOutputKind.EpisodeNfo;
                var nfo = plan.Sidecars.Single(item => item.Kind == nfoKind);
                var inLibrary = video && result.Items.Any(item => item.Kind == nfoKind && item.Success && PathEquals(item.Path, nfo.Path)) && File.Exists(nfo.Path);
                if (video)
                {
                    // 重试补写会用这一轮的新元数据重建条目；已看与收藏是用户状态，必须跟着走。
                    _entries.TryGetValue(key, out var prior);
                    _entries[key] = new(key, libraryId, plan.Source, plan.VideoPath, WithoutRemoteUrls(plan.Metadata),
                        Array.AsReadOnly(result.Items.Select(item => item with { Message = SafeMessage(item.Message) }).ToArray()),
                        DateTimeOffset.UtcNow, inLibrary, inLibrary && result.Success)
                    {
                        Watched = prior?.Watched ?? false,
                        Favorite = prior?.Favorite ?? false
                    };
                }
                _attempts[key] = _attempts[key] with
                {
                    Message = result.Success ? "" : result.Cancelled ? "已取消后续入库，请核对已完成项，未自动重试"
                        : "入库部分失败或未完成，请核对目标后手动处理，未自动重试",
                    UpdatedAt = DateTimeOffset.UtcNow
                };
            }
            SaveLocked();
        }
        Notify();
        return result;
    }

    internal bool WasAttempted(string libraryId, string path)
    { lock (_gate) return WasAttemptedLocked(libraryId, path, allowRecognitionRetry: false); }

    private bool WasAttemptedLocked(string libraryId, string path, bool allowRecognitionRetry) =>
        _attempts.Values.Any(attempt => attempt.LibraryId == libraryId && (!allowRecognitionRetry || attempt.WriteAttempted) &&
            (PathEquals(attempt.SourcePath, path) || attempt.TargetPath.Length > 0 && PathEquals(attempt.TargetPath, path)));

    internal void RecognitionFailed(string libraryId, string path, string message)
    {
        lock (_gate)
        {
            var key = Key(libraryId, path);
            if (_attempts.TryGetValue(key, out var prior) && prior.WriteAttempted) return;
            var upgrade = _upgradeOnly.Remove(key);
            _attempts[key] = new(libraryId, path, "", false,
                upgrade ? "" : "待校正：" + SafeMessage(message), DateTimeOffset.UtcNow);
            SaveLocked();
        }
        Notify();
    }

    /// <summary>
    /// 上一版取图会把 TMDB 的 WebP 当成后缀不符拒写。这一版启动时自动放开这类条目一次，
    /// 让它们补上海报与背景图；其他失败仍留给用户按「重试未完成项」，不在后台反复打网络。
    /// </summary>
    internal void RetryBrokenArtwork(string libraryId)
    {
        lock (_gate)
        {
            var broken = _entries.Values
                .Where(entry => entry.LibraryId == libraryId && !entry.Complete && entry.Outputs.Any(output =>
                    !output.Success && output.Kind is LocalMediaOutputKind.Poster or LocalMediaOutputKind.Backdrop or LocalMediaOutputKind.EpisodeImage
                    && output.Message.Contains("实际格式与预览目标后缀不一致", StringComparison.Ordinal)))
                .Select(entry => entry.Source.Path)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (broken.Count == 0) return;
            var keys = _attempts
                .Where(pair => pair.Value.LibraryId == libraryId && broken.Contains(pair.Value.SourcePath))
                .Select(pair => pair.Key)
                .ToArray();
            if (keys.Length == 0) return;
            foreach (var key in keys) _attempts.Remove(key);
            SaveLocked();
        }
        Notify();
    }

    /// <summary>
    /// 上一版只存了姓名和角色、没有 TMDB 人物编号与头像。启动时把这些条目放开一次，
    /// 让监控重新识别一遍并把头像补进元数据目录；补过之后人物带上编号，不会再放开。
    /// 只在 TMDB 识别方式下调用 —— MoviePilot 拿不到编号，放开也没用。
    /// </summary>
    internal void RetryMissingPeople(string libraryId)
    {
        lock (_gate)
        {
            var outdated = _entries.Values
                .Where(entry => entry.LibraryId == libraryId
                    && entry.Metadata.MediaSource is "themoviedb" or "tmdb"
                    && entry.Metadata.People.Any(person => person.Id.Length == 0))
                .Select(entry => entry.Source.Path)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (outdated.Count == 0) return;
            foreach (var path in outdated) _upgradeOnly.Add(Key(libraryId, path));
            var keys = _attempts
                .Where(pair => pair.Value.LibraryId == libraryId && outdated.Contains(pair.Value.SourcePath))
                .Select(pair => pair.Key)
                .ToArray();
            if (keys.Length == 0) return;
            foreach (var key in keys) _attempts.Remove(key);
            SaveLocked();
        }
        Notify();
    }

    /// <summary>
    /// 本机已看/收藏：按作品分组一起改，剧集里每一集都跟着走。只动本地索引，不碰任何服务器状态。
    /// </summary>
    internal bool SetUserState(string libraryId, string groupKey, bool? watched, bool? favorite)
    {
        if (string.IsNullOrWhiteSpace(libraryId) || string.IsNullOrWhiteSpace(groupKey)) return false;
        var updated = false;
        lock (_gate)
        {
            foreach (var pair in _entries.Where(pair => pair.Value.LibraryId == libraryId
                && string.Equals(LocalMediaEntryArtwork.GroupKey(pair.Value), groupKey, StringComparison.Ordinal)).ToArray())
            {
                var entry = pair.Value;
                _entries[pair.Key] = entry with
                {
                    Watched = watched ?? entry.Watched,
                    Favorite = favorite ?? entry.Favorite
                };
                updated = true;
            }
            if (updated) SaveLocked();
        }
        if (updated) Notify();
        return updated;
    }

    internal void RetryRecognition(string libraryId)
    {
        lock (_gate)
        {
            var keys = _attempts.Where(pair => pair.Value.LibraryId == libraryId && !pair.Value.WriteAttempted).Select(pair => pair.Key).ToArray();
            if (keys.Length == 0) return;
            foreach (var key in keys) _attempts.Remove(key);
            SaveLocked();
        }
        Notify();
    }

    /// <summary>
    /// 用户明确要求重试「已入库但附属文件未完成」的条目：只放开这些条目的防重记录，让下一轮扫描
    /// 重新识别并补写缺失的海报、剧照或 NFO；已经写好的同作品 NFO 由组织器核对身份后复用，不覆盖。
    /// </summary>
    internal void RetryIncomplete(string libraryId)
    {
        lock (_gate)
        {
            var incomplete = _entries.Values
                .Where(entry => entry.LibraryId == libraryId && !entry.Complete)
                .Select(entry => entry.Source.Path)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (incomplete.Count == 0) return;
            var keys = _attempts
                .Where(pair => pair.Value.LibraryId == libraryId && incomplete.Contains(pair.Value.SourcePath))
                .Select(pair => pair.Key)
                .ToArray();
            if (keys.Length == 0) return;
            foreach (var key in keys) _attempts.Remove(key);
            SaveLocked();
        }
        Notify();
    }

    private void SaveLocked()
    {
        using var files = new LocalMediaFiles();
        files.Directories(Path.GetDirectoryName(FilePath)!, create: true);
        if (LocalMediaFiles.Exists(FilePath)) LocalMediaFiles.RequirePlain(FilePath, directory: false);
        var document = new Document { Entries = _entries.Values.ToList(), Attempts = _attempts.Values.ToList() };
        files.Recheck();
        AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(document, JsonOptions), new UTF8Encoding(false));
    }

    private void Notify()
    {
        if (Changed is not { } handlers) return;
        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try { handler(this, EventArgs.Empty); }
            catch (Exception) { /* Presentation failure cannot replay a write. */ }
        }
    }

    private static LocalMediaMetadata WithoutRemoteUrls(LocalMediaMetadata metadata) => metadata with
    {
        PosterUrl = "",
        BackdropUrl = "",
        EpisodeImageUrl = "",
        People = metadata.People.Select(person => person with { ImageUrl = "" }).ToArray()
    };
    internal static string SafeMessage(string message) => Regex.Replace(message, @"https?://[^\s<>""']+", "[图片或服务地址]", RegexOptions.IgnoreCase);
    private static bool PathEquals(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static string Key(string libraryId, string path) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(libraryId + "\n" + path.ToUpperInvariant())));

    private sealed class Document
    {
        public int Version { get; set; } = 1;
        public List<LocalMediaLibraryEntry> Entries { get; set; } = [];
        public List<Attempt> Attempts { get; set; } = [];
    }
    // 字段名沿用旧索引的 TargetPath，读回旧文件的 Attempt 时不会得到 null；它现在记录的是原地入库的视频路径。
    private sealed record Attempt(string LibraryId, string SourcePath, string TargetPath, bool WriteAttempted, string Message, DateTimeOffset UpdatedAt);
}
