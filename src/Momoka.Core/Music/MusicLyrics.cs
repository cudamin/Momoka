using System.ComponentModel;
using System.Text.RegularExpressions;

namespace Momoka.Music;

/// <summary>
/// 一首歌的一句歌词。带时间轴时 <see cref="Time"/> 是它该亮起的位置；纯文本歌词全部为 0。
/// <para>
/// 可变的那一位只有「是不是正在唱」。它由播放进度驱动，页面只负责照 <see cref="ActiveOpacity"/> 画，
/// 所以这一位必须发通知 —— 歌词列表是稳定的，换曲时清空重填，不重建控件。
/// </para>
/// </summary>
public sealed class MusicLyricLine : INotifyPropertyChanged
{
    private bool _active;

    public MusicLyricLine(TimeSpan time, string text)
    {
        Time = time < TimeSpan.Zero ? TimeSpan.Zero : time;
        Text = text;
    }

    public TimeSpan Time { get; }
    public string Text { get; }

    public bool IsActive
    {
        get => _active;
        internal set
        {
            if (_active == value) return;
            _active = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveOpacity)));
        }
    }

    /// <summary>正在唱的那一句整亮，其余压到三成多 —— 与飞牛音乐播放页的歌词栏同一档对比。</summary>
    public double ActiveOpacity => _active ? 1 : 0.38;

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// 内嵌歌词的解析结果：带时间轴的 LRC 与纯文本共用一份行模型。
/// <para>
/// 歌词来自当前音频文件自己的标签（mpv 的 <c>metadata/by-key</c>），不依赖任何服务端歌词接口。
/// 标签名各家不同（<c>Lyrics</c>、<c>LYRICS</c>、<c>UNSYNCEDLYRICS</c>…），挑选与兜底见
/// <see cref="FindTag"/> 和 <see cref="FallbackTags"/>；这里只负责把拿到的原文变成行。
/// </para>
/// </summary>
public sealed class MusicLyrics
{
    // [mm:ss]、[mm:ss.xx]、[mm:ss.xxx]、[mm:ss:xx] 四种写法都收。分钟允许三位，长篇有声书用得上。
    private static readonly Regex Stamp = new(
        @"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // LRC 的头部标签（[ar:…]、[ti:…]、[offset:…]）不是歌词，不能画到屏上。
    private static readonly Regex Header = new(
        @"^\[(ar|ti|al|by|offset|re|ve|length|au):",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>标签名里出现这个词就当作歌词。大小写不敏感，<c>UnsynchronizedLyrics</c> 也命中。</summary>
    private const string TagHint = "lyric";

    /// <summary>读不到 <c>display-tags</c> 时按这几个常见键名再问一遍。</summary>
    public static readonly string[] FallbackTags =
        ["Lyrics", "LYRICS", "lyrics", "UNSYNCEDLYRICS", "UnsyncedLyrics", "UNSYNCED LYRICS", "USLT"];

    private MusicLyrics(IReadOnlyList<MusicLyricLine> lines, bool synced)
    {
        Lines = lines;
        Synced = synced;
    }

    public IReadOnlyList<MusicLyricLine> Lines { get; }

    /// <summary>true＝原文带时间轴，可以随播放高亮；false＝纯文本，按原文顺序静态列出。</summary>
    public bool Synced { get; }

    public bool IsEmpty => Lines.Count == 0;

    /// <summary>从标签名列表里挑第一个像歌词的键；没有就返回 null。</summary>
    public static string? FindTag(IEnumerable<string>? tags)
    {
        if (tags is null) return null;
        foreach (var tag in tags)
            if (tag.Length > 0 && tag.Contains(TagHint, StringComparison.OrdinalIgnoreCase))
                return tag;
        return null;
    }

    /// <summary>
    /// 解析歌词原文。null、空白、只有 LRC 头部标签都得到空结果。
    /// <para>
    /// 带时间轴的行按时间排序；同一行写了多个时间戳就拆成多句（副歌重复）。没有时间戳的行在
    /// LRC 里是注释或空行，不画。纯文本保留原文顺序，去掉首尾空白与空行。
    /// </para>
    /// </summary>
    public static MusicLyrics Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Empty();

        var raw = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim('\uFEFF', ' ', '\t', '\n');
        if (raw.Length == 0) return Empty();

        var offset = ReadOffset(raw);
        var timed = new List<MusicLyricLine>();
        var plain = new List<MusicLyricLine>();

        foreach (var source in raw.Split('\n'))
        {
            var line = source.Trim();
            if (line.Length == 0) continue;
            if (Header.IsMatch(line)) continue;

            var stamps = Stamp.Matches(line);
            if (stamps.Count == 0)
            {
                plain.Add(new MusicLyricLine(TimeSpan.Zero, line));
                continue;
            }

            var last = stamps[stamps.Count - 1];
            var start = last.Index + last.Length;
            var content = line[start..].Trim();
            if (content.Length == 0) continue;
            foreach (Match stamp in stamps)
            {
                var time = TimeOf(stamp) + offset;
                timed.Add(new MusicLyricLine(time, content));
            }
        }

        if (timed.Count > 0)
        {
            timed.Sort((left, right) => left.Time.CompareTo(right.Time));
            return new MusicLyrics(timed, synced: true);
        }

        return plain.Count > 0 ? new MusicLyrics(plain, synced: false) : Empty();
    }

    /// <summary>
    /// 此刻该亮第几句。二分找「最后一句时间不超过 <paramref name="seconds"/>」，返回 -1 表示还没到第一句。
    /// 纯文本与空歌词一律 -1 —— 没有时间轴就没有「正在唱」。
    /// </summary>
    public int ActiveIndex(double seconds)
    {
        if (!Synced || Lines.Count == 0 || !double.IsFinite(seconds)) return -1;
        var low = 0;
        var high = Lines.Count - 1;
        var found = -1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            if (Lines[middle].Time.TotalSeconds <= seconds + 0.0005)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }
        return found;
    }

    private static MusicLyrics Empty() => new(Array.Empty<MusicLyricLine>(), synced: false);

    private static TimeSpan TimeOf(Match stamp)
    {
        var minutes = int.Parse(stamp.Groups[1].Value);
        var seconds = int.Parse(stamp.Groups[2].Value);
        var fraction = stamp.Groups[3].Value;
        var milliseconds = fraction.Length switch
        {
            0 => 0,
            1 => int.Parse(fraction) * 100,
            2 => int.Parse(fraction) * 10,
            _ => int.Parse(fraction[..3])
        };
        return TimeSpan.FromMilliseconds((minutes * 60L + seconds) * 1000 + milliseconds);
    }

    private static TimeSpan ReadOffset(string raw)
    {
        foreach (var line in raw.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("[offset:", StringComparison.OrdinalIgnoreCase)) continue;
            var end = trimmed.IndexOf(']');
            if (end < 0) continue;
            if (int.TryParse(trimmed[8..end].Trim(), out var value))
                return TimeSpan.FromMilliseconds(value);
        }
        return TimeSpan.Zero;
    }
}
