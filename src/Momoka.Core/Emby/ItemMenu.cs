using Momoka.MoviePilot;

namespace Momoka.Emby;

/// <summary>
/// 「更多」菜单上的一条命令。每一行都是其中一个，行上写什么字由 <see cref="ItemMenu"/> 定 —— 有几条的字跟着
/// 条目自己的状态走（「继续播放」还是「立即播放」、「标记为已观看」还是「标记为未观看」）。
/// </summary>
public enum ItemCommand
{
    /// <summary>立即播放 / 继续播放。</summary>
    Play,

    /// <summary>详细信息：能播的条目才有这一条，落在详情页。</summary>
    Details,

    /// <summary>
    /// 打开：不能播的条目（剧集、合集、媒体库）只有这一条 —— 落在详情页还是另一格网格，不是该问用户的事。
    /// </summary>
    Open,

    MarkPlayed,

    /// <summary>标记为未观看。半途看过的条目也有这一条：它把进度一起清掉，也就是用户说的「标记为未播放」。</summary>
    MarkUnplayed,

    Favorite,

    Unfavorite,

    /// <summary>从继续观看中移除：进度留着，只是这个条目不再出现在那一排里。</summary>
    HideFromResume,

    AddToCollection,

    /// <summary>下载到设备。一个文件下一个，一部剧或者一季把里头的单集都下下来。</summary>
    Download,

    EditMetadata,

    ChangeCover,

    Subtitles,

    /// <summary>
    /// 识别：把这个条目对到刮削源上的某一条记录（弹那张表，见 <see cref="ItemIdentify"/>）。和「缺什么补什么、
    /// 现有的一个字不动」的 <see cref="RefreshMetadata"/> 是两条不同的话。
    /// <para>
    /// 2026-10-10 由「刮削元数据信息」改来：从前这一条按下去就是一次全量重刮（按名字抓一遍、盖掉现有的），
    /// 而官方网页端那一条是「识别」—— 先把候选摆出来让人挑一条，再落下去。改的是同一个位置的同一件事，
    /// 只是把「服务器自己猜的那一条」换成「用户挑的那一条」。
    /// </para>
    /// </summary>
    Identify,

    /// <summary>管理版本：列出这个条目背后的文件（飞牛的「管理版本」）。</summary>
    ManageVersions,

    /// <summary>手动匹配影片：飞牛的刮削候选表。</summary>
    FnNasMatch,

    /// <summary>解除匹配影片：清掉飞牛上这个条目的识别结果。</summary>
    FnNasUnmatch,

    RefreshMetadata,

    ScanLibrary,

    OpenSeries,

    /// <summary>
    /// 查看遗漏集：服务器按刮削源的元数据算出这部剧（或这一季）本该有、而库里没有的单集，列在一张表上。
    /// <para>
    /// 只有剧和季有这一条 —— 电影没有「漏集」这回事，单集自己也不是一个装着一叠集的容器（它已经是其中
    /// 一集）。见 <see cref="ItemMenu"/> 里 <c>MissingEpisodes</c> 那一段。
    /// </para>
    /// </summary>
    ViewMissingEpisodes,

    /// <summary>
    /// 在 MoviePilot 搜索这个条目的其他版本：新开一个窗口，按条目算好的关键字（见
    /// <see cref="MoviePilotVersionQuery"/>）去 MoviePilot 的站点搜索里找可下载的版本。
    /// 只在用户开了 MoviePilot、且条目对得上一部片时出现。
    /// </summary>
    MoviePilotVersions,

    /// <summary>
    /// 让 MoviePilot 手动整理这个条目背后的文件：识别、改名、搬进媒体库目录（见
    /// <see cref="MoviePilot.MoviePilotTransfer"/>）。同「搜索其他版本」一道，只在开了 MoviePilot 且条目
    /// 对得上一部片时出现 —— 它会移动服务器上的文件，出现的位置比它能做的事保守。
    /// </summary>
    MoviePilotReorganize,

    /// <summary>折叠的「重新整理」子菜单：手动整理、智能整理和 AniBT 识别都收在这里；父项本身不执行动作。</summary>
    MoviePilotReorganizeMenu,

    /// <summary>折叠的 MoviePilot 媒体管理菜单；父项本身不执行删除。</summary>
    MoviePilotMediaManagement,
    MoviePilotDeleteRecord,
    MoviePilotDeleteSource,
    MoviePilotDeleteDestination,
    MoviePilotDeleteAll,

