using System.Text;
using Momoka.Configuration;
using Momoka.Emby;

namespace Momoka.Danmaku;

/// <summary>标题搜索不等于文件识别。只自动接受唯一的精确标题；续季与特别篇不猜，留给手动选择。</summary>
public static class DandanPlayMatching
{
    public static string Title(EmbyItem item) => item.Type == EmbyItemType.Episode && !string.IsNullOrWhiteSpace(item.SeriesName)
        ? item.SeriesName : item.Name;

    public static string? EpisodeNumber(EmbyItem item) => item.Type == EmbyItemType.Episode
        && item.ParentIndexNumber is not 0 && item.IndexNumber is > 0
            ? item.IndexNumber.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;

    public static DandanPlayEpisode? UniqueMatch(EmbyItem item, IReadOnlyList<DandanPlayEpisode> candidates, bool hasMore = false)
    {
        // 官方 hasMore 或本地截断表示还有未见候选，当前页的一项不能证明唯一。
        if (hasMore) return null;
        // 一套 SeriesName 经常覆盖多季；相同标题的 S02E01 不能悄悄挂到第一季第一集。
        if (item.Type == EmbyItemType.Episode && (item.ParentIndexNumber is null or 0 or > 1 || item.IndexNumber is not > 0)) return null;
        var title = Normalize(Title(item));
        var matches = candidates.Where(candidate => Normalize(candidate.AnimeTitle) == title).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static string Normalize(string value) => new(value.Normalize(NormalizationForm.FormKC)
        .Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public static bool CanRender(MpvSettings settings) => settings.Backend == MpvBackendKind.BuiltInLibMpv;

    public static bool IsConfigured(AppSettings settings) => settings.Danmaku.Enabled && CanRender(settings.Mpv)
        && (settings.Danmaku.CredentialSource != DanmakuCredentialSource.Custom
            || !string.IsNullOrWhiteSpace(settings.Danmaku.AppId) && settings.Danmaku.ProtectedAppSecret.Length > 0);
}
