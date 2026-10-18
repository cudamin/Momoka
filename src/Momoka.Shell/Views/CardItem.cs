using System.ComponentModel;
using System.Runtime.CompilerServices;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Shell.Media;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Momoka.Shell.Views;

/// <summary>
/// One poster card's worth of state: what to draw, and the artwork once it has arrived.
/// <para>
/// Public because <c>x:Bind</c> in a DataTemplate compiles against the type by name. A class rather
/// than a record: the poster arrives after construction, so the type has to raise change
/// notifications, and value equality on something holding a decoded bitmap is not a useful idea.
/// </para>
/// <para>
/// The artwork is loaded from here rather than from the control, and released again from here, because
/// the control is recycled and the item is not. That split is also what makes the release honest: a
/// discarded container drops its reference to the card, but the card is still in the page's list, so
/// nulling <see cref="Poster"/> is the only thing that actually frees the decoded surface.
/// </para>
/// </summary>
public sealed class CardItem : INotifyPropertyChanged
{
    private const string Category = "卡片";

    /// <summary>
    /// Segoe Fluent codepoints, spelled as numbers rather than as characters. The characters are in the
    /// Unicode private-use area: pasted into source they are invisible in a diff, and any tool that
    /// re-encodes the file can silently replace them with a replacement character.
    /// </summary>
    private const int MovieGlyph = 0xE8B2;
    private const int SeriesGlyph = 0xE7F4;
    private const int FolderGlyph = 0xE8B7;
    private const int PersonGlyph = 0xE77B;

    private readonly EmbyItem _item;
    private readonly EmbyImageStore _images;
    /// <summary>
    /// 画出来、解码、取图三处共用的那个宽度。**不是 readonly**：窗口被拖动时整面墙一起换宽度（见
    /// <see cref="Resize"/>），三处必须一起动，否则卡片要么糊要么白占内存。
    /// </summary>
    private int _width;

    /// <summary>True for a 16:9 card. Kept, not just consumed, because it also decides the height.</summary>
    private readonly bool _wide;

    /// <summary>
    /// 这一张卡自己的高宽比（高 ÷ 宽），null 就是按 <see cref="_wide"/> 走那两档（16:9 或 2:3）。
    /// <para>
    /// 索引页的「类型」是方卡（1:1，服务器拼的四格封面就是方图），而那两档里没有 1 这个值：喂 1 进
    /// <see cref="CardSize.HeightFor"/> 只会拿到 2:3 的 304，屏上是一张图上下各留一大条空白。所以宽高比由
    /// 造卡片的人给（见 <c>LibraryViewModel.NewCard</c> 与 <see cref="LibraryIndexCards.Aspect"/>）。
    /// </para>
    /// </summary>
    private readonly double? _aspect;

    /// <summary>
    /// 这一张卡画不画角标 —— 已看、收藏和未看集数。**不再有开关**：2026-10-16 按用户的话删掉了设置里那
    /// 一行（「删掉这个开关，改为默认开启」），角标从此一直画；说 false 的只剩几处「这一排本来就没有状态
    /// 可言」的地方（演职人员、主页的媒体库那一排、搜索建议那张小卡），见各自的调用点。
    /// <para>
    /// 它管不着悬浮层里那两颗按钮：按钮是那些状态**改**得动的地方，而一排没有状态可言的卡上那两颗按钮
    /// 由 <see cref="UserStateVisibility"/> 按条目类型收起来，跟这里是不是画角标是两件事。
    /// </para>
    /// </summary>
    private readonly bool _indicators;

    /// <summary>What to write on the second line instead of the item's own; null to use the item's.</summary>
    private readonly string? _subtitle;
    private readonly string? _title;
    private readonly ArtworkRef? _artwork;

    /// <summary>第三行：勾了宣传语或概要才有，null 就是这一行整行收起。</summary>
    private readonly string? _note;

    /// <summary>Whether artwork exists to ask for, or null when the item has none worth asking for.</summary>
    private readonly string? _imageType;

    private CancellationTokenSource? _loading;