    /// <summary>二次确认后交给 MoviePilot 智能助手按原整理记录重整。</summary>
    MoviePilotSmartReorganize,

    /// <summary>用原文件名交 AniBT 识别；确认后按 MoviePilot 原记录重新整理，没有记录时用 Emby 识别。</summary>
    AniBtScrape,

    /// <summary>直接使用 AniBT Open API 搜索番剧及其资源。</summary>
    AniBtVersions,

    Delete
}

/// <summary>
/// 菜单上的一行：一条命令加它这一次该写的字，或者一条分隔线（<see cref="Command"/> 为空）。
/// </summary>
public readonly record struct ItemMenuEntry(ItemCommand? Command, string Label, IReadOnlyList<ItemMenuEntry>? Children = null)
{
    /// <summary>一条分隔线。</summary>
    public static ItemMenuEntry Rule { get; } = new(null, "");

    public bool IsRule => Command is null;
}

/// <summary>
/// 菜单上那几条要看权限的入口所依据的账号能力。每一项只在服务器明确答 false 时收起对应入口；
/// null 是「还没问到」——照旧显示，由服务器最终拒绝。
/// </summary>
public readonly record struct ItemMenuPermissions(
    bool? IsAdministrator = null,
    bool? CanDownload = null,
    bool? CanDeleteContent = null,
    bool? CanManageSubtitles = null);

/// <summary>
/// 一张卡片的「更多」菜单上有哪几条、按什么次序、每条写什么字 —— 整个答案由条目自己决定，一个输入一个输出。
/// <para>
/// 放在 Core 而不是留在菜单那段代码里，理由是覆盖：单元测试只够得到 Core，而「这一类条目该有哪几条」正是那种
/// 写错了屏上也看着正常的判断 —— 一部剧上冒出「搜索字幕」（剧集不是一个文件，服务器没处可放）、一张媒体库卡片
/// 上冒出「删除」（那会去删整个库的目录），两样都要有人一条条盯着才看得出来。
/// </para>
/// <para>
/// <b>判据是条目的类型和状态，不是这张卡画在哪一页。</b>同一个条目会同时出现在继续观看、媒体库和搜索结果里，
/// 而「它是不是一个文件」「它有没有看到一半」在三处是同一个答案。按页面分菜单的下场是同一张卡在两个地方点开
/// 来不一样，而两边都说不出道理。
/// </para>
/// <para>
/// <b>每一行的中文措辞不超过七个字（2026-10-27 用户令）</b>，弹表的那几条以「…」收尾；品牌名保持原样，
/// 版本来源的子菜单只留「MoviePilot」和「AniBT」两个名字。菜单宽度由最长的一行决定，一条长的就把整张菜单
/// 撑宽 —— 续播位置不再写进「继续播放」那一条，卡片自己画着进度。
/// </para>
/// <para>
/// 「重新整理」把手动整理、智能整理和 AniBT 识别收成一层子菜单（2026-10-30 用户令）：这三条都是对文件本身
/// 动手的整理动作，平铺时会让菜单更长；分组以后和「媒体管理」的折叠范围并排，找整理入口仍然只有一处。
/// </para>
/// </summary>
public static class ItemMenu
{
    public static bool Allows(ItemCommand command, ItemMenuPermissions permissions) => command switch
    {
        ItemCommand.Download => CanDownload(permissions),
        ItemCommand.Delete or ItemCommand.ManageVersions => CanDelete(permissions),
        ItemCommand.Subtitles => CanManageSubtitles(permissions),
        ItemCommand.EditMetadata or ItemCommand.ChangeCover or ItemCommand.Identify or ItemCommand.RefreshMetadata
            or ItemCommand.ScanLibrary or ItemCommand.FnNasMatch or ItemCommand.FnNasUnmatch
            or ItemCommand.AddToCollection => IsAdministrator(permissions),
        _ => true
    };
    /// <summary>
    /// 这个条目的菜单，从上到下。分隔线已经摆好：空的那一段不会留下一条线，也不会有两条线挨在一起、
    /// 或者一条线开头结尾（见 <see cref="Join"/>）。
    /// </summary>
    /// <param name="moviePilotEnabled">
    /// 用户在设置里开没开 MoviePilot。开了才给「在 MoviePilot 搜索其他版本」那一条 —— 没开就和从前一模一样，
    /// 免得点下去弹个连不上的窗口。默认关：全新装机没有 MoviePilot，测试里不点名这一项的也照旧只看基础菜单。
    /// </param>
    /// <param name="aniBtScrapeAvailable">识别要求当前管理来源；独立版本搜索不受此限制，Shell 传本次来源能力。</param>
    /// <param name="permissions">
    /// 账号确认过的能力（<see cref="ItemMenuPermissions"/>）。只有服务器明确答 false 才收起对应入口；
    /// 默认全是 null，也就是「还没问到」——照旧显示，由服务器最终拒绝。
    /// </param>
    public static IReadOnlyList<ItemMenuEntry> For(EmbyItem item, bool moviePilotEnabled = false, bool animeGenre = false,
        bool jellyfin = false, bool fnNas = false, bool plex = false, bool aniBtScrapeAvailable = true,
        bool moviePilotManagementAvailable = false, bool moviePilotSmartAvailable = false,
        ItemMenuPermissions permissions = default, bool aniBtEnabled = true) => fnNas
        ? Join(Opening(item, false), UserState(item, true), FnNasManagement(item, permissions),
            MoviePilotBlock(item, moviePilotEnabled, animeGenre, false, moviePilotManagementAvailable, moviePilotSmartAvailable,
                manualReorganize: false, aniBtEnabled: aniBtEnabled), Navigation(item))
        // Plex answers 已观看/未观看 but has no per-item favourite, so that row is left out rather than
        // offered and then refused.
        : plex
        ? Join(Opening(item, false), UserState(item, true, favorites: false),
            MoviePilotBlock(item, moviePilotEnabled, animeGenre, false, moviePilotManagementAvailable, moviePilotSmartAvailable,
                manualReorganize: false, aniBtEnabled: aniBtEnabled), Navigation(item))
        : Join(
        Opening(item, jellyfin),
        MissingEpisodes(item),
        UserState(item, jellyfin),
        Organising(item, jellyfin, permissions),
        MoviePilotBlock(item, moviePilotEnabled, animeGenre, aniBtScrapeAvailable, moviePilotManagementAvailable, moviePilotSmartAvailable,
            aniBtEnabled: aniBtEnabled),
        Metadata(item, jellyfin, permissions),
        Navigation(item),
        Destructive(item, jellyfin, permissions));

