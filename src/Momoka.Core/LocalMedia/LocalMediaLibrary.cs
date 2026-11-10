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
                return new([new(plan.TargetPath, LocalMediaOutputKind.VideoLink, false, "此媒体库已尝试整理该文件，请先核对原结果，未重复写入")], false);
            _attempts[key] = new(libraryId, plan.Source.Path, plan.TargetPath, true, "整理已提交，完成状态待核对", DateTimeOffset.UtcNow);
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
            if (result.Cancelled && result.Items.Count == 0) _attempts.Remove(key);
            else
            {
                var video = result.Items.Any(item => item.Kind == LocalMediaOutputKind.VideoLink && item.Success &&
                    PathEquals(item.Path, plan.TargetPath)) && File.Exists(plan.TargetPath);
                var nfoKind = plan.Metadata.MediaType == LocalMediaType.Movie ? LocalMediaOutputKind.MovieNfo : LocalMediaOutputKind.EpisodeNfo;
                var nfo = plan.Sidecars.Single(item => item.Kind == nfoKind);
                var inLibrary = video && result.Items.Any(item => item.Kind == nfoKind && item.Success && PathEquals(item.Path, nfo.Path)) && File.Exists(nfo.Path);
                if (video)
                    _entries[key] = new(key, libraryId, plan.Source, plan.TargetPath, WithoutRemoteUrls(plan.Metadata), plan.Mode,
                        Array.AsReadOnly(result.Items.Select(item => item with { Message = SafeMessage(item.Message) }).ToArray()),
                        DateTimeOffset.UtcNow, inLibrary, inLibrary && result.Success);
                _attempts[key] = _attempts[key] with
                {
                    Message = result.Success ? "" : result.Cancelled ? "已取消后续整理，请核对已完成项，未自动重试"
                        : "整理部分失败或未完成，请核对目标后手动处理，未自动重试",
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
            _attempts[key] = new(libraryId, path, "", false, "待校正：" + SafeMessage(message), DateTimeOffset.UtcNow);
            SaveLocked();
        }
        Notify();
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
    { PosterUrl = "", BackdropUrl = "", EpisodeImageUrl = "" };
    internal static string SafeMessage(string message) => Regex.Replace(message, @"https?://[^\s<>""']+", "[图片或服务地址]", RegexOptions.IgnoreCase);
    private static bool PathEquals(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static string Key(string libraryId, string path) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(libraryId + "\n" + path.ToUpperInvariant())));

    private sealed class Document
    {
        public int Version { get; set; } = 1;
        public List<LocalMediaLibraryEntry> Entries { get; set; } = [];
        public List<Attempt> Attempts { get; set; } = [];
    }
    private sealed record Attempt(string LibraryId, string SourcePath, string TargetPath, bool WriteAttempted, string Message, DateTimeOffset UpdatedAt);
}
