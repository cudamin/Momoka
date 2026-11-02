using Momoka.Configuration;

namespace Momoka.Danmaku;

/// <summary>来源模式、可用性、发送能力与手动搜索可选来源的唯一判据。</summary>
public static class DanmakuSourceRules
{
    public static bool Includes(DanmakuSourceMode mode, DanmakuSource source) => source switch
    {
        DanmakuSource.DandanPlay => mode is DanmakuSourceMode.DandanPlay or DanmakuSourceMode.Both,
        DanmakuSource.DanmuApi => mode is DanmakuSourceMode.DanmuApi or DanmakuSourceMode.Both,
        _ => false
    };

    public static bool IncludesDandanPlay(DanmakuSourceMode mode) =>
        mode is DanmakuSourceMode.DandanPlay or DanmakuSourceMode.Both;

    public static bool IncludesDanmuApi(DanmakuSourceMode mode) =>
        mode is DanmakuSourceMode.DanmuApi or DanmakuSourceMode.Both;

    public static bool CanSend(DanmakuSourceMode mode) => IncludesDandanPlay(mode);

    /// <summary>至少有一个被选中的来源可以发起请求；合并模式缺 danmu_api 地址时仍可用弹弹play。</summary>
    public static bool IsAvailable(AppSettings settings)
    {
        var danmaku = settings.Danmaku;
        if (!danmaku.Enabled || !DandanPlayMatching.CanRender(settings.Mpv)) return false;
        if (IncludesDandanPlay(danmaku.Source)) return true;
        return IncludesDanmuApi(danmaku.Source)
            && DanmuApiEndpoint.TryCreate(danmaku.DanmuApiBaseUrl, null, out _, out _);
    }

    public static string SourceName(DanmakuSource source) => source switch
    {
        DanmakuSource.DandanPlay => "弹弹play",
        DanmakuSource.DanmuApi => "danmu_api",
        _ => "弹幕"
    };

    public static string ModeLabel(DanmakuSourceMode mode) => mode switch
    {
        DanmakuSourceMode.DandanPlay => "弹弹play",
        DanmakuSourceMode.DanmuApi => "danmu_api",
        DanmakuSourceMode.Both => "弹弹play + danmu_api",
        _ => "弹幕"
    };

    /// <summary>手动搜索一次只选一个来源；只返回当前模式里已配置好的来源。</summary>
    public static IReadOnlyList<DanmakuSource> AvailableSources(AppSettings settings)
    {
        var sources = new List<DanmakuSource>(2);
        var danmaku = settings.Danmaku;
        if (!danmaku.Enabled || !DandanPlayMatching.CanRender(settings.Mpv)) return sources;
        if (IncludesDandanPlay(danmaku.Source)) sources.Add(DanmakuSource.DandanPlay);
        if (IncludesDanmuApi(danmaku.Source)
            && DanmuApiEndpoint.TryCreate(danmaku.DanmuApiBaseUrl, null, out _, out _))
            sources.Add(DanmakuSource.DanmuApi);
        return sources;
    }
}
