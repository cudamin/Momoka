using System.Text;
using System.Text.Json;
using Momoka.Diagnostics;
using Momoka.Infrastructure;

namespace Momoka.MoviePilot;

/// <summary>
/// 日历页的本地缓存：把每次成功读到的六周窗口写进数据目录，下次打开先把缓存摆上屏，再向 MoviePilot 要新的。
/// <para>
/// <b>按「连接身份 + 日期窗口」存，不按月份存。</b>页面一次读的是月视图那六周（可能跨月），所以缓存命中判据
/// 也是「有一个覆盖这次窗口的完整快照」—— 按月份拼缓存会在月初月末各缺几天，屏上却看不出来少了什么。
/// 连接身份只取规范化后的服务地址和用户名：换服务器、换账号后旧缓存自然不命中；密码、令牌这类凭据不进缓存。
/// </para>
/// <para>
/// <b>失败一律当成没缓存。</b>文件坏了、目录不可写、版本认不出都只是少一份加速，日历本身仍有网络那条路；
/// 因此读取吞掉异常返回 null，写入失败也只记一行日志。写入走 <see cref="AtomicFile"/>，半截文件不会被读到。
/// </para>
/// <para>
/// 线程安全：读取发生在日历页，写入也在页面读取与启动后台刷新两条路上，都用同一把锁；文件只在第一次使用时读一次。
/// </para>
/// </summary>
public sealed class MoviePilotCalendarCache
{
    private const string Category = "moviepilot";
    private const int CurrentVersion = 1;

    /// <summary>最多留几个窗口。八个月足够翻回上一季，也不会让这份缓存无限长大。</summary>
    private const int MaximumWindows = 8;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private readonly string _file;
    private readonly object _gate = new();
    private Document? _document;
    private bool _loaded;

    public MoviePilotCalendarCache(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _file = Path.GetFullPath(filePath);
    }

    /// <summary>
    /// 缓存里覆盖 <paramref name="start"/>～<paramref name="endExclusive"/> 的那一份；没有、连接不符或文件读不了时返回
    /// null。条目的连接代次一律清空 —— 那是本次进程内的东西，缓存跨进程重启后只用于显示。
    /// </summary>
    public IReadOnlyList<MoviePilotCalendarEntry>? Read(string connection, DateOnly start, DateOnly endExclusive)
    {
        lock (_gate)
        {
            var document = Load();
            if (document is null || !string.Equals(document.Connection, connection, StringComparison.Ordinal)) return null;
            var window = document.Windows
                .Where(window => window.Start <= start && window.End >= endExclusive)
                .OrderByDescending(window => window.FetchedAt)
                .FirstOrDefault();
            return window?.Entries.Where(entry => entry is not null)
                .Select(entry => entry with { ConnectionStamp = "" }).ToArray();
        }
    }

    /// <summary>
    /// 用这次读到的结果替换同窗口的旧快照。缓存写失败不影响调用方 —— 日历已经拿到了数据，少一份加速而已。
    /// </summary>
    public void Write(string connection, DateOnly start, DateOnly endExclusive, IReadOnlyList<MoviePilotCalendarEntry> entries)
    {
        lock (_gate)
        {
            try
            {
                var document = Load();
                if (document is null || !string.Equals(document.Connection, connection, StringComparison.Ordinal))
                    document = new Document { Connection = connection };

                document.Windows.RemoveAll(window => window.Start == start && window.End == endExclusive);
                document.Windows.Add(new Window
                {
                    Start = start,
                    End = endExclusive,
                    FetchedAt = DateTimeOffset.Now,
                    Entries = [.. entries]
                });
                document.Windows = [.. document.Windows.OrderByDescending(window => window.FetchedAt).Take(MaximumWindows)];

                AtomicFile.WriteAllText(_file, JsonSerializer.Serialize(document, Options), Utf8);
                _document = document;
                _loaded = true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                Log.Info(Category, "日历缓存写入失败：" + Failure.Describe(error));
            }
        }
    }

    private Document? Load()
    {
        if (_loaded) return _document;
        _loaded = true;
        try
        {
            if (!File.Exists(_file)) return null;
            var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(_file, Utf8), Options);
            if (document is not { Version: CurrentVersion }) return null;

            // 手改或旧版本留下的显式 null 不应让读缓存变成异常；把两份集合修成可用形状。
            document.Windows ??= [];
            document.Windows = [.. document.Windows.Where(window => window is not null)];
            foreach (var window in document.Windows) window.Entries ??= [];
            _document = document;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            _document = null;
        }
        return _document;
    }

    private sealed class Document
    {
        public int Version { get; set; } = CurrentVersion;
        public string Connection { get; set; } = "";
        public List<Window> Windows { get; set; } = [];
    }

    private sealed class Window
    {
        public DateOnly Start { get; set; }
        public DateOnly End { get; set; }
        public DateTimeOffset FetchedAt { get; set; }
        public List<MoviePilotCalendarEntry> Entries { get; set; } = [];
    }
}
