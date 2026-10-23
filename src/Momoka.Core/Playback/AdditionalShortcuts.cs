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
        "show-stats" => ["script-binding", MpvStats.ShowOnceBinding],
        "toggle-stats" => ["script-binding", MpvStats.CycleBinding],
        _ => null,
    };
}
