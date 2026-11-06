using System.Globalization;
using System.Text.Json;

namespace Momoka.MoviePilot;

public sealed record MoviePilotTransferHistory
{
    public long Id { get; init; }
    internal string ConnectionStamp { get; init; } = "";
    internal JsonElement Snapshot { get; init; }
    public string Title { get; init; } = "";
    public string Type { get; init; } = "";
    public string MediaSource { get; init; } = "";
    public string MediaId { get; init; } = "";
    public string TmdbId { get; init; } = "";
    public string Image { get; init; } = "";
    public string Year { get; init; } = "";
    public long? Size { get; init; }
    public string SourceStorage { get; init; } = "";
    public string TargetStorage { get; init; } = "";
    public string Seasons { get; init; } = "";
    public string Episodes { get; init; } = "";
    public string Mode { get; init; } = "";
    public bool Success { get; init; }
    public string Date { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public string TargetPath { get; init; } = "";
    public JsonElement SourceFile { get; init; }
    public JsonElement TargetFile { get; init; }

    public JsonElement TransferFile => Success && Mode.Contains("move", StringComparison.OrdinalIgnoreCase) ? TargetFile : SourceFile;
    public string TransferPath => MoviePilotTransfer.Text(TransferFile, "path");
    public string Display => $"#{Id} · {Title} · {Seasons}{Episodes} · {(Success ? "成功" : "失败")} · {Date}";
    public bool Matches(string path) => MoviePilotTransfer.SamePath(TargetPath, path) || MoviePilotTransfer.SamePath(SourcePath, path);

    public static MoviePilotTransferHistory? Parse(JsonElement item)
    {
        if (!long.TryParse(Scalar(item, "id"), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0) return null;
        var sourceFile = Member(item, "src_fileitem");
        var targetFile = Member(item, "dest_fileitem");
        return new MoviePilotTransferHistory
        {
            Id = id,
            Snapshot = item.Clone(),
            Title = Scalar(item, "title"),
            Type = Scalar(item, "type"),
            MediaSource = Scalar(item, "media_source"),
            MediaId = Scalar(item, "media_id"),
            TmdbId = Scalar(item, "tmdbid"),
            Image = Scalar(item, "image"),
            Year = Scalar(item, "year"),
            Size = FileSize(sourceFile) ?? FileSize(targetFile) ?? ScalarSize(item, "filesize"),
            SourceStorage = Scalar(item, "src_storage") is { Length: > 0 } sourceStorage
                ? sourceStorage : MoviePilotTransfer.Text(sourceFile, "storage"),
            TargetStorage = Scalar(item, "dest_storage") is { Length: > 0 } targetStorage
                ? targetStorage : MoviePilotTransfer.Text(targetFile, "storage"),
            Seasons = Scalar(item, "seasons"),
            Episodes = Scalar(item, "episodes"),
            Mode = Scalar(item, "mode"),
            Success = MoviePilotTransfer.Bool(item, "status"),
            Date = Scalar(item, "date"),
            SourcePath = MoviePilotTransfer.Text(sourceFile, "path") is { Length: > 0 } src ? src : Scalar(item, "src"),
            TargetPath = MoviePilotTransfer.Text(targetFile, "path") is { Length: > 0 } dest ? dest : Scalar(item, "dest"),
            SourceFile = sourceFile,
            TargetFile = targetFile
        };
    }

    private static long? FileSize(JsonElement file) => ScalarSize(file, "size");

    private static long? ScalarSize(JsonElement item, string name) =>
        long.TryParse(Scalar(item, name), NumberStyles.None, CultureInfo.InvariantCulture, out var size) && size >= 0 ? size : null;

    internal static string Scalar(JsonElement item, string name)
    {
        var value = Member(item, name);
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.GetRawText(),
            _ => ""
        };
    }

    internal static JsonElement Member(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var member) ? member.Clone() : default;
}

public sealed record MoviePilotTransferHistoryPage(IReadOnlyList<MoviePilotTransferHistory> Items, int Total);

public static class MoviePilotTransferHistoryMatch
{
    /// <summary>Storage belongs to file identity; contradictory snapshots cannot authorize file operations.</summary>
    internal static string Storage(MoviePilotTransferHistory history, bool source)
    {
        var file = source ? history.SourceFile : history.TargetFile;
        var declared = source ? history.SourceStorage : history.TargetStorage;
        var embedded = MoviePilotTransfer.Text(file, "storage");
        if (declared.Length > 0 && embedded.Length > 0 && declared != embedded)
            throw new MoviePilotException("整理记录的文件存储身份互相矛盾，请先在 MoviePilot 核对");
        return declared.Length > 0 ? declared : embedded.Length > 0 ? embedded
            : file.ValueKind == JsonValueKind.Object ? "local" : "";
    }

