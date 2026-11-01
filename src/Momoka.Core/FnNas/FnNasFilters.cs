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
/// 筛选面板上出现哪几行。规则照官方 0.9.8 <c>layout/list</c> 的筛选组件：行序固定为
/// 影视分类、类型、分辨率、视频动态范围、音频规格、国家/地区、发行年份、匹配状态、是否已观看；
/// 具体哪些行出现由这一页的 <c>defaultType</c> 与是不是文件夹决定。
/// </summary>
public static class FnNasFilterCatalog
{
    private static readonly string[] PageRows =
        ["type", "genres", "resolutions", "color_range", "audio_type", "locate", "decades", "recognition_status", "watched"];

    private static readonly string[] FolderRows = ["resolutions", "color_range", "audio_type", "watched"];

    /// <summary>官方是 <c>Me({defaultType, isFolder})</c>：文件夹只有四行；直播只有类型；其余按默认类型裁剪。</summary>
    public static IReadOnlyList<string> Rows(string? defaultType, bool isFolder)
    {
        if (isFolder) return FolderRows;
        if (defaultType == FnNasLibraryQuery.Live) return ["genres"];
        return [.. PageRows.Where(key => Shows(defaultType, key))];
    }

    private static bool Shows(string? defaultType, string key) => defaultType switch
    {
        null or "" => true,
        FnNasLibraryQuery.All => true,
        // 「其他」与单集页：分类、题材、地区、年代都不适用。
        FnNasLibraryQuery.Other or "Episode" => key is not ("type" or "genres" or "locate" or "decades"),
        // 具体类型页（电影／电视节目／直播）：不再让人换分类。
        _ => key != "type"
    };

    public static string RowLabel(string key) => key switch
    {
        "type" => "影视分类",
        "genres" => "类型",
        "resolutions" => "分辨率",
        "color_range" => "视频动态范围",
        "audio_type" => "音频规格",
        "locate" => "国家/地区",
        "decades" => "发行年份",
        "recognition_status" => "匹配状态",
        "watched" => "是否已观看",
        _ => key
    };
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