    /// <summary>服务器明说没有这张图（404，或这个条目压根没有这一种图）。终局答案，不再问。</summary>
    private bool _missing;

    /// <summary>
    /// 上一次「没取到」（超时、连不上、5xx，重试也没救回来）落在几点，<see cref="DateTime.MinValue"/> 是从没
    /// 失败过。跟 <see cref="_missing"/> 分开记，因为两者的下一步不一样：「没有」再问一万次还是 404；「没取到」
    /// 隔一阵就值得再问（见 <see cref="ImageCachePolicy.WorthRetrying"/>）—— 从前两者混在一个 <c>_missing</c>
    /// 里，一次网络打嗝就把封面永久钉成了灰格子。
    /// </summary>
    private DateTime _failedAt;

    private BitmapImage? _poster;

    /// <summary>
    /// 屏上此刻要不要这张图。<see cref="EnsurePosterAsync"/> 把它抬起来、<see cref="ReleasePoster"/> 按下去，而
    /// 在路上那一趟回来时照它决定「塞回这张卡」还是「只留进 <see cref="PosterCache"/>」。
    /// <para>
    /// 存在的理由是回收：容器搬家那一下先卸下、后进树，而卸下不再取消请求（见 <see cref="ReleasePoster"/> 上那段
    /// 2026-09-05 的账），所以「这一趟还有没有人等着」得有个地方记着。
    /// </para>
    /// </summary>
    private bool _wanted;

    /// <summary>
    /// 此刻把这张卡摆在屏上的那个容器，没有就是没有。**弱引用**：卡片活在页面的清单里（<c>CardShelf.Cards</c>），
    /// 攥着一个已经回收掉的容器不放，一页卡片就会拖着几十个容器一起活着。
    /// <para>
    /// 存在的理由是回收的那一下会认错人：同一张卡会短暂地挂在两个容器上 —— 回收池里那个还攥着它，另一个容器已经
    /// 接手了 —— 而旧容器换手时也要喊一声「我放手了」。照单全收的话，屏上那张卡的图会被这一声清掉，之后再没有人
    /// 补回来：2026-09-13 那三张「标题正常、封面永远灰着」的卡就是这么来的。
    /// </para>
    /// </summary>
    private WeakReference<object>? _holder;

    /// <param name="width">
    /// The card's width in device-independent pixels. Used three times: to pick the size to ask the
    /// server for, as the decode width, and as the width the card is drawn at — which is the point of
    /// taking it here rather than in the DataTemplate, because those three can then not disagree.
    /// </param>
    /// <param name="wide">
    /// True for a 16:9 card (继续观看, 接下来看, 媒体库), which prefers the episode still over the poster.
    /// </param>
    /// <param name="subtitle">
    /// Overrides the second line. Two rows use it, both for the same reason — the item's own
    /// <c>CardSubtitle</c> would repeat the row's own name: 演职人员 (a person's subtitle is the word
    /// 演职人员, while the useful line is their role) and 主页 的 媒体库 那一排 (a library's subtitle is the
    /// word 媒体库, and there is nothing else to say, so that row passes an empty string).
    /// </param>
    /// <param name="indicators">False to draw no corner badges; see <see cref="_indicators"/>.</param>
    /// <param name="current">
    /// True for the one row that is the page you are already on; see <see cref="IsCurrentEpisode"/>.
    /// </param>
    /// <param name="imageTypes">
    /// 这一面墙要的那几种图，按先后排。媒体库七种视图各要各的（横幅图要 Banner、光盘封面要 Disc、
    /// 徽标要背景图），取不到就往后退一格；null 或空数组表示按卡片的宽窄自己挑。
    /// </param>
    /// <param name="tableFields">
    /// 表格视图的列。非空时卡片同时算出每一格的文字与宽度（<see cref="Cells"/>），行模板只负责摆出来。
    /// </param>
    public CardItem(
        EmbyItem item,
        EmbyImageStore images,
        int width,
        bool wide = false,
        string? subtitle = null,
        bool indicators = true,
        bool current = false,
        IReadOnlyList<string>? imageTypes = null,
        IReadOnlyList<LibraryField>? tableFields = null,
        string? note = null,
        string? title = null,
        ArtworkRef? artwork = null,
        bool upcoming = false,
        bool centeredText = false,
        bool containArtwork = false,
        bool folderFrame = false,
        double? aspect = null)
    {
        _item = item;
        _images = images;
        Scope = images.CaptureScope(item);
        if (item.Source is null) Scope?.Bind(item);
        _width = width;
        _wide = wide;
        _subtitle = subtitle;
        _title = title;
        _artwork = artwork;
        IsUpcoming = upcoming;
        CenteredText = upcoming || centeredText;
        FolderFrame = folderFrame;
        _aspect = aspect;
        PosterStretch = containArtwork ? Stretch.Uniform : Stretch.UniformToFill;
        _note = note;
        _indicators = indicators;
        IsCurrentEpisode = current;

        _imageType = imageTypes is { Count: > 0 } wanted
            ? wanted.FirstOrDefault(type => EmbyImageStore.TagFor(item, type) is not null)
            : wide ? PreferWide(item) : PreferTall(item);

        Cells = tableFields is null
            ? []
            : [.. tableFields.Select(field => new CardCell(
                LibraryCardText.Cell(item, field), LibraryTableColumns.WidthOf(field)))];
    }

