namespace Momoka.Playback;

/// <summary>章节动作可重绑，真正执行仍由宿主核对当前文件。</summary>
public static class ChapterShortcuts
{
    public static IReadOnlyList<ShortcutAction> Actions { get; } =
    [
        new("chapter-mark", "标记章节时间", new("C", false, true, false)),
        new("chapter-rename", "编辑当前章节标题", new("E", false, true, false)),
        new("chapter-remove", "删除当前章节", new("R", false, true, false)),
        new("chapter-export-chp", "导出 CHP 章节文件", new("W", false, true, false)),
        new("chapter-export-ogm", "导出 OGM 章节文件", new("G", false, true, true))
    ];

    public static bool Contains(string action) => Actions.Any(item => item.Id == action);
}
