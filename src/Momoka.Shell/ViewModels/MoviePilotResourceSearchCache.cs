using Momoka.MoviePilot;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// 「MoviePilot 资源」这一格上次搜到的一批东西（用户令 2026-11-22「持久化保存 MoviePilot 资源搜索的资源，
/// 搜索其他资源时重置」）。
/// <para>
/// 只活一次运行：换页面、从详情页跳进来再回去都还能摆出上一批；应用重启就没了。不落盘 —— 它是一屏搜索
/// 结果，不是用户的资料。下一次搜索（不论成功、空结果还是失败）都会整份换掉，不会把上一部片的结果挂到
/// 新词下面。
/// </para>
/// </summary>
internal sealed class MoviePilotResourceSearchCache
{
    private MoviePilotResourceSearchSnapshot? _last;

    internal void Save(MoviePilotResourceSearchSnapshot snapshot) => _last = snapshot;

    /// <summary>
    /// 同一连接、同一个词才算命中。连接换过（地址/账号/令牌代次变了）或者词不同，都按「没有」处理 ——
    /// 旧结果既不属于这条连接，也不是这次要找的东西。
    /// </summary>
    internal MoviePilotResourceSearchSnapshot? Match(string connectionStamp, string keyword)
    {
        var text = keyword.Trim();
        return _last is { } last && text.Length > 0
            && string.Equals(last.ConnectionStamp, connectionStamp, StringComparison.Ordinal)
            && string.Equals(last.Keyword, text, StringComparison.Ordinal) ? last : null;
    }
}

/// <summary>
/// 一份可复原的资源搜索结果：只要媒体数据与那几句说明，不带行对象、命令、下载过程状态或页面引用。
/// </summary>
internal sealed record MoviePilotResourceSearchSnapshot(string ConnectionStamp, string Keyword,
    IReadOnlyList<MoviePilotResource> Resources, int? AutoEpisode, string EmptyNotice, bool ShowEmptyNotice,
    MoviePilotDetailSearch? DetailSearch = null, IReadOnlyList<MoviePilotSearchPage>? NextPages = null);
