namespace Momoka.Danmaku;

/// <summary>
/// 进度条弹幕热力的纯计算快照：按秒聚合已加载弹幕，再按轨道宽度切桶。
/// <para>
/// 只统计调用方传入的弹幕，不发请求、不读设置、不碰渲染。归一化用非零桶的 95 分位再开方，
/// 一个几万条的高能点不会把整条进度条压成一条平线；关掉弹幕显示或换片时由调用方丢掉快照。
/// </para>
/// </summary>
public sealed class DanmakuHeat
{
    private const int MaximumSeconds = 86400;

    private readonly int[] _perSecond;

    private DanmakuHeat(double duration, int[] perSecond, int total, int peakSecond)
    {
        Duration = duration;
        _perSecond = perSecond;
        Total = total;
        PeakSecond = peakSecond;
    }

    public static DanmakuHeat Empty { get; } = new(0, [], 0, -1);

    /// <summary>快照对应的视频时长；未知或非正数时是空快照。</summary>
    public double Duration { get; }

    /// <summary>每一整秒的弹幕条数，下标即秒。</summary>
    public IReadOnlyList<int> PerSecond => _perSecond;

    /// <summary>落在时长范围内的弹幕总数。</summary>
    public int Total { get; }

    /// <summary>条数最多的那一秒；空快照为 -1。</summary>
    public int PeakSecond { get; }

    public bool IsEmpty => Total == 0;

    /// <summary>按时间聚合成一条 1 秒粒度的直方图；超出时长、负数和非有限时间都不进入统计。</summary>
    public static DanmakuHeat Build(IEnumerable<DanmakuComment> comments, double duration)
    {
        if (!double.IsFinite(duration) || duration <= 0) return Empty;
        var seconds = (int)Math.Min(Math.Ceiling(duration), MaximumSeconds);
        if (seconds <= 0) return Empty;

        var counts = new int[seconds];
        var total = 0;
        var peak = 0;
        var peakSecond = -1;
        foreach (var comment in comments)
        {
            var time = comment.Time;
            if (!double.IsFinite(time) || time is < 0 || time >= duration) continue;
            var index = Math.Min((int)time, seconds - 1);
            var count = ++counts[index];
            total++;
            if (count <= peak) continue;
            peak = count;
            peakSecond = index;
        }
        return total == 0 ? Empty : new DanmakuHeat(duration, counts, total, peakSecond);
    }

    /// <summary>
    /// 把按秒直方图切成 <paramref name="buckets"/> 个进度条桶，返回 0..1 的柱高。
    /// 每个整秒按桶中心归入一个桶；非零桶的 95 分位当满高，再开方压缩长尾。
    /// </summary>
    public double[] Levels(int buckets)
    {
        if (buckets <= 0 || Total == 0 || Duration <= 0) return [];
        buckets = Math.Clamp(buckets, 1, _perSecond.Length);

        var sums = new int[buckets];
        for (var second = 0; second < _perSecond.Length; second++)
        {
            var count = _perSecond[second];
            if (count == 0) continue;
            var bucket = (int)((second + 0.5) * buckets / Duration);
            sums[Math.Min(bucket, buckets - 1)] += count;
        }

        var nonZero = sums.Where(value => value > 0).OrderBy(value => value).ToArray();
        if (nonZero.Length == 0) return new double[buckets];
        var ceiling = Math.Max(1, nonZero[Math.Min(nonZero.Length - 1, (int)Math.Ceiling(nonZero.Length * 0.95) - 1)]);

        var levels = new double[buckets];
        for (var i = 0; i < buckets; i++)
            levels[i] = Math.Sqrt(Math.Min(1d, sums[i] / (double)ceiling));
        return levels;
    }

    /// <summary>指针前后各 <paramref name="windowSeconds"/> 秒内的弹幕条数，用于悬停读数。</summary>
    public int CountAround(double seconds, double windowSeconds = 5)
    {
        if (Total == 0 || !double.IsFinite(seconds)) return 0;
        var half = Math.Max(0, windowSeconds) / 2;
        var start = Math.Max(0, seconds - half);
        var end = Math.Min(Duration, seconds + half);
        var first = (int)Math.Clamp(Math.Floor(start), 0, _perSecond.Length);
        var last = (int)Math.Clamp(Math.Ceiling(end), 0, _perSecond.Length);
        var sum = 0;
        for (var second = first; second < last && second < _perSecond.Length; second++)
            if (second >= 0) sum += _perSecond[second];
        return sum;
    }
}
