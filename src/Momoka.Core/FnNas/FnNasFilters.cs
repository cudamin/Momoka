namespace Momoka.FnNas;

/// <summary>
/// 飞牛媒体库筛选面板的一次选择。键就是官方前端 <c>item/list</c> 的 <c>tags</c> 里的那几个名字，
/// 值保持服务器的原始形状（题材用 id、分辨率用服务器返回的短名），显示时的中文另由
/// <see cref="FnNasFilterLabels"/> 与标签映射给出，不把显示文字写回查询。
/// </summary>
public sealed class FnNasFilterSelection
{
    public string Type { get; set; } = "";
    public List<string> Genres { get; } = [];
    public List<string> Resolutions { get; } = [];
    public List<string> ColorRanges { get; } = [];
    public List<string> AudioTypes { get; } = [];
    public List<string> Locations { get; } = [];
    public List<string> Decades { get; } = [];
    public List<string> RecognitionStatuses { get; } = [];
    public string Watched { get; set; } = "";

    public bool IsBlank => Type.Length == 0 && Watched.Length == 0 && Count == 0;

    /// <summary>有几项筛选正在生效，不含「全部」。</summary>
    public int Count
    {
        get
        {
            var count = 0;
            if (Type.Length > 0) count++;
            if (Watched.Length > 0) count++;
            foreach (var values in AllLists) if (values.Count > 0) count++;
            return count;
        }
    }

    private IEnumerable<List<string>> AllLists =>
        [Genres, Resolutions, ColorRanges, AudioTypes, Locations, Decades, RecognitionStatuses];

    public void Clear()
    {
        Type = "";
        Watched = "";
        foreach (var values in AllLists) values.Clear();
    }

    public FnNasFilterSelection Clone()
    {
        var clone = new FnNasFilterSelection { Type = Type, Watched = Watched };
        clone.Genres.AddRange(Genres);
        clone.Resolutions.AddRange(Resolutions);
        clone.ColorRanges.AddRange(ColorRanges);
        clone.AudioTypes.AddRange(AudioTypes);
        clone.Locations.AddRange(Locations);
        clone.Decades.AddRange(Decades);
        clone.RecognitionStatuses.AddRange(RecognitionStatuses);
        return clone;
    }
}

public sealed record FnNasTagOption(string Value, string Label);

/// <summary>
/// 一个媒体库在筛选面板上能选的全部值。<c>/tag/list</c> 给可选项，题材与国家/地区再经官方映射表换成中文。
/// </summary>
public sealed record FnNasFilterOptions(
    IReadOnlyList<FnNasTagOption> Genres,
    IReadOnlyList<FnNasTagOption> Resolutions,
    IReadOnlyList<FnNasTagOption> ColorRanges,
    IReadOnlyList<FnNasTagOption> AudioTypes,
    IReadOnlyList<FnNasTagOption> Locations,
    IReadOnlyList<FnNasTagOption> Decades,
    IReadOnlyList<FnNasTagOption> RecognitionStatuses)
{
    public static FnNasFilterOptions Empty { get; } = new([], [], [], [], [], [], []);
}

/// <summary>
/// 标签值到中文的那一层。服务器对分辨率、动态范围、音频规格给的是短名（<c>4k</c>、<c>HDR10</c>、
/// <c>Stereo</c>），面板直接显示它们会和网页端的中文对不上；未知值原样交回，宁可显示英文，也不猜。
/// </summary>
public static class FnNasFilterLabels
{
    public const string All = "全部";

    public static string Type(string value) => value switch
    {
        "Movie" => "电影",
        "TV" => "电视节目",
        "LiveChannel" => "电视直播",
        "Directory" => "文件夹",
        "Video" => "其他视频",
        _ => value
    };

    public static string Resolution(string value) => value.ToLowerInvariant() switch
    {
        "4k" => "4k",
        "1080p" => "1080P",
        "720p" => "720P",
        "2160p" => "4k",
        "others" or "other" => "其他",
        _ => value
    };

    public static string ColorRange(string value) => value.ToLowerInvariant() switch
    {
        "hdr10" => "HDR10",
        "hdr10+" => "HDR10+",
        "sdr" => "SDR",
        "dolby vision" or "dolbyvision" or "dv" => "杜比视界",
        "hlg" => "HLG",
        "others" or "other" => "其他",
        _ => value
    };

    public static string AudioType(string value) => value.ToLowerInvariant() switch
    {
        "stereo" => "立体声",
        "dolby surround" or "dolby_surround" or "dolbysurround" or "dolbydigital" => "杜比环绕",
        "dolby atmos" or "dolby_atmos" or "dolbyatmos" => "杜比全景声",
        "dts" or "dts-hd" or "dts_hd" or "dtsx" or "dts:x" => "DTS",
        "others" or "other" => "其他",
        _ => value
    };

    public static string Decade(string value) => value switch
    {
        "Recent" => "今年",
        "2020s" => "2020年代",
        "2010s" => "2010年代",
        "2000s" => "2000年代",
        "1990s" => "1990年代",
        "1980s" => "1980年代",
        "1970s" => "1970年代",
        "Others" or "Other" => "其他",
        _ => value
    };

    public static string RecognitionStatus(string value) => value switch
    {
        "1" => "未匹配",
        "2" => "已匹配",
        "3" => "读取 NFO",
        _ => value
    };

    public static string Watched(string value) => value switch
    {
        "1" => "已观看",
        "0" => "未观看",
        _ => value
    };
}
