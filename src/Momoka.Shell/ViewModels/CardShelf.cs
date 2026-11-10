using CommunityToolkit.Mvvm.ComponentModel;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Shell.Views;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// One shelf of cards: a heading and the cards under it. Used by the home page's four horizontal rows, by
/// the detail page's 演职人员 and 全部剧季 rows, and — through <see cref="FillRows"/> — by its 单集 list,
/// which is the same data drawn down the page instead of across it.
/// <para>
/// The home page used to be three near-identical blocks of XAML — a heading, a fixed-height
/// <c>ScrollView</c> and an <c>ItemsRepeater</c>, written out once per row with the height and the
/// template swapped — and a matching <c>Fill(row, section, …)</c> call per row in code-behind. Adding
/// 媒体库 as a fourth row under that arrangement meant copying twenty lines of markup and getting the
/// two magic heights right by hand. Here a row is data, the page draws whatever rows it is handed, and
/// a fifth one is one line in <see cref="HomeViewModel"/>.
/// </para>
/// <para>
/// It was called <c>HomeShelf</c> while the home page was the only page with rows. The detail page has
/// two of exactly this shape, and its markup carried the same two literal heights — 228 and 246 — that
/// this type exists to derive.
/// </para>
/// <para>
/// Public because <c>x:Bind</c> in a DataTemplate compiles against the type by name; see
/// <see cref="CardItem"/>.
/// </para>
/// </summary>
public sealed partial class CardShelf : ObservableObject
{
    internal string HomeKey { get; init; } = "";
    private readonly EmbyImageStore _images;
    private readonly int _width;
    private readonly bool _wide;
    private readonly bool _indicators;
    private readonly bool _sourceLine;
    private Func<EmbyItem, string?>? _cardSubtitle;
    private Func<EmbyItem, string?>? _cardTitle;

    /// <param name="wide">
    /// True for a 16:9 row, which prefers the episode still over the poster — a 继续观看 card is about
    /// the scene you stopped at, not the cover art.
    /// </param>
    /// <param name="indicators">
    /// 这一排的卡片画不画角标（已看、收藏、未看集数），转给 <see cref="CardItem"/>。**默认画**：2026-10-16
    /// 之后它不再是一个用户开关，只有「这一排本来就没有状态可言」的几处调用点说不画 —— 主页的媒体库那一排、
    /// 演职人员。
    /// </param>
    /// <param name="libraryId">
    /// 这一排代表哪个媒体库，「最近添加 · 电影」那一排才有值（主页媒体库那几排各自是
    /// <c>HomeLayout.LibraryKey</c> 造出来的）。有了它，牌子右端那个大于号才知道该把
    /// 读的人送到哪儿去 —— 2026-09-13 用户原话「在最近添加右边添加一个大于号，点击标题后可以进入对应媒体库」。
    /// </param>
    /// <param name="target">
    /// 这一排点进哪儿，见 <see cref="HomeLayout.HomeRowTarget"/>。2026-09-14 之前「能不能点」全靠
    /// <paramref name="libraryId"/> 非空来表达，于是「继续观看」「接下来看」这两排 —— 它们不属于任何库 ——
    /// 牌子一直是死的；而用户那句「新增点击图中红框的标题可以进入对应的页面」圈的正是「继续观看」。
    /// 判据改成问版面要一个去处，库里那几排照旧，那两排也有了。
    /// </param>
    /// <param name="sourceLine">
    /// True to write the server name on a third line under every card's subtitle, and reserve that
    /// line's height in <see cref="RowHeight"/>. Only the home page's 继续观看 row sets it, and only
    /// while more than one account is online.
    /// </param>
    internal CardShelf(string title, EmbyImageStore images, int width, bool wide, bool indicators = true,
        string? libraryId = null, HomeLayout.HomeRowTarget target = HomeLayout.HomeRowTarget.None,
        bool sourceLine = false)
    {
        Title = title;
        _images = images;
        _width = width;
        _wide = wide;
        _indicators = indicators;
        _sourceLine = sourceLine;
        LibraryId = libraryId;
        Target = target;

        RowHeight = CardSize.HeightFor(width, wide) + CardSize.Chrome
            + (sourceLine ? CardSize.SourceLine : 0);

        // 读数跟着内容走。挂在集合的事件上而不是在 Clear/Fill/FillRows 里各喊一遍：那三个方法之外还有
        // 别人往 Cards 里加东西（详情页换季就是），漏喊一处的症状是牌子右端那个数字停在上一季。
        Cards.CollectionChanged += (_, _) => OnPropertyChanged(nameof(Note));
    }

    /// <summary>
    /// 这一排代表哪个媒体库，不是库排就是空。只读：一排认哪个库从建出来那一刻就定了，不是会改的东西。
    /// </summary>
    public string? LibraryId { get; }

    public EmbySessionScope? Source { get; init; }

