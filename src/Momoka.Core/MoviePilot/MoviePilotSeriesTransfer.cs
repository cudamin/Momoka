using System.Globalization;

namespace Momoka.MoviePilot;

/// <summary>整部剧的重新整理只认当前剧的身份与明确勾选的原历史，不猜片名或扩大到父目录。</summary>
public static class MoviePilotSeriesTransfer
{
    public static string Query(string tmdbId)
    {
        if (!TryId(tmdbId, out var id))
            throw new MoviePilotException("当前剧没有有效的 TMDB 编号，请先补齐剧的媒体信息后重新打开整理页面");
        return "tmdbid=" + id.ToString(CultureInfo.InvariantCulture);
    }

    public static bool Matches(MoviePilotTransferHistory history, string tmdbId)
    {
        if (!TryId(tmdbId, out var expected) || history.Type != MoviePilotTransferRequest.TypeSeries)
            return false;
        var tmdbSource = history.MediaSource == "themoviedb";
        if (tmdbSource && !SameId(history.MediaId, expected)) return false;
        // 新历史可能使用另一数据源的主编号；tmdbid 是独立的交叉编号，不能互相冒充。
        return history.TmdbId.Length > 0 ? SameId(history.TmdbId, expected) : tmdbSource;
    }

    public static MoviePilotTransferContext CreateContext(MoviePilotTransferContext context,
        IReadOnlyList<MoviePilotTransferHistory> histories)
    {
        if (!context.BrowseSeriesHistory || context.Type != MoviePilotTransferRequest.TypeSeries)
            throw new MoviePilotException("当前条目不是剧资源整理页面");
        Query(context.MediaId);
        if (histories.Count == 0) throw new MoviePilotException("请先勾选需要重新整理的资源");
        var files = new List<MoviePilotTransferFile>(histories.Count);
        var ids = new HashSet<long>();
        foreach (var history in histories)
        {
            if (history.Id <= 0 || !ids.Add(history.Id))
                throw new MoviePilotException("勾选的整理记录包含重复或无效条目，请重新选择");
            if (!Matches(history, context.MediaId))
                throw new MoviePilotException("勾选的资源不属于当前剧，请重新加载整理记录");
            MoviePilotTransfer.RequireFile(history.TransferFile, history.TransferPath);
            if (files.Any(file => MoviePilotTransfer.SamePath(file.Path, history.TransferPath)))
                throw new MoviePilotException("勾选的多条记录使用同一个源文件，请保留其中一条，避免重复整理");
            files.Add(new MoviePilotTransferFile(MoviePilotTransfer.PathName(history.TransferPath), history.TransferPath));
        }
        return context with
        {
            Files = files.AsReadOnly(),
            Season = null,
            Episode = "",
            BrowseSeriesHistory = false
        };
    }

    private static bool SameId(string value, long expected) => TryId(value, out var id) && id == expected;

    private static bool TryId(string value, out long id) =>
        long.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
}
