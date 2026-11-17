namespace Momoka.Emby;

/// <summary>
/// 本次运行里，主页各媒体库「最近添加」排上已经被手动标记已看的条目。
/// <para>
/// 用户令（2026-11-17）：「在主页的最近添加 点击标记为已观看之后，要自动隐藏」。只记这一件事，不参与删除
/// （<see cref="RemovedMedia"/>），也不挡媒体库、搜索和详情页 —— 那些地方已看的条目照旧还在。按来源与 Id
/// 认同一件，两台服务器上同号的条目不互相遮挡。
/// </para>
/// <para>
/// 只在内存里、不写设置：与 <see cref="RemovedMedia"/> 同一条规矩，关闭程序即清空。页面被缓存回收、重新
/// 建出来时仍从这一份里读，所以「隐藏」在同一次运行里不会因为走开再回来就失效。
/// </para>
/// </summary>
public sealed class HiddenWatchedMedia
{
    private readonly object _gate = new();
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

    /// <summary>本次运行记下的件数；关闭程序即归零。</summary>
    public int Count
    {
        get
        {
            lock (_gate) return _keys.Count;
        }
    }

    /// <summary>记住这一件已经在主页「最近添加」上被标成已看。同一件重复记住只算一次。</summary>
    public void Remember(EmbyItem item)
    {
        if (string.IsNullOrEmpty(item.Id)) return;
        lock (_gate) _keys.Add(item.IdentityKey);
    }

    /// <summary>这一件是不是已经在「最近添加」上被标成已看、不该再出现。</summary>
    public bool Hides(EmbyItem item)
    {
        lock (_gate) return _keys.Contains(item.IdentityKey);
    }

    /// <summary>忘掉全部记录。给测试，以及整份换身份、要按新服务器重新读一遍的地方用。</summary>
    public void Clear()
    {
        lock (_gate) _keys.Clear();
    }
}
