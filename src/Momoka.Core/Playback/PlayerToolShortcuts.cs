namespace Momoka.Playback;

/// <summary>外部工具动作可重绑，真正执行仍由宿主核对当前文件和工具能力。</summary>
public static class PlayerToolShortcuts
{
    public static IReadOnlyList<ShortcutAction> Actions { get; } =
    [
        new("tool-start", "标记片段开始", new("W", false, false, false)),
        new("tool-end", "标记片段结束", new("W", false, false, true)),
        new("tool-clip", "导出无损片段", new("C", true, false, true)),
        new("tool-gif", "导出 GIF", new("W", true, false, false)),
        new("tool-gif-subtitles", "导出带字幕的 GIF", new("W", true, false, true)),
        new("tool-subtitle-export", "导出当前字幕", new("M", false, true, false)),
        new("tool-subtitle-sync", "自动同步字幕", new("M", true, true, false)),
        new("tool-subtitle-generate", "生成 AI 字幕", new("F", false, true, false)),
        new("tool-subtitle-translate", "翻译整份字幕", new("T", true, true, true)),
        new("capture-crop", "自定义裁切截图", new("C", true, true, false)),
        new("capture-subtitle", "截取字幕区域", new("R", true, true, false)),
        new("capture-stitch", "生成字幕拼接图", new("R", true, true, true)),
        new("chapter-mark", "标记章节时间", new("C", false, true, false)),
        new("chapter-rename", "编辑当前章节标题", new("E", false, true, false)),
        new("chapter-remove", "删除当前章节", new("R", false, true, false)),
        new("chapter-export-chp", "导出 CHP 章节文件", new("W", false, true, false)),
        new("chapter-export-ogm", "导出 OGM 章节文件", new("G", false, true, true))
    ];

    public static bool Contains(string action) => Actions.Any(item => item.Id == action);
}
