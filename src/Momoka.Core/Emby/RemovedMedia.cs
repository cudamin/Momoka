namespace Momoka.Emby;

/// <summary>
/// 本次运行里已经删掉媒体库文件的条目。服务器上的元数据往往要等到下一次扫描目录才消失，在那之前列表接口
/// 仍会把这些条目原样发回来；这份记忆让主页在同一个进程里不再展示它们。
/// <para>
/// <b>只在内存里，不写设置、不落磁盘。</b>用户令 2026-11-03：隐藏状态关闭 Momoka 后即清空 ——
/// 重启之后服务器扫描结果是什么就显示什么，不拿上一轮运行时的判断替它做主。
/// </para>
/// <para>
/// 判据是「同一份文件已经不在了」而不是「这个 id 见过」：删一部剧，它的季和单集一起不显示；删一季，只有那一季的
/// 单集；删一集，只有那一集。两台服务器上编号相同的条目是两件东西，所以来源也要对上。
/// </para>
/// </summary>
public sealed class RemovedMedia
{
    private readonly object _gate = new();
    private readonly List<EmbyItem> _items = [];

    /// <summary>本次运行记下的件数；关闭程序即归零。</summary>
    public int Count
    {
        get { lock (_gate) return _items.Count; }
    }

    /// <summary>记住这一件已经删掉。同一件重复提交（菜单和确认窗各喊一声）只算一次。</summary>
    public void Remember(EmbyItem item)
    {
        if (string.IsNullOrEmpty(item.Id)) return;
        lock (_gate)
        {
            if (_items.Any(known => SameSource(known, item) && known.Id == item.Id)) return;
            _items.Add(item);
        }
    }

    /// <summary>这个条目是不是属于已经删掉的那一份媒体。</summary>
    public bool Hides(EmbyItem item)
    {
        lock (_gate) return _items.Any(removed => Covers(removed, item));
    }

    /// <summary>忘掉全部记录。给测试，以及整份换身份、要按新服务器重新读一遍的地方用。</summary>
    public void Clear()
    {
        lock (_gate) _items.Clear();
    }

    /// <summary>
    /// 删掉 <paramref name="removed"/> 之后，<paramref name="candidate"/> 是不是也该一起不显示：它自己、它的
    /// 直接子项，以及剧／季下面那些单集。来源不同的一律不算。
    /// </summary>
    public static bool Covers(EmbyItem removed, EmbyItem candidate)
    {
        if (!SameSource(removed, candidate)) return false;
        if (candidate.Id == removed.Id || candidate.ParentId == removed.Id) return true;
        if (removed.Type == EmbyItemType.Series && candidate.SeriesId == removed.Id) return true;
        return removed.Type == EmbyItemType.Season && candidate.SeasonId == removed.Id;
    }

    private static bool SameSource(EmbyItem left, EmbyItem right) =>
        string.Equals(left.Source?.Key, right.Source?.Key, StringComparison.Ordinal);
}