    /// <summary>
    /// 菜单和迟到的点击共用同一能力判断；Jellyfin 没有保留续播点的隐藏接口，飞牛没有合集与
    /// HideFromResume，Plex 既没有收藏也没有保留续播点。
    /// </summary>
    public static bool Supports(ItemCommand command, bool jellyfin, bool fnNas = false, bool plex = false)
    {
        if (IsMoviePilot(command) && command != ItemCommand.MoviePilotReorganize || command == ItemCommand.AniBtVersions) return true;
        if (fnNas) return command is ItemCommand.Play or ItemCommand.Details or ItemCommand.Open or ItemCommand.MarkPlayed
            or ItemCommand.MarkUnplayed or ItemCommand.Favorite or ItemCommand.Unfavorite or ItemCommand.OpenSeries
            or ItemCommand.ManageVersions or ItemCommand.FnNasMatch or ItemCommand.FnNasUnmatch
            or ItemCommand.RefreshMetadata or ItemCommand.EditMetadata or ItemCommand.Delete
            or ItemCommand.MoviePilotVersions or ItemCommand.AniBtVersions;
        if (plex) return command is ItemCommand.Play or ItemCommand.Details or ItemCommand.Open or ItemCommand.MarkPlayed
            or ItemCommand.MarkUnplayed or ItemCommand.OpenSeries or ItemCommand.MoviePilotVersions or ItemCommand.AniBtVersions;
        return !jellyfin || command != ItemCommand.HideFromResume;
    }

