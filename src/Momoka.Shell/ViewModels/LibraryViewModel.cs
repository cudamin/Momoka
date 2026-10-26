using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Momoka.Configuration;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.FnNas;
using Momoka.Infrastructure;
using Momoka.Services;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// One line of the sort menu: a key to choose, whether it is the one in force, and which way it runs.
/// <para>
/// The menu is a <c>ListView</c> in a flyout rather than a <c>MenuFlyout</c>, because the web client's
/// row is 「勾 + 字段名 + 方向箭头」 and a <c>MenuFlyoutItem</c> can draw a check or an icon but not a
/// second pressable thing at the right edge. Every decision that row needs is made here — which keys this
/// grid may offer, their order, which one is ticked, which arrow it carries — so the page's loop is a
/// translation with no judgement in it and the list can be read without a window on screen.
/// </para>
/// </summary>
/// <param name="Option">The key.</param>
/// <param name="IsChecked">Whether this is the key in force.</param>
/// <param name="Descending">Which way it runs; only drawn on the ticked row.</param>
public sealed record SortEntry(EmbySortOption Option, bool IsChecked, bool Descending)
{
    /// <summary>What the row says.</summary>
    public string Label => Option.Label;

    /// <summary>The tick, or nothing at all — the empty column is what keeps the labels aligned.</summary>
    public Visibility CheckVisibility => IsChecked ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The direction arrow, only on the row in force.</summary>
    public Visibility ArrowVisibility => CheckVisibility;

    /// <summary>The arrow itself: the same two glyphs the toolbar's sort button uses.</summary>
    public string Glyph => char.ConvertFromUtf32(Descending ? 0xE70D : 0xE70E);

    /// <summary>What pressing the arrow does, for its accessible name.</summary>
    public string ArrowName => Descending ? "改为升序" : "改为降序";
}

/// <summary>
/// 一格筛选条：屏上那句话，加上点掉它这个动作。<see cref="FilterChip"/> 是 Core 那半（这一格是什么、点掉它
/// 要动哪一处），这里加上的只有 <c>x:Bind</c> 要的那个命令。
/// <para>
/// 单独一个类型而不是直接把 <see cref="FilterChip"/> 摆上屏：那一格是 Core 里的一个记录，给它挂一个
/// <c>RelayCommand</c> 就等于把 UI 的东西装进 Core。
/// </para>
/// </summary>
public sealed partial class FilterChipRow
{
    private readonly Action<FilterChip>? _dropChip;
    private readonly Action? _drop;
    private readonly string? _label;

    internal FilterChipRow(FilterChip chip, Action<FilterChip> drop)
    {
        Chip = chip;
        _dropChip = drop;
    }

    /// <summary>飞牛那一套筛选不是 <see cref="FilterChip"/>：它只给一句屏上的字和一个去掉它的动作。</summary>
    internal FilterChipRow(string label, Action drop)
    {
        _label = label;
        _drop = drop;
    }

    internal FilterChip? Chip { get; }

    public string Label => Chip?.Label ?? _label ?? "";

    /// <summary>读屏软件念出来的那一句：光一个片名念不出「点它会怎样」。</summary>
    public string AutomationName => $"去掉筛选：{Label}";

    [RelayCommand]
    private void Drop()
    {
        if (Chip is { } chip) _dropChip?.Invoke(chip);
        else _drop?.Invoke();
    }
}

/// <summary>收藏页账号筛选浮层的一行；<see cref="Key"/> 为 null 代表全部账号。</summary>
public sealed record FavoriteAccountEntry(string? Key, string Label, bool IsChecked)
{
    public Visibility CheckVisibility => IsChecked ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>
/// A library, a folder, a season, a person's credits or a set of search results: one grid, whose only
/// difference from the next is the <see cref="LibraryRequest"/> behind it.
/// <para>
/// The paging is hand-written on purpose. <c>ISupportIncrementalLoading</c> is honoured only by the
/// ListViewBase controls; <c>ItemsView</c> and <c>ItemsRepeater</c> ignore it, so the collection would
/// have implemented an interface nothing calls. The page watches its own scroll offset instead and asks
/// here for the next page — which is the one part of this that has to stay in the view, because how far
/// down a list the user is is not something a view model can know.
/// </para>
/// <para>
/// 需求 4 (sort) and 需求 5 (filters) both live here rather than in their controls. Both outlive the
/// flyout they are set from: they go into every query, into the button that shows them, and into
/// settings, so the flyout is the one place that must not own them.
/// </para>
/// </summary>
public sealed partial class LibraryViewModel : PageViewModel
{
    private const string Category = "媒体库";

    /// <summary>Segoe Fluent codepoints; see the note in <see cref="CardItem"/> on why numbers.</summary>
    private const int AscendingIcon = 0xE70E;
    private const int DescendingIcon = 0xE70D;

    /// <summary>
    /// <see cref="_total"/> before the first page comes back, which is what
    /// <see cref="HasMore"/> reads as 「ask and find out」.
    /// </summary>
    private const int NotLoaded = -1;

    /// <summary>
    /// Which grid this is: the navigation payload, and at the same time the witness that
    /// <see cref="Attach"/> has run. The four services below arrive in that same call as non-nullable
    /// parameters, so the one gate every entry point already had covers all five.
    /// </summary>
    private LibraryRequest? _request;
    private IShellActions? _actions;

    private ISettingsService? _settings;
    private EmbyImageStore? _images;
    private IServerCapabilities? _capabilities;

    private Version? SourceServerVersion => _scope?.IsCurrent == true ? _capabilities?.ServerVersion
        : Version.TryParse(_scope?.Connection.ServerVersion, out var version) ? version : null;

    /// <summary>Total the server reported; <see cref="NotLoaded"/> until the first page has come back.</summary>
    private int _total = NotLoaded;

    /// <summary>
    /// 需求 5：what the filter panel is set to. Owned here rather than by the panel, because it outlives
    /// the flyout: it goes into every query, into the badge, and into settings.
    /// </summary>
    private ItemFilters _filters = new();

    /// <summary>
    /// Set while <see cref="SortDescending"/> is being pushed to match a key's default, so its own
    /// change handler does not fire a second reload of the query the key change is already reloading.
    /// </summary>
    private bool _syncingDirection;
    private readonly LoadGeneration _queries = new();
    private readonly LoadGeneration _jumps = new();
    private CancellationToken _queryToken;
    private EmbySessionScope? _scope;
    private EmbySessionScope? _defaultScope;
    private IReadOnlyList<EmbySessionScope> _sources = [];
    private IReadOnlyList<EmbySessionScope> _favoriteAccounts = [];
    private AggregateQueryCursor? _cursor;
    private readonly HashSet<string> _resumeRead = new(StringComparer.Ordinal);
    private bool Aggregated => _sources.Count > 1;
    // 单来源继续观看同样按服务器消费量计数；补入的下一集只占显示槽，不占 Resume 偏移。
    private bool UsesQueryCursor => Aggregated || IsResumePage;
    private bool SourcesAvailable => _sources.Any(source => source.IsAvailable);
    private bool _active;
    private bool _firstPage;
    private bool _pageFailed;

    public LibraryViewModel()
    {
        Heading = "媒体库";
        EmptyNotice = "这里没有内容";
        SearchText = "";
        SortKey = EmbySortBy.Name;
        SortLabel = EmbySortBy.LabelOf(EmbySortBy.Name);
        FilterTooltip = "筛选";
        CardWidth = Momoka.Infrastructure.CardSize.PosterWidth;
    }

    /// <summary>
    /// Raised after a page of rows has been added. The page answers it by asking for one more if the
    /// window is still not full; see <see cref="LibraryPage"/>.
    /// </summary>
    internal event Action? PageArrived;

    public ObservableCollection<CardItem> Cards { get; } = [];

    /// <summary>
    /// 这一页的名字（库名、文件夹名、人名、搜索词）。**页头 2026-09-11 删掉之后它不再上屏** —— 留着是因为
    /// 日志和自检拿它认人（<c>LibraryPage.HeadingText</c>：<c>--show-library</c> 那几行、自检的「媒体库页面
    /// 「电影」N 张卡片」）。要删它得先给这两处另找一个说法。
    /// </summary>
    [ObservableProperty]
    public partial string Heading { get; set; }

    /// <summary>
    /// 这一面网格上每张卡建出来、画出来的宽度，固定是 <see cref="CardSize.PosterWidth"/>（装机那一档 170）。
    /// <para>
    /// The grid used to be the one place the setting did not reach: the markup carried
    /// <c>CardWidth="170" PosterHeight="255" MinItemWidth="170"</c> as literals, so moving 设置 → 海报宽度
    /// resized the home page's rows and left the library — the page the setting is obviously about —
    /// exactly as it was. The view model owned the number from then on; 2026-09-05 the setting itself was
    /// deleted and the number settled back to the one constant every page shares. The property stays
    /// because the pages and the self-check read the width here, and <see cref="RowHeight"/> and
    /// <see cref="CellWidth"/> ride on its announcement.
    /// </para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowHeight))]
    [NotifyPropertyChangedFor(nameof(CellWidth))]
    public partial int CardWidth { get; set; }

    /// <summary>
    /// How tall one cell of the grid has to be: the picture plus the text under it — the title, the field
    /// line, and the extra line when 宣传语 or 概要 is switched on.
    /// Computed rather than typed in, which is how the old markup's <c>MinItemHeight="308"</c> came to
    /// be six pixels short of the card it was sizing. Same arithmetic as
    /// <see cref="CardShelf.RowHeight"/>, for the same reason.
    /// </summary>
    public double RowHeight => LibraryCardMetrics.Height(View, CardBuildWidth, Section, IndexKind)
        + (IndexKind == LibraryIndexKind.None ? Momoka.Infrastructure.CardSize.Chrome : LibraryIndexCards.TextHeight)
        + NoteHeight
        + (IsCardFrameView ? LibraryCardMetrics.CardFrameChrome : 0);

    /// <summary>Jellyfin's two card layouts draw their artwork and text on a card surface.</summary>
    private bool IsCardFrameView => View is LibraryView.PosterCard or LibraryView.ThumbCard;

    /// <summary>多出来的那一行字（宣传语或概要）占的高度；没勾这两项就是 0。</summary>
    private double NoteHeight => Fields.Contains(LibraryField.Tagline) || Fields.Contains(LibraryField.Overview) ? 18 : 0;

    /// <summary>The cell width the live layout should be using, in the current view.</summary>
    /// <remarks>
    /// Read by 海报 and 缩略图 only. 列表 lays out with a <c>StackLayout</c> — a row spans the page, and
    /// nothing but the page's own width can say how wide that is — so neither measurement reaches it.
    /// </remarks>
    public double CellWidth => CardBuildWidth;

    /// <summary>The <c>SortBy</c> value currently in effect.</summary>
    internal string SortKey { get; private set; }

    /// <summary>What the sort button says. Set together with <see cref="SortKey"/>; see <see cref="ShowSort"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolbarSortLabel))]
    public partial string SortLabel { get; set; }

    /// <summary>
    /// Bound two-way to the button's toggle half, so clicking it is a sort change rather than a picture
    /// of one. The glyph and the tooltip are derived from it by the generator, which is what stops the
    /// three from ever disagreeing — the old code set all three by hand in one method and had to be
    /// trusted to call it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortGlyph))]
    [NotifyPropertyChangedFor(nameof(SortTooltip))]
    public partial bool SortDescending { get; set; }

    public string SortGlyph => Glyph(SortDescending ? DescendingIcon : AscendingIcon);

    public string SortTooltip => SortDescending ? "降序，点击改为升序" : "升序，点击改为降序";

    /// <summary>
    /// 这一面墙上一次量到的可用宽度（<c>CardWall.ActualWidth</c>），0 是还没量过 —— 见
    /// <see cref="CardBuildWidth"/> 与 <see cref="ResizeGrid"/>。页面在尺寸变化时推过来，窗口被拖动时跟着变。
    /// </summary>
    private double _gridWidth;

    /// <summary>
    /// 这一面墙现在的整套显示设置：视图、图像大小、分组、显示字段。网页端那颗齿轮里的四组东西，
    /// 一个库一份 —— 电视剧库和电影库互不影响（见 <see cref="RestoreDisplay"/>）。
    /// <para>
    /// 换一个形状就要按新尺寸重建卡片：解码宽度必须跟着画出来的宽度走，否则每一张都是糊的。
    /// 两种格子的量法也都挂在它上面：形状决定卡片多大，而一个还按旧形状算的格子会把新卡片裁掉一截。
    /// </para>
    /// </summary>
    private LibraryDisplay _display = new();

    /// <summary>这一面墙的视图形状。改它走 <see cref="ApplyDisplay"/>，不要直接赋值。</summary>
    public LibraryView View => _display.View;

    /// <summary>网页端「图像大小」那一档。</summary>
    public LibraryCardSize CardSize => _display.CardSize;

    /// <summary>网页端「将项目分组到合集中」。</summary>
    public bool GroupIntoCollections => _display.GroupIntoCollections;

    /// <summary>
    /// 这一面墙要显示的字段，顺序按这一页的表（见 <see cref="LibraryDisplayCatalog.Applied"/>）。
    /// 存下来的那一份是整库共用的，别的页独有的字段（文件夹页的文件名、集页的集号）在这里被滤掉 ——
    /// 那些字段在别的页上没有开关，漏过来就是一段关不掉的字。
    /// </summary>
    public IReadOnlyList<LibraryField> Fields => _fields;

    private IReadOnlyList<LibraryField> _fields = [];

    /// <summary>设置面板读的那一份副本；改动请走 <see cref="ApplyDisplay"/>。</summary>
    internal LibraryDisplay Display => _display.Clone();

    /// <summary>What the view button shows; see <see cref="ViewGlyphOf"/>.</summary>
    public string ViewGlyph => Glyph(ViewGlyphOf(View));

    public string ViewLabel => LibraryDisplayCatalog.LabelOf(View);

    /// <summary>Segoe Fluent codepoints for the view shapes.</summary>
    internal static int ViewGlyphOf(LibraryView view) => view switch
    {
        LibraryView.Thumb or LibraryView.ThumbCard => 0xE8A9,   // 网格（宽格）
        LibraryView.List => 0xE8FD,    // 列表
        LibraryView.Table => 0xE8FD,   // 表格
        LibraryView.Banner => 0xE7C4,  // 横幅
        LibraryView.Disc => 0xE958,    // 光盘
        LibraryView.Logo => 0xE8D2,    // 徽标
        _ => 0xE80A                    // 平铺（海报 / 海报卡片）
    };

    /// <summary>Whether this grid draws a 16:9 still rather than a poster.</summary>
    internal bool ViewIsWide => View is LibraryView.Thumb or LibraryView.ThumbCard or LibraryView.Logo
        or LibraryView.List or LibraryView.Table;