    internal static bool SameStorage(string left, string right) =>
        left.Length == 0 || right.Length == 0 || left == right;

    public static IReadOnlyList<MoviePilotTransferHistory> Dependents(
        IEnumerable<MoviePilotTransferHistory> selected, IEnumerable<MoviePilotTransferHistory> all)
    {
        var chosen = selected.ToList();
        return all.Where(history => history.Success && history.Mode == "softlink" &&
            chosen.Any(original => original.Id != history.Id && original.TargetPath.Length > 0 &&
                SameStorage(Storage(history, source: true), Storage(original, source: false)) &&
                MoviePilotTransfer.SamePath(history.SourcePath, original.TargetPath))).ToList();
    }

    /// <summary>Emby 指向整理后的文件；目标路径匹配优先，不能误选把软链接再次当源的后续记录。</summary>
    public static IReadOnlyList<MoviePilotTransferHistory> Candidates(IEnumerable<MoviePilotTransferHistory> histories, string path)
    {
        var records = histories.Where(history => history.Matches(path)).DistinctBy(history => history.Id).ToList();
        var targets = records.Where(history => MoviePilotTransfer.SamePath(history.TargetPath, path)).ToList();
        return (targets.Count > 0 ? targets : records).OrderByDescending(history => history.Id).ToList();
    }

    public static IReadOnlyList<MoviePilotTransferHistory> RequireMatches(
        MoviePilotTransferContext context, IEnumerable<MoviePilotTransferHistory> histories)
    {
        var records = histories.ToList();
        var matched = new List<MoviePilotTransferHistory>();
        foreach (var file in context.Files)
        {
            var candidates = Candidates(records, file.Path);
            if (candidates.Count != 1)
                throw new MoviePilotException(candidates.Count == 0
                    ? $"没有找到此文件的原整理记录：{file.Path}。请先在 MoviePilot 核对整理历史及路径映射；不会改为整理父目录。"
                    : $"此文件有 {candidates.Count} 条原整理记录，请先选择要重新整理的那一条：{file.Path}");
            matched.Add(candidates[0]);
        }
        if (matched.DistinctBy(history => history.Id).Count() != matched.Count)
            throw new MoviePilotException("多个文件对应同一条整理记录，已停止，避免重复提交");
        return matched;
    }

    /// <summary>删除按明确文件收集全部原记录供选择；同一目标的多条历史不等于多个文件版本。</summary>
    public static IReadOnlyList<MoviePilotTransferHistory> RequireDeletionMatches(
        MoviePilotTransferContext context, IEnumerable<MoviePilotTransferHistory> histories, bool requireDestination = false)
    {
        var records = histories.ToList();
        var matched = new List<MoviePilotTransferHistory>();
        foreach (var file in context.Files)
        {
            var candidates = DeletionCandidates(records, file.Path, requireDestination);
            if (candidates.Count == 0)
                throw new MoviePilotException($"没有找到此文件的原整理记录：{file.Path}。请先在 MoviePilot 核对整理历史及路径映射；不会改为删除父目录。");
            matched.AddRange(candidates);
        }
        return matched.DistinctBy(history => history.Id).ToList();
    }

    internal static IReadOnlyList<MoviePilotTransferHistory> DeletionCandidates(
        IEnumerable<MoviePilotTransferHistory> histories, string path, bool requireDestination = false)
    {
        var records = histories.DistinctBy(history => history.Id).ToArray();
        var targets = records.Where(history => MatchesLocalFile(history, source: false, path)).ToArray();
        return (targets.Length > 0 || requireDestination ? targets
            : records.Where(history => MatchesLocalFile(history, source: true, path)))
            .OrderByDescending(history => history.Id).ToArray();
    }

    private static bool MatchesLocalFile(MoviePilotTransferHistory history, bool source, string path)
    {
        var file = source ? history.SourceFile : history.TargetFile;
        var rawPath = MoviePilotTransferHistory.Scalar(history.Snapshot, source ? "src" : "dest");
        if (rawPath.Length > 0 && !MoviePilotTransfer.SamePath(rawPath, MoviePilotTransfer.Text(file, "path")))
            throw new MoviePilotException("整理记录的文件路径身份互相矛盾，请先在 MoviePilot 核对");
        return file.ValueKind == JsonValueKind.Object && MoviePilotTransfer.Text(file, "type") == "file" &&
            Storage(history, source) == "local" && MoviePilotTransfer.IsAbsolutePath(path) &&
            MoviePilotTransfer.SamePath(MoviePilotTransfer.Text(file, "path"), path);
    }
}
