using System.Globalization;
using System.Text.Json.Nodes;
using Momoka.Music;

namespace Momoka.FnMusic;

/// <summary>
/// 飞牛音乐自己的歌词表。<c>GET lyric/list?trackGUID=</c> 返回一首歌所有来源的歌词
/// （内嵌、同名 .lrc、手工填写），挑选顺序与官方 1.1.19 播放页一致。
/// <para>
/// 条目形状 <c>{guid, content, offset, source, updatedAt, createdAt}</c>：<c>content</c> 是 LRC 或
/// 纯文本原文；<c>offset</c> 是用户在飞牛音乐里调过的毫秒偏移，单独存放、不改原文；
/// <c>source</c> 1＝内嵌、2＝外部 lrc、4＝手工，越小越原始、优先级越高。
/// </para>
/// </summary>
public sealed partial class FnMusicMediaAdapter
{
    /// <summary>服务端收藏夹、容器没有歌词；读不到或没有条目都返回 null，由调用方退回文件标签。</summary>
    public async Task<MusicLyrics?> GetLyricsAsync(string id, CancellationToken ct)
    {
        if (id == LibraryId || Container(id).Kind is not null) return null;
        var data = await client.GetAsync(connection, "lyric/list", ct, ("trackGUID", id)).ConfigureAwait(false);
        return Select(data);
    }

    private static MusicLyrics? Select(JsonObject data)
    {
        if (data["list"] is not JsonArray rows) return null;

        var preferred = Text(data, "preferred");
        var candidates = rows.OfType<JsonObject>()
            .Select(row => new LyricCandidate(
                Text(row, "guid"),
                Text(row, "content"),
                Number(row, "offset") ?? 0,
                (int)(Number(row, "source") ?? 0),
                Number(row, "updatedAt") ?? 0,
                Number(row, "createdAt") ?? 0))
            .Where(candidate => candidate.Content.Length > 0)
            .OrderByDescending(candidate => SourcePriority(candidate.Source))
            .ThenByDescending(candidate => candidate.UpdatedAt)
            .ThenByDescending(candidate => candidate.CreatedAt)
            .ToList();
        if (candidates.Count == 0) return null;

        // 用户在飞牛音乐里选过的那一条优先，哪怕它本身没有时间轴。
        if (preferred.Length > 0 && candidates.Find(candidate => candidate.Guid == preferred) is { } chosen
            && Parse(chosen) is { IsEmpty: false } preferredLyrics)
            return preferredLyrics;

        // 否则按来源优先级找第一条能带上时间轴亮起来的；都没有就退到第一份纯文本。
        MusicLyrics? plain = null;
        foreach (var candidate in candidates)
        {
            if (Parse(candidate) is not { IsEmpty: false } lyrics) continue;
            if (lyrics.Synced) return lyrics;
            plain ??= lyrics;
        }
        return plain;
    }

    /// <summary>
    /// 官方 <c>source</c>：1 内嵌、2 外部 lrc、4 手工，其余按未知；映射成 4／3／2／1 的优先级。
    /// </summary>
    private static int SourcePriority(int source) => source switch { 1 => 4, 2 => 3, 4 => 2, _ => 1 };

    private static MusicLyrics? Parse(LyricCandidate candidate)
    {
        // TTML（&lt;tt&gt;）和 YRC（[起始毫秒,时长]）不是 LRC；本机解析器会把标签画成歌词，
        // 不如交给文件内嵌标签兜底，不硬猜别人的时间轴语法。
        var content = candidate.Content.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (content.StartsWith('<') || IsYrc(content)) return null;
        return MusicLyrics.Parse(candidate.Content, TimeSpan.FromMilliseconds(candidate.Offset));
    }

    private static bool IsYrc(string content)
    {
        // YRC 每行以 [起始毫秒,时长] 开头，和 LRC 的 [mm:ss.xx] 不同。
        if (content.Length < 4 || content[0] != '[') return false;
        var close = content.IndexOf(']');
        if (close < 0) return false;
        var parts = content[1..close].Split(',');
        return parts.Length == 2
            && long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
    }

    private sealed record LyricCandidate(string Guid, string Content, long Offset, int Source, long UpdatedAt, long CreatedAt);
}