    /// <summary>
    /// 这一排点进去是哪一页。<see cref="HomeLayout.HomeRowTarget.Library"/> 的走
    /// <see cref="LibraryId"/>，「继续观看」「接下来看」各有自己的那一页，
    /// <see cref="HomeLayout.HomeRowTarget.None"/>（详情页的每一排、演职人员、单集列表）点不动。
    /// </summary>
    public HomeLayout.HomeRowTarget Target { get; }

    /// <summary>这一排是一扇门 —— 牌子右端那个大于号露不露、点击面收不收，全看它。</summary>
    public bool CanOpen => Target != HomeLayout.HomeRowTarget.None;

    /// <summary>
    /// The heading above the row. Settable, and observable, because one row's heading is a fact about
    /// its contents rather than about the row: 详情页's 单集 heading reads 「更多来自：第一季」, and scrolling
    /// the strip into another season rewrites it without rebuilding the shelf. The home page's four
    /// headings are assigned once and never again, which this still allows.
    /// </summary>
    [ObservableProperty]
    public partial string Title { get; set; }

    /// <summary>
    /// 这一排是不是压在一张剧照上。主页第一排是（大图占满第一屏，这一排坐在它的下半截上），详情页的单集带在
    /// 集页上也是 —— 两处要的是同一件事：牌子和卡片底下那两行字换成压在图上那套不随主题走的浅墨，否则晴昼那套
    /// 浅色主题的近黑字会消失在暗罩里。开关本身在 <see cref="Views.ShelfHead.OnScrim"/>、
    /// <see cref="Views.ShelfStrip.OnScrim"/> 和 <see cref="Views.PosterCard.OnScrim"/>；这一支是让标记绑得到它。
    /// </summary>
    [ObservableProperty]
    public partial bool OnScrim { get; set; }

    public CardCollection Cards { get; } = new();

    /// <summary>
    /// 牌子右端的读数覆盖值。飞牛的库排要写库里的条目总数而不是货架张数（货架有上限），
    /// null 时退回 <see cref="Note"/> 原来的「这一排装了多少」。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Note))]
    public partial string? NoteOverride { get; set; }

    /// <summary>
    /// 牌子右端那个读数，「24 项」。空的时候是空字符串，<see cref="Views.ShelfHead"/> 会把那一格整个收起 ——
    /// 一带还没载入完的时候写个「0 项」，说的是「这里什么都没有」，而事实是「还不知道」。
    /// </summary>
    public string Note => NoteOverride ?? (Cards.Count > 0 ? $"{Cards.Count} 项" : string.Empty);

    /// <summary>
    /// How tall the row's scroller has to be, picture plus the two lines of text under it. A horizontal
    /// scroller inside a vertical stack has no other way to know: its content measures itself
    /// unconstrained in the scrolling direction, so the vertical parent would give it either nothing or
    /// everything. Computed from the card's own size rather than typed in as a literal, which is how the
    /// old markup's 228 and 314 came to be two numbers nobody could re-derive.
    /// </summary>
    public double RowHeight { get; }

    /// <summary>
    /// Empties the row, releasing what was in it. Its own method rather than <c>Fill([])</c>: an empty
    /// collection expression cannot pick between the two overloads below — there are no elements to pick
    /// by — and 「this row now has nothing in it」 is worth saying in one word anyway.
    /// </summary>
    internal void Clear()
    {
        // AbandonPoster 而不是 ReleasePoster：这一批卡整个作废，在路上那几趟的结果没有任何人在等
        // （两者的分工见 CardItem.ReleasePoster 上那段）。
        foreach (var card in Cards.Materialized) card.AbandonPoster();
        Cards.Clear();
        NoteOverride = null;
        _cardSubtitle = null;
        _cardTitle = null;
    }

    /// <summary>
    /// Replaces the row's contents, releasing what was there. Whatever the row held before is still
    /// holding decoded bitmaps until it is told otherwise, and a reload that skipped this would leak
    /// one poster per card per refresh.
    /// </summary>
    /// <param name="subtitle">Overrides every card's second line, from the item it belongs to.</param>
    /// <param name="title">
    /// Overrides every card's title, for example the home page's library source prefix and the
    /// series name on its resume cards. Retained with the subtitle for a next-episode replacement.
    /// </param>
    internal void Fill(IEnumerable<EmbyItem> items, Func<EmbyItem, string?>? subtitle = null,
        Func<EmbyItem, string?>? title = null)
    {
        Clear();
        _cardSubtitle = subtitle;
        _cardTitle = title;

        foreach (var item in items)
            Cards.Add(NewCard(item));
    }

    /// <summary>Publish the full episode index once; only layout access creates its card state.</summary>
    internal void FillDeferred(IReadOnlyList<EmbyItem> items, Func<EmbyItem, string?>? subtitle = null,
        EmbyItem? page = null)
    {
        foreach (var card in Cards.Materialized) card.AbandonPoster();
        NoteOverride = null;
        _cardSubtitle = subtitle;
        _cardTitle = null;
        Cards.ResetDeferred(items, item => new CardItem(item, _images, _width, _wide,
            subtitle?.Invoke(item), _indicators, page is not null && ItemDetail.IsCurrentEpisode(item, page),
            sourceLine: _sourceLine));
    }

