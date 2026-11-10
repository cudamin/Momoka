using System.Globalization;
using System.Text;

namespace Momoka.Playback;

/// <summary>与 chapter-make-read.lua 的 CHP/OGM 格式一致，不写媒体地址或服务器元数据。</summary>
public static class PlayerChapterDocument
{
    public static string Write(IEnumerable<SkipChapter> chapters, bool ogm)
    {
        var builder = new StringBuilder();
        var index = 0;
        foreach (var chapter in chapters.OrderBy(chapter => chapter.Start))
        {
            if (!double.IsFinite(chapter.Start) || chapter.Start < 0 || chapter.Start > 359999.999)
                throw new ArgumentOutOfRangeException(nameof(chapters), "章节时间无效。");
            var milliseconds = (long)Math.Round(chapter.Start * 1000);
            var stamp = string.Create(CultureInfo.InvariantCulture,
                $"{milliseconds / 3600000:00}:{milliseconds / 60000 % 60:00}:{milliseconds / 1000 % 60:00}.{milliseconds % 1000:000}");
            var title = (chapter.Title ?? "").Replace('\r', ' ').Replace('\n', ' ');
            index++;
            if (ogm) builder.Append(CultureInfo.InvariantCulture, $"CHAPTER{index:00}={stamp}\nCHAPTER{index:00}NAME={title}\n");
            else builder.Append(stamp).Append(' ').Append(title).Append('\n');
        }
        return builder.ToString();
    }
}
