using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Momoka.Configuration;
using Momoka.Diagnostics;
using Momoka.Emby;
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
    private readonly Action<FilterChip> _drop;

    internal FilterChipRow(FilterChip chip, Action<FilterChip> drop)
    {
        Chip = chip;
        _drop = drop;
    }

    internal FilterChip Chip { get; }

    public string Label => Chip.Label;

    /// <summary>读屏软件念出来的那一句：光一个片名念不出「点它会怎样」。</summary>
    public string AutomationName => $"去掉筛选：{Chip.Label}";

    [RelayCommand]
    private void Drop() => _drop(Chip);
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
    private IReadOnlyList<EmbySessionScope> _sources = [];
    private AggregateQueryCursor? _cursor;
    private readonly HashSet<string> _resumeRead = new(StringComparer.Ordinal);
    private bool Aggregated => _sources.Count > 1;
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
    public double RowHeight => LibraryCardMetrics.Height(View, CardBuildWidth) + Momoka.Infrastructure.CardSize.Chrome + NoteHeight;

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
        LibraryView.Thumb => 0xE8A9,   // 网格（宽格）
        LibraryView.List => 0xE8FD,    // 列表
        LibraryView.Table => 0xE8FD,   // 表格
        LibraryView.Banner => 0xE7C4,  // 横幅
        LibraryView.Disc => 0xE958,    // 光盘
        LibraryView.Logo => 0xE8D2,    // 徽标
        _ => 0xE80A                    // 平铺
    };

    /// <summary>Whether this grid draws a 16:9 still rather than a poster.</summary>
    internal bool ViewIsWide => View is LibraryView.Thumb or LibraryView.Logo or LibraryView.List or LibraryView.Table;

    /// <summary>
    /// The width a card is built at in the current view. The poster shape draws at
    /// <see cref="CardSize.PosterWidth"/>; the thumb and list shapes draw the same wide width the detail
    /// page's episode rows use, so the two list styles in the app agree with each other.
    /// </summary>
    internal int CardBuildWidth => LibraryCardMetrics.Width(View, CardSize);

    internal bool CardBuildWide => ViewIsWide;

    /// <summary>
    /// 这一面墙取图的先后顺序。网页端七种视图各要各的图：海报要竖图、横幅图要 Banner、光盘封面要
    /// Disc、徽标与缩略图要背景图（Backdrop），取不到再往后退一格。空数组就是让卡片按自己的老规矩挑
    /// （竖卡先要 Primary、宽卡先要 Thumb）。
    /// </summary>
    internal IReadOnlyList<string> CardImageTypes => View switch
    {
        LibraryView.Banner => ["Banner", "Backdrop", "Primary"],
        LibraryView.Disc => ["Disc", "Primary"],
        LibraryView.Logo => ["Backdrop", "Thumb"],
        _ => []
    };

    /// <summary>How many filters are on, for the badge on the filter button.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterBadgeVisibility))]
    [NotifyPropertyChangedFor(nameof(FilterButtonWidth))]
    public partial int FilterCount { get; set; }

    public Visibility FilterBadgeVisibility => Show(FilterCount > 0);
    public double FilterButtonWidth => FilterCount > 0 ? 52 : 32;

    /// <summary>
    /// 工具栏底下那一行筛选条，一条筛选一格。见 <see cref="EmbyFilterBy.Chips"/>：那颗按钮上的数字说「筛了
    /// 几条」，这一行说「筛的是哪几条」，而且每一格自己能被点掉。
    /// </summary>
    public ObservableCollection<FilterChipRow> FilterChips { get; } = [];

    public Visibility FilterChipsVisibility => Show(FilterChips.Count > 0 && FilterVisibility == Visibility.Visible);

    /// <summary>
    /// Whether the letter bar belongs on this grid — Emby's own rule: name-ordered, and enough rows
    /// that jumping is worth the trip. Recomputed on every page arrival, because the count is what
    /// the second half of the rule reads.
    /// </summary>
    public Visibility AlphaVisibility => Show(
        !Aggregated && !IsBrowseIndex && Section != LibrarySection.Recommended && SupportsBrowseConditions && _request?.IsSearch != true && SortKey == EmbySortBy.Name && !SortDescending && _total is > 30);

    /// <summary>
    /// 这台服务器认不认 Emby 那套浏览条件 —— 字母跳转、筛选面板、按库记住筛选。飞牛与 Plex 各有自己的
    /// 查询语言，装不出同一套条件，所以这三样在它们名下一起收起来（排序仍按各自支持的那几档单独裁剪，
    /// 见 <see cref="SupportsSort"/>）。
    /// </summary>
    private bool SupportsBrowseConditions => _scope?.Connection.IsFnNas != true && _scope?.Connection.IsPlex != true;

    /// <summary>Whether this grid has anything the player could start, for the two play buttons.</summary>
    internal bool HasPlayable => _active && SourcesAvailable && _firstPage && !Busy
        && Cards.Any(card => card.Item.IsPlayable);

    protected override void BusyChanged() => RowsArrived();

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
    internal string? SortPreset => IsSectionLibrary
        ? LibraryBrowse.SortPreset(_request!.CollectionType, _request.Section) : _request?.SortPreset;

    internal bool IsSectionLibrary => _request is { ParentId.Length: > 0, CollectionType: "movies" or "tvshows", IsSearch: false };
    internal LibrarySection Section => _request?.Section ?? LibrarySection.Items;
    internal bool IsBrowseIndex => IsSectionLibrary && LibraryBrowse.IsIndex(Section, _request!.BrowseGroup);
    internal IReadOnlyList<LibrarySectionEntry> BrowseSections => IsSectionLibrary
        ? LibraryBrowse.Sections(_request!.CollectionType, _scope?.Connection.ServerKind ?? MediaServerKind.Emby) : [];
    public Visibility SectionVisibility => Show(IsSectionLibrary && BrowseSections.Count > 1);

    /// <summary>
    /// 这一页底下那一条工具栏（总数、播放、排序、筛选、设置）在不在。网页端不是每一页都有：
    /// 推荐、预告、收藏三页只有内容，总数与按键一起收起 —— 那三页的顺序或范围是服务器定的，摆一排按不动的键
    /// 只是占地方。索引页（类型、播出平台）留总数与设置；文件夹没有筛选可筛（它问的是文件树）。
    /// </summary>
    private bool HasToolbar => !IsSectionLibrary || Section is not
        (LibrarySection.Recommended or LibrarySection.Trailers or LibrarySection.Favorites);

    public Visibility ToolbarVisibility => Show(HasToolbar);
    public Visibility SettingsVisibility => Show(IsSectionLibrary && HasToolbar);
    public Visibility BrowseGroupVisibility => Show(IsSectionLibrary && _request?.BrowseGroup is not null);
    public string BrowseGroupLabel => _request?.BrowseGroup ?? "";
    /// <summary>
    /// 播放全部与随机播放两颗键在不在。电影库与「集」那一页有东西可播；电视剧库的「节目」页装的是剧集本身，
    /// 一集都播不了 —— 那种页面上两颗键没有意义，整对收起（原来它们藏在「更多」里，那个菜单 2026-10-17 按
    /// 用户的话删掉了：菜单里除了这两颗常年灰着的键什么都没有）。
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
        // 换页会换字段表（文件夹 / 集 / 其余），生效的字段跟着这一页重算。
        _fields = LibraryDisplayCatalog.Applied(_display.Fields, Section);
        foreach (var name in new[] { nameof(SectionVisibility), nameof(BrowseGroupVisibility), nameof(BrowseGroupLabel),
            nameof(DirectPlayVisibility), nameof(SortFilterVisibility), nameof(FilterVisibility),
            nameof(ToolbarVisibility), nameof(SettingsVisibility), nameof(Fields),
            nameof(AlphaVisibility), nameof(FilterChipsVisibility), nameof(TotalLabel),
            nameof(RecommendedVisibility), nameof(GridVisibility) }) OnPropertyChanged(name);
    }

    internal int LoadedCount => Cards.Count;

    /// <summary>The first row this grid loaded, for <c>--play</c>. Null before the first page arrives.</summary>
    internal EmbyItem? FirstItem => Cards.Count > 0 ? Cards[0].Item : null;

    /// <summary>
    /// Whether the server has rows this grid has not asked for yet. True before the first page, because
    /// 「nothing has been asked」 and 「everything has arrived」 must not look the same to the pager.
    /// </summary>
    internal bool HasMore => !IsRecommendedPage && !(_request?.IsSearch == true && string.IsNullOrWhiteSpace(_request.SearchTerm))
        && (Aggregated ? _cursor?.HasMore ?? true : _total < 0 || Cards.Count < _total);

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
        && LibraryRecommended.Rows(_request!.CollectionType).Count > 0;

    /// <summary>三排货架在不在（网格由 <see cref="GridVisibility"/> 收起来，两者互斥）。</summary>
    public Visibility RecommendedVisibility => Show(IsRecommendedPage);

    /// <summary>网格在不在。推荐页把整块网格（含它的滚动器与表格列头）收起，屏上只剩那三排。</summary>
    public Visibility GridVisibility => Show(!IsRecommendedPage);

    /// <summary>
    /// 照这一页该有的排数把货架造出来。已经是对的就不重建 —— 每次刷新都 <c>Clear</c> 再 <c>Add</c>，屏上那三排
    /// 会被拆掉重装一遍，滚到一半的位置也会回开头。换库、换分类时排数或标题变了才重造。
    /// </summary>
    private void EnsureRecommendedShelves()
    {
        if (_images is not { } images || _request is null) return;

        var rows = LibraryRecommended.Rows(_request.CollectionType);
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
            var rows = LibraryRecommended.Rows(request.CollectionType);
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
                return [.. resume.Items.Where(item => !EmbyItemType.IsMusic(item.Type))];
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
    public Visibility SortFilterVisibility => Show(HasToolbar && !IsResumePage && !Aggregated && !IsBrowseIndex);
    public Visibility FilterVisibility => Show(HasToolbar && !IsResumePage && !Aggregated && !IsBrowseIndex
        && Section != LibrarySection.Folders && SupportsBrowseConditions);

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
        _sources = request.Source is { } source ? [source]
            : request.ParentId is not null || request.PersonId is not null ? _scope is null ? [] : [_scope]
            : session.Sources.ToArray();
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
        EmptyNotice = request.IsSearch ? "输入关键字开始搜索"
            : request.Row == HomeLayout.HomeRowTarget.Resume ? "这里还没有看到一半的内容"
            : request.Row is not null ? "这里还没有看过的内容"
            : "这里没有内容";
        OnPropertyChanged(nameof(SearchVisibility));
        OnPropertyChanged(nameof(MoviePilotTabsVisibility));

        // 这两颗键在不在，也是这一页的身份说的（见 SortFilterVisibility）—— 换一个请求就得重问一次。
        OnPropertyChanged(nameof(SortFilterVisibility));
        OnPropertyChanged(nameof(FilterVisibility));

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

    /// <summary>删除只改变当前集合；分页起点跟随新数量，删除前发出的分页响应作废。</summary>
    internal async Task<bool> RemoveAsync(CardItem card, CancellationToken queryToken, bool deleted = true)
    {
        if (!QueryCurrent(queryToken) || !Cards.Contains(card)) return false;
        // 容器会连带删除未加载的后代；查询刚切换时屏上也可能仍是旧排序，不能据旧集合推算新分页。
        if (Aggregated || !_firstPage || deleted && (card.Item.IsFolder || card.Item.Type is EmbyItemType.Series
            or EmbyItemType.Season or EmbyItemType.Folder or EmbyItemType.BoxSet or EmbyItemType.Playlist))
            return await RefreshAfterRemovalAsync(queryToken).ConfigureAwait(true);
        base.Cancel();
        // 保留查询身份，菜单里已发出的其他删除仍属于这一页；只撤销读取和字母跳转。
        _jumps.Cancel();
        card.AbandonPoster();
        Cards.Remove(card);
        if (_total > 0) _total--;
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

        // 下一集先问回来再动屏（同主页那一排）：先撤后插会让这一格空着闪一下。问不出来（最后一集、断线、
        // 离页）都按「没有下一集」办。
        var next = ResumeRow.WantsNext(watched)
            ? await ResumeSlot.NextEpisodeAsync(scope, watched, Category).ConfigureAwait(true)
            : null;

        // 这一趟往返里页面可能已经走了、换了账号、或者换了一次查询：那就一个字都别动。
        if (!QueryCurrent(queryToken) || !card.IsCurrent || !Cards.Contains(card)) return;

        var slot = ResumeSlot.Apply(Cards, card, next, NewCard);
        if (_resumeRead.Remove(watched.IdentityKey)) _cursor?.RemoveConsumed(scope.Key);
        Log.Info(Category, slot is null
            ? $"「{watched.Name}」标记已看，从继续观看撤下"
            : $"「{watched.Name}」标记已看，那一格换成「{slot.Name}」");

        // 撤掉一格少一项、换掉那一格总数不变（同 RemoveAsync 里那份账）：分页起点跟着新数量走。
        if (slot is null && _total > 0) _total--;
        ShowEmptyNotice = Cards.Count == 0;
        RowsArrived();
    }

    /// <summary>照这一页当前的显示设置造一张卡（同 <see cref="LoadAsync"/> 那一句）。</summary>
    private CardItem NewCard(EmbyItem item) =>
        new(item, _images!, CardBuildWidth, CardBuildWide,
            subtitle: CardLine(item), imageTypes: CardImageTypes,
            tableFields: View == LibraryView.Table ? Fields : null,
            note: LibraryCardText.Note(item, Fields));

    /// <summary>
    /// 卡片底下那一行字。勾了字段就按勾的拼；一项能说的都没有时交回卡片自己的副标题（
    /// 剧集的「S01E02 · 剧名」、文件夹的「文件夹」），总比留一行空白强。
    /// </summary>
    private string? CardLine(EmbyItem item) =>
        LibraryCardText.Line(item, Fields) is { Length: > 0 } line ? line : null;

    private async Task<bool> RefreshAfterRemovalAsync(CancellationToken queryToken)
    {
        var wanted = Math.Max(Cards.Count, _settings!.PageSize);
        _firstPage = false;
        _jumps.Cancel();
        var token = BeginLoad();
        try
        {
            ResetCursor();
            var items = new List<EmbyItem>();
            var total = 0;
            do
            {
                var result = await ReadAsync(items.Count, token).ConfigureAwait(true);
                if (!QueryCurrent(queryToken) || !IsCurrent(token)) return false;
                total = result?.TotalRecordCount ?? 0;
                if (result is null || result.Items.Count == 0) break;
                items.AddRange(result.Items);
            } while (items.Count < Math.Min(wanted, total));

            // 原有卡片原位保留，只移除消失的项并补入需要的项，避免容器、焦点和海报整页重建。
            var retained = Cards.DistinctBy(card => card.Item.IdentityKey).ToDictionary(card => card.Item.IdentityKey, StringComparer.Ordinal);
            var desired = items.Select(item => retained.TryGetValue(item.IdentityKey, out var existing) ? existing
                : NewCard(item)).ToArray();
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
        if (!_active || !SourcesAvailable || _request is null || _actions is null) return;
        // 推荐页不是一面网格：三排货架各问一次，没有翻页、没有字母跳转、没有「共 N 项」。整条路分开走，
        // 下面那些分页与游标的账一笔都不记（见 LoadRecommendedAsync）。
        if (IsRecommendedPage)
        {
            await LoadRecommendedAsync(reset).ConfigureAwait(true);
            return;
        }
        if (!reset && (Busy || !HasMore || !_firstPage || _pageFailed)) return;
        if (reset)
        {
            _queryToken = _queries.Begin();
            _jumps.Cancel();
            _firstPage = false;
            _pageFailed = false;
            _total = NotLoaded;
            ResetCursor();
            ShowEmptyNotice = false;
            if (_request.IsSearch || IsSectionLibrary)
            {
                foreach (var card in Cards) card.AbandonPoster();
                Cards.Clear();
            }
        }
        var queryToken = _queryToken;
        var token = BeginLoad();
        try
        {
            var start = reset ? 0 : Cards.Count;
            var result = await ReadAsync(start, token).ConfigureAwait(true);
            if (!QueryCurrent(queryToken) || !IsCurrent(token)) return;
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
                    Cards.Add(NewCard(item));
                    if (IsResumePage) _resumeRead.Add(item.IdentityKey);
                }
                if (result.Items.Count == 0) _total = Cards.Count;
            }
            _firstPage = true;
            _pageFailed = false;
            EmptyNotice = result is null ? "输入关键字开始搜索"
                : _request.IsSearch ? $"没有找到“{SearchText}”的匹配结果" : "这里没有内容";
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
        if (Aggregated)
            return await ReadAggregatedAsync(_cursor ??= NewCursor(), token).ConfigureAwait(true);
        return await ReadSourceAsync(_sources[0], start, token).ConfigureAwait(true);
    }

    private AggregateQueryCursor NewCursor() => new(_sources.Select(source => source.Key));

    private void ResetCursor()
    {
        _cursor = Aggregated ? NewCursor() : null;
        _resumeRead.Clear();
    }

    private async Task<ItemsResult> ReadAggregatedAsync(AggregateQueryCursor cursor, CancellationToken token)
    {
        var page = await cursor.ReadAsync((key, offset, ct) =>
            ReadSourceAsync(_sources.First(source => source.Key == key), offset, ct), token).ConfigureAwait(true);
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

        var preset = SortPreset;

        return
        [
            .. EmbySortBy.For(preset, SourceServerVersion)
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
        if (option.ValueOf(SortPreset) != SortKey) return;

        SortDescending = !SortDescending;
    }

    /// <summary>A key was picked out of the flyout.</summary>
    internal void ChooseSort(EmbySortOption option)
    {
        if (!_active || !SourcesAvailable || _request is null || _actions is null || Aggregated) return;

        var key = option.ValueOf(SortPreset);
        if (!SupportsSort(key)) return;

        // Emby's own behaviour, and the only sensible one: a date key opens newest first and a name key
        // A to Z. Choosing 加入日期 and being shown the oldest thing in the library is never what was
        // meant, however the direction happened to be left by the previous key.
        if (SortFilterVisibility != Visibility.Visible || !option.AppliesTo(SortPreset, SourceServerVersion)) return;
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
                    async (_, ct) => await ReadAggregatedAsync(cursor, ct).ConfigureAwait(true),
                    () => IsCurrent(token), token).ConfigureAwait(true);
                return (all, token);
            }
            var items = await PlayableList.CollectAsync(
                loaded, hasMore, ReadAsync, () => IsCurrent(token), token).ConfigureAwait(true);
            return (items, token);
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
    private static async Task<long> CountAsync(EmbySessionScope scope, ItemQuery source, string? letter, CancellationToken token)
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
        var result = await scope
            .ExecuteAsync((client, ct) => client.GetItemsAsync(query, ct), token)
            .ConfigureAwait(true);
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
        var preset = SortPreset;
        var fallback = EmbySortBy.Default(preset);

        SortKey = fallback.ValueOf(preset);
        var label = fallback.Label;
        var descending = fallback.DescendingByDefault;

        if (MemoryKey() is { } memoryKey
            && Settings.Ui.Sort.TryGetValue(memoryKey, out var saved)
            && EmbySortBy.Find(saved.By) is { } option
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
            // 剧集那一页（「集」）装的是单集：一行一条比一面海报墙好读，所以默认就是缩略图，
            // 电影库和电视剧库的条目页默认还是海报。这是两种库本来就不同的地方之一。
            _display = new LibraryDisplay
            {
                View = Section == LibrarySection.Episodes ? LibraryView.Thumb : LibraryView.Poster
            };
        }
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
        // 这一页真正生效的字段：换页（文件夹 ↔ 节目 ↔ 集）也要重算，所以放在这里而不是只算一次。
        _fields = LibraryDisplayCatalog.Applied(_display.Fields, Section);
        // 卡片宽度就是这一面墙此刻按哪一档画的 —— 自检与探针读的是它，它得跟着图像大小走。
        CardWidth = CardBuildWidth;
        OnPropertyChanged(nameof(View));
        OnPropertyChanged(nameof(CardSize));
        OnPropertyChanged(nameof(GroupIntoCollections));
        OnPropertyChanged(nameof(Fields));
        OnPropertyChanged(nameof(ViewGlyph));
        OnPropertyChanged(nameof(ViewLabel));
        OnPropertyChanged(nameof(RowHeight));
        OnPropertyChanged(nameof(CellWidth));
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
        FilterCount = _filters.Count;
        FilterTooltip = FilterCount == 0 ? "筛选" : _filters.Describe();

        // 工具栏底下那一行：一条筛选一格，点一下去掉那一条。整行重建而不是逐格对账 —— 一次改动最多动一格，
        // 而这一行最长也就十几格，重建一遍比「哪一格该留」这道题便宜得多，也不会算错。
        FilterChips.Clear();
        foreach (var chip in EmbyFilterBy.Chips(_filters))
            FilterChips.Add(new FilterChipRow(chip, DropFilter));

        OnPropertyChanged(nameof(FilterChipsVisibility));
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
                start, limit, SortKey, SortDescending, _filters, request.BrowseGroup, _display);

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