    /// <summary>
    /// 暖缓存和后台刷新用的增量填充：身份和展示指纹都没变的卡留着，只增删、移动真正变了的那些。
    /// <para>
    /// 与 <see cref="Fill"/> 的区别只在「没变的东西不碰」：整排重来会让每张卡重新解码、重新做进场动画，
    /// 也会把用户正在看的那一排短暂清空。调用方仍要保证这一排的尺寸、角标和覆盖文案没有换档 ——
    /// 换档时页面会重建货架，走不到这里。
    /// </para>
    /// </summary>
    internal void Apply(IReadOnlyList<EmbyItem> items, Func<EmbyItem, string?>? subtitle = null,
        Func<EmbyItem, string?>? title = null)
    {
        _cardSubtitle = subtitle;
        _cardTitle = title;

        if (Cards.Count == items.Count)
        {
            var same = true;
            for (var index = 0; index < items.Count; index++)
            {
                if (ItemFingerprint.Same(Cards[index].Item, items[index])) continue;
                same = false;
                break;
            }
            if (same) return;
        }

        var existing = Cards.ToList();
        var used = new HashSet<CardItem>();
        var selected = new List<CardItem>(items.Count);
        foreach (var item in items)
        {
            CardItem? keep = null;
            foreach (var card in existing)
            {
                if (card.IsNavigation || used.Contains(card)) continue;
                if (!string.Equals(card.Item.IdentityKey, item.IdentityKey, StringComparison.Ordinal)) continue;
                if (!ItemFingerprint.Same(card.Item, item)) continue;
                keep = card;
                break;
            }

            if (keep is null) keep = NewCard(item);
            else used.Add(keep);
            selected.Add(keep);
        }

        // Local destinations are outside the server DTO list. Keep their containers and focus
        // during an incremental refresh; their owner removes them when availability changes.
        selected.AddRange(existing.Where(card => card.IsNavigation));

        foreach (var card in existing)
        {
            if (used.Contains(card) || selected.Contains(card)) continue;
            card.AbandonPoster();
        }

        for (var index = 0; index < selected.Count; index++)
        {
            var card = selected[index];
            var current = Cards.IndexOf(card);
            if (current < 0) Cards.Insert(index, card);
            else if (current != index) Cards.Move(current, index);
        }

        while (Cards.Count > selected.Count)
        {
            var last = Cards[Cards.Count - 1];
            Cards.RemoveAt(Cards.Count - 1);
            if (!selected.Contains(last)) last.AbandonPoster();
        }
    }

    /// <summary>
    /// The same, for a row whose second line does not come from the item at all.
    /// <para>
    /// 演职人员 is the one such row: the line under a portrait is the role, which belongs to the credit
    /// rather than to the person. Paired here rather than looked up by id, because the same actor can
    /// hold two credits on one title — a director who also appears, a voice actor with two parts — and a
    /// lookup keyed on the person would have to decide which of the two to throw away.
    /// </para>
    /// </summary>
    internal void Fill(IEnumerable<(EmbyItem Item, string? Subtitle)> items)
    {
        Clear();

        foreach (var (item, subtitle) in items)
            Cards.Add(new CardItem(item, _images, _width, _wide, subtitle, _indicators,
                sourceLine: _sourceLine));
    }

    /// <summary>
    /// 照这一排自己的尺寸、形状和角标造一张卡。给「继续观看那一格标记已看之后换成下一集」用
    /// （<see cref="HomeViewModel.MarkedWatched"/>）：卡片怎么造是这一排的私事（宽、16:9、角标），
    /// 换卡的那一头不该知道那三样、更不该各写一份。
    /// </summary>
    internal CardItem NewCard(EmbyItem item) =>
        new(item, _images, _width, _wide, _cardSubtitle?.Invoke(item), _indicators,
            title: _cardTitle?.Invoke(item), sourceLine: _sourceLine);

    /// <summary>
    /// The same, for a shelf drawn as 列表 rows rather than as cards — 详情页's 单集 list. Its own path
    /// because a row asks two things a card never does: there is no second line to override (a row reads
    /// its own <see cref="CardItem.RowTitle"/>, <see cref="CardItem.RowInfo"/> and
    /// <see cref="CardItem.RowOverview"/> off the item), and one of the rows may be the page the reader is
    /// already on.
    /// </summary>
    /// <param name="page">
    /// The item whose page this shelf is on, or null. Only used to mark the 「you are here」 row, so a season
    /// or movie page — and a season switched in the drop-down that does not contain the open episode —
    /// simply marks nothing.
    /// </param>
    internal void FillRows(IEnumerable<EmbyItem> items, EmbyItem? page)
    {
        Clear();

        foreach (var item in items)
            Cards.Add(new CardItem(
                item, _images, _width, _wide, subtitle: null, _indicators,
                ItemDetail.IsCurrentEpisode(item, page)));
    }
}