    /// <summary>
    /// 飞牛的「更多」里照官网页端那六件事排：管理版本、手动匹配、解除匹配是一组；刷新与编辑元数据
    /// 一组；删除单独收尾。条目自己的播放／已看／收藏仍排在它们前面 —— 那是这个客户端一直在做的
    /// 事，删掉不是用户这次要的。
    /// <para>
    /// 管理版本只落在真有文件的条目上（电影、单集、视频）；剧和季是一叠文件，官网页端把版本表开在
    /// 具体那一集上。删除和其余五项都要求「目录里的一个条目」，媒体库本身和演职人员不在其中。
    /// </para>
    /// </summary>
    private static List<ItemMenuEntry> FnNasManagement(EmbyItem item, ItemMenuPermissions permissions)
    {
        if (!IsCatalogue(item)) return [];

        var rows = new List<ItemMenuEntry>(8);
        if (IsFile(item) && CanDelete(permissions)) rows.Add(new(ItemCommand.ManageVersions, "管理版本"));
        if (IsAdministrator(permissions))
        {
            rows.Add(new(ItemCommand.FnNasMatch, "手动匹配影片"));
            rows.Add(new(ItemCommand.FnNasUnmatch, "解除匹配影片"));
            rows.Add(ItemMenuEntry.Rule);
            rows.Add(new(ItemCommand.RefreshMetadata, "刷新元数据"));
            rows.Add(new(ItemCommand.EditMetadata, "编辑元数据"));
        }
        if (CanDelete(permissions))
        {
            if (rows.Count > 0) rows.Add(ItemMenuEntry.Rule);
            rows.Add(new(ItemCommand.Delete, "删除"));
        }
        return rows;
    }

    /// <summary>
    /// 查看遗漏集。**只有剧和季**：一部剧、一季是一叠文件，服务器按刮削源的元数据算得出「本该有哪几集、
    /// 库里缺哪几集」；电影没有「漏集」这回事，而单集自己就是那一叠里的一集，问它「你还缺谁」没有意义。
    /// <para>
    /// 和「缺失剧集」那个筛选（<c>IsMissing</c>）不是一回事 —— 那一位是 Jellyfin 的，Emby 会静默丢掉
    /// （见 <c>EmbyFilterBy</c> 那段）。这一条走的是 Emby 自己那条 <c>/Shows/Missing</c>，4.8 起就有，
    /// 官方网页端那三点的菜单里也挂着同一件事。
    /// </para>
    /// <para>
    /// 摆在「打开」之后：它是这一部剧／这一季上最想先看的一件事，埋在「删除」旁边就找不着了。
    /// </para>
    /// </summary>
    private static List<ItemMenuEntry> MissingEpisodes(EmbyItem item) =>
        item.Type is EmbyItemType.Series or EmbyItemType.Season
            ? [new(ItemCommand.ViewMissingEpisodes, "查看遗漏集")]
            : [];

    /// <summary>
    /// 一个文件：能播的那几种类型（电影、单集、视频……）背后正好是服务器上的一个文件。剧和季是一叠文件 ——
    /// 字幕挂在文件上，所以那两条对它们没有意义。
    /// </summary>
    private static bool IsFile(EmbyItem item) => item.IsPlayable;

    /// <summary>
    /// 目录里的一个条目：不是媒体库本身，也不是一个人名。
    /// <para>
    /// 「删除」尤其要看这一位：<c>DELETE /Items/{Id}</c> 连磁盘上的文件一起删，落在一张媒体库卡片上就是去删
    /// 整个库的目录。演职人员那一排是存根（只有 id、名字和类型），照它去刮削或者改封面是拿一个人名当影片使。
    /// </para>
    /// </summary>
    private static bool IsCatalogue(EmbyItem item) =>
        item.Type is not EmbyItemType.CollectionFolder and not EmbyItemType.Person;

    /// <summary>
    /// 一叠文件的容器，下载时要把里头的单集一个个下下来：剧和季。
    /// <para>
    /// 合集不算 —— 那是用户自己攒的一篮子东西，可以横跨几个库、几种类型，「下载这个合集」不是一句说得清的话。
    /// </para>
    /// </summary>
    public static bool IsEpisodeSet(EmbyItem item) =>
        item.Type is EmbyItemType.Series or EmbyItemType.Season;

    private static List<ItemMenuEntry> Opening(EmbyItem item, bool jellyfin) => item.IsPlayable
        ? [
            new(ItemCommand.Play, jellyfin ? "播放" : item.HasResumePosition ? "继续播放" : "立即播放"),
            new(ItemCommand.Details, "详细信息")
        ]
        : [new(ItemCommand.Open, "打开")];