    /// <summary>
    /// The width a card is built at in the current view — **not** the constant the shape prefers, but the
    /// width this window's row can give it; see <see cref="LibraryCardMetrics.Fit"/>.
    /// <para>
    /// 从前它就是那个常量（海报 170、剧照 300）：格子按「塞得下几列」定，余下的宽度留在格子里，卡片居中，
    /// 于是两张封面之间空出一条比封面还显眼的缝。2026-10-18 用户令「封面随窗口大小变化，互相之间不要留太大的
    /// 空隙」之后，宽度由窗口算出来（<see cref="_gridWidth"/>）：列数按档位宽定，剩下的宽度平摊给这几列，
    /// 卡片画出来的宽度就是格宽。
    /// </para>
    /// <para>
    /// 还没量到窗口宽度时退回档位宽。新建的页面在第一帧之前就可能把卡片建好了，那时宽度无从谈起 ——
    /// 量到之后 <see cref="RebuildGrid"/> 会按新宽度重造一遍。
    /// </para>
    /// </summary>
    internal int CardBuildWidth => GridFit.Cell;

    /// <summary>这一行排几列、每列多宽；还没量到宽度时就是档位宽一列。</summary>
    private (int Columns, int Cell) GridFit => _gridWidth > 0
        ? LibraryCardMetrics.Fit(_gridWidth, MinimumCardWidth)
        : (1, MinimumCardWidth);

    /// <summary>图像大小那一档给的最小宽度：列数与格宽都从它算起。</summary>
    private int MinimumCardWidth => LibraryCardMetrics.Width(View, CardSize, Section, IndexKind);

    /// <summary>
    /// 版面的 <c>MinItemWidth</c>。**是档位宽，不是 <see cref="CardBuildWidth"/>**：格宽是摊出来的，拿它当最小宽，
    /// n 列加 n 条缝就比可用宽多出一条缝，版面只肯排 n-1 列（见 <see cref="LibraryCardMetrics.Fit"/>）。
    /// </summary>
    internal int CardMinWidth => MinimumCardWidth;

    /// <summary>
    /// 这张卡是不是 16:9 的宽卡（<see cref="CardItem"/> 拿它挑图、也拿它算高）。
    /// <para>
    /// 索引页不由「视图」决定，而由**服务器给这两类条目存的图是什么形状**决定：播出平台的台标存在 Thumb 里
    /// （16:9），类型的四格封面是方图。见 <see cref="LibraryIndexCards"/>。
    /// </para>
    /// </summary>
    internal bool CardBuildWide => IndexKind == LibraryIndexKind.Studio
        || IndexKind == LibraryIndexKind.None
        && (ViewIsWide || Section == LibrarySection.Episodes && View is LibraryView.Poster or LibraryView.PosterCard);

    /// <summary>
    /// 这一面墙取图的先后顺序。网页端七种视图各要各的图：海报要竖图、横幅图要 Banner、光盘封面要
    /// Disc、徽标与缩略图要背景图（Backdrop），取不到再往后退一格。空数组就是让卡片按自己的老规矩挑
    /// （竖卡先要 Primary、宽卡先要 Thumb）。
    /// <para>
    /// 索引页也不由视图决定：类型要它自己的 Primary（四格封面），播出平台先要 Thumb（台标存的那一格）。
    /// </para>
    /// </summary>
    internal IReadOnlyList<string> CardImageTypes => IndexKind != LibraryIndexKind.None
        ? LibraryIndexCards.ImageTypes(IndexKind)
        : View switch
        {
            LibraryView.Banner => ["Banner", "Backdrop", "Primary"],
            LibraryView.Disc => ["Disc", "Primary"],
            LibraryView.Logo => ["Backdrop", "Thumb"],
            LibraryView.ThumbCard => [EmbyImageStore.Thumb, EmbyImageStore.Backdrop],
            _ => []
        };

    /// <summary>
    /// 这一页是哪一种索引页（类型 / 播出平台 / 不是）。**一切「索引页特别怎么办」的判断都从它出发**，
    /// 别再各处写一遍 <c>Section == Genres || Section == Studios</c> —— 那样漏掉下钻那一层的情况，就会把
    /// 「类型 → 动画」那一页也当成索引页（2026-10-18 差点那么写）。
    /// </summary>
    internal LibraryIndexKind IndexKind => IsSectionLibrary
        ? LibraryBrowse.IndexKind(Section, _request!.BrowseGroup) : LibraryIndexKind.None;

    /// <summary>How many filters are on, for the badge on the filter button.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterBadgeVisibility))]
    [NotifyPropertyChangedFor(nameof(FilterButtonWidth))]
    public partial int FilterCount { get; set; }

    public Visibility FilterBadgeVisibility => Show(FilterCount > 0);
    public double FilterButtonWidth => IsFnNas ? FilterCount > 0 ? 84 : 70 : FilterCount > 0 ? 52 : 32;

    /// <summary>飞牛那一套筛选的选择；Emby 的 <see cref="_filters"/> 对它无效。</summary>
    private readonly FnNasFilterSelection _fnNasFilters = new();
    private CancellationTokenSource? _fnNasFilterLifetime;

