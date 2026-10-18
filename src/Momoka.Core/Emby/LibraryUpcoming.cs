using System.Globalization;
using Momoka.Infrastructure;

namespace Momoka.Emby;

public sealed record UpcomingDateGroup(DateOnly? Date, IReadOnlyList<EmbyItem> Items);

/// <summary>电视节目预告是播出日历，不是电影预告片；日期窗口由 Shows/Upcoming 决定。</summary>
public static class LibraryUpcoming
{
    public const int PageSize = 100;
    public const int CardWidth = 248;
    public const int CardChrome = 40;

    public static ItemQuery Query(string parentId, int start, int limit = PageSize) => new()
    {
        ParentId = parentId,
        StartIndex = start,
        Limit = limit,
        Recursive = true,
        IncludeItemTypes = [EmbyItemType.Episode],
        Fields = "PrimaryImageAspectRatio,PremiereDate,Overview",
        SortBy = "PremiereDate,SortName"
    };

    public static (string Key, string? Value)[] Parameters(ItemQuery query, string userId, MediaServerKind kind)
    {
        var parameters = new List<(string Key, string? Value)>
        {
            ("UserId", userId),
            ("ParentId", query.ParentId),
            ("StartIndex", query.StartIndex.ToString(CultureInfo.InvariantCulture)),
            ("Limit", query.Limit.ToString(CultureInfo.InvariantCulture)),
            ("Fields", "PrimaryImageAspectRatio,PremiereDate,Overview"),
            ("ImageTypeLimit", "1"),
            ("EnableImageTypes", "Primary,Thumb,Backdrop"),
            ("EnableUserData", "true")
        };
        // Jellyfin 的 Upcoming 只声明用户、分页、字段与图片参数，不接受通用查询的排序和筛选。
        if (kind == MediaServerKind.Emby)
        {
            parameters.Add(("SortBy", "PremiereDate,SortName"));
            parameters.Add(("SortOrder", "Ascending"));
        }
        return [.. parameters];
    }

    public static IReadOnlyList<UpcomingDateGroup> Group(IEnumerable<EmbyItem> items, TimeZoneInfo? timeZone = null)
    {
        var zone = timeZone ?? TimeZoneInfo.Local;
        return [.. items.GroupBy(item => item.PremiereDate is { } date
                ? (DateOnly?)DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(date, zone).DateTime) : null)
            .OrderBy(group => group.Key is null)
            .ThenBy(group => group.Key)
            .Select(group => new UpcomingDateGroup(group.Key, [.. group]))];
    }

    public static string DateTitle(DateOnly? date, DateOnly today)
    {
        if (date is null) return "播出日期待定";
        if (date.Value.DayNumber == today.DayNumber - 1) return "昨天";
        return date.Value.ToString("M月d日dddd", CultureInfo.GetCultureInfo("zh-CN"));
    }

    public static string Title(EmbyItem item) =>
        string.IsNullOrWhiteSpace(item.SeriesName) ? item.Name : item.SeriesName.Trim();

    public static string Subtitle(EmbyItem item)
    {
        var code = item.ParentIndexNumber is { } season && item.IndexNumber is { } episode
            ? $"S{season}:E{episode}" : item.IndexNumber is { } number ? $"E{number}" : "";
        if (item.IndexNumberEnd is { } end && item.IndexNumber is { } start && end > start) code += $"–{end}";
        var name = item.Name.Trim();
        return code.Length == 0 ? name : name.Length == 0 ? code : $"{code} - {name}";
    }

    public static ArtworkRef? Artwork(EmbyItem item)
    {
        foreach (var type in new[] { EmbyImageStore.Thumb, EmbyImageStore.Primary })
            if (EmbyImageStore.TagFor(item, type) is { Length: > 0 } tag)
                return new(item.Id, type, tag);

        var inherited = ItemArtwork.Inherited(item);
        foreach (var picture in inherited)
            if (picture.ImageType == EmbyImageStore.Thumb) return picture;
        if (inherited.Count > 0) return inherited[0];
        if (EmbyImageStore.TagFor(item, EmbyImageStore.Backdrop) is { Length: > 0 } own)
            return new(item.Id, EmbyImageStore.Backdrop, own);
        return null;
    }

    public static int WidthFor(double viewportWidth) =>
        (int)Math.Clamp(Math.Floor(double.IsFinite(viewportWidth) ? viewportWidth : CardWidth), 1, CardWidth);

    public static double RowHeight(int width) => CardSize.HeightFor(width, wide: true) + CardChrome;
}
