using Momoka.Mpv;

namespace Momoka.Playback;

/// <summary>新增快捷键的 mpv 命令与窗口无关，两条内置播放管线共用。弹幕与弹窗由驻留视图模型处理。</summary>
public static class AdditionalShortcuts
{
    public static IReadOnlyList<string>? Command(string action) => action switch
    {
        "screenshot" => ["screenshot", "scaled+subtitles"],
        "screenshot-original" => ["screenshot", "video"],
        "toggle-subtitle" => ["cycle", "sub-visibility"],
        "subtitle-next" => ["cycle", "sid"],
        "subtitle-previous" => ["cycle", "sid", "down"],
        "audio-next" => ["cycle", "aid"],
        "audio-previous" => ["cycle", "aid", "down"],
        "frame-forward" => ["frame-step"],
        "frame-backward" => ["frame-back-step"],
        "seek-start" => ["seek", "0", "absolute+exact"],
        "revert-seek" => ["revert-seek"],
        "seek-backward-fine" => ["seek", "-1", "relative+exact"],
        "seek-forward-fine" => ["seek", "1", "relative+exact"],
        "subtitle-seek-previous" => ["sub-seek", "-1"],
        "subtitle-seek-next" => ["sub-seek", "1"],
        "video-zoom-out" => ["add", "video-zoom", "-0.1"],
        "video-zoom-in" => ["add", "video-zoom", "0.1"],
        "video-zoom-reset" => ["set", "video-zoom", "0"],
        "video-pan-left" => ["add", "video-pan-x", "-0.1"],
        "video-pan-right" => ["add", "video-pan-x", "0.1"],
        "video-pan-up" => ["add", "video-pan-y", "-0.1"],
        "video-pan-down" => ["add", "video-pan-y", "0.1"],
        "subtitle-scale-down" => ["add", "sub-scale", "-0.1"],
        "subtitle-scale-up" => ["add", "sub-scale", "0.1"],
        "subtitle-position-up" => ["add", "sub-pos", "-1"],
        "subtitle-position-down" => ["add", "sub-pos", "1"],
        "toggle-ab-loop" => ["ab-loop"],
        "toggle-loop" => ["cycle-values", "loop-file", "inf", "no"],
        "show-stats" => ["script-binding", MpvStats.ShowOnceBinding],
        "toggle-stats" => ["script-binding", MpvStats.CycleBinding],
        _ => null,
    };

    /// <summary>
    /// 复用画面菜单的顺序执行、失败回执与播放身份检查。复位是多条独立命令，不能把 input.conf 的分号串传给 libmpv。
    /// </summary>
    public static PlayerMenuNode? Plan(string action)
    {
        IReadOnlyList<IReadOnlyList<string>>? commands = action switch
        {
            "video-pan-reset" => [["set", "video-pan-x", "0"], ["set", "video-pan-y", "0"]],
            "clear-ab-loop" => [["set", "ab-loop-a", "no"], ["set", "ab-loop-b", "no"]],
            _ => Command(action) is { } command ? [command] : null
        };
        if (commands is null) return null;
        var notice = action switch
        {
            "screenshot" or "screenshot-original" => "截图已保存到 ${screenshot-directory}",
            "video-zoom-out" or "video-zoom-in" or "video-zoom-reset" => "画面缩放：${video-zoom}",
            "video-pan-left" or "video-pan-right" or "video-pan-up" or "video-pan-down" or "video-pan-reset"
                => "画面位置：${video-pan-x} / ${video-pan-y}",
            "subtitle-scale-down" or "subtitle-scale-up" => "字幕缩放：${sub-scale}",
            "subtitle-position-up" or "subtitle-position-down" => "字幕位置：${sub-pos}",
            "toggle-ab-loop" => "片段循环：${ab-loop-a} - ${ab-loop-b}",
            "clear-ab-loop" => "片段循环：${ab-loop-a} - ${ab-loop-b}",
            "toggle-loop" => "单片循环：${loop-file}",
            _ => ""
        };
        return new(PlayerMenuKind.Command, ShortcutCatalog.Label(action), commands, notice, []);
    }
}