    /// <summary>
    /// 已观看、收藏、从继续观看中移除 —— 都是「这个账号跟这个条目的关系」，服务器上存在用户数据那一头。
    /// <para>
    /// 「标记为已观看」和「标记为未观看」不是一个开关的两头，两条会同时出现：看到一半的条目既可以说「看完了」，
    /// 也可以说「没看过」（那一下把进度清掉）。从前这里只有一条、按 <c>Played</c> 翻面，于是继续观看那一排上
    /// 永远只有「标记为已观看」—— 用户要的「标记为未播放」在那儿根本没有。
    /// </para>
    /// <para>
    /// 「从继续观看中移除」只在真看到一半的条目上出现，而它和上面那条不重复：这一条把进度<b>留着</b>，只是那一排
    /// 里不再列它（服务器上是 <c>HideFromResume</c>），下次还能接着看；「标记为未观看」是把进度清成零。
    /// </para>
    /// </summary>
    private static List<ItemMenuEntry> UserState(EmbyItem item, bool jellyfin, bool favorites = true)
    {
        var rows = new List<ItemMenuEntry>(4);
        if (!item.TracksUserState) return rows;

        var played = item.UserData?.Played == true;
        if (!played) rows.Add(new(ItemCommand.MarkPlayed, jellyfin ? "标为已播放" : "标记为已观看"));
        if (played || item.HasResumePosition)
            rows.Add(new(ItemCommand.MarkUnplayed, jellyfin ? "标为未播放" : "标记为未观看"));

        if (favorites)
            rows.Add(item.UserData?.IsFavorite == true
                ? new(ItemCommand.Unfavorite, "取消收藏")
                : new(ItemCommand.Favorite, jellyfin ? "加入收藏" : "添加到收藏"));

        if (item.HasResumePosition && Supports(ItemCommand.HideFromResume, jellyfin))
            rows.Add(new(ItemCommand.HideFromResume, "从继续观看移除"));

        return rows;
    }

    /// <summary>把它放进哪儿、拿到哪儿去：合集和下载。</summary>
    private static List<ItemMenuEntry> Organising(EmbyItem item, bool jellyfin, ItemMenuPermissions permissions)
    {
        var rows = new List<ItemMenuEntry>(2);
        if (IsCatalogue(item) && CanEdit(permissions))
            rows.Add(new(ItemCommand.AddToCollection, jellyfin ? "加入集合" : "添加到合集…"));
        if ((IsFile(item) || IsEpisodeSet(item)) && CanDownload(permissions))
            rows.Add(new(ItemCommand.Download, jellyfin ? "下载" : "下载到设备"));
        return rows;
    }

    /// <summary>
    /// MoviePilot 跨媒体服务器共用历史管理；媒体库、合集和演员没有一部作品的整理范围。
    /// 保留 FnNas/Plex 不提供原手动整理的边界，管理与智能助手分别按已确认的权限显示。
    /// 三条整理入口（手动整理、智能整理、AniBT 识别）收进「重新整理」子菜单，和删除范围一样用折叠收窄菜单宽度；
    /// 子菜单一个可用条目都没有时整组不出现。删除范围在确认表完整说明，菜单仍遵守七字以内的既有约定。
    /// </summary>
    private static List<ItemMenuEntry> MoviePilotBlock(EmbyItem item, bool enabled, bool animeGenre, bool canScrape,
        bool canManage, bool canSmartReorganize, bool manualReorganize = true, bool aniBtEnabled = true)
    {
        if (!enabled || !MoviePilotVersionQuery.Supports(item)) return [];
        List<ItemMenuEntry> rows =
        [
            new(ItemCommand.MoviePilotVersions, "搜索其他版本")
        ];

        var reorganize = new List<ItemMenuEntry>(3);
        if (manualReorganize) reorganize.Add(new(ItemCommand.MoviePilotReorganize, "手动整理…"));
        if (canManage && canSmartReorganize)
            reorganize.Add(new(ItemCommand.MoviePilotSmartReorganize,
                item.Type is EmbyItemType.Series or EmbyItemType.Season ? "智能批量整理" : "智能整理"));
        if (aniBtEnabled && canScrape && AniBt.AniBtEligibility.CanIdentify(item, animeGenre))
            reorganize.Add(new(ItemCommand.AniBtScrape, "AniBT 识别"));
        if (reorganize.Count > 0)
            rows.Add(new(ItemCommand.MoviePilotReorganizeMenu, "重新整理", reorganize));

        if (canManage)
            rows.Add(new(ItemCommand.MoviePilotMediaManagement, "媒体管理",
            [
                new(ItemCommand.MoviePilotDeleteRecord, "仅删除记录"),
                new(ItemCommand.MoviePilotDeleteSource, "删记录和源文件"),
                new(ItemCommand.MoviePilotDeleteDestination, "删记录和库文件"),
                new(ItemCommand.MoviePilotDeleteAll, "删除记录及两端")
            ]));
        if (aniBtEnabled && AniBt.AniBtEligibility.Supports(item) && (animeGenre || AniBt.AniBtEligibility.HasGenre(item)))
            rows[0] = new(ItemCommand.MoviePilotVersions, "搜索不同版本",
                [new(ItemCommand.MoviePilotVersions, "MoviePilot"), new(ItemCommand.AniBtVersions, "AniBT")]);
        return rows;
    }

