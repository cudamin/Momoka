using System.Globalization;
using System.Text.Json.Nodes;

namespace Momoka.FnNas;

/// <summary>
/// 飞牛「编辑元数据」表单上我们实际改得动的那几格。官方 <c>item/getEditDetail</c> 交回来的是一整份
/// 带锁位的对象，保存时 <c>item/saveEditDetail</c> 要的也是整份 —— 所以这里只在原对象上改有变化的键，
/// 其余字段（图片、类型、演职人员）原样带回去。
/// <para>
/// 只开四格是有意的：这四格在官网页端的影片编辑表上都是普通输入框，形状已经核对；类型是标签 id
/// 数组、演职人员是另一张子表，把它们塞进一张短表只会把服务器上原有的内容改坏。
/// </para>
/// </summary>
public sealed record FnNasMetadataEdit(string Name, string Overview, string Rating, string ReleaseDate, string ReleaseKey)
{
    /// <summary>剧集用首播日期，其余用发行日期；服务端给哪个键、哪个键有值就写回哪个键。</summary>
    public static FnNasMetadataEdit Read(JsonObject detail)
    {
        var release = Date(detail, "release_date");
        var firstAir = Date(detail, "first_air_date");
        var useFirst = release.Length == 0 && firstAir.Length > 0;
        return new FnNasMetadataEdit(
            Text(detail, "title"),
            Text(detail, "overview"),
            Number(detail, "rating"),
            useFirst ? firstAir : release,
            useFirst ? "first_air_date" : "release_date");
    }

    public string? Problem
    {
        get
        {
            if (Name.Trim().Length == 0) return "名称不能为空";

            var ratingText = Rating.Trim();
            if (ratingText.Length > 0)
            {
                if (!double.TryParse(ratingText, NumberStyles.Float, CultureInfo.InvariantCulture, out var rating)
                    || !double.IsFinite(rating) || rating < 0 || rating > 10) return "评分要填 0 到 10 之间的数字";
            }

            if (ReleaseDate.Trim().Length > 0 && !DateOnly.TryParseExact(ReleaseDate.Trim(), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                return "发行日期要填成 2024-05-01 这样的年-月-日";
            return null;
        }
    }

    /// <summary>把改动写回服务器交来的整份对象；一个键都没动时返回 false。</summary>
    public bool ApplyTo(JsonObject detail)
    {
        if (Problem is not null) throw new InvalidOperationException($"表单还有问题：{Problem}");

        var changed = SetText(detail, "title", Name);
        changed |= SetText(detail, "overview", Overview);
        changed |= SetRating(detail, Rating);
        changed |= SetDate(detail, ReleaseKey, ReleaseDate);
        return changed;
    }

    private static bool SetText(JsonObject detail, string key, string value)
    {
        var trimmed = value.Trim();
        return Set(detail, key, trimmed.Length == 0 ? null : JsonValue.Create(trimmed));
    }

    private static bool SetRating(JsonObject detail, string value)
    {
        if (value.Trim().Length == 0) return Set(detail, "rating", null);
        var rating = double.Parse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
        return Set(detail, "rating", JsonValue.Create(rating));
    }

    private static bool SetDate(JsonObject detail, string key, string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return Set(detail, key, null);
        var date = DateOnly.ParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return Set(detail, key, JsonValue.Create(date));
    }

    private static bool Set(JsonObject detail, string key, JsonNode? value)
    {
        var before = detail[key];
        if (before is null && value is null) return false;
        if (before is not null && value is not null && before.ToJsonString() == value.ToJsonString()) return false;
        detail[key] = value;
        return true;
    }

    private static string Text(JsonObject detail, string key) => detail[key] is JsonValue value
        && value.TryGetValue<string>(out var text) ? text : "";

    private static string Number(JsonObject detail, string key) => detail[key] is JsonValue value ? value.ToString() : "";

    private static string Date(JsonObject detail, string key)
    {
        var text = Text(detail, key);
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        // 元数据里的日期不是时间点：带时区时也保留服务端写出的日历日期，不换算成本地那一天。
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp)
            ? DateOnly.FromDateTime(timestamp.DateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : text;
    }
}
