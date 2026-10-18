namespace Momoka.Emby;

/// <summary>
/// 主页「服务器上有没有新入库的东西」的判据：把每个来源此刻最新的一批条目折成一行字（<see cref="Stamp"/>），
/// 和上一次记下的比一比，只说哪些来源的样子变了。
/// <para>
/// 为什么要有这一层：主页停着不动的时候，服务器新入库的内容不会自己出现（这一页只在导航进来时读一次）。
/// 补这一档的办法是定时问一句，但**不能问完就整页重读** —— 没变化也重读，等于每隔半分钟把一页海报连轮播
/// 全拆了重装，屏上闪一下、滚动位置和轮播位置全丢。所以定时那一趟只拿指纹，真变了才走重读。
/// </para>
/// <para>
/// 按来源分开记，不是把所有来源并成一份：多账号聚合时某个来源这一趟恰好读失败（超时、断线）会让合并后的
/// 清单少一截，拿它当「变了」就是每隔半分钟白刷一次。读不出来的来源由调用方直接不放进这一趟的结果里
/// （<see cref="Changed"/> 只认交上来的那些），它上一次的读数因此原样留着。
/// </para>
/// <para>
/// 纯的、在 Core 里，理由和 <see cref="HomeRefresh"/> 一样：指纹怎么算、什么算变了，是能被单测钉住的那部分，
/// 而屏上看不出来 —— 判错的那一版只会表现为「偶尔自己刷一下」或者「有新东西却一直不动」。
/// </para>
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