    public static bool IsMoviePilot(ItemCommand command) => command is ItemCommand.MoviePilotVersions
        or ItemCommand.MoviePilotReorganize or ItemCommand.MoviePilotReorganizeMenu or ItemCommand.MoviePilotMediaManagement
        or ItemCommand.MoviePilotDeleteRecord or ItemCommand.MoviePilotDeleteSource
        or ItemCommand.MoviePilotDeleteDestination or ItemCommand.MoviePilotDeleteAll or ItemCommand.MoviePilotSmartReorganize;

    /// <summary>
    /// 元数据那一叠。<see cref="ItemCommand.Identify"/> 和 <see cref="ItemCommand.RefreshMetadata"/> 是两条不同的话：
    /// 识别是「把候选摆出来，挑一条落下去」，刷新是「缺什么补什么，现有的一个字不动」。
    /// <para>
    /// 识别还要再看 <see cref="ItemIdentify.Supports"/>：服务器的 <c>Items/RemoteSearch</c> 只认电影、剧、合集、
    /// 预告片和音乐那几种 —— 季和单集没有「单独对哪一部片子」这回事（它们跟着所属的剧一起识别），文件夹和播放
    /// 列表也没有可对上的片子。给那几种摆一条「识别」，点下去只有 404。
    /// </para>
    /// </summary>
    private static List<ItemMenuEntry> Metadata(EmbyItem item, bool jellyfin, ItemMenuPermissions permissions)
    {
        // 编辑元数据对媒体库自己也成立（库有名字、简介和封面），所以它不看 IsCatalogue。
        var rows = new List<ItemMenuEntry>(6);
        if (CanEdit(permissions)) rows.Add(new(ItemCommand.EditMetadata, jellyfin ? "编辑元数据" : "编辑元数据…"));

        if (IsCatalogue(item))
        {
            if (CanEdit(permissions)) rows.Add(new(ItemCommand.ChangeCover, jellyfin ? "修改图片" : "修改封面图…"));
            if (IsFile(item) && CanManageSubtitles(permissions))
                rows.Add(new(ItemCommand.Subtitles, jellyfin ? "修改字幕" : "搜索字幕…"));
            if (ItemIdentify.Supports(item) && IsAdministrator(permissions))
                rows.Add(new(ItemCommand.Identify, jellyfin ? "刮削元数据" : "识别"));
            if (IsAdministrator(permissions)) rows.Add(new(ItemCommand.RefreshMetadata, "刷新元数据"));
        }

        // 扫库是服务器整体的一件事，跟这个条目是什么关系不大 —— 演职人员那一排除外，那不是库里的东西。
        if (item.Type != EmbyItemType.Person && IsAdministrator(permissions))
            rows.Add(new(ItemCommand.ScanLibrary, "重新扫描媒体库"));

        return rows;
    }

    private static List<ItemMenuEntry> Navigation(EmbyItem item) =>
        item.Type == EmbyItemType.Episode && item.SeriesId is { Length: > 0 }
            ? [new(ItemCommand.OpenSeries, "打开所属剧集")]
            : [];

