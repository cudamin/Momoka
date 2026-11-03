using System.Globalization;
using System.Text;

namespace Momoka.Emby;

/// <summary>
/// A stable description of everything a browse card draws from an <see cref="EmbyItem"/>.
/// <para>
/// 暖缓存先上屏、再后台核对时，只有指纹相同的条目才允许保留原来那张卡。少了这一步，服务器上改过的评分、
/// 封面标签或观看进度会一直停在旧卡上；而只看条目身份又太粗，任何一次播放进度变化都要把整排卡片拆了重装。
/// </para>
/// <para>
/// 字段清单只覆盖卡片和列表行会显示、会参与图片选择的内容。详情页自己的字段不在这里 —— 这一份不承担
/// 「条目完整快照」的职责，只回答「这张卡还能不能留着」。
/// </para>
/// </summary>
public static class ItemFingerprint
{
    public static string Card(EmbyItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var text = new StringBuilder(320);
        Add(text, item.Id);
        Add(text, item.Name);
        Add(text, item.Type);
        Add(text, item.SeriesId);
        Add(text, item.SeriesName);
        Add(text, item.SeasonId);
        Add(text, item.SeasonName);
        Add(text, item.IndexNumber);
        Add(text, item.IndexNumberEnd);
        Add(text, item.ParentIndexNumber);
        Add(text, item.ProductionYear);
        Add(text, item.RunTimeTicks);
        Add(text, item.IsFolder);
        Add(text, item.Status);
        Add(text, item.OfficialRating);
        Add(text, item.CommunityRating);
        Add(text, item.CriticRating);
        Add(text, item.PrimaryImageAspectRatio);
        Add(text, item.ChildCount);
        Add(text, item.RecursiveItemCount);
        Add(text, item.LocalEpisodeCount);
        Add(text, item.Overview);
        Add(text, item.PrimaryImageItemId);
        Add(text, item.PrimaryImageTag);
        Add(text, item.ThumbImageTag);
        Add(text, item.ParentLogoItemId);
        Add(text, item.ParentLogoImageTag);
        Add(text, item.ParentBackdropItemId);
        Add(text, item.ParentThumbItemId);
        Add(text, item.ParentThumbImageTag);
        Add(text, item.SeriesThumbImageTag);
        AddList(text, item.BackdropImageTags);
        AddList(text, item.ParentBackdropImageTags);
        AddList(text, item.PosterPaths);
        AddList(text, item.Taglines);
        AddList(text, item.Genres);
        AddList(text, item.Resolutions);
        AddList(text, item.Studios.Select(studio => studio.Name));

        foreach (var pair in item.ImageTags.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Add(text, pair.Key);
            Add(text, pair.Value);
        }

        foreach (var source in item.MediaSources)
        {
            Add(text, source.Id);
            Add(text, source.Name);
            Add(text, source.Path);
            Add(text, source.Container);
            Add(text, source.Size);
            Add(text, source.Bitrate);
            foreach (var stream in source.MediaStreams)
            {
                Add(text, stream.Type);
                Add(text, stream.Codec);
                Add(text, stream.Width);
                Add(text, stream.Height);
                Add(text, stream.BitRate);
                Add(text, stream.ChannelLayout);
            }
        }

        var data = item.UserData;
        Add(text, data?.Played);
        Add(text, data?.IsFavorite);
        Add(text, data?.PlaybackPositionTicks);
        Add(text, data?.PlayedPercentage);
        Add(text, data?.UnplayedItemCount);
        Add(text, data?.PlayCount);
        return text.ToString();
    }

    /// <summary>
    /// 两串展示内容是否相同。调用方通常已经按身份对齐，这里只回答内容问题。
    /// </summary>
    public static bool Same(EmbyItem left, EmbyItem right) =>
        string.Equals(left.IdentityKey, right.IdentityKey, StringComparison.Ordinal)
        && string.Equals(Card(left), Card(right), StringComparison.Ordinal);

    private static void Add(StringBuilder text, string? value)
    {
        if (value is null)
        {
            text.Append("-1|");
            return;
        }

        text.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append('|');
    }

    private static void Add(StringBuilder text, bool? value) =>
        Add(text, value is null ? null : value.Value ? "1" : "0");

    private static void Add(StringBuilder text, int? value) =>
        Add(text, value?.ToString(CultureInfo.InvariantCulture));

    private static void Add(StringBuilder text, long? value) =>
        Add(text, value?.ToString(CultureInfo.InvariantCulture));

    private static void Add(StringBuilder text, float? value) =>
        Add(text, value?.ToString("R", CultureInfo.InvariantCulture));

    private static void Add(StringBuilder text, double? value) =>
        Add(text, value?.ToString("R", CultureInfo.InvariantCulture));

    private static void AddList(StringBuilder text, IEnumerable<string>? values)
    {
        if (values is null)
        {
            text.Append("-1|");
            return;
        }

        var list = values.ToList();
        text.Append(list.Count.ToString(CultureInfo.InvariantCulture)).Append('|');
        foreach (var value in list) Add(text, value);
    }
}
