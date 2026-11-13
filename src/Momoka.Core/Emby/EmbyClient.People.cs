using System.Net;

namespace Momoka.Emby;

/// <summary>
/// 人物相关的两条读取：这个人自己的条目，和「这个人参演了什么」。
/// <para>
/// 人物详情没有一条全服务器统一的专用路由：Jellyfin 网页端把一个 Person 当普通条目，走
/// <c>Users/{userId}/Items/{itemId}</c>；Emby 另有一条 <c>Persons/{Name}</c>。这里先按 id 问普通条目
/// （两家都认 id），404/400 时再退回那条按名字的路由 —— 两条路都返回 <c>BaseItemDto</c>，字段由
/// <see cref="EmbyFields.Person"/> 点名。
/// </para>
/// <para>
/// 作品清单不另开接口：它就是演职人员那一格一直在用的 <c>Users/{userId}/Items</c>，多带一个
/// <c>PersonIds</c>（以及可选的 <c>PersonTypes</c>）。这样排序、分页、图片和用户数据都与媒体库网格
/// 是同一套，人物页不需要自己解释一遍。
/// </para>
/// </summary>
public sealed partial class EmbyClient
{
    /// <summary>
    /// 读一个人物的条目。失败时抛 <see cref="EmbyApiException"/>；调用方决定是整页报错还是只收起
    /// 人物资料、仍把作品摆出来。
    /// </summary>
    public async Task<EmbyItem> GetPersonAsync(EmbyItem person, CancellationToken cancellationToken)
    {
        if (Connection.IsFnNas || Connection.IsPlex)
            throw new NotSupportedException("人物页只接 Emby 与 Jellyfin");

        if (person.Id.Length > 0)
        {
            try
            {
                return await GetItemAsync(person.Id, cancellationToken, EmbyFields.Person).ConfigureAwait(false);
            }
            // id 对不上（旧缓存、服务器换了 id 规则）时按名字再问一次。401/超时等不能吞掉：那一条
            // 按名字重试也只会得到同一句错误，还会把「登录过期」说成「找不到这个人」。
            catch (EmbyApiException error) when (error.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            {
            }
        }

        var name = person.Name.Trim();
        if (name.Length == 0)
            throw new EmbyApiException("人物条目缺少 id 和名字，无法读取详情");

        return await http.GetJsonAsync<EmbyItem>(
            Url($"Persons/{Uri.EscapeDataString(name)}",
                ("UserId", Connection.UserId),
                ("Fields", EmbyFields.Person)),
            Context,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 一个人物的作品。一个职务一趟：服务器答回的 <c>TotalRecordCount</c> 是这一种署名的总数，
    /// 影视作品每一组标题上的数字就是它，不需要为了数数把整份清单拉回来。
    /// </summary>
    /// <param name="personType">空表示不分职务（节目/电影/集那三排用）；否则是 <see cref="PersonDetail.CreditTypes"/> 之一。</param>
    public Task<ItemsResult> GetPersonCreditsAsync(
        string personId,
        string? personType,
        IReadOnlyList<string> includeItemTypes,
        string fields,
        int limit,
        string sortBy,
        bool descending,
        CancellationToken cancellationToken)
    {
        if (Connection.IsFnNas || Connection.IsPlex)
            throw new NotSupportedException("人物作品页只接 Emby 与 Jellyfin");

        var query = new ItemQuery
        {
            PersonId = personId,
            PersonTypes = personType is { Length: > 0 } type ? [type] : [],
            IncludeItemTypes = includeItemTypes,
            Recursive = true,
            Fields = fields,
            Limit = Math.Max(0, limit),
            SortBy = sortBy,
            Descending = descending
        };

        return GetItemsAsync(query, cancellationToken);
    }
}
