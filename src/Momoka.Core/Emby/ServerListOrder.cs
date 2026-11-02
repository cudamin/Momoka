namespace Momoka.Emby;

/// <summary>服务器页的排序方式。Manual 是拖拽出来的保存顺序，其余两种按显示字段重排。</summary>
public enum ServerSortMode
{
    Manual,
    Name,
    Status
}

/// <summary>
/// 服务器页两层列表的排序规则：服务器组和组内账号共用同一套模式。纯函数，方便单测；
/// 默认用当前区域文化的比较器，中文名称按系统排序习惯走，而不是按 Unicode 码位。
/// </summary>
public static class ServerListOrder
{
    public const string ManualValue = "manual";
    public const string NameValue = "name";
    public const string StatusValue = "status";

    public static ServerSortMode Parse(string? value) => value switch
    {
        NameValue => ServerSortMode.Name,
        StatusValue => ServerSortMode.Status,
        _ => ServerSortMode.Manual
    };

    public static string Serialize(ServerSortMode mode) => mode switch
    {
        ServerSortMode.Name => NameValue,
        ServerSortMode.Status => StatusValue,
        _ => ManualValue
    };

    /// <summary>
    /// 返回按 <paramref name="mode"/> 排好的一组新记录；Manual 原样复制，保留调用方给的顺序。
    /// 状态排序先按 <paramref name="rank"/> 分组（数字小的在前），同组再按名称升序；方向只翻主键。
    /// </summary>
    public static IReadOnlyList<T> Apply<T>(
        IEnumerable<T> items,
        ServerSortMode mode,
        bool descending,
        Func<T, string> name,
        Func<T, int> rank,
        IComparer<string>? comparer = null)
    {
        var source = items.ToList();
        if (mode == ServerSortMode.Manual) return source;

        comparer ??= StringComparer.CurrentCultureIgnoreCase;

        return mode == ServerSortMode.Name
            ? (descending
                ? source.OrderByDescending(name, comparer)
                : source.OrderBy(name, comparer)).ToList()
            : (descending
                ? source.OrderByDescending(rank).ThenBy(name, comparer)
                : source.OrderBy(rank).ThenBy(name, comparer)).ToList();
    }
}