    /// <summary>删除，单独一段摆在最后：它连磁盘上的文件一起删，不该和「刷新元数据」贴在一起被误点。</summary>
    private static List<ItemMenuEntry> Destructive(EmbyItem item, bool jellyfin, ItemMenuPermissions permissions) =>
        IsCatalogue(item) && CanDelete(permissions)
            ? [new(ItemCommand.Delete, jellyfin ? JellyfinDeleteLabel(item) : "删除")]
            : [];

    /// <summary>
    /// 编辑这一条（编辑元数据、改封面、加入合集）。官方网页端在条目没答 <c>CanEditItems</c> 时要求管理员；
    /// 这里只按账号策略判断，管理员未知就照旧显示，由服务器拒绝。
    /// </summary>
    private static bool CanEdit(ItemMenuPermissions permissions) => permissions.IsAdministrator != false;

    /// <summary>删这一条：看账号的删除策略；未知按可用处理。</summary>
    private static bool CanDelete(ItemMenuPermissions permissions) => permissions.CanDeleteContent != false;

    /// <summary>下载：看账号的下载策略；未知按可用处理。剧和季也走这一条。</summary>
    private static bool CanDownload(ItemMenuPermissions permissions) => permissions.CanDownload != false;

    /// <summary>
    /// 字幕：账号明确答能管字幕就放行；两个策略位都没答时退回管理员判断（官方网页端在没答时也要求管理员）。
    /// </summary>
    private static bool CanManageSubtitles(ItemMenuPermissions permissions) =>
        permissions.CanManageSubtitles == true
        || (permissions.CanManageSubtitles is null && permissions.IsAdministrator != false);

    /// <summary>识别、刷新元数据、扫库都要求管理员；未知按可用处理，由服务器拒绝。</summary>
    private static bool IsAdministrator(ItemMenuPermissions permissions) => permissions.IsAdministrator != false;

    /// <summary>The context-specific wording Jellyfin Web uses for its delete command.</summary>
    private static string JellyfinDeleteLabel(EmbyItem item) => item.Type switch
    {
        EmbyItemType.Series => "删除系列",
        EmbyItemType.Episode => "删除剧集",
        EmbyItemType.Playlist or EmbyItemType.BoxSet => "删除",
        _ => "删除媒体"
    };

    /// <summary>
    /// 删除之前那一句问话：标题和正文。
    /// <para>
    /// <b>这是整个客户端上唯一一句「按下去就没得恢复」的话</b>（<c>DELETE /Items/{id}</c> 连服务器磁盘上的文件
    /// 一起删），而它从前只活在外壳的代码后置里、一条测试都碰不到。2026-09-05 搬进 Core，钉的是三件事：话里
    /// 说清了删的是什么（片名）、说清了删到哪一步（磁盘上的文件、不可撤销），以及**一叠单集要多一句**。
    /// </para>
    /// <para>
    /// 那多出来的一句不是客套：在一部剧或者一季上按下删除，删掉的是里面每一集，而标题里的「剧」「季」两个字
    /// 说不出这件事 —— 少了它，一次点击和用户以为的事情差着二十四个文件。
    /// </para>
    /// <para>
    /// 类型名问不出来时退到「条目」而不是留空：标题会变成「删除这个」，读起来像句子断在半截上。
    /// </para>
    /// </summary>
    public static (string Title, string Body) DeletePrompt(EmbyItem item)
    {
        var what = item.DisplayTypeName is { Length: > 0 } kind ? kind : "条目";

        return ($"删除这个{what}",
            $"将从媒体库中删除「{item.Name}」，并把它在服务器磁盘上的文件一起删掉。此操作无法撤销。"
                + (IsEpisodeSet(item) ? "里面的所有单集都会被删除。" : ""));
    }

    /// <summary>
    /// 把几段拼成一张菜单，段与段之间摆一条分隔线。空的段整段跳过 —— 不然屏上就是两条挨着的线，或者一条
    /// 开头、结尾的线，而那正是「哪几条不出现」这件事唯一会留下的痕迹。
    /// </summary>
    private static IReadOnlyList<ItemMenuEntry> Join(params List<ItemMenuEntry>[] blocks)
    {
        var rows = new List<ItemMenuEntry>(16);

        foreach (var block in blocks)
        {
            if (block.Count == 0) continue;
            if (rows.Count > 0) rows.Add(ItemMenuEntry.Rule);
            rows.AddRange(block);
        }

        return rows;
    }
}