    /// <summary>表格视图这一行的每一格；别的视图是空数组。</summary>
    public IReadOnlyList<CardCell> Cells { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The item behind the card. Whoever opens it needs the real thing, not a projection.</summary>
    public EmbyItem Item => _item;

    internal EmbySessionScope? Scope { get; }

    internal bool IsCurrent => Scope?.IsAvailable == true;

    public string Title => _title ?? _item.Name;
    public bool IsUpcoming { get; }
    public bool CenteredText { get; }
    public bool FolderFrame { get; }
    public Visibility FolderFrameVisibility => Show(FolderFrame);
    public Thickness FrameMargin => FolderFrame ? new Thickness(0, 7, 0, 0) : new Thickness(0);
    public Thickness ArtworkPadding => FolderFrame ? new Thickness(8, 18, 8, 10) : new Thickness(0);
    public string DurationText => FolderFrame && _item.RunTimeTicks > 0
        ? TimeFormat.Clock(_item.RunTimeTicks) : "";
    public Visibility DurationVisibility => Show(DurationText.Length > 0);
    public Visibility TitleVisibility => Show(Title.Length > 0);
    public Visibility SubtitleVisibility => Show(Subtitle.Length > 0);
    public Stretch PosterStretch { get; }
    public TextAlignment TitleAlignment => CenteredText ? TextAlignment.Center : TextAlignment.Left;
    public Thickness TextMargin => new(0, CenteredText ? 4 : 8, 0, 0);

    /// <summary>
    /// 列表视图 row's own title: 「1. 集名」. Kept separate from <see cref="Title"/> because the numbered
    /// form belongs to a list read down the left edge, while the card under a hero keeps the plain name.
    /// </summary>
    public string RowTitle => ItemDetail.EpisodeRowTitle(_item);

    /// <summary>
    /// The dim line under a row title (「2016/4/17  ·  46 分钟」), or null when the item gives it nothing to
    /// say. Null rather than empty because <see cref="RowInfoVisibility"/> has to hide the line without
    /// reserving its height. Read off the item, like <see cref="RowTitle"/>: the row is one shape of one
    /// item, and a fill that had to pass the line in could only ever pass this same string.
    /// </summary>
    public string? RowInfo => ItemDetail.EpisodeRowInfo(_item) is { Length: > 0 } info ? info : null;

    public Visibility RowInfoVisibility => Show(RowInfo is not null);

    /// <summary>
    /// The row's synopsis, clamped to two lines by the row itself rather than shortened here. Null when
    /// the server sent none: an empty string would still claim the second line's height, and a row that
    /// has nothing to say is allowed to be shorter.
    /// </summary>
    public string? RowOverview => string.IsNullOrWhiteSpace(_item.Overview) ? null : _item.Overview.Trim();

    public Visibility RowOverviewVisibility => Show(RowOverview is not null);

    /// <summary>
    /// 「You are here」: this row is the episode whose page is open. An episode page lists its own siblings,
    /// so the list it appears in contains the page itself, and without a mark the reader has to compare
    /// titles with the one at the top of the screen to work out where they are.
    /// <para>
    /// Fixed at construction rather than settable: a fill is where the answer is known, and a card whose
    /// 「this is you」 could change after it was drawn would need a notification path for something that
    /// never happens — a different episode is a different page and a different fill.
    /// </para>
    /// </summary>
    public bool IsCurrentEpisode { get; }

    /// <summary>The accent bar down the row's left edge, which is how <see cref="IsCurrentEpisode"/> reads.</summary>
    public Visibility CurrentVisibility => Show(IsCurrentEpisode);

    public string Subtitle => _subtitle ?? _item.CardSubtitle;

    /// <summary>第三行（宣传语或概要）。媒体库设置里勾了才有；别的页面一律空。</summary>
    public string Note => _note ?? "";

    public Visibility NoteVisibility => Show(Note.Length > 0);

    /// <summary>Drawn behind the poster, so an item with no artwork still looks deliberate.</summary>
    public string Glyph => char.ConvertFromUtf32(_item.Type switch
    {
        EmbyItemType.Series or EmbyItemType.Season or EmbyItemType.Episode => SeriesGlyph,
        EmbyItemType.BoxSet or EmbyItemType.Folder or EmbyItemType.CollectionFolder
            or EmbyItemType.Playlist => FolderGlyph,
        EmbyItemType.Person => PersonGlyph,
        _ => MovieGlyph
    });

    /// <summary>
    /// The size the card is drawn at, bound by the DataTemplate. Here rather than in the markup so that
    /// the drawn width is provably the width the bitmap was decoded for; see <see cref="CardSize"/>.
    /// </summary>
    public double CardWidth => _width;

    /// <summary>
    /// 这一张卡换个宽度重画（窗口被拖动时，一面墙上的卡片一起换）。
    /// <para>
    /// **宽度不只是画多大**：它是取图要的尺寸、解码宽度和画出来的宽度三件事，所以换宽度就得把图重新取一遍、
    /// 重新解一遍 —— 旧宽度解出来的图放到新格子里就是一张糊图。
    /// </para>
    /// <para>
    /// 原地改而不是把卡片换掉：卡片对象活在页面的清单里（<c>LibraryViewModel.Cards</c>），删除、继续观看那一格、
    /// 探针都攥着这些对象；换掉对象等于把那些引用全作废（2026-10-18 探针「删除保持列表位置」就是这么红起来的）。
    /// </para>
    /// </summary>
    internal void Resize(int width)
    {
        if (width <= 0 || width == _width) return;

        _width = width;
        Raise(nameof(CardWidth));
        Raise(nameof(PosterHeight));

        // 在路上的那一趟是按旧宽度取的：撤掉，按新宽度重来。
        _loading?.Cancel();
        _loading = null;

        if (_poster is null) return;

        Poster = null;

        // 只有屏上这一张要立刻补回来；不在屏上的，等容器再把它递进来时按新宽度取（见 EnsurePosterAsync）。
        if (_wanted) _ = EnsurePosterAsync();
    }

    /// <summary>画出来的图有多高。索引卡自带宽高比时按它算（见 <see cref="_aspect"/>），否则按两档形状。</summary>
    public double PosterHeight => _aspect is { } aspect ? Math.Round(_width * aspect) : CardSize.HeightFor(_width, _wide);

    public BitmapImage? Poster
    {
        get => _poster;
        private set
        {
            if (ReferenceEquals(_poster, value)) return;
            _poster = value;
            Raise();
        }
    }

    public bool Watched => _item.IsWatched;

    public bool IsFavorite => _item.UserData?.IsFavorite ?? false;

    /// <summary>Percent watched, for the bar across the bottom of the poster. Zero hides it.</summary>
    public double Progress => _item.HasResumePosition ? _item.ProgressFraction * 100 : 0;

    /// <summary>
    /// 「看到一半」那条线的两段。压在胶片格下边框上的进度不是一个控件，是两列宽度 —— 一个 Grid 分成
    /// <c>已看 : 还没看</c> 两份星号列，强调色那块 Border 占第一列，于是「长度就是进度」是布局算出来的，
    /// 而不是谁把百分比换算成像素。
    /// <para>
    /// 这么写还有一个用处：海报 170、剧照 300、单集行的图又是另一个宽度，三种宽度共用这一对属性，没有
    /// 哪一处需要知道自己多宽。
    /// </para>
    /// </summary>
    public GridLength ProgressDone => new(Progress, GridUnitType.Star);

    /// <inheritdoc cref="ProgressDone"/>
    public GridLength ProgressLeft => new(100 - Progress, GridUnitType.Star);

    /// <summary>How many episodes of a series are still unwatched, blank when none or not a series.</summary>
    public string UnplayedText =>
        _item.UserData?.UnplayedItemCount is { } count and > 0 ? count.ToString() : string.Empty;

    // Visibility rather than bool plus a converter: these are view-model properties on a view-model
    // that only a view uses, and a converter per flag would be four more files saying less.
    //
    // 角标和按钮各有一对，名字里带 Badge 的那对才受 _indicators 管：角标是这一排画不画状态，悬浮按钮上的
    // 图标不是 —— 一排不画角标的卡上按钮照旧要有图标，两个图标要是都被 _indicators 关掉，那颗按钮就成了
    // 一块什么都不画的空白。
    public Visibility WatchedBadgeVisibility => Show(_indicators && Watched);

    public Visibility FavoriteBadgeVisibility => Show(_indicators && IsFavorite);

    /// <summary>
    /// The two halves of each of the hover buttons' icon pairs: the white glyph shows when the state is
    /// off and the coloured one when it is on — same shape either way, so the button does not appear to
    /// change into a different button. Negated here rather than by a converter for the same reason as
    /// above.
    /// </summary>
    public Visibility WatchedVisibility => Show(Watched);

    /// <inheritdoc cref="WatchedVisibility"/>
    public Visibility NotWatchedVisibility => Show(!Watched);

    /// <inheritdoc cref="WatchedVisibility"/>
    public Visibility FavoriteVisibility => Show(IsFavorite);

    /// <inheritdoc cref="WatchedVisibility"/>
    public Visibility NotFavoriteVisibility => Show(!IsFavorite);

    /// <summary>What the hover strip's buttons promise to do, which is the opposite of the state shown.</summary>
    public string WatchedActionTip => Watched ? "标记为未观看" : "标记为已观看";

    public string FavoriteActionTip => IsFavorite ? "取消收藏" : "添加到收藏";

    /// <summary>
    /// The play button in the middle of the artwork. A movie or an episode streams directly; a series
    /// and a season are a stack of files, and the badge on their posters means 「开始看这部」 — the
    /// player resolves them down to the next unwatched episode (StartPlaybackAsync's Series/Season
    /// branch, where the same click from the home shelves, the library, search and the menu lands), so
    /// 「给电视媒体库的封面也加上播放按钮」（2026-09-12） is this line. A folder and a person still open
    /// instead: a button offering to play one of them would be offering nothing.
    /// </summary>
    public Visibility PlayVisibility =>
        Show((!IsUpcoming || !string.Equals(_item.LocationType, "Virtual", StringComparison.OrdinalIgnoreCase))
            && (_item.IsPlayable || _item.Type is EmbyItemType.Series or EmbyItemType.Season));

    /// <summary>
    /// 已观看 and 收藏 on the hover strip, which the 媒体库 row on the home page and the 演职人员 shelf
    /// on a detail page do without: neither a library nor a name has a watched state, so those two
    /// buttons there promised something the server would refuse (「主页的媒体库不要收藏和已观看」).
    /// <para>
    /// The item's type decides it, not the caller: the same library card can appear on another page, and
    /// which buttons make sense is a fact about the item rather than about where it is drawn. Separate
    /// from <see cref="_indicators"/> for the reason given there — that flag is about how the row is drawn
    /// and this one is whether the action exists at all.
    /// </para>
    /// </summary>
    public Visibility UserStateVisibility => Show(_item.TracksUserState);

    public string PlayActionTip =>
        _item.HasResumePosition ? $"继续播放（{TimeFormat.Clock(_item.ResumeTicks)}）" : "立即播放";

    public Visibility ProgressVisibility => Show(Progress > 0);

    /// <summary>The unplayed count is suppressed once everything is watched, so the two never stack.</summary>
    public Visibility UnplayedVisibility => Show(_indicators && !Watched && UnplayedText.Length > 0);

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Starts the artwork load if it has not been done. Idempotent, and safe to call again every time
    /// a recycled container is handed this card.
    /// <para>
    /// 第一句是「屏上要这张图」（<see cref="_wanted"/>）：容器回收时 <see cref="ReleasePoster"/> 把它按下去，而
    /// 那一趟请求照旧在路上 —— 容器又回来的时候由这一句把它抬起来，那一趟的结果于是照旧塞得回来。
    /// </para>
    /// </summary>
    public async Task EnsurePosterAsync()
    {
        _wanted = true;

        if (_imageType is null && _artwork is null || _missing || _poster is not null || _loading is not null) return;
        if (Scope is not { IsAvailable: true } scope) return;

        // 上一次「没取到」还没隔够冷却时间就不问了：滚动一路会把同一张卡反复递进来，不拦的话一次断网就是每一趟
        // 滚动都朝服务器排一遍车轮战。隔够了才放行 —— 服务器多半已经喘过气来了。
        if (_failedAt != default && !ImageCachePolicy.WorthRetrying(_failedAt, DateTime.UtcNow)) return;

        // Already decoded once this run: hand it straight over. Synchronous on purpose — a container
        // coming back to a poster it has shown before should not flicker through an empty frame first.
        if (PosterKey() is { } key && PosterCache.TryGet(key, out var kept))
        {
            Poster = kept;
            return;
        }

        var cts = new CancellationTokenSource();
        _loading = cts;

        try
        {
            if (PosterArtwork is not { } picture) return;
            var cacheKey = PosterCache.Key(scope.Connection, picture.ItemId, picture.ImageType, picture.Tag, _width);
            var bytes = await _images.GetAsync(scope, picture.ItemId, picture.ImageType, picture.Tag, EmbyImageStore.RequestWidth(_width), cts.Token)
                .ConfigureAwait(true);

            if (cts.IsCancellationRequested || !scope.IsAvailable) return;

            if (bytes is null || bytes.Length == 0)
            {
                // 走到这里的一定是服务器明说的 404（取不到的那几档在 EmbyImageStore 里已经先重试过、救不回来才
                // 抛出来），所以放心记死：滚动路过一个真没有图的条目时不必每一次都去问服务器。
                _missing = true;

                // 留一行。这一档和「加载失败」在屏上是同一种灰，而日志里从前一个字都没有 —— 「服务器没这张图」
                // 和「有图没取到」分不开，就只能靠猜。
                Log.Debug(Category, $"服务器没有这张图（{_item.Name}，{_imageType}）");
                return;
            }

            var bitmap = await PosterLoader.DecodeAsync(bytes, _width).ConfigureAwait(true);
            if (cts.IsCancellationRequested || !scope.IsAvailable) return;

            if (bitmap is not null) PosterCache.Remember(cacheKey, bitmap);

            // 屏上还要不要它（见 ReleasePoster）：不要了就只留进上面那份缓存，不塞回这张卡 —— 塞回去就是一张
            // 不在屏上的卡攥着一张解出来的画面，而那正是「放开」要防的事。
            if (_wanted) Poster = bitmap;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            // 「这次没取到」不是「服务器没有」：不记死，只记下时间。等冷却过了（见开头的 WorthRetrying）、容器
            // 再把这张卡递进来的时候重新问。在屏上纹丝不动的卡不会自己触发重问，但 EmbyImageStore 那头已经先
            // 重试过两轮，短命的网络打嗝根本走不到这里；走得到的是比较长的断连，那种本来就要等下一次滚动或
            // 换页才谈得上恢复。
            _failedAt = DateTime.UtcNow;
            Log.Debug(Category, $"加载图片失败（{_item.Name}）：{error.Message}");
        }
        finally
        {
            // Null first, then dispose: there is no await between the two, so nothing can observe a
            // disposed source through the field.
            if (ReferenceEquals(_loading, cts)) _loading = null;
            cts.Dispose();
        }
    }

    /// <summary>
    /// 一个容器接手这张卡（<c>PosterCard.Card</c> 被赋值的那一刻）。见 <see cref="_holder"/>：回收的那一下会有两个
    /// 容器同时说自己拿着它，认最后一个接手的。
    /// </summary>
    public void ClaimCard(object container) => _holder = new WeakReference<object>(container);

    /// <summary>
    /// 放开手上那张解出来的画面。容器离树时走这里：不放开的话，一次长滚动结束时手上攥着一路经过的每一张海报。
    /// <para>
    /// **不取消在路上那一趟，也默认继续等着它**（2026-09-05 修的那四张灰占位卡）。两件事各有原因：
    /// </para>
    /// <para>
    /// ①不取消 —— 取消掉的那一趟没有人会再发起：重新发起只有两个入口（容器进树、容器换卡），而回收进来的这一张
    /// 两个都不会再走一遍。触发它的是 WinUI 的回收池：<c>CardTemplate</c> 这一份 DataTemplate 同时挂在主页每一条
    /// 横带的 <c>ItemsRepeater</c> 上，容器于是在几排之间搬家（日志里是「继续观看」的容器变成了「最近添加 · 电视
    /// 节目」那一排的卡）。
    /// </para>
    /// <para>
    /// ②继续等着 —— **<c>Unloaded</c> 会对一个仍然在屏上的元素喊一声**。那四张卡的实测时序是：容器换到这张卡
    /// （45 毫秒后）进树、发出请求，59 毫秒后一声 <c>Unloaded</c>，而**此后再没有 <c>Loaded</c>**，卡却好好地画在
    /// 屏上。所以「卸下了」不等于「不要了」，照它把结果扔掉就是永久的灰占位图。真正权威的那一句是
    /// <paramref name="keepWaiting"/>=false：容器改去装另一张卡了，那张卡确确实实失去了容器。
    /// </para>
    /// <para>
    /// **③ 但「这个容器放手」也不等于「它失去了容器」** —— 还要问一句：放手的是不是此刻拿着它的那一个。同一张卡
    /// 会短暂地挂在两个容器上（回收池里那个仍攥着它，另一个已经接手），旧容器换手时也会来喊这一声；照单全收就把
    /// 屏上那张卡的图清掉了，而在此之后没有任何人会补回来 —— 2026-09-13 实测的那三张「标题正常、封面永远灰着」
    /// 的卡（最近添加·电视节目 那一排的第 3、4、5 张）就是这一声的产物，日志里它们是：容器 B 拿到这张卡 → 容器 A
    /// 把它换走、顺手放掉了这张共享的卡 → 屏上那张卡当场变空。
    /// </para>
    /// <para>
    /// 在路上那一趟照旧跑完、结果照旧进 <see cref="PosterCache"/>（那份缓存自己有上限）；不等了的那一档就只进
    /// 缓存、不塞回这张卡（见 <see cref="EnsurePosterAsync"/> 末尾那句）。
    /// </para>
    /// </summary>
    /// <param name="container">要放手的那个容器 —— 只有它正是持有者时这一声才算数。</param>
    /// <param name="keepWaiting">
    /// 这张卡还是那个容器在装吗。默认是（只是暂时离树）；容器改去装别的卡时传 false —— 那时它的结果没人在等，
    /// 而一张不在屏上的卡攥着一张解出来的画面正是这个方法要防的事。
    /// </param>
    public void ReleasePoster(object container, bool keepWaiting = true)
    {
        // 不是持有者就没有资格放手：它手上那张卡只是回收池留下来的旧引用，屏上这张还有人看着。
        if (!HeldBy(container)) return;

        _holder = null;
        if (!keepWaiting) _wanted = false;

        Poster = null;
    }

    /// <summary>这个容器是不是此刻拿着这张卡的那一个。</summary>
    private bool HeldBy(object container) =>
        _holder is { } weak && weak.TryGetTarget(out var target) && ReferenceEquals(target, container);

    /// <summary>
    /// 这张卡不要了：连在路上那一趟一起取消。<see cref="CardShelf.Clear"/> 和媒体库换整页时走这里 —— 那时这些
    /// <see cref="CardItem"/> 整批作废，没有任何容器会再要它们的图。
    /// </summary>
    public void AbandonPoster()
    {
        _holder = null;
        _wanted = false;
        _loading?.Cancel();
        _loading = null;
        Poster = null;
    }

    /// <summary>
    /// The name this card's decoded picture goes under, or null when the item has no artwork of the kind
    /// this card wants. Built each time rather than kept: the tag in it is the server's, and artwork
    /// replaced there has to read as a different picture.
    /// </summary>
    private ArtworkRef? PosterArtwork => _artwork ?? (_imageType is not null
        && EmbyImageStore.TagFor(_item, _imageType) is { } tag ? new(_item.Id, _imageType, tag) : null);

    private string? PosterKey() =>
        Scope is { } scope && PosterArtwork is { } picture
            ? PosterCache.Key(scope.Connection, picture.ItemId, picture.ImageType, picture.Tag, _width)
            : null;

    /// <summary>Re-reads the badges after the item's user data changes. Requirement 6 calls this.</summary>
    public void RefreshUserData()
    {
        Raise(nameof(Watched));
        Raise(nameof(IsFavorite));
        Raise(nameof(Progress));
        Raise(nameof(ProgressDone));
        Raise(nameof(ProgressLeft));
        Raise(nameof(UnplayedText));
        Raise(nameof(WatchedBadgeVisibility));
        Raise(nameof(FavoriteBadgeVisibility));
        Raise(nameof(WatchedVisibility));
        Raise(nameof(FavoriteVisibility));
        Raise(nameof(NotWatchedVisibility));
        Raise(nameof(NotFavoriteVisibility));
        Raise(nameof(WatchedActionTip));
        Raise(nameof(FavoriteActionTip));
        Raise(nameof(PlayActionTip));
        Raise(nameof(ProgressVisibility));
        Raise(nameof(UnplayedVisibility));
    }

    /// <summary>
    /// Tall cards want the poster and will settle for a still; wide cards want the still and will
    /// settle for a backdrop, then the poster. Null means the item has no artwork at all, and the
    /// glyph behind the picture is what the card shows for good.
    /// </summary>
    private static string? PreferTall(EmbyItem item) =>
        item.PrimaryImageTag is not null ? EmbyImageStore.Primary
        : item.ThumbImageTag is not null ? EmbyImageStore.Thumb
        : null;

    private static string? PreferWide(EmbyItem item) =>
        item.ThumbImageTag is not null ? EmbyImageStore.Thumb
        : item.BackdropImageTags.Count > 0 ? EmbyImageStore.Backdrop
        : item.PrimaryImageTag is not null ? EmbyImageStore.Primary
        : null;

    private void Raise([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property ?? string.Empty));
}

/// <summary>
/// 表格视图的一格：这一项字段的文字，以及这一列多宽。宽度与列头同源（<see cref="LibraryTableColumns"/>），
/// 所以行和列头不会各算一套。
/// </summary>
public sealed class CardCell(string text, double width)
{
    public string Text { get; } = text;

    public double Width { get; } = width;
}
