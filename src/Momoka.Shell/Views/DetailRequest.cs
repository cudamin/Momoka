using Momoka.Emby;

namespace Momoka.Shell.Views;

/// <summary>
/// Everything <see cref="DetailPage"/> needs. Carries the container and the shell for the same reason
/// <see cref="LibraryRequest"/> does — the page reaches for no static — and the item only as a starting
/// point: the page re-fetches it by id, because the fields a grid asks for leave out the media sources,
/// the cast and the chapters that the whole page is about.
/// </summary>
internal sealed record DetailRequest
{
    /// <inheritdoc cref="LibraryRequest.Services"/>
    public required IServiceProvider Services { get; init; }

    /// <summary>What the grid or shelf had. Used for the id, the type and the first heading.</summary>
    public required EmbyItem Item { get; init; }

    /// <summary>
    /// 本机媒体的集列表（剧集页/集页）。空表示这一页不是本地条目，或本地电影没有集。
    /// 有值时详情页不向任何服务器请求条目、季和集。
    /// </summary>
    public IReadOnlyList<EmbyItem> LocalEpisodes { get; init; } = [];

    public string Title => Item.Name;

    public static DetailRequest For(IServiceProvider services, EmbyItem item) =>
        new() { Services = services, Item = item };

    /// <summary>本机媒体的详情页请求：条目与集都来自本机索引，不连服务器。</summary>
    public static DetailRequest ForLocal(IServiceProvider services, EmbyItem item, IReadOnlyList<EmbyItem>? episodes = null) =>
        new() { Services = services, Item = item, LocalEpisodes = episodes ?? [] };

    /// <summary>
    /// Whether this item has a detail page at all, so the grid and the home shelves route the same way.
    /// A series and a season do — the page shows their episodes, seasons and cast — but a box set, a
    /// playlist and a plain folder are collections of unrelated things, and the answer to opening one is
    /// another grid.
    /// </summary>
    public static bool Supports(EmbyItem item) =>
        item.Type is EmbyItemType.Series or EmbyItemType.Season || EmbyItemType.IsPlayable(item.Type);
}
