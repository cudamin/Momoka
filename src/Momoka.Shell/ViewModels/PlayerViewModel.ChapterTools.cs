using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Momoka.Infrastructure;
using Momoka.Mpv;
using Momoka.Playback;

namespace Momoka.Shell.ViewModels;

public sealed partial class PlayerViewModel
{
    private IReadOnlyList<SkipChapter> _editedChapters = [];
    private long _chapterEditGeneration = -1;
    internal string ChapterTitleDraft { get; private set; } = "";
    private double? _chapterRenameStart;
    private IReadOnlyList<SkipChapter> CurrentChapters => _chapterEditGeneration == _playback.Generation ? _editedChapters : ChapterMarks;

    private async Task RunChapterActionAsync(string action, InteractionContext context, string? title = null)
    {
        var fileGeneration = _playback.ActiveFileGeneration;
        var seconds = await _playback.GetNumberAsync("time-pos").ConfigureAwait(true);
        if (seconds is null || !IsCurrentInteraction(context) || fileGeneration != _playback.ActiveFileGeneration) return;
        var chapters = CurrentChapters.ToList();
        var current = chapters.FindLastIndex(chapter => chapter.Start <= seconds + 0.05);
        if (action == "chapter-rename")
        {
            if (title is not null) current = chapters.FindIndex(chapter => chapter.Start == _chapterRenameStart);
            if (current < 0) return;
            if (title is null)
            {
                ChapterTitleDraft = chapters[current].Title ?? "";
                _chapterRenameStart = chapters[current].Start;
                PlayerDialogRequested?.Invoke("chapter-rename");
                return;
            }
            chapters[current] = chapters[current] with { Title = title.Trim() };
        }
        else if (action == "chapter-remove") { if (current < 0) return; chapters.RemoveAt(current); }
        else if (action == "chapter-mark")
        {
            if (!chapters.Any(chapter => Math.Abs(chapter.Start - seconds.Value) < 0.05))
                chapters.Add(new(seconds.Value, $"章节 {chapters.Count + 1}"));
            chapters = chapters.OrderBy(chapter => chapter.Start).ToList();
        }
        else if (action is "chapter-export-chp" or "chapter-export-ogm")
        {
            try
            {
                var directory = AppPaths.ResolveScreenshotDirectory(Settings.Mpv.ScreenshotDirectory, AppPaths.Default.ScreenshotDirectory)!;
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "Momoka-chapters-" + Guid.NewGuid().ToString("N")[..8] + ".chp");
                await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync(PlayerChapterDocument.Write(chapters, action.EndsWith("-ogm", StringComparison.Ordinal))).ConfigureAwait(true);
                if (IsCurrentInteraction(context)) Noticed?.Invoke("章节已保存：" + path, InfoBarSeverity.Informational);
            }
            catch (Exception) { if (IsCurrentInteraction(context)) Noticed?.Invoke("章节导出失败，请检查输出目录。", InfoBarSeverity.Warning); }
            return;
        }
        var json = JsonSerializer.Serialize(chapters.Select(chapter => new { time = chapter.Start, title = chapter.Title ?? "" }));
        if (await _playback.CommandForActiveFileAsync(context.PlaybackGeneration, fileGeneration,
            "script-message-to", "momoka_analysis", "chapters", json).ConfigureAwait(true) && IsCurrentInteraction(context))
        {
            for (var retry = 0; retry < 10; retry++)
            {
                var actual = await _playback.GetChaptersAsync().ConfigureAwait(true);
                if (!IsCurrentInteraction(context) || fileGeneration != _playback.ActiveFileGeneration) return;
                if (actual.Count == chapters.Count && actual.Select((entry, index) =>
                    Math.Abs(entry.Start - chapters[index].Start) < 0.001 && entry.Title == chapters[index].Title).All(value => value))
                {
                    _editedChapters = chapters;
                    _chapterEditGeneration = _playback.Generation;
                    Noticed?.Invoke("本次播放的章节已更新", InfoBarSeverity.Informational);
                    return;
                }
                await Task.Delay(30).ConfigureAwait(true);
            }
            if (IsCurrentInteraction(context)) Noticed?.Invoke("章节修改未能确认，请重试。", InfoBarSeverity.Warning);
        }
    }

    internal Task RenameCurrentChapterAsync(string title, InteractionContext context) => RunChapterActionAsync("chapter-rename", context, title);

    private PlayerMenuNode ChapterToolMenu() => DynamicGroup("章节制作", new[]
    {
        HostRow("标记章节时间", "chapter-mark", "chapter-mark"),
        HostRow("编辑当前章节标题", "chapter-rename", "chapter-rename"),
        HostRow("删除当前章节", "chapter-remove", "chapter-remove"),
        HostRow("导出 CHP 章节文件", "chapter-export-chp", "chapter-export-chp"),
        HostRow("导出 OGM 章节文件", "chapter-export-ogm", "chapter-export-ogm")
    }.Where(row => row.HostAction == "chapter-mark" || CurrentChapters.Count > 0));
}