    /// <summary>飞牛筛选面板在屏上、正在读标签、以及它自己那几行。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FnNasFilterVisibility))]
    public partial bool FnNasFilterOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FnNasFilterLoadingVisibility))]
    public partial bool FnNasFilterLoading { get; set; }

    public ObservableCollection<FnNasFilterRow> FnNasFilterRows { get; } = [];

    public Visibility FnNasFilterVisibility => Show(IsFnNas && FnNasFilterOpen);
    public Visibility FnNasFilterTextVisibility => Show(IsFnNas);
    public Visibility FnNasFilterLoadingVisibility => Show(FnNasFilterLoading);

    internal bool IsFnNas => _scope?.Connection.IsFnNas == true;

    /// <summary>This grid belongs to a Jellyfin server; its tabs, sorts, filters and layouts are Jellyfin Web's.</summary>
    internal bool IsJellyfin => _scope?.Connection.IsJellyfin == true;

    /// <summary>
    /// 工具栏底下那一行筛选条，一条筛选一格。见 <see cref="EmbyFilterBy.Chips"/>：那颗按钮上的数字说「筛了
    /// 几条」，这一行说「筛的是哪几条」，而且每一格自己能被点掉。
    /// </summary>
    public ObservableCollection<FilterChipRow> FilterChips { get; } = [];

    public Visibility FilterChipsVisibility => Show(
        LibraryFilterChips.ShowsActiveChips(IsFnNas) && FilterChips.Count > 0 && FilterVisibility == Visibility.Visible);

    /// <summary>
    /// Whether the letter bar belongs on this grid — Emby's own rule: name-ordered, and enough rows
    /// that jumping is worth the trip. Recomputed on every page arrival, because the count is what
    /// the second half of the rule reads.
    /// <para>
    /// **索引页也算在内**（2026-10-18 用户令）：类型与播出平台本来就是按名字排的清单，本机 133 个台正是
    /// 最该有字母条的那一页；从前 `!IsBrowseIndex` 把这两页一起挡掉了，理由是「索引页没有排序」，
    /// 而实际上 `LibraryBrowse.Query` 给它们的排序恒为名称升序，判据的第二半（名称序）自己就成立。
    /// 跳转的计数走 `/Genres` 与 `/Studios`（见 <see cref="CountAsync"/>），与这一页翻页的是同一个端点。
    /// </para>
    /// </summary>
    public Visibility AlphaVisibility => Show(
        !Aggregated && !IsUpcomingPage && !IsFavoritesPage && Section != LibrarySection.Recommended
        && Section != LibrarySection.Collections
        && SupportsBrowseConditions && _request?.IsSearch != true && IsNameSortInForce
        && (!IsJellyfin || JellyfinLibrary.HasAlphabetPicker(CollectionType, Section))
        && !SortDescending && _total is > 30);

    /// <summary>
    /// Jellyfin's name sort carries a year tie-breaker (<c>SortName,ProductionYear</c>) and the mixed
    /// library's default starts with <c>IsFolder</c>; both are still name orderings for the letter bar.
    /// </summary>
    private bool IsNameSortInForce => IsJellyfin ? JellyfinLibrary.IsNameSort(SortKey) : SortKey == EmbySortBy.Name;

    /// <summary>
    /// 这台服务器认不认 Emby 那套浏览条件 —— 字母跳转、筛选面板、按库记住筛选。飞牛与 Plex 各有自己的
    /// 查询语言，装不出同一套条件，所以这三样在它们名下一起收起来（排序仍按各自支持的那几档单独裁剪，
    /// 见 <see cref="SupportsSort"/>）。
    /// </summary>
    private bool SupportsBrowseConditions => _scope?.Connection.IsFnNas != true && _scope?.Connection.IsPlex != true;

    /// <summary>飞牛有自己的一套标签筛选；Emby 那套条件与 Plex 仍按原样收起。</summary>
    private bool SupportsFilterPanel => IsFnNas || SupportsBrowseConditions;

    /// <summary>Whether this grid has anything the player could start, for the two play buttons.</summary>
    internal bool HasPlayable => _active && SourcesAvailable && _firstPage && !Busy
        && Cards.Any(card => card.Item.IsPlayable);

    protected override void BusyChanged()
    {
        RowsArrived();
        RefreshUpcomingState();
    }

    // ---------------------------------------------------------------- 播放命令

    /// <summary>CanExecute is read off the loaded rows: an empty grid or one of folders only offers
    /// nothing to start, and a button that promises 播放全部 and plays nothing is worse than one that
    /// stays grey until the first page lands.</summary>
    [RelayCommand(CanExecute = nameof(HasPlayable))]
    private Task PlayAll() => PlayAllAsync();

    [RelayCommand(CanExecute = nameof(HasPlayable))]
    private Task PlayRandom() => PlayRandomAsync();

    /// <summary>Called by the page after a page of rows lands, when the two commands' answer and the
    /// letter bar's rule both change. The collection itself is not watched: the page already has an
    /// event for this exact moment.</summary>
    internal void RowsArrived()
    {
        OnPropertyChanged(nameof(TotalLabel));
        // 两颗播放键在不在，看的是这一页有没有可播的行（见 DirectPlayVisibility）。
        OnPropertyChanged(nameof(DirectPlayVisibility));
        PlayAllCommand.NotifyCanExecuteChanged();
        PlayRandomCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(AlphaVisibility));
    }

    [ObservableProperty]
    public partial string FilterTooltip { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyVisibility))]
    public partial bool ShowEmptyNotice { get; set; }

    public Visibility EmptyVisibility => Show(ShowEmptyNotice && (_request?.IsSearch != true || SearchText.Length > 0));

    /// <summary>
    /// What the middle of an empty grid says. A property rather than the literal the markup used to
    /// carry, because the search page opens empty on purpose and 「这里没有内容」 would read as a
    /// failed search nobody ran.
    /// </summary>
    [ObservableProperty]
    public partial string EmptyNotice { get; set; }

    /// <summary>
    /// Whether the search box belongs on this grid. Only the search page has one — every other grid is
    /// already 「these rows」, and a box on it would be a second, different question in the same header.
    /// </summary>
    public Visibility SearchVisibility => Show(_request?.IsSearch == true);

    /// <summary>
    /// 搜索框旁边那个「我的媒体库 / MoviePilot」分段切换在不在。只有搜索页、而且用户在设置里开了 MoviePilot
    /// 时才出现；没接 MoviePilot 的话这一页和从前一模一样。判据留在这里而不是页面，和 <see cref="SearchVisibility"/>
    /// 一伙 —— 换一个请求或改了设置就重问一次。
    /// </summary>
    public Visibility MoviePilotTabsVisibility =>
        Show(_request?.IsSearch == true && _moviePilot?.Available == true);

    private Momoka.MoviePilot.MoviePilotService? _moviePilot;

    internal void RefreshMoviePilotAvailability() => OnPropertyChanged(nameof(MoviePilotTabsVisibility));

    /// <summary>What the box holds, so it survives the page being rebuilt around it.</summary>
    [ObservableProperty]
    public partial string SearchText { get; set; }

    /// <summary>What the filter panel is currently set to; the panel edits it in place.</summary>
    internal ItemFilters Filters => _filters;

    /// <summary>Which kind of row this grid holds, as the sort catalogue sees it.</summary>
    internal MediaServerKind SourceServerKind => _scope?.Connection.ServerKind ?? MediaServerKind.Emby;

    internal string? SortPreset => IsSectionLibrary
        ? LibraryBrowse.SortPreset(_request!.CollectionType, _request.Section) : _request?.SortPreset;

    /// <summary>
    /// 筛选面板用的预设。与 <see cref="SortPreset"/> 分开：电影库的「预告片」排序菜单是电影那份，
    /// 筛选却是 Trailer 那份；混合内容库排序走文件夹菜单，筛选走 Mixed 那份。
    /// </summary>
    internal string? FilterPreset => IsSectionLibrary
        ? LibraryBrowse.FilterPreset(_request!.CollectionType, _request.Section) : _request?.SortPreset;

    /// <summary>
    /// The Jellyfin filter panel for this page, or null for an Emby/FnNas/Plex grid (those keep
    /// <see cref="EmbyFilterBy.For"/> through <see cref="FilterPreset"/>). An empty list is a Jellyfin
    /// page with no filter rows.
    /// </summary>
    internal IReadOnlyList<EmbyFilterField>? FilterFieldsForPage() =>
        IsJellyfin ? JellyfinLibrary.FilterFields(FilterCollectionType, Section) : null;

    internal string? CollectionType => _request?.CollectionType;

    internal bool IsSectionLibrary => _request is { ParentId.Length: > 0, CollectionType: "movies" or "tvshows" or "mixed", IsSearch: false };
    internal LibrarySection Section => _request?.Section ?? LibrarySection.Items;
    internal bool IsBrowseIndex => IsSectionLibrary && LibraryBrowse.IsIndex(Section, _request!.BrowseGroup);

    internal IReadOnlyList<LibrarySectionEntry> BrowseSections => IsSectionLibrary
        ? LibraryBrowse.Sections(_request!.CollectionType, _scope?.Connection.ServerKind ?? MediaServerKind.Emby) : [];
    /// <summary>飞牛媒体库不摆「影片 / 收藏」分段：官网页端没有这一条，收藏仍在别处可达。</summary>
    public Visibility SectionVisibility => Show(!IsFnNas && IsSectionLibrary && BrowseSections.Count > 1);

    /// <summary>
    /// 这一页底下那一条工具栏（总数、播放、排序、筛选、设置）在不在。网页端不是每一页都有：
    /// 推荐与预告两页只有内容，总数与按键一起收起；电视剧库的收藏是两排货架，也收起。电影库的
    /// 预告片与收藏是普通网格，网页端照样给总数、排序、筛选与设置（播放/随机播放再看有没有可播的行），
    /// 不能跟着电视剧那两页一起收。索引页（类型、播出平台）留总数与设置；文件夹没有筛选可筛。
    /// </summary>
    private bool HasToolbar => !IsSectionLibrary
        || (Section is not (LibrarySection.Recommended or LibrarySection.Upcoming)
            && !(Section == LibrarySection.Favorites && IsFavoritesPage));

    public Visibility ToolbarVisibility => Show(HasToolbar);
    /// <summary>Emby 那颗齿轮里的分组与显示字段对飞牛不生效，飞牛改用官网页端的「布局」两项。</summary>
    public Visibility SettingsVisibility => Show(IsSectionLibrary && HasToolbar && !IsFnNas && !IsJellyfin);
    public Visibility LayoutVisibility => Show(IsFnNas && HasToolbar && !IsResumePage && !Aggregated && !IsBrowseIndex
        && Section != LibrarySection.Folders);

    /// <summary>
    /// Jellyfin Web's own 视图 button. It replaces Emby's gear on a Jellyfin library: the gear's grouping
    /// and field switches are Emby settings, while Jellyfin's tabs offer the six layout shapes (three on
    /// the episodes tab). Trailers and the index pages have no layout button in the web client.
    /// </summary>
    public Visibility JellyfinLayoutVisibility => Show(IsJellyfin && IsSectionLibrary && HasToolbar
        && !IsResumePage && !Aggregated && !IsBrowseIndex && _request?.IsSearch != true
        && JellyfinLibrary.HasLayout(CollectionType, Section));
    public bool FnNasLayoutWide => IsFnNas && View == LibraryView.Thumb;
    public Visibility BrowseGroupVisibility => Show(IsSectionLibrary && _request?.BrowseGroup is not null);
    public string BrowseGroupLabel => _request?.BrowseGroup ?? "";
    /// <summary>
    /// 播放全部与随机播放两颗键在不在。判据是这一页的卡片里有没有能播的：电影库的「影片」、混合内容库的
    /// 「电影和节目」里的电影、以及「集」那一页都有；电视剧库的「节目」页装的是剧集本身，一集都播不了 ——
    /// 那种页面上两颗键没有意义，整对收起（原来它们藏在「更多」里，那个菜单 2026-10-17 按用户的话删掉了：
    /// 菜单里除了这两颗常年灰着的键什么都没有）。
    /// </summary>
    public Visibility DirectPlayVisibility => Show(!IsSectionLibrary
        || (!IsBrowseIndex && _firstPage && Cards.Any(card => card.Item.IsPlayable)));
    public string TotalLabel => _total < 0 ? _pageFailed ? "加载失败" : "正在加载" : $"共 {_total} 项";
    public string ToolbarSortLabel => SortLabel;

    internal async Task ChooseSectionAsync(LibrarySection section)
    {
        if (!IsSectionLibrary || !BrowseSections.Any(entry => entry.Section == section)) return;
        if (_request!.Section == section && _request.BrowseGroup is null) return;
        _request.Section = section;
        _request.BrowseGroup = null;
        _request.ScrollOffset = 0;
        _request.LoadedCount = 0;
        ResetFnNasFilters();
        RestoreSort();
        RestoreFilters();
        RestoreDisplay();
        RefreshBrowseChrome();
        await ReloadAsync().ConfigureAwait(true);
    }

    internal async Task LeaveBrowseGroupAsync()
    {
        if (!IsSectionLibrary || _request!.BrowseGroup is null) return;
        _request.BrowseGroup = null;
        RefreshBrowseChrome();
        await ReloadAsync().ConfigureAwait(true);
    }

    private void RefreshBrowseChrome()
    {
        // 换页会换字段表（文件夹 / 集 / 索引页 / 其余），生效的字段跟着这一页重算。
        _fields = LibraryDisplayCatalog.Applied(_display.Fields, Section, View, IndexKind, CollectionType);
        RefreshEmptyNotice();

        foreach (var name in new[] { nameof(SectionVisibility), nameof(BrowseGroupVisibility), nameof(BrowseGroupLabel),
            nameof(DirectPlayVisibility), nameof(SortFilterVisibility), nameof(FilterVisibility),
            nameof(ToolbarVisibility), nameof(SettingsVisibility), nameof(Fields),
            nameof(AlphaVisibility), nameof(FilterChipsVisibility), nameof(TotalLabel),
            nameof(LayoutVisibility), nameof(JellyfinLayoutVisibility), nameof(FnNasFilterVisibility), nameof(FnNasFilterTextVisibility),
            nameof(FnNasLayoutWide), nameof(FilterButtonWidth),
            nameof(FavoriteAccountFilterVisibility), nameof(FavoriteAccountLabel),
            nameof(RecommendedVisibility), nameof(GridVisibility), nameof(UpcomingVisibility), nameof(FavoritesVisibility) }) OnPropertyChanged(name);
        RefreshUpcomingState();
    }

    /// <summary>
    /// 空页中间那句话。电影库的「预告片」在网页端有一句专门的提示：这一页空着多半不是库里没有内容，
    /// 而是服务器没装 Trailer 插件；照截图把插件目录写在句子里，别的页仍是「这里没有内容」。
    /// </summary>
    private void RefreshEmptyNotice()
    {
        if (_request is not { } request)
        {
            EmptyNotice = "这里没有内容";
            return;
        }

        EmptyNotice = request.IsSearch ? "输入关键字开始搜索"
            : request.Row == HomeLayout.HomeRowTarget.Resume ? "这里还没有看到一半的内容"
            : request.Row is not null ? "这里还没有看过的内容"
            : IsJellyfin && request.Section is LibrarySection.Trailers or LibrarySection.Collections or LibrarySection.Recommended
                ? JellyfinLibrary.EmptyNotice(request.CollectionType, request.Section)
            : request.CollectionType == "movies" && request.Section == LibrarySection.Trailers
                ? "未找到预告片。要添加预告片，请在 Emby 插件目录中安装 Trailers 插件。"
            : "这里没有内容";
    }

    internal int LoadedCount => Cards.Count;

    /// <summary>The first row this grid loaded, for <c>--play</c>. Null before the first page arrives.</summary>
    internal EmbyItem? FirstItem => Cards.Count > 0 ? Cards[0].Item : null;

    /// <summary>
    /// Whether the server has rows this grid has not asked for yet. True before the first page, because
    /// 「nothing has been asked」 and 「everything has arrived」 must not look the same to the pager.
    /// </summary>
    internal bool HasMore => !IsRecommendedPage && !IsFavoritesPage && !(_request?.IsSearch == true && string.IsNullOrWhiteSpace(_request.SearchTerm))
        && (UsesQueryCursor ? _cursor?.HasMore ?? true : _total < 0 || (IsUpcomingPage ? _upcomingReadCount : Cards.Count) < _total);

    // ---------------------------------------------------------------- 推荐页（三排货架）

    /// <summary>
    /// 网页端电视剧库「推荐」那一格：三排横着走的货架（继续观看、最新集、最近发布的剧集），不是一面卡片墙。
    /// <para>
    /// 2026-10-17 用户令「复刻网页端的推荐页面，三个分区 继续观看、最新集、最近发布的剧集」。这一页的「说什么、
    /// 怎么问」在 <see cref="LibraryRecommended"/> 里；这里只管把三份数据装进三排，以及这一页该显示什么。
    /// </para>
    /// <para>
    /// 电影库没有这几排（网页端电影库的推荐是另一组行，本轮没做），别的分类也不是推荐页 —— 那些情况
    /// <see cref="IsRecommendedPage"/> 为假，页面照旧是网格，一行标记都不用改。
    /// </para>
    /// </summary>
    public ObservableCollection<CardShelf> RecommendedShelves { get; } = [];

    /// <summary>这一页是货架页而不是网格页。</summary>
    internal bool IsRecommendedPage => IsSectionLibrary && Section == LibrarySection.Recommended
        && LibraryRecommended.Rows(_request!.CollectionType, SourceServerKind).Count > 0;

    /// <summary>三排货架在不在（网格由 <see cref="GridVisibility"/> 收起来，两者互斥）。</summary>
    public Visibility RecommendedVisibility => Show(IsRecommendedPage);

    /// <summary>网格在不在。推荐页把整块网格（含它的滚动器与表格列头）收起，屏上只剩那三排。</summary>
    public Visibility GridVisibility => Show(!IsRecommendedPage && !IsUpcomingPage && !IsFavoritesPage);

    /// <summary>
    /// 这一面墙此刻在不在：推荐页、预告页、收藏页各自有自己的一块。只有它在的时候才按窗口宽度重造卡片
    /// （<see cref="ResizeGrid"/>）—— 那几块的卡片各有各的量法，拿这一面墙的宽度去改它们就是改错人。
    /// <para>
    /// **索引页也算这一面墙**（2026-10-18 起）：类型与播出平台的卡片走的就是这个网格，格宽同样随窗口变。
    /// 从前这里把它排除在外，因为那时索引页是一块写死 170×72 的文字格、跟宽度没关系。
    /// </para>
    /// </summary>
    private bool IsCardWall => !IsRecommendedPage && !IsUpcomingPage && !IsFavoritesPage;

    internal bool IsUpcomingPage => IsSectionLibrary && Section == LibrarySection.Upcoming;
    public ObservableCollection<UpcomingDay> UpcomingDays { get; } = [];
    public Visibility UpcomingVisibility => Show(IsUpcomingPage);
    public bool UpcomingLoading => IsUpcomingPage && Busy && !_firstPage;
    public Visibility UpcomingLoadingVisibility => Show(UpcomingLoading);
    public Visibility UpcomingErrorVisibility => Show(IsUpcomingPage && !Busy && _pageFailed && !_firstPage);
    public Visibility LibraryBusyVisibility => Show(Busy && !UpcomingLoading);
    private int _upcomingCardWidth = LibraryUpcoming.CardWidth;
    private int _upcomingReadCount;

    private void RefreshUpcomingState()
    {
        OnPropertyChanged(nameof(UpcomingLoading));
        OnPropertyChanged(nameof(UpcomingLoadingVisibility));
        OnPropertyChanged(nameof(UpcomingErrorVisibility));
        OnPropertyChanged(nameof(LibraryBusyVisibility));
    }

    /// <summary>
    /// 页面量到了这一面墙的可用宽度（窗口被拖动，或者这一页刚上屏）：列数、格宽、行高按它重算。
    /// **只算账，不重造卡片** —— 拖窗口时每帧都会走到这里，而重造卡片要按新宽度重新解码（一趟一趟地取图），
    /// 那一半交给窗口停下来之后的 <see cref="RebuildGrid"/>。
    /// <para>
    /// 格宽真变了才会喊一声：窗口挪几个像素时列数通常不变、格宽也不变，喊了也是白排一遍。
    /// </para>
    /// </summary>
    /// <returns>格宽变了没有 —— 变了页面才要逼版面重排一次（见 <c>LibraryPage.RelayoutWall</c>）。</returns>
    internal bool ResizeGrid(double width)
    {
        if (!(width > 0) || !double.IsFinite(width)) return false;
        if (Math.Abs(width - _gridWidth) < 0.5) return false;

        var before = CardBuildWidth;
        _gridWidth = width;
        if (CardBuildWidth == before) return false;

        // 它一变，RowHeight 与 CellWidth 跟着喊（见属性上的两个 NotifyPropertyChangedFor），
        // 版面的 MinItemWidth / MinItemHeight 当场换成新的。
        CardWidth = CardBuildWidth;
        return true;
    }

    /// <summary>
    /// 窗口停下来之后按当前格宽重画这一面墙的卡片：宽度变了，取图尺寸、解码宽度和画出来的宽度一起换
    /// （见 <see cref="CardItem.Resize"/>）。为旧宽度解出来的图放到新格子里就是一张糊图，这条账在
    /// <see cref="ApplyDisplay"/> 上已经记过一遍，窗口拖宽是同一件事。
    /// <para>
    /// **原地改，不换卡片对象、也不清空列表**：卡片对象活在清单里，删除、继续观看那一格和探针都攥着它们；
    /// 换掉对象等于把那些引用全作废，清空重填还会让滚到一半的一面墙跳回顶上。
    /// </para>
    /// </summary>
    internal void RebuildGrid()
    {
        if (_images is null || Cards.Count == 0 || !IsCardWall) return;

        var width = CardBuildWidth;
        foreach (var card in Cards) card.Resize(width);
    }

    internal void ResizeUpcoming(double width)
    {
        if (width <= 0 || !double.IsFinite(width)) return;
        var cardWidth = LibraryUpcoming.WidthFor(width);
        if (_upcomingCardWidth == cardWidth) return;
        _upcomingCardWidth = cardWidth;
        if (!IsUpcomingPage || _images is null) return;
        var items = Cards.Select(card => card.Item).ToArray();
        foreach (var card in Cards) card.AbandonPoster();
        Cards.Clear();
        foreach (var item in items) Cards.Add(NewCard(item));
        UpdateUpcomingDays();
    }

    private void UpdateUpcomingDays()
    {
        if (!IsUpcomingPage)
        {
            UpcomingDays.Clear();
            return;
        }
        var today = DateOnly.FromDateTime(DateTime.Now);
        var cards = Cards.ToDictionary(card => card.Item.IdentityKey, StringComparer.Ordinal);
        var groups = LibraryUpcoming.Group(Cards.Select(card => card.Item));
        var desired = groups.Select(group =>
        {
            var title = LibraryUpcoming.DateTitle(group.Date, today);
            var day = UpcomingDays.FirstOrDefault(existing => existing.Date == group.Date
                && existing.Title == title && existing.CardWidth == _upcomingCardWidth)
                ?? new UpcomingDay(group.Date, title, _upcomingCardWidth);
            var contents = group.Items.Select(item => cards[item.IdentityKey]).ToArray();
            for (var index = day.Cards.Count - 1; index >= 0; index--)
                if (!contents.Contains(day.Cards[index])) day.Cards.RemoveAt(index);
            for (var index = 0; index < contents.Length; index++)
            {
                var oldIndex = day.Cards.IndexOf(contents[index]);
                if (oldIndex < 0) day.Cards.Insert(index, contents[index]);
                else if (oldIndex != index) day.Cards.Move(oldIndex, index);
            }
            return day;
        }).ToArray();
        for (var index = UpcomingDays.Count - 1; index >= 0; index--)
            if (!desired.Contains(UpcomingDays[index])) UpcomingDays.RemoveAt(index);
        for (var index = 0; index < desired.Length; index++)
        {
            var oldIndex = UpcomingDays.IndexOf(desired[index]);
            if (oldIndex < 0) UpcomingDays.Insert(index, desired[index]);
            else if (oldIndex != index) UpcomingDays.Move(oldIndex, index);
        }
    }

    /// <summary>
    /// 照这一页该有的排数把货架造出来。已经是对的就不重建 —— 每次刷新都 <c>Clear</c> 再 <c>Add</c>，屏上那三排
    /// 会被拆掉重装一遍，滚到一半的位置也会回开头。换库、换分类时排数或标题变了才重造。
    /// </summary>
    private void EnsureRecommendedShelves()
    {
        if (_images is not { } images || _request is null) return;

        var rows = LibraryRecommended.Rows(_request.CollectionType, SourceServerKind);
        if (RecommendedShelves.Count == rows.Count
            && RecommendedShelves.Select(shelf => shelf.Title).SequenceEqual(rows.Select(row => row.Title), StringComparer.Ordinal))
            return;

        RecommendedShelves.Clear();
        foreach (var row in rows)
            RecommendedShelves.Add(new CardShelf(
                row.Title, images, row.Wide ? Momoka.Infrastructure.CardSize.HomeWideWidth : Momoka.Infrastructure.CardSize.PosterWidth, row.Wide));
    }

    /// <summary>
    /// 推荐页那一趟：三排各问一次，谁也不等谁。和主页那几排同一个理由 —— 一排一趟往返地串起来，这一页就是
    /// 一排一秒；三排之间没有任何依赖。
    /// </summary>
    /// <remarks>
    /// 不分页：三排各自只要 <see cref="LibraryRecommended.Size"/> 条，翻页由货架自己那一对箭头管（它是在已有
    /// 的那些卡里翻，不再问服务器）。
    /// </remarks>
    private async Task LoadRecommendedAsync(bool reset)
    {
        if (!reset) return;
        if (!_active || !SourcesAvailable || _request is not { } request || _images is null) return;

        _favoriteLoads.Cancel();
        foreach (var row in FavoriteRows) row.Loading = false;
        _queryToken = _queries.Begin();
        _jumps.Cancel();
        _firstPage = false;
        _pageFailed = false;
        _total = NotLoaded;
        ShowEmptyNotice = false;
        var queryToken = _queryToken;
        var token = BeginLoad();
        try
        {
            EnsureRecommendedShelves();
            var rows = LibraryRecommended.Rows(request.CollectionType, SourceServerKind);
            var items = await Task.WhenAll(rows.Select(row => ReadRecommendedAsync(row, request, token))).ConfigureAwait(true);
            if (!QueryCurrent(queryToken) || !IsCurrent(token)) return;

            for (var index = 0; index < rows.Count && index < RecommendedShelves.Count; index++)
                RecommendedShelves[index].Fill(items[index], LibraryRecommended.Subtitle);

            _total = RecommendedShelves.Sum(shelf => shelf.Cards.Count);
            _firstPage = true;
            ShowEmptyNotice = RecommendedShelves.All(shelf => shelf.Cards.Count == 0);
            RowsArrived();
            EndLoad(token);
            PageArrived?.Invoke();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!QueryCurrent(queryToken) || !IsCurrent(token)) return;
            _pageFailed = true;
            Log.Warn(Category, $"读取「{Heading}」的推荐失败", error);
            Report($"读取「{Heading}」失败，请刷新重试", error);
        }
        finally { EndLoad(token); }
    }

    /// <summary>
    /// 一排的数据。多来源时轮流取各来源的一项（同主页那一排）：默认账号的库填满整排、别的账号一条都露不出来，
    /// 是「多服务器」支持里最容易犯的那个错。
    /// </summary>
    private async Task<List<EmbyItem>> ReadRecommendedAsync(RecommendedRow row, LibraryRequest request, CancellationToken token)
    {
        var sources = _sources.Where(source => source.IsAvailable).ToArray();
        if (sources.Length == 0) return [];

        var results = await Task.WhenAll(sources.Select(source => ReadRecommendedSourceAsync(source, row, request, token)))
            .ConfigureAwait(true);

        var items = new List<EmbyItem>();
        for (var index = 0; results.Any(result => result.Count > index); index++)
            foreach (var result in results)
                if (index < result.Count) items.Add(result[index]);
        return items;
    }

    private async Task<List<EmbyItem>> ReadRecommendedSourceAsync(
        EmbySessionScope scope, RecommendedRow row, LibraryRequest request, CancellationToken token)
    {
        try
        {
            if (!scope.IsAvailable) return [];

            // 继续观看整份交给服务器那条 Resume 接口（「有没有播放进度、有没有被移出」的口径都在它手里，
            // 通用查询拼不出来），这一趟只是把它收到本库 —— 见 EmbyClient.QueryResumeAsync 的 parentId。
            if (row.Shelf == RecommendedShelf.ContinueWatching)
            {
                var resume = await scope
                    .ExecuteAsync((client, ct) => client.QueryResumeAsync(0, LibraryRecommended.Size, ct, request.ParentId), token)
                    .ConfigureAwait(true);
                var items = resume.Items.Where(item => !EmbyItemType.IsMusic(item.Type));
                // Jellyfin's two suggestions pages ask for the kind of item that page is about, rather
                // than letting a movie appear on a series row or the other way round.
                if (scope.Connection.IsJellyfin)
                    items = request.CollectionType switch
                    {
                        "movies" => items.Where(item => item.Type is EmbyItemType.Movie or EmbyItemType.Video),
                        "tvshows" => items.Where(item => item.Type == EmbyItemType.Episode),
                        _ => items
                    };
                return [.. items];
            }

            // Jellyfin's movie suggestions are categories from /Movies/Recommendations, each with its own
            // reason. The shelf flattens them into one row titled 为你推荐; the duplicate film can appear
            // under several reasons, so it is kept once.
            if (row.Shelf == RecommendedShelf.MovieRecommendations)
            {
                if (!scope.Connection.IsJellyfin || request.ParentId is not { Length: > 0 } parent) return [];
                var recommendations = await scope.ExecuteAsync((client, ct) =>
                    client.GetMovieRecommendationsAsync(parent, LibraryRecommended.Size, ct), token).ConfigureAwait(true);
                return [.. recommendations
                    .SelectMany(recommendation => recommendation.Items)
                    .Where(item => !EmbyItemType.IsMusic(item.Type))
                    .DistinctBy(item => item.IdentityKey)];
            }

            // Jellyfin's third series row is NextUp, scoped to the library, not a PremiereDate-sorted list.
            if (row.Shelf == RecommendedShelf.NextUp)
            {
                var items = await scope.ExecuteAsync((client, ct) =>
                    client.GetNextUpAsync(LibraryRecommended.Size, ct, parentId: request.ParentId), token).ConfigureAwait(true);
                return [.. items.Where(item => !EmbyItemType.IsMusic(item.Type))];
            }

            // Jellyfin's latest movie / episode rows come from /Items/Latest with the library scope. The
            // endpoint does not filter out watched items there, unlike the Emby call this client already
            // made for its home carousel, so both switches are turned off explicitly.
            if (row.Shelf == RecommendedShelf.LatestMovies)
            {
                var items = await scope.ExecuteAsync((client, ct) => client.GetLatestAsync(
                    request.ParentId, LibraryRecommended.Size, ct, EmbyItemType.Movie,
                    unplayedOnly: false, groupItems: false), token).ConfigureAwait(true);
                return [.. items.Where(item => !EmbyItemType.IsMusic(item.Type))];
            }

            if (row.Shelf == RecommendedShelf.LatestEpisodes && scope.Connection.IsJellyfin)
            {
                var items = await scope.ExecuteAsync((client, ct) => client.GetLatestAsync(
                    request.ParentId, LibraryRecommended.Size, ct, EmbyItemType.Episode,
                    unplayedOnly: false, groupItems: false), token).ConfigureAwait(true);
                return [.. items.Where(item => !EmbyItemType.IsMusic(item.Type))];
            }

            var query = LibraryRecommended.Query(request.ParentId ?? "", row.Shelf);
            var result = await scope
                .ExecuteAsync((client, ct) => client.GetItemsAsync(query, ct), token)
                .ConfigureAwait(true);
            return [.. result.Items];
        }
        catch (OperationCanceledException)
        {
            return [];
        }
        catch (Exception error)
        {
            Log.Warn(Category, $"读取{scope.DisplayName}的「{row.Title}」失败", error);
            if (IsCurrent(token) && !NoticeOpen) Report($"读取「{row.Title}」失败", error);
            return [];
        }
    }

    /// <summary>
    /// 这一页是不是主页「继续观看」那一排点进来的。见 <see cref="ReadAsync"/>：这一页的条目整份由服务器那条
    /// Resume 接口给，通用查询一条都不发。
    /// </summary>
    private bool IsResumePage => _request?.Row == HomeLayout.HomeRowTarget.Resume;

    /// <summary>
    /// 「排序」与「筛选」这两颗键在这一页在不在。
    /// <para>
    /// 继续观看那一页的次序和口径都由服务器那条 Resume 接口定（已看的不列、被移出的不列），两颗键按下去
    /// 什么都不会变 —— <see cref="SortKey"/> 改了不发、<see cref="_filters"/> 也进不了那次请求。与其摆着两颗
    /// 骗人的键（项目规矩：界面不许骗人），不如收起来；Emby 自己的继续观看也不给这两颗。视图切换留着：
    /// 那是这张网格怎么画，与服务器无关。
    /// </para>
    /// </summary>
    public Visibility SortFilterVisibility => Show(HasToolbar && !IsResumePage && !Aggregated && !IsBrowseIndex
        // 飞牛搜索走 /search/list，排序参数不会随搜索发出；摆一颗按了不动的排序键比不摆更糟。
        && !(IsFnNas && _request?.IsSearch == true));

    /// <summary>Jellyfin's filter button: its own row catalogue, and its own tabs that do not have one.</summary>
    private bool JellyfinFiltersSupported => IsJellyfin
        && (JellyfinLibrary.HasFilter(FilterCollectionType, Section) || _request?.IsSearch == true);

    /// <summary>
    /// The collection type used for the filter catalogue. A search page has no library root in its
    /// request, so it borrows the movie row set rather than falling back to Emby's.
    /// </summary>
    private string? FilterCollectionType => CollectionType ?? (_request?.IsSearch == true ? "movies" : null);

    public Visibility FilterVisibility => Show(HasToolbar && !IsResumePage && !Aggregated && !IsBrowseIndex
        && Section != LibrarySection.Folders && Section != LibrarySection.Collections
        && (IsJellyfin ? JellyfinFiltersSupported : SupportsFilterPanel)
        // 飞牛搜索走的是 /search/list，它没有标签参数；在搜索页摆一个不起作用的筛选比不摆更糟。
        && !(IsFnNas && _request?.IsSearch == true));

    /// <summary>
    /// Handed the navigation payload and the four capabilities one grid draws from, once per navigation to
    /// the page.
    /// <para>
    /// <paramref name="settings"/> is kept rather than only read here, because this grid writes back to it
    /// too — the sort and the filters are remembered per library root. <paramref name="capabilities"/> is
    /// kept for the same shape of reason: the server version may still have been in flight when the page
    /// opened, so the sort menu asks for it when it is opened rather than now.
    /// </para>
    /// </summary>
    internal void Attach(
        LibraryRequest request,
        IShellActions actions,
        ISettingsService settings,
        EmbySession session,
        EmbyImageStore images,
        IServerCapabilities capabilities,
        Momoka.MoviePilot.MoviePilotService? moviePilot = null)
    {
        Cancel();
        _active = true;
        _scope = request.Source ?? images.CaptureScope();
        _defaultScope = _scope;
        _sources = request.Source is { } source ? [source]
            : request.ParentId is not null || request.PersonId is not null ? _scope is null ? [] : [_scope]
            : session.Sources.ToArray();
        _favoriteAccounts = [];
        if (request.IsFavorites)
        {
            _favoriteAccounts = FavoriteAccounts.Applicable(_sources);
            if (request.FavoriteAccountKey is { } selected
                && _favoriteAccounts.All(source => source.Key != selected))
                request.FavoriteAccountKey = null;
            _sources = FavoriteAccounts.Select(_favoriteAccounts, request.FavoriteAccountKey);
            // 收藏页只问一个账号时，这一页的排序、筛选与服务器风味都换成那个账号的。
            if (_sources.Count == 1) _scope = _sources[0];
        }
        _request = request;
        _actions = actions;
        _settings = settings;
        _images = images;
        _capabilities = capabilities;
        _moviePilot = moviePilot;

        // 「海报宽度」那一行设置 2026-09-05 删掉了，宽度回到 CardSize 固定的默认档。
        CardWidth = Momoka.Infrastructure.CardSize.PosterWidth;

        Heading = request.IsSearch ? LibraryRequest.SearchTitle(request.SearchTerm!) : request.Title;
        SearchText = request.SearchTerm ?? "";

        // 「这里没有内容」读起来像仓库空了，而这一页是「你还没看什么」—— 一部都没看完过的时候，那两句话说的
        // 其实是两件事（2026-09-14「继续观看」那一排接通之后才有人走到这一页）。两排的空页文案也分开：继续观看
        // 那一页装的是「看到一半的」（见 BuildQuery 的 Resume 分支），空了说的是「还没开头」；接下来看那一页
        // 装的是「没看完的剧集」，空了说的是「都看完了」。
        RefreshEmptyNotice();
        OnPropertyChanged(nameof(SearchVisibility));
        OnPropertyChanged(nameof(MoviePilotTabsVisibility));

        // 这两颗键在不在，也是这一页的身份说的（见 SortFilterVisibility）—— 换一个请求就得重问一次。
        OnPropertyChanged(nameof(SortFilterVisibility));
        OnPropertyChanged(nameof(FilterVisibility));
        OnPropertyChanged(nameof(FnNasFilterTextVisibility));
        OnPropertyChanged(nameof(FilterButtonWidth));
        OnPropertyChanged(nameof(LayoutVisibility));

        ResetFnNasFilters();

        if (IsSectionLibrary && !BrowseSections.Any(entry => entry.Section == request.Section))
        {
            request.Section = LibrarySection.Items;
            request.BrowseGroup = null;
        }
        RestoreSort();
        RestoreFilters();
        RestoreDisplay();
        RefreshBrowseChrome();
        LoadRecentSearches();
    }

    /// <summary>
    /// Runs the search page's box. Reloads in place rather than navigating: a new page per search would
    /// stack an entry on the back stack for every word typed, and the grid the user is looking at —
    /// its sort, its view shape, its filters — is the same grid either way.
    /// </summary>
    internal async Task SearchAsync(string term, bool remember = true)
    {
        if (!_active || !SourcesAvailable || _request is not { IsSearch: true } request) return;

        var query = term.Trim();
        request.SearchTerm = query;
        request.ScrollOffset = 0;
        request.LoadedCount = 0;
        SearchText = query;
        Heading = LibraryRequest.SearchTitle(query);

        var loading = ReloadAsync();
        var queryToken = QueryToken;
        await loading.ConfigureAwait(true);
        if (!QueryCurrent(queryToken) || !ReferenceEquals(_request, request) || request.SearchTerm != query || _pageFailed) return;
        if (remember && query.Length > 0) RememberSearch();
        if (query.Length == 0) LoadRecentSearches();
    }

    public override Task ReloadAsync() => LoadAsync(reset: true);

    /// <summary>删除原位改变集合并回退所属服务器的消费量，删除前发出的分页响应作废。</summary>
    internal async Task<bool> RemoveAsync(CardItem card, CancellationToken queryToken, bool deleted = true)
    {
        if (!QueryCurrent(queryToken) || !Cards.Contains(card)) return false;
        if (IsFavoritesPage) return await RemoveFavoriteAsync(card, deleted).ConfigureAwait(true);
        // 容器会连带删除未加载的后代；查询刚切换时屏上也可能仍是旧排序，不能据旧集合推算新分页。
        if (Aggregated || !_firstPage || deleted && (card.Item.IsFolder || card.Item.Type is EmbyItemType.Series
            or EmbyItemType.Season or EmbyItemType.Folder or EmbyItemType.BoxSet or EmbyItemType.Playlist))
            return await RefreshAfterRemovalAsync(queryToken).ConfigureAwait(true);
        base.Cancel();
        // 保留查询身份，菜单里已发出的其他删除仍属于这一页；只撤销读取和字母跳转。
        _jumps.Cancel();
        card.AbandonPoster();
        Cards.Remove(card);
        if (IsUpcomingPage && _upcomingReadCount > 0) _upcomingReadCount--;
        UpdateUpcomingDays();
        var consumed = !IsResumePage || _resumeRead.Remove(card.Item.IdentityKey);
        if (IsResumePage && consumed && card.Scope is { } source) _cursor?.RemoveConsumed(source.Key);
        if (consumed && _total > 0) _total--;
        _firstPage = true;
        _pageFailed = false;
        IsReady = true;
        ShowEmptyNotice = Cards.Count == 0;
        RowsArrived();
        return true;
    }

    /// <summary>
    /// 「继续观看」那一页的一张卡被标成已看之后，把那一格收拾干净 —— 单集换成本剧的下一集，电影、以及没有
    /// 下一集的单集一律撤掉（用户令 2026-10-15）。
    /// <para>
    /// 与主页那一排同一份判据、同一段收尾（<see cref="ResumeSlot"/>）：那两个面子讲的是同一件事，口径必须一样
    /// —— 2026-09-22「一排 1 项、进去 9 项」那次就是两个面子各说各话。
    /// </para>
    /// <para>
    /// 只在继续观看那一页动手：别的页上的卡标记已看只是它自己那个对勾的事，条目还在这一页里。
    /// </para>
    /// </summary>
    internal void MarkedWatched(CardItem card)
    {
        if (IsResumePage) _ = AfterWatchedAsync(card);
    }

    private async Task AfterWatchedAsync(CardItem card)
    {
        var queryToken = _queryToken;
        if (!QueryCurrent(queryToken) || card.Scope is not { IsAvailable: true } scope || !Cards.Contains(card)) return;

        var watched = card.Item;

        // 写入已确认后原项已经从服务器 Resume 清单移除。先撤销可能还用旧偏移读取的分页，再回退
        // 它所属来源；不能等下一集补读完才记账，更不能把补入的无续播下一集算作原始记录。
        if (_resumeRead.Remove(watched.IdentityKey))
        {
            base.Cancel();
            _jumps.Cancel();
            IsReady = _firstPage;
            _cursor?.RemoveConsumed(scope.Key);
            if (_total > 0) _total--;
            RowsArrived();
        }

        // 下一集先问回来再动屏（同主页那一排）：先撤后插会让这一格空着闪一下。问不出来（最后一集、断线、
        // 离页）都按「没有下一集」办。
        var next = ResumeRow.WantsNext(watched)
            ? await ResumeSlot.NextEpisodeAsync(scope, watched, Category).ConfigureAwait(true)
            : null;

        // 这一趟往返里页面可能已经走了、换了账号、或者换了一次查询：那就一个字都别动。
        if (!QueryCurrent(queryToken) || !card.IsCurrent || !Cards.Contains(card)) return;

        var slot = ResumeSlot.Apply(Cards, card, next, NewCard);
        Log.Info(Category, slot is null
            ? $"「{watched.Name}」标记已看，从继续观看撤下"
            : $"「{watched.Name}」标记已看，那一格换成「{slot.Name}」");

        // 总数说的是服务器 Resume 记录，合成显示槽的撤换不再改这份账。
        ShowEmptyNotice = Cards.Count == 0;
        RowsArrived();
    }

    /// <summary>照这一页当前的显示设置造一张卡（同 <see cref="LoadAsync"/> 那一句）。</summary>
    /// <remarks>
    /// 索引页那一支（2026-10-18 用户令）：卡片上只有**一张图 + 居中的一个名字**，没有第二行字、没有角标。
    /// 那几个决定各自都有原因：
    /// <list type="bullet">
    /// <item><c>subtitle: ""</c> 是必须的 —— 不传的话 <see cref="EmbyItem.CardSubtitle"/> 会把
    /// <c>Type</c> 原样写出来，屏上于是变成「动画 / Genre」「AT-X / Studio」这种中英各一半的行。</item>
    /// <item><c>indicators: false</c>：一个类型没有「看过 / 收藏」可言（服务器那边也一样，见
    /// <see cref="EmbyItemType.TracksUserState"/> 对 Genre / Studio 的判据）。</item>
    /// <item><c>containArtwork</c>：只有台标要留边（铺满会把字切掉），四格封面照旧铺满。</item>
    /// </list>
    /// </remarks>
    private CardItem NewCard(EmbyItem item) => IsUpcomingPage
        ? new(item, _images!, _upcomingCardWidth, wide: true,
            subtitle: LibraryUpcoming.Subtitle(item), title: LibraryUpcoming.Title(item),
            artwork: LibraryUpcoming.Artwork(item), indicators: false, upcoming: true)
        : IndexKind != LibraryIndexKind.None
        ? new(item, _images!, CardBuildWidth, CardBuildWide,
            subtitle: "", indicators: false, imageTypes: CardImageTypes,
            title: item.Name, centeredText: true,
            containArtwork: LibraryIndexCards.ContainArtwork(IndexKind),
            aspect: LibraryIndexCards.Aspect(IndexKind))
        : new(item, _images!, CardBuildWidth, CardBuildWide,
            subtitle: CardLine(item),
            // 飞牛横卡把竖版海报摆在正中、同一张图铺底，所以先要 Primary；别的视图照旧。
            imageTypes: IsFnNas && View == LibraryView.Thumb
                ? [EmbyImageStore.Primary, EmbyImageStore.Thumb] : CardImageTypes,
            tableFields: View == LibraryView.Table ? Fields : null,
            note: LibraryCardText.Note(item, Fields),
            title: Section is LibrarySection.Episodes or LibrarySection.Folders
                ? LibraryCardText.Title(item, Fields, Section) : null,
            // 飞牛的「横幅海报」照官网页端：卡片底下两行字居中；竖幅海报仍是默认的左对齐。
            centeredText: Section is LibrarySection.Episodes or LibrarySection.Folders
                || IsFnNas && View == LibraryView.Thumb
                || IsCardFrameView,
            fnNasWide: IsFnNas && View == LibraryView.Thumb,
            folderFrame: Section == LibrarySection.Folders && View == LibraryView.Poster,
            cardLayout: IsCardFrameView);

    /// <summary>
    /// 卡片底下那一行字。勾了字段就按勾的拼；一项能说的都没有时交回卡片自己的副标题（
    /// 剧集的「S01E02 · 剧名」、文件夹的「文件夹」），总比留一行空白强。
    /// </summary>
    private string? CardLine(EmbyItem item) => Section is LibrarySection.Episodes or LibrarySection.Folders
        ? LibraryCardText.Subtitle(item, Fields, Section)
        : LibraryCardText.Line(item, Fields) is { Length: > 0 } line ? line : null;

    private async Task<bool> RefreshAfterRemovalAsync(CancellationToken queryToken)
    {
        var wanted = Math.Max(Cards.Count, _settings!.PageSize);
        _firstPage = false;
        _jumps.Cancel();
        var token = BeginLoad();
        try
        {
            var cursor = UsesQueryCursor ? NewCursor() : null;
            var items = new List<EmbyItem>();
            var total = 0;
            do
            {
                var result = cursor is not null
                    ? await ReadCursorAsync(cursor, token).ConfigureAwait(true)
                    : await ReadAsync(items.Count, token).ConfigureAwait(true);
                if (!QueryCurrent(queryToken) || !IsCurrent(token)) return false;
                total = result?.TotalRecordCount ?? 0;
                if (result is null || result.Items.Count == 0) break;
                items.AddRange(result.Items);
            } while (items.Count < Math.Min(wanted, total));

            // 原有卡片原位保留，只移除消失的项并补入需要的项，避免容器、焦点和海报整页重建。
            var retained = Cards.DistinctBy(card => card.Item.IdentityKey).ToDictionary(card => card.Item.IdentityKey, StringComparer.Ordinal);
            var desired = items.Select(item => retained.TryGetValue(item.IdentityKey, out var existing) ? existing
                : NewCard(item)).ToArray();
            _cursor = cursor;
            _resumeRead.Clear();
            if (IsResumePage)
                foreach (var item in items) _resumeRead.Add(item.IdentityKey);
            var kept = desired.ToHashSet();
            for (var index = Cards.Count - 1; index >= 0; index--)
                if (!kept.Contains(Cards[index]))
                {
                    Cards[index].AbandonPoster();
                    Cards.RemoveAt(index);
                }
            for (var index = 0; index < desired.Length; index++)
            {
                var current = Cards.IndexOf(desired[index]);
                if (current < 0) Cards.Insert(index, desired[index]);
                else if (current != index) Cards.Move(current, index);
            }
            _total = total;
            if (IsUpcomingPage)
            {
                _upcomingReadCount = items.Count;
                UpdateUpcomingDays();
            }
            _firstPage = true;
            _pageFailed = false;
            ShowEmptyNotice = Cards.Count == 0;
            RowsArrived();
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception error)
        {
            if (QueryCurrent(queryToken) && IsCurrent(token))
            {
                _pageFailed = true;
                Report("更新删除后的列表失败，请刷新重试", error);
            }
            return false;
        }
        finally { EndLoad(token); }
    }

    /// <summary>
    /// Reads one page of rows: the first, or the next.
    /// </summary>
    /// <param name="reset">
    /// True to replace what is on screen. A reset supersedes whatever is in flight — the user changed
    /// the sort, and the rows now arriving belong to a query nobody is looking at. An append waits its
    /// turn instead, because two appends at once would both start from the same offset.
    /// </param>
    private async Task LoadAsync(bool reset)
    {
        if (!_active || _request is null || _actions is null) return;
        if (!SourcesAvailable)
        {
            // 收藏页在没有任何能列收藏的来源（例如只登录了 Plex）时给出空态，不留下永远转不完的加载。
            if (_request.IsFavorites && _sources.Count == 0)
            {
                _total = 0;
                _firstPage = true;
                _pageFailed = false;
                EmptyNotice = "没有可读取收藏的账号";
                ShowEmptyNotice = true;
                IsReady = true;
                RowsArrived();
            }
            return;
        }
        // 推荐页不是一面网格：三排货架各问一次，没有翻页、没有字母跳转、没有「共 N 项」。整条路分开走，
        // 下面那些分页与游标的账一笔都不记（见 LoadRecommendedAsync）。
        if (IsRecommendedPage)
        {
            await LoadRecommendedAsync(reset).ConfigureAwait(true);
            return;
        }
        if (IsFavoritesPage)
        {
            await LoadFavoritesAsync(reset).ConfigureAwait(true);
            return;
        }
        if (!reset && (Busy || !HasMore || !_firstPage || _pageFailed)) return;
        if (reset)
        {
            _favoriteLoads.Cancel();
            foreach (var row in FavoriteRows) row.Loading = false;
            _queryToken = _queries.Begin();
            _jumps.Cancel();
            _firstPage = false;
            _pageFailed = false;
            _total = NotLoaded;
            _upcomingReadCount = 0;
            ResetCursor();
            ShowEmptyNotice = false;
            UpcomingDays.Clear();
            if (_request.IsSearch || IsSectionLibrary)
            {
                foreach (var card in Cards) card.AbandonPoster();
                Cards.Clear();
            }
        }
        var queryToken = _queryToken;
        var token = BeginLoad();
        RefreshUpcomingState();
        try
        {
            var start = reset ? 0 : IsUpcomingPage ? _upcomingReadCount : Cards.Count;
            // HTTP 可以先完成而页面接收回调仍在队列里。读取只消费副本，在同一 UI 提交点确认
            // 查询/载入仍有效后，才把新游标和它的卡片一起发布；已看/删除取消旧页时原账不受影响。
            var cursor = UsesQueryCursor ? (_cursor ?? NewCursor()).Copy() : null;
            var result = cursor is not null
                ? await ReadCursorAsync(cursor, token).ConfigureAwait(true)
                : await ReadAsync(start, token).ConfigureAwait(true);
            if (!QueryCurrent(queryToken) || !IsCurrent(token)) return;
            if (cursor is not null) _cursor = cursor;
            if (reset)
            {
                foreach (var card in Cards) card.AbandonPoster();
                Cards.Clear();
            }
            _total = result?.TotalRecordCount ?? 0;
            if (result is not null)
            {
                foreach (var item in result.Items)
                {
                    if (IsResumePage) _resumeRead.Add(item.IdentityKey);
                    if ((IsUpcomingPage || IsResumePage) && Cards.Any(card => card.Item.IdentityKey == item.IdentityKey)) continue;
                    Cards.Add(NewCard(item));
                }
                if (IsUpcomingPage) _upcomingReadCount += result.Items.Count;
                if (result.Items.Count == 0 && !UsesQueryCursor) _total = IsUpcomingPage ? _upcomingReadCount : Cards.Count;
            }
            UpdateUpcomingDays();
            _firstPage = true;
            _pageFailed = false;
            EmptyNotice = result is null ? "输入关键字开始搜索"
                : _request.IsSearch ? $"没有找到“{SearchText}”的匹配结果"
                : IsUpcomingPage ? "暂无即将播出的剧集，可稍后刷新查看" : "这里没有内容";
            ShowEmptyNotice = Cards.Count == 0;
            EndLoad(token);
            PageArrived?.Invoke();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!QueryCurrent(queryToken) || !IsCurrent(token)) return;
            _pageFailed = true;
            Log.Warn(Category, $"读取「{Heading}」失败", error);
            Report($"读取「{Heading}」失败，请刷新重试", error);
        }
        finally { EndLoad(token); }
    }

    internal CancellationToken QueryToken => _queryToken;

    internal bool QueryCurrent(CancellationToken token) =>
        _active && SourcesAvailable && _queries.IsCurrent(token);

    public override void Cancel()
    {
        _active = false;
        _queries.Cancel();
        _jumps.Cancel();
        _favoriteLoads.Cancel();
        _fnNasFilterLifetime?.Cancel();
        FnNasFilterLoading = false;
        foreach (var row in FavoriteRows) row.Loading = false;
        base.Cancel();
        RowsArrived();
    }

    internal void Resume(bool reload = true)
    {
        if (_active) return;
        _active = SourcesAvailable;
        if (_active && reload) _ = ReloadAsync();
    }

    public override void Dispose()
    {
        Cancel();
        foreach (var card in Cards) card.AbandonPoster();
        foreach (var shelf in RecommendedShelves) shelf.Clear();
        _favoriteLoads.Dispose();
        _fnNasFilterLifetime?.Dispose();
        _fnNasFilterLifetime = null;
        foreach (var row in RecentSearches) row.Card.AbandonPoster();
        _queries.Dispose();
        _jumps.Dispose();
        base.Dispose();
    }

    /// <summary>
    /// One page of rows, from whichever source this grid's identity says.
    /// <para>
    /// 继续观看那一页（<see cref="IsResumePage"/>）整份交给服务器自己那条 Resume 接口 —— 那一排点进去要的
    /// 就是那一排的整个清单，两边同源才不会「一排 1 项、进去 9 项」（2026-09-22 用户报的正是这个）。别的页
    /// 走通用查询，排序、筛选、翻页、字母跳转照旧。
    /// </para>
    /// </summary>
    /// <returns>null ＝ 这一页没什么可问的（搜索页空着那个框，<see cref="BuildQuery"/> 也为它返回 null）。</returns>
    private async Task<ItemsResult?> ReadAsync(int start, CancellationToken token)
    {
        if (!_active || !SourcesAvailable) throw new OperationCanceledException();
        if (!IsResumePage && BuildQuery(start) is null) return null;
        return await ReadSourceAsync(_sources[0], start, token).ConfigureAwait(true);
    }

    private AggregateQueryCursor NewCursor() => new(_sources.Select(source => source.Key));

    private void ResetCursor()
    {
        _cursor = UsesQueryCursor ? NewCursor() : null;
        _resumeRead.Clear();
    }

    private async Task<ItemsResult> ReadCursorAsync(AggregateQueryCursor cursor, CancellationToken token)
    {
        var page = await cursor.ReadAsync((key, offset, ct) =>
            ReadSourceAsync(_sources.First(source => source.Key == key), offset, ct), token,
            allowPartialFailure: Aggregated).ConfigureAwait(true);
        if (_active && !token.IsCancellationRequested && page.Failures.Count > 0)
        {
            Notify(null, "部分来源暂时无法读取，已显示其他来源；刷新可重试。", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
            foreach (var (key, error) in page.Failures) Log.Warn(Category, $"聚合读取来源 {key} 失败", error);
        }
        return page.Result;
    }

    private async Task<ItemsResult> ReadSourceAsync(EmbySessionScope scope, int start, CancellationToken token)
    {
        scope.ThrowIfUnavailable();
        if (IsResumePage)
            return await scope
                .ExecuteAsync((client, ct) => client.QueryResumeAsync(start, _settings!.PageSize, ct), token)
                .ConfigureAwait(true);

        if (BuildQuery(start) is not { } query) return new ItemsResult();

        if (IsSectionLibrary)
        {
            var request = _request!;
            var section = request.Section;
            var group = request.BrowseGroup;
            return await scope.ExecuteAsync((client, ct) => client.GetLibraryItemsAsync(
                request.CollectionType!, section, group, query, ct), token).ConfigureAwait(true);
        }
        return await scope
            .ExecuteAsync((client, ct) => client.GetItemsAsync(query, ct), token)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// The next page, if there is one and nothing else is already asking. Called by the page when the
    /// scroll comes within a viewport of the bottom.
    /// </summary>
    internal void LoadMore()
    {
        if (Busy || !HasMore) return;
        _ = LoadAsync(reset: false);
    }

    /// <summary>
    /// Loads pages until <paramref name="index"/> is materialised — the letter bar's request, whose
    /// target the grid may not have scrolled anywhere near yet. Stops at the end; a letter past the
    /// last row lands on the last row.
    /// </summary>
    internal async Task LoadUntilAsync(int index)
    {
        var queryToken = _queryToken;
        while (QueryCurrent(queryToken) && !Busy && HasMore && _firstPage && !_pageFailed && Cards.Count <= index)
        {
            var count = Cards.Count;
            await LoadAsync(reset: false).ConfigureAwait(true);
            if (Cards.Count <= count) break;
        }
    }

    /// <summary>
    /// The sort flyout's contents, rebuilt on every open rather than once. Which keys apply depends on
    /// what this grid lists and on a server version that may still have been in flight when the page
    /// opened, and the tick has to follow a sort restored from settings — all three are known at open
    /// time and none of them reliably before it.
    /// </summary>
    internal IReadOnlyList<SortEntry> SortEntries()
    {
        if (_request is null) return [];

        if (IsFnNas)
            return [.. FnNasSortCatalog.Options.Select(option =>
                new SortEntry(option, option.Value == SortKey, SortDescending))];

        if (IsJellyfin)
            return [.. JellyfinLibrary.SortOptions(_request.CollectionType, Section)
                .Where(option => SupportsSort(option.Value))
                .Select(option => new SortEntry(option, option.Value == SortKey, SortDescending))];

        var preset = SortPreset;

        return
        [
            .. (IsSectionLibrary ? LibrarySortCatalog.For(_request.CollectionType, Section, SourceServerVersion)
                    : EmbySortBy.For(preset, SourceServerVersion))
                .Where(option => SupportsSort(option.ValueOf(preset)))
                .Select(option => new SortEntry(option, option.ValueOf(preset) == SortKey, SortDescending))
        ];
    }

    /// <summary>
    /// The arrow on the ticked row: same field, other direction. Guarded rather than trusting the caller,
    /// because a menu rebuilt while the query was in flight can be pressed for a key that is no longer it.
    /// </summary>
    internal void FlipSort(EmbySortOption option)
    {
        if (!_active || !SourcesAvailable || _request is null || Aggregated) return;
        if ((IsJellyfin ? option.Value : option.ValueOf(SortPreset)) != SortKey) return;

        SortDescending = !SortDescending;
    }

    /// <summary>A key was picked out of the flyout.</summary>
    internal void ChooseSort(EmbySortOption option)
    {
        if (!_active || !SourcesAvailable || _request is null || _actions is null || Aggregated) return;

        var key = IsJellyfin ? option.Value : option.ValueOf(SortPreset);
        if (!SupportsSort(key)) return;

        // Emby's own behaviour, and the only sensible one: a date key opens newest first and a name key
        // A to Z. Choosing 加入日期 and being shown the oldest thing in the library is never what was
        // meant, however the direction happened to be left by the previous key.
        if (SortFilterVisibility != Visibility.Visible) return;
        if (IsJellyfin)
        {
            if (!JellyfinLibrary.SortOptions(_request.CollectionType, Section).Any(entry => entry.Value == key)) return;
        }
        else if (!IsFnNas && !option.AppliesTo(SortPreset, SourceServerVersion)) return;
        if (key == SortKey && option.DescendingByDefault == SortDescending) return;

        SortKey = key;
        ShowSort(option.Label, option.DescendingByDefault);
        RememberSort();
        Requery();

        Log.Debug(Category, $"排序方式：{option.Label}（{key}）{(SortDescending ? "降序" : "升序")}");
    }

    /// <summary>
    /// 设置面板改了东西。按新的一套重建卡片，而不是把旧卡片拉大 —— 为 170 像素解出来的图放到 300 像素上
    /// 就是一张糊图，而换了字段之后服务器还没把新字段送过来，只有重查一次才拿得到。
    /// </summary>
    /// <param name="next">面板上拼出来的那一套。</param>
    /// <param name="remember">探针走查时传 false：看过就走，不把库留在别的形状上。</param>
    /// <param name="requery">
    /// 探针走查传 false：那一趟只想知道每种形状把网格量成什么样，不要顺手把卡片清空重查 ——
    /// 自检随后要数这一页的卡片，重查还没回来时数到的是 0。
    /// </param>
    internal void ApplyDisplay(LibraryDisplay next, bool remember = true, bool requery = true)
    {
        var normalized = next.Normalized();
        if (SameDisplay(normalized, _display)) return;

        _display = normalized;
        if (remember) RememberDisplay();
        RaiseDisplay();
        if (requery) Requery();

        Log.Debug(Category, $"显示设置：{ViewLabel}，{LibraryDisplayCatalog.LabelOf(CardSize)}，"
            + $"字段 {Fields.Count} 项，分组 {(GroupIntoCollections ? "开" : "关")}");
    }

    private static bool SameDisplay(LibraryDisplay left, LibraryDisplay right) =>
        left.View == right.View
        && left.CardSize == right.CardSize
        && left.GroupIntoCollections == right.GroupIntoCollections
        && left.Fields.SequenceEqual(right.Fields);

    /// <summary>
    /// 播放全部: the grid's first playable row, with the whole grid as the playlist — Emby's own
    /// btnPlay. Falls back to the first page's rows when not everything has arrived; the player asks
    /// the server for what follows.
    /// </summary>
    internal async Task PlayAllAsync()
    {
        if (_request is null || _actions is null || !HasPlayable) return;

        var (items, token) = await PlayableItemsAsync().ConfigureAwait(true);
        if (items.Count == 0 || !IsCurrent(token)) return;

        Log.Info(Category, $"播放全部：{items.Count} 项，从「{items[0].Name}」开始");
        await _actions.PlayAsync(items[0], episodes: items).ConfigureAwait(true);
    }

    /// <summary>
    /// 随机播放: the same list, shuffled — Emby's btnShuffle. Shuffled here rather than by asking the
    /// server to sort randomly, so 选集 shows the order the user is actually going to hear.
    /// </summary>
    internal async Task PlayRandomAsync()
    {
        if (_request is null || _actions is null || !HasPlayable) return;

        var (items, token) = await PlayableItemsAsync().ConfigureAwait(true);
        if (items.Count == 0 || !IsCurrent(token)) return;

        var random = Random.Shared;
        for (var index = items.Count - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (items[index], items[swap]) = (items[swap], items[index]);
        }

        Log.Info(Category, $"随机播放：{items.Count} 项，从「{items[0].Name}」开始");
        await _actions.PlayAsync(items[0], episodes: items).ConfigureAwait(true);
    }

    /// <summary>
    /// The playable rows this grid can hand the player right now. Loaded rows first; when the server
    /// has more than one page and the user has not scrolled, the rest are asked for in one go — 播放
    /// 全部 with only the first hundred is 播放前一百.
    /// </summary>
    private async Task<(List<EmbyItem> Items, CancellationToken Token)> PlayableItemsAsync()
    {
        var loaded = Cards.Select(card => card.Item).ToList();
        var hasMore = HasMore;
        var token = BeginLoad();
        try
        {
            if (Aggregated)
            {
                var cursor = NewCursor();
                var all = await PlayableList.CollectAsync([], true,
                    async (_, ct) => await ReadCursorAsync(cursor, ct).ConfigureAwait(true),
                    () => IsCurrent(token), token).ConfigureAwait(true);
                return (all, token);
            }
            var items = await PlayableList.CollectAsync(
                loaded, hasMore,
                IsResumePage ? async (start, ct) => await ReadSourceAsync(_sources[0], start, ct).ConfigureAwait(true) : ReadAsync,
                () => IsCurrent(token), token, startIndex: IsResumePage ? _cursor?.ConsumedCount : null).ConfigureAwait(true);
            return (IsResumePage ? items.DistinctBy(item => item.IdentityKey).ToList() : items, token);
        }
        finally
        {
            EndLoad(token);
        }
    }

    /// <summary>
    /// 字母跳转：which row the letter bar should land on. The count of rows at or after the letter,
    /// in the grid's own sort, tells the index — asked of the server with <c>Limit 0</c> rather than
    /// walked locally, because not every row of the library is in memory. Null when the query cannot
    /// be answered.
    /// </summary>
    /// <returns>The index of the first row at or after the letter, and the letter to light up.</returns>
    internal async Task<(int Index, string Letter)?> JumpToAsync(string letter)
    {
        if (!_active || Aggregated || Busy || !_firstPage || _scope is not { IsAvailable: true } scope || BuildQuery(0) is not { } source)
            return null;
        var queryToken = _queryToken;
        var jumpToken = _jumps.Begin();
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(queryToken, jumpToken);
        var token = stopping.Token;
        try
        {
            var total = await CountAsync(scope, source, null, token).ConfigureAwait(true);
            if (!QueryCurrent(queryToken) || token.IsCancellationRequested) return null;
            var before = letter == "#" ? total : await CountAsync(scope, source, letter, token).ConfigureAwait(true);
            if (!QueryCurrent(queryToken) || token.IsCancellationRequested) return null;
            var index = Math.Clamp((int)(total - before), 0, Math.Max(0, (int)total - 1));
            await LoadUntilAsync(index).ConfigureAwait(true);
            if (!QueryCurrent(queryToken) || token.IsCancellationRequested || index >= Cards.Count) return null;
            return (index, CurrentLetterOf(index));
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception error)
        {
            if (QueryCurrent(queryToken)) Report("字母跳转失败", error);
            return null;
        }
    }

    /// <summary>One count query with <c>Limit 0</c>, or null when it cannot be answered. With a
    /// letter, the count is of rows <b>at or after</b> that letter — <c>ItemQuery</c> sends it as
    /// <c>NameStartsWithOrGreater</c>, not a plain prefix count.</summary>
    /// <remarks>
    /// 索引页（类型、播出平台）要问的是 `/Genres` 与 `/Studios`，不是 `Users/{id}/Items` —— 那一页列出来的
    /// 是类型名与台名，拿剧集去数只会数出另一批东西，字母跳转会落在错的行上。请求仍借 <see cref="ItemQuery"/>
    /// 拼：这两个端点接的是同一套查询参数（ParentId / IncludeItemTypes / Recursive / SortBy /
    /// NameStartsWithOrGreater / StartIndex / Limit），差别只在端点和要不要 UserId，而那两件事
    /// <see cref="EmbyClient.GetLibraryItemsAsync"/> 已经知道怎么分（2026-10-18 对着本机 4.10.1.0 实测：
    /// 播出平台 133 项、`NameStartsWithOrGreater=B` 数回 124）。`Fields` 清空：这一趟只读总数。
    /// </remarks>
    private async Task<long> CountAsync(EmbySessionScope scope, ItemQuery source, string? letter, CancellationToken token)
    {
        var query = new ItemQuery
        {
            ParentId = source.ParentId,
            SearchTerm = source.SearchTerm,
            Recursive = source.Recursive,
            IncludeItemTypes = source.IncludeItemTypes,
            SortBy = source.SortBy,
            Descending = source.Descending,
            StartIndex = 0,
            Limit = 0,
            Fields = "",
            IsPlayed = source.IsPlayed,
            IsFavorite = source.IsFavorite,
            Genre = source.Genre,
            Studio = source.Studio,
            PersonId = source.PersonId,
            Filters = source.Filters,
            MediaTypes = source.MediaTypes,
            NameStartsWith = letter
        };

        scope.ThrowIfUnavailable();
        var result = IsBrowseIndex
            ? await scope.ExecuteAsync((client, ct) => client.GetLibraryItemsAsync(
                _request!.CollectionType!, Section, null, query, ct), token).ConfigureAwait(true)
            : await scope.ExecuteAsync((client, ct) => client.GetItemsAsync(query, ct), token).ConfigureAwait(true);
        scope.ThrowIfUnavailable();

        return result.TotalRecordCount;
    }

    /// <summary>
    /// The letter a given row starts with — what the bar should light up after a jump or a scroll.
    /// SortName when the server sent one, else the name; uppercased, and # for anything that is not a
    /// letter, which is the bar's own first bucket.
    /// </summary>
    internal string CurrentLetterOf(int index)
    {
        if (index < 0 || index >= Cards.Count) return "#";

        var name = Cards[index].Item.SortName is { Length: > 0 } sort ? sort : Cards[index].Item.Name;
        var first = name.TrimStart().FirstOrDefault();
        return char.IsAsciiLetter(first) ? first.ToString().ToUpperInvariant() : "#";
    }

    /// <summary>
    /// 需求 5：something was ticked in the panel, which edits <see cref="Filters"/> in place. Applied at
    /// once, with the panel still open: the grid is right behind it, and it is the answer to the tick.
    /// </summary>
    internal void ApplyFilters()
    {
        ShowFilterState();
        RememberFilters();
        Requery();

        Log.Debug(Category, _filters.IsEmpty ? "清除筛选" : $"筛选：{_filters.Describe()}");
    }

    /// <summary>
    /// 飞牛筛选按钮：面板是页面里的一条（官网页端也是展开在工具栏下面），不是 Emby 那种盖住整页的浮层。
    /// 第一次打开才去问标签；标签失败不关面板，让人能看到失败原因并再点一次。
    /// </summary>
    internal async Task ToggleFnNasFilterAsync()
    {
        if (!IsFnNas) return;
        if (FnNasFilterOpen)
        {
            FnNasFilterOpen = false;
            return;
        }

        FnNasFilterOpen = true;
        OnPropertyChanged(nameof(FnNasFilterVisibility));
        if (FnNasFilterRows.Count > 0 || FnNasFilterLoading) return;
        await LoadFnNasFilterRowsAsync().ConfigureAwait(true);
    }

    internal void CloseFnNasFilter()
    {
        if (FnNasFilterOpen) FnNasFilterOpen = false;
    }

    private async Task LoadFnNasFilterRowsAsync()
    {
        if (!_active || Aggregated || _scope is not { IsAvailable: true } scope || _request is not { } request) return;
        _fnNasFilterLifetime ??= new CancellationTokenSource();
        if (_fnNasFilterLifetime.IsCancellationRequested) _fnNasFilterLifetime = new CancellationTokenSource();
        var token = _fnNasFilterLifetime.Token;
        FnNasFilterLoading = true;
        try
        {
            var options = await scope.ExecuteAsync((client, ct) => client.GetFnNasFilterOptionsAsync(
                request.ParentId, request.IsFavorites, "zh-CN", ct), token).ConfigureAwait(true);
            if (!FnNasFilterLoadCurrent(token, scope, request)) return;
            BuildFnNasFilterRows(options);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!FnNasFilterLoadCurrent(token, scope, request)) return;
            Log.Warn(Category, "读取飞牛媒体库筛选项失败", error);
            Report("读取媒体库筛选项失败，请稍后重试", error);
        }
        finally
        {
            // 旧来源的 finally 不能把另一个来源仍在读取的面板标为结束。
            if (_fnNasFilterLifetime?.Token == token) FnNasFilterLoading = false;
        }
    }

    private bool FnNasFilterLoadCurrent(CancellationToken token, EmbySessionScope scope, LibraryRequest request) =>
        _active && !token.IsCancellationRequested && _fnNasFilterLifetime?.Token == token
        && ReferenceEquals(_scope, scope) && ReferenceEquals(_request, request) && scope.IsAvailable;

    private void BuildFnNasFilterRows(FnNasFilterOptions options)
    {
        FnNasFilterRows.Clear();

        // 官方前端只在库不是电影或电视剧时给「影视分类」；混合内容库才有电影/剧集两类。
        if (CollectionType is not ("movies" or "tvshows"))
            AddFnNasRow("type", "影视分类", [new("Movie", "电影"), new("TV", "电视节目")]);
        AddFnNasRow("genres", "类型", options.Genres);
        AddFnNasRow("resolutions", "分辨率", options.Resolutions);
        AddFnNasRow("color_range", "视频动态范围", options.ColorRanges);
        AddFnNasRow("audio_type", "音频规格", options.AudioTypes);
        AddFnNasRow("locate", "国家/地区", options.Locations);
        AddFnNasRow("decades", "发行年份", options.Decades);
        AddFnNasRow("recognition_status", "匹配状态", options.RecognitionStatuses);
        AddFnNasRow("watched", "是否已观看", [new("1", "已观看"), new("0", "未观看")]);

        ShowFilterState();
    }

    private void AddFnNasRow(string key, string label, IEnumerable<FnNasTagOption> options)
    {
        var values = options.ToList();
        if (values.Count == 0) return;

        var row = new FnNasFilterRow(key, label, ApplyFnNasFilters);
        row.Add("", FnNasFilterLabels.All);
        foreach (var option in values) row.Add(option.Value, option.Label);
        row.Restore(FnNasValue(key));
        FnNasFilterRows.Add(row);
    }

    private string FnNasValue(string key) => key switch
    {
        "type" => _fnNasFilters.Type,
        "genres" => _fnNasFilters.Genres.FirstOrDefault() ?? "",
        "resolutions" => _fnNasFilters.Resolutions.FirstOrDefault() ?? "",
        "color_range" => _fnNasFilters.ColorRanges.FirstOrDefault() ?? "",
        "audio_type" => _fnNasFilters.AudioTypes.FirstOrDefault() ?? "",
        "locate" => _fnNasFilters.Locations.FirstOrDefault() ?? "",
        "decades" => _fnNasFilters.Decades.FirstOrDefault() ?? "",
        "recognition_status" => _fnNasFilters.RecognitionStatuses.FirstOrDefault() ?? "",
        "watched" => _fnNasFilters.Watched,
        _ => ""
    };

    private void ApplyFnNasFilters()
    {
        _fnNasFilters.Clear();
        foreach (var row in FnNasFilterRows)
        {
            var value = row.SelectedValue;
            if (value.Length == 0) continue;
            switch (row.Key)
            {
                case "type": _fnNasFilters.Type = value; break;
                case "genres": _fnNasFilters.Genres.Add(value); break;
                case "resolutions": _fnNasFilters.Resolutions.Add(value); break;
                case "color_range": _fnNasFilters.ColorRanges.Add(value); break;
                case "audio_type": _fnNasFilters.AudioTypes.Add(value); break;
                case "locate": _fnNasFilters.Locations.Add(value); break;
                case "decades": _fnNasFilters.Decades.Add(value); break;
                case "recognition_status": _fnNasFilters.RecognitionStatuses.Add(value); break;
                case "watched": _fnNasFilters.Watched = value; break;
            }
        }

        ShowFilterState();
        Requery();
        Log.Debug(Category, _fnNasFilters.IsBlank
            ? "清除飞牛筛选"
            : "飞牛筛选：" + string.Join("、", FnNasFilterRows
                .Where(row => row.SelectedValue.Length > 0)
                .Select(row => $"{row.Label}：{row.SelectedLabel}")));
    }

    private void ResetFnNasFilters()
    {
        _fnNasFilterLifetime?.Cancel();
        _fnNasFilterLifetime?.Dispose();
        _fnNasFilterLifetime = null;
        FnNasFilterLoading = false;
        _fnNasFilters.Clear();
        FnNasFilterRows.Clear();
        FnNasFilterOpen = false;
        ShowFilterState();
    }

    /// <summary>
    /// 飞牛工具栏上的「布局」：官网页端只有竖幅海报与横幅海报两项。横幅落到缩略图视图，
    /// 仍是客户端自己的显示设置，不改服务器上的媒体库。
    /// </summary>
    internal void SetFnNasLayout(bool wide)
    {
        if (!IsFnNas) return;
        var next = wide ? LibraryView.Thumb : LibraryView.Poster;
        var size = wide ? LibraryCardSize.Smaller : LibraryCardSize.Default;
        if (View == next && CardSize == size) return;
        var display = _display.Clone();
        display.View = next;
        // 飞牛的布局菜单没有「图像大小」第二档；横幅按官网页端的密度收小，竖幅回到默认。
        display.CardSize = size;
        ApplyDisplay(display);
    }

    /// <summary>The layout choices Jellyfin Web offers on this tab, in the web client's menu order.</summary>
    internal IReadOnlyList<LibraryView> JellyfinViews() =>
        IsJellyfin ? JellyfinLibrary.Views(CollectionType, Section) : [];

    /// <summary>Jellyfin's 布局 menu picked a shape. Same display pipeline as every other view change.</summary>
    internal void ChooseJellyfinView(LibraryView view)
    {
        if (!IsJellyfin || !JellyfinViews().Contains(view) || View == view) return;
        var display = _display.Clone();
        display.View = view;
        ApplyDisplay(display);
    }

    /// <summary>
    /// Where a card goes when it is invoked: wherever the shell says. <see cref="ShellPage.OpenItem"/>
    /// is the one place the 「detail page or another grid」 choice is made, so a poster in a library, one
    /// on the home page and 打开 in the context menu all agree about the same item.
    /// <para>
    /// This grid used to make that choice again for itself, and made it differently: a series, a season
    /// and a box set all went straight to a child grid. Opening 「99.9 刑事专业律师」 from its own library
    /// therefore landed on a bare grid of two season folders — no 简介, no 演职人员, no 单集列表 — while
    /// the very same poster clicked on the home page opened the series page. A movie went to the player
    /// for the same reason, which is the other half of 点卡片进详情页、点正中央的播放键才播放: the play key
    /// is what plays, and a card without one (a series, a season, a folder, a person) had nothing to lose
    /// by opening instead. A box set and a plain folder still answer with another grid — that is
    /// <see cref="DetailRequest.Supports"/>'s answer, not this page's.
    /// </para>
    /// </summary>
    internal void Open(CardItem card)
    {
        if (_actions is null || !_active || !card.IsCurrent) return;

        if (IsBrowseIndex)
        {
            _request!.BrowseGroup = card.Item.Name;
            RefreshBrowseChrome();
            _ = ReloadAsync();
            return;
        }
        RememberSearch(card.Item);
        _actions.OpenItem(card.Item);
    }

    /// <summary>
    /// Tooling: invokes the first row that drills down, exactly as a click on it would, and answers which
    /// row that was. Null when this grid holds nothing with a detail page — a library of box sets or of
    /// playlists answers a grid for every row it holds, so this is deliberately not
    /// <see cref="FirstItem"/>, which would leave the caller waiting for a page nobody opened.
    /// <para>
    /// Through <see cref="Open"/> rather than by asking the shell for a detail page directly, because that
    /// substitution is what let the routing above stay wrong: a check that opens the page itself proves
    /// the page works and says nothing about whether clicking a card ever reaches it.
    /// </para>
    /// </summary>
    internal EmbyItem? OpenFirstDetail()
    {
        if (Cards.FirstOrDefault(card => DetailRequest.Supports(card.Item)) is not { } card) return null;

        Open(card);
        return card.Item;
    }

    /// <summary>
    /// Every episode on this page, or null when its rows are not episodes — a mixed grid's neighbours
    /// are not each other's episodes, and offering them as 选集 would be wrong rather than merely
    /// unhelpful. What makes 「选集」 and 上一集/下一集 work off this grid instead of asking the server for a
    /// list it has already sent.
    /// </summary>
    internal IReadOnlyList<EmbyItem>? Episodes()
    {
        // 推荐页没有 Cards（三排各自装着卡），选集要从三排里凑：这一页上的单集本来就散在「继续观看」和
        // 「最近发布的剧集」两排里，右键一张卡时拿到的应该是它自己那一排那一批，不是空。
        var source = IsRecommendedPage
            ? RecommendedShelves.SelectMany(shelf => shelf.Cards)
            : Cards;
        var rows = source.ToList();
        var episodes = rows
            .Select(card => card.Item)
            .Where(item => item.Type == EmbyItemType.Episode)
            .ToList();

        return episodes.Count == rows.Count && episodes.Count > 0 ? episodes : null;
    }

    /// <summary>
    /// Which genres, tags or years the rows this grid is listing actually have.
    /// <para>
    /// The scope is this page's own query, which <see cref="EmbyClient.GetFilterValuesAsync"/> strips
    /// down to where to look: a list narrowed by the selection made from it empties itself as it is used.
    /// </para>
    /// </summary>
    internal async Task<IReadOnlyList<string>> FetchFilterValuesAsync(string key, CancellationToken cancellationToken)
    {
        if (_request is null || Aggregated || _scope is not { IsAvailable: true } source) return [];

        if (BuildQuery(0) is not { } scope) return [];

        return await source
            .ExecuteAsync((client, token) => client.GetFilterValuesAsync(key, scope, token), cancellationToken)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// A sort or filter change: the count the server reported is about a query that no longer exists,
    /// so the pager must not go on believing it.
    /// </summary>
    private void Requery()
    {
        _total = NotLoaded;
        _ = LoadAsync(reset: true);
    }

    partial void OnSortDescendingChanged(bool value)
    {
        if (_syncingDirection) return;

        RememberSort();
        Requery();
    }

    /// <summary>Puts the button in step with a newly chosen key, without that counting as a second change.</summary>
    private void ShowSort(string label, bool descending)
    {
        SortLabel = label;

        _syncingDirection = true;
        try { SortDescending = descending; }
        finally { _syncingDirection = false; }
    }

    /// <summary>
    /// The live settings object, read through the service every time rather than copied into a field: it is
    /// one instance for the process and the settings page edits it in place, so a copy taken at
    /// <see cref="Attach"/> would go stale the next time the settings window touched anything.
    /// </summary>
    private AppSettings Settings => _settings!.Settings;

    /// <summary>
    /// The sort this grid should open on: what it was last left in, or the key that suits what it lists.
    /// A season opened in alphabetical order is the one ordering that is always wrong, which is why the
    /// default is per preset rather than a single global one.
    /// </summary>
    private void RestoreSort()
    {
        var request = _request!;
        if (IsFnNas)
        {
            var fnNasFallback = FnNasSortCatalog.Default;
            SortKey = fnNasFallback.Value;
            var fnNasLabel = fnNasFallback.Label;
            var fnNasDescending = fnNasFallback.DescendingByDefault;

            if (MemoryKey() is { } fnNasMemoryKey
                && Settings.Ui.Sort.TryGetValue(fnNasMemoryKey, out var fnNasSaved)
                && FnNasSortCatalog.Find(fnNasSaved.By) is { } savedOption)
            {
                SortKey = savedOption.Value;
                fnNasLabel = savedOption.Label;
                fnNasDescending = fnNasSaved.Descending;
            }

            ShowSort(fnNasLabel, fnNasDescending);
            return;
        }

        if (IsJellyfin)
        {
            var jellyfinOptions = JellyfinLibrary.SortOptions(request.CollectionType, Section)
                .Where(option => SupportsSort(option.Value)).ToList();
            var jellyfinFallback = JellyfinLibrary.DefaultSort(request.CollectionType, Section);
            if (!jellyfinOptions.Contains(jellyfinFallback)) jellyfinFallback = jellyfinOptions.FirstOrDefault() ?? jellyfinFallback;

            SortKey = jellyfinFallback.Value;
            var jellyfinLabel = jellyfinFallback.Label;
            var jellyfinDescending = jellyfinFallback.DescendingByDefault;

            if (MemoryKey() is { } jellyfinMemoryKey
                && Settings.Ui.Sort.TryGetValue(jellyfinMemoryKey, out var jellyfinSaved)
                && jellyfinOptions.FirstOrDefault(option => option.Value == jellyfinSaved.By) is { } jellyfinSavedOption)
            {
                SortKey = jellyfinSavedOption.Value;
                jellyfinLabel = jellyfinSavedOption.Label;
                jellyfinDescending = jellyfinSaved.Descending;
            }

            ShowSort(jellyfinLabel, jellyfinDescending);
            return;
        }

        var preset = SortPreset;
        var fallback = IsSectionLibrary ? LibrarySortCatalog.Default(request.CollectionType, Section) : EmbySortBy.Default(preset);
        var options = IsSectionLibrary ? LibrarySortCatalog.For(request.CollectionType, Section, SourceServerVersion)
            : EmbySortBy.For(preset, SourceServerVersion);

        if (!SupportsSort(fallback.ValueOf(preset))) fallback = EmbySortBy.Default(preset);
        SortKey = fallback.ValueOf(preset);
        var label = fallback.Label;
        var descending = fallback.DescendingByDefault;

        if (MemoryKey() is { } memoryKey
            && Settings.Ui.Sort.TryGetValue(memoryKey, out var saved)
            && options.FirstOrDefault(option => option.ValueOf(preset) == saved.By) is { } option
            && option.AppliesTo(preset, SourceServerVersion)
            && SupportsSort(saved.By))
        {
            SortKey = saved.By;
            label = option.Label;
            descending = saved.Descending;
        }

        ShowSort(label, descending);
    }

    private bool SupportsSort(string value) => _scope?.Connection.IsFnNas == true
        ? Momoka.FnNas.FnNasMediaAdapter.SupportsSort(value)
        : _scope?.Connection.IsPlex == true
            ? Momoka.Plex.PlexMediaAdapter.SupportsSort(value)
            : _scope?.Connection.IsJellyfin != true || JellyfinQuery.SupportsSort(value);

    private void RememberSort()
    {
        if (MemoryKey() is not { } memoryKey) return;

        var ui = Settings.Ui;
        ui.Sort[memoryKey] = new LibrarySort { By = SortKey, Descending = SortDescending };

        // Not saved here: the settings file is written on exit, and a sort change is not worth a disk
        // write on every click.
    }

    /// <summary>
    /// 这一面墙打开时用哪一套显示设置，与排序同一套记忆：一个库一份，电影库和电视剧库各是各的。
    /// </summary>
    /// <remarks>
    /// 主页某一排点进来的两页（继续观看、接下来看）不问记忆，直接开在缩略图视图 —— 2026-09-14
    /// 「图片要用背景图」：这两页装的是看了一半的条目，卡片该是 16:9 的剧照（<c>CardItem</c> 宽卡先要 Thumb、
    /// 再要 Backdrop），和主页那两排长得一样，而不是一面 2:3 的海报墙。这两页没有 <see cref="MemoryKey"/>
    /// （不是哪个库的根），视图选择记不住也不该记：每次进来都先给背景图，页内想换海报再换。
    /// <para>
    /// 旧文件里只有 <c>UiSettings.Views</c>（一个库一个形状）。读到它就搬进新的那一份，从此
    /// 视图、图像大小、分组、字段一起记 —— 不迁移的话，升级后每个库都会退回默认。
    /// </para>
    /// </remarks>
    private void RestoreDisplay()
    {
        if (_request?.Row is not null)
        {
            _display = new LibraryDisplay { View = LibraryView.Thumb };
            RaiseDisplay();
            return;
        }

        var saved = MemoryKey() is { } memoryKey ? SavedDisplay(memoryKey) : null;
        if (saved is not null)
        {
            _display = saved.Normalized();
        }
        else
        {
            _display = LibraryDisplayCatalog.DefaultFor(Section, IndexKind);
        }

        // Jellyfin's tabs have their own layout menus; an Emby-only shape (表格, 光盘封面, 徽标) restored
        // from a shared library entry becomes a poster rather than a view the Jellyfin 布局 menu cannot
        // offer or change back from.
        if (IsJellyfin) _display = JellyfinLibrary.Fit(_display, CollectionType, Section);

        RaiseDisplay();
    }

    /// <summary>
    /// 这个库记着的那一份，或者 null。旧字段（<see cref="UiSettings.Views"/>）只在没有新记录时读一次，
    /// 读到就顺手写进新记录，下一次起就走新字段。
    /// </summary>
    private LibraryDisplay? SavedDisplay(string memoryKey)
    {
        if (Settings.Ui.Display.TryGetValue(memoryKey, out var saved)) return saved;
        if (!Settings.Ui.Views.TryGetValue(memoryKey, out var legacy)) return null;

        var migrated = new LibraryDisplay { View = legacy };
        Settings.Ui.Display[memoryKey] = migrated;
        return migrated;
    }

    private void RememberDisplay()
    {
        if (MemoryKey() is not { } memoryKey) return;

        Settings.Ui.Display[memoryKey] = _display.Clone();
    }

    /// <summary>把依赖这套设置的读数一起喊一遍（行高、格宽、视图名、取图顺序）。</summary>
    private void RaiseDisplay()
    {
        // 这一页真正生效的字段：换页（文件夹 ↔ 节目 ↔ 集 ↔ 索引页）也要重算，所以放在这里而不是只算一次。
        _fields = LibraryDisplayCatalog.Applied(_display.Fields, Section, View, IndexKind, CollectionType);
        // 卡片宽度就是这一面墙此刻按哪一档画的 —— 自检与探针读的是它，它得跟着图像大小走。
        CardWidth = CardBuildWidth;
        OnPropertyChanged(nameof(View));
        OnPropertyChanged(nameof(CardSize));
        OnPropertyChanged(nameof(GroupIntoCollections));
        OnPropertyChanged(nameof(Fields));
        OnPropertyChanged(nameof(ViewGlyph));
        OnPropertyChanged(nameof(ViewLabel));
        OnPropertyChanged(nameof(FnNasLayoutWide));
        OnPropertyChanged(nameof(RowHeight));
        OnPropertyChanged(nameof(CellWidth));
        OnPropertyChanged(nameof(CardMinWidth));
        OnPropertyChanged(nameof(CardBuildWidth));
        OnPropertyChanged(nameof(CardImageTypes));
    }

    /// <summary>
    /// 需求 5：the filters this grid should open on, restored on the same terms as the sort. A clone,
    /// because the panel edits this object in place and settings must not change under the file writer
    /// until <see cref="RememberFilters"/> says so.
    /// </summary>
    private void RestoreFilters()
    {
        _filters = SupportsBrowseConditions && MemoryKey() is { } memoryKey
            && Settings.Ui.Filters.TryGetValue(memoryKey, out var saved)
                ? saved.Clone()
                : new ItemFilters();

        // A library that switched product (or a settings file written while it was Emby) may carry ids
        // Jellyfin's panel cannot show. They would not be counted or drawn, but they would still be sent;
        // pruning keeps the visible selection and the query saying the same thing.
        if (IsJellyfin) _filters = JellyfinLibrary.Prune(_filters);

        ShowFilterState();
    }

    private void RememberFilters()
    {
        if (MemoryKey() is not { } memoryKey) return;

        var ui = Settings.Ui;

        // IsBlank, not IsEmpty: a selection this build cannot read is still the user's, and dropping it
        // the moment they touch the panel would lose it for the build that can.
        if (_filters.IsBlank) ui.Filters.Remove(memoryKey);
        else ui.Filters[memoryKey] = _filters.Clone();
    }

    private void ShowFilterState()
    {
        if (IsFnNas)
        {
            ShowFnNasFilterState();
            return;
        }

        FilterCount = _filters.Count;
        var fields = IsJellyfin ? JellyfinLibrary.FilterFields(FilterCollectionType, Section) : EmbyFilterBy.Fields;
        FilterTooltip = FilterCount == 0 ? "筛选" : _filters.Describe(fields);

        // 工具栏底下那一行：一条筛选一格，点一下去掉那一条。整行重建而不是逐格对账 —— 一次改动最多动一格，
        // 而这一行最长也就十几格，重建一遍比「哪一格该留」这道题便宜得多，也不会算错。
        FilterChips.Clear();
        foreach (var chip in EmbyFilterBy.Chips(_filters, fields))
            FilterChips.Add(new FilterChipRow(chip, DropFilter));

        OnPropertyChanged(nameof(FilterChipsVisibility));
    }

    private void ShowFnNasFilterState()
    {
        var active = FnNasFilterRows.Where(row => row.SelectedValue.Length > 0).ToList();
        FilterCount = active.Count;
        FilterTooltip = FilterCount == 0 ? "筛选" : string.Join("；", active.Select(row => $"{row.Label}：{row.SelectedLabel}"));

        // 飞牛的筛选面板自己就是「哪几条在生效」：选中的值在面板里保持高亮，收起来的时候按钮上还有数字。
        // 不再往工具栏下面铺一行可点的标签 —— 用户 2026-10-19 令「点击选项后不要在界面添加标签」。
        // 标签条本身留给 Emby，那里筛选面板整块盖住页面，收起后确实只剩那一条能说明筛了什么。
        FilterChips.Clear();
        OnPropertyChanged(nameof(FilterChipsVisibility));
        OnPropertyChanged(nameof(FilterButtonWidth));
    }

    /// <summary>
    /// 点掉一格筛选条。走的是面板打勾去勾的同一条路（<see cref="ApplyFilters"/>）：写进设置、重查、
    /// 那颗按钮上的数跟着变。
    /// </summary>
    private void DropFilter(FilterChip chip)
    {
        chip.Remove(_filters);
        ApplyFilters();
    }

    /// <summary>整行清空 —— 那一行末尾那颗键。</summary>
    [RelayCommand]
    private void ClearFilters()
    {
        if (IsFnNas)
        {
            if (FnNasFilterRows.Count == 0) return;
            foreach (var row in FnNasFilterRows) row.Reset();
            ApplyFnNasFilters();
            return;
        }

        if (_filters.IsBlank) return;

        _filters.Toggles.Clear();
        foreach (var list in EmbyFilterBy.Lists) _filters.Values(list.Key).Clear();

        ApplyFilters();
    }

    /// <summary>
    /// Where this grid's sort and filters are remembered, or null for a grid whose state is not worth
    /// keeping. Only library roots qualify: a folder or a season would add an entry to settings.json
    /// every time one was opened.
    /// </summary>
    private string? MemoryKey() =>
        _request is { CollectionType: not null, ParentId: { Length: > 0 } id } && _scope is { IsAvailable: true } scope
            ? BrowsingPreferences.LibraryKey(scope.Connection, id)
                + (Section == LibrarySection.Items ? "" : "/section/" + Section) : null;

    private string Describe() =>
        _total <= 0 ? (Cards.Count == 0 ? "没有内容" : $"共 {Cards.Count} 项")
        : Cards.Count >= _total ? $"共 {_total} 项"
        : $"已加载 {Cards.Count} / {_total} 项";

    /// <summary>
    /// The query this grid is showing, or null when there is nothing to ask: the search page before a
    /// term is typed. Null rather than a query with an empty term, because Emby answers 「no term, no
    /// parent」 with its library root — a wrong answer that looks like a right one.
    /// </summary>
    private ItemQuery? BuildQuery(int start)
    {
        var query = BuildQueryCore(start);
        if (query is not null) query.FnNasFilters = IsFnNas ? _fnNasFilters.Clone() : null;
        return query;
    }

    private ItemQuery? BuildQueryCore(int start)
    {
        var request = _request!;
        var limit = _settings!.PageSize;

        if (request.IsSearch)
        {
            return LibrarySearch.Query(request.SearchTerm, request.SearchCategory, start, limit);
        }

        if (request.IsFavorites)
            return ItemQuery.Favorites(start, limit, SortKey, SortDescending, _filters.Clone());

        // 主页某一排点进来的那一页（2026-09-14「新增点击图中红框的标题可以进入对应的页面」）。和演职人员、
        // 类型那两支并排：它同样不是文件夹、没有父级，落到下面那一支就成了「服务器的媒体库列表」。
        // 排在它们之前是因为这一页的身份最强 —— Row 一填上，别的字段本就该都是空的。
        if (request.Row is { } row)
        {
            return row switch
            {
                // 继续观看这一页没有通用查询可发：它整份由服务器那条 Resume 接口给（见 IsResumePage 与
                // ReadAsync），这里返回 null 是「这一页不发通用查询」，不是「这一页没得问」—— LoadAsync 先问
                // ReadAsync，这一支走不到。
                //
                // 2026-09-22 之前这里发的是 ItemQuery.Resume（Filters=IsResumable）。那一条只问「有没有播放
                // 进度」，不问「标没标已看」「有没有被移出继续观看」：同一天服务器上它给 9 条、Resume 接口给
                // 1 条，于是主页那排 1 项、点进去 9 项。这个差别在通用查询里补不出来 ——
                // Filters=IsResumable,IsUnplayed 只收到 4 条，而「被移出」没有对应的筛选词，把
                // NotHiddenFromResume 塞进去会被服务器静默丢掉（实测）。那个工厂因此一并删了：留着它只会让
                // 下一个人再走一遍这条错路。
                HomeLayout.HomeRowTarget.Resume => null,

                // 接下来看：Emby 的 NextUp 是「这部剧的下一集还没看」，落到通用查询上是「一集都还没看过的
                // 剧集」。所以走自己的工厂（2026-09-14 从 Resume 拆出来的另一半），多给一个把单集排除掉的
                // 类型名单 —— 一集继承整部剧的已看状态，单集留在里面，一部看了一半的剧也会满足「没看完」，
                // 两页就长成同一页了。这一页的判据不跟继续观看那一页走：两页问的是两个问题。
                HomeLayout.HomeRowTarget.NextUp =>
                    ItemQuery.NextUp(start, limit, SortKey, SortDescending, _filters,
                        [EmbyItemType.Series, EmbyItemType.Movie, EmbyItemType.Video]),

                _ => null
            };
        }

        // 演职人员：a person's credits. Ahead of the folder query on purpose — a person has no children,
        // so the query below would answer with the server's library list instead of an empty grid, which
        // is a far more confusing wrong answer.
        if (request.PersonId is { Length: > 0 } personId)
        {
            return new ItemQuery
            {
                PersonId = personId,
                Recursive = true,
                IncludeItemTypes = ItemQuery.SweptTypes,
                SortBy = SortKey,
                Descending = SortDescending,
                StartIndex = start,
                Limit = limit,
                Filters = _filters.Clone()
            };
        }

        // 按类型浏览：一个类型横跨所有媒体库，所以没有 ParentId，全库递归加一个 Genres。排在文件夹那一支
        // 前面，理由和演职人员那一支一样 —— 它也没有父级，落到下面就成了「服务器的媒体库列表」。
        // 类型不进 Filters 而是走请求自己那一格：它是「这一页是什么」，不是「在这一页里再筛一下」，所以工具栏
        // 底下那一行筛选条点不掉它 —— 点掉了这一页就没有身份了。
        if (request.Genre is { Length: > 0 } genre)
        {
            return new ItemQuery
            {
                Genre = genre,
                Recursive = true,

                // 不含单集：一集继承整部剧的类型，带上它就是同一部剧在这一格里出现二十遍。音乐同理不在内
                // （同搜索那一支）。
                IncludeItemTypes = [EmbyItemType.Movie, EmbyItemType.Series, EmbyItemType.Video],
                SortBy = SortKey,
                Descending = SortDescending,
                StartIndex = start,
                Limit = limit,
                Filters = _filters.Clone()
            };
        }

        if (IsSectionLibrary)
            return LibraryBrowse.Query(request.CollectionType!, request.ParentId!, request.Section,
                start, IsUpcomingPage ? LibraryUpcoming.PageSize : limit, SortKey, SortDescending, _filters, request.BrowseGroup, _display);

        var types = ItemQuery.FlattenedTypes(request.CollectionType);

        return new ItemQuery
        {
            ParentId = request.ParentId,
            // Recursive only at a library root. Below one, the folder tree is what the user is walking:
            // flattening a series would put every episode of every season on one page.
            Recursive = types.Count > 0,
            IncludeItemTypes = types,
            SortBy = SortKey,
            Descending = SortDescending,
            StartIndex = start,
            Limit = limit,
            // Sent unconditionally: an empty selection contributes no parameters, so there is nothing to
            // branch on and no way for the query and the panel to disagree.
            Filters = _filters.Clone()
        };
    }

    private static string Glyph(int codepoint) => char.ConvertFromUtf32(codepoint);
}
