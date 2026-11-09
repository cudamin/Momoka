namespace Momoka.Emby;

/// <summary>
/// 按来源保存最新条目的 ID 与顺序；收到库通知后比较，确实变化才重读货架与轮播。
/// 某来源读取失败时不提交该来源，保留原基线，避免把暂时少一批数据当成真实删除。
/// </summary>
public sealed class LibraryWatch
{
    /// <summary>指纹里条目之间的分隔符：一个不会出现在 id 里的控制字符。</summary>
    private const char Separator = '\u001f';

    private readonly Dictionary<string, string> _stamps = new(StringComparer.Ordinal);

    /// <summary>
    /// 一批条目此刻的样子。<b>顺序敏感</b>：<c>Items/Latest</c> 是「新到旧」，多一条新入库的就从头上插进来，
    /// 同一部剧来了新一集（服务器那边 <c>GroupItems</c> 并剧之后）也是头上那一条换人 —— 两种都该让指纹变。
    /// <para>
    /// 认不出的（没有 id 的）条目跳过而不是拿空串占位：服务器偶尔回一条半成品不该让指纹整个错位。
    /// </para>
    /// </summary>
    public static string Stamp(IEnumerable<EmbyItem> items)
    {
        var builder = new System.Text.StringBuilder();
        var count = 0;

        foreach (var item in items)
        {
            if (item.Id is not { Length: > 0 } id) continue;
            if (count > 0) builder.Append(Separator);
            builder.Append(id);
            count++;
        }

        return $"{count}:{builder}";
    }

    /// <summary>
    /// 记下这一批来源此刻的样子。整页重读之后调用，作为下一次比较的基准。
    /// <para>
    /// 只覆盖交上来的那些来源，其余原样留着 —— 这一趟没读到的来源不该被当成「它变成空的了」。
    /// </para>
    /// </summary>
    public void Reset(IReadOnlyDictionary<string, string> stamps)
    {
        foreach (var (source, stamp) in stamps) _stamps[source] = stamp;
    }

    /// <summary>
    /// 哪些来源的样子和上一次不一样（包括从没记过的）。空表就是「没有新入库的东西，这一趟什么都不用做」。
    /// <para>
    /// 只报不记：判定变了之后由调用方决定重读，重读（或者放弃重读）之后再 <see cref="Reset"/>。
    /// </para>
    /// </summary>
    public IReadOnlyList<string> Changed(IReadOnlyDictionary<string, string> stamps) =>
        [.. stamps
            .Where(entry => !_stamps.TryGetValue(entry.Key, out var known)
                || !string.Equals(known, entry.Value, StringComparison.Ordinal))
            .Select(entry => entry.Key)];

    /// <summary>忘掉全部读数。换账号、换服务器之后这一份不再作数（见 <c>HomeViewModel.Attach</c>）。</summary>
    public void Clear() => _stamps.Clear();
}
