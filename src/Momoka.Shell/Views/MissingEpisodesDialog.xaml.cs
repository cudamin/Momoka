using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Shell.Media;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Momoka.Shell.Views;

/// <summary>
/// 查看遗漏集那张表：服务器按刮削源算出来的「这部剧本该有、而库里没有」的单集，一集一行 —— 左边一张剧照，
/// 右边「S1:E1 - 集名」、播出日期和两行简介。
/// <para>
/// 遗漏的单集没有文件，行上的搜索入口只选择来源；关闭清单后再打开资源窗口。
/// 清单由 <see cref="ItemCommands"/> 在弹表<b>之前</b>问好（<c>EmbyClient.GetMissingEpisodesAsync</c>），
/// 这里只为一件事再走网络：每一行那张剧照（<see cref="Func{T1,T2,TResult}"/> 那个取图口子）。清单本身不再问，
/// 所以这张表在自检里能离线造出来（见 <c>ShellSelfCheck.ReadDialogs</c>）。
/// </para>
/// <para>
/// 上面两颗开关（特别篇／预告）在本地筛（<see cref="ItemDetail.MissingShown"/>）：实测服务器那条接口不认这两个
/// 筛选，而两个条件客户端手上都答得出，所以拨一下立刻出结果、不必再问一趟。
/// </para>
/// <para>
/// 抬头那一句由 Core 给（<see cref="ItemDetail.MissingHeading"/>）：剧和季两种说法各只有一处，而「一集都没缺」
/// 和「筛出来是空的」在屏上是同一张空表、说的是两件事。
/// </para>
/// </summary>
public sealed partial class MissingEpisodesDialog : ContentDialog
{
    private const string Category = "ui";

    private readonly EmbyItem _owner;
    private readonly IReadOnlyList<EmbyItem> _all;
    private readonly Func<ArtworkRef, CancellationToken, Task<byte[]?>> _fetch;
    private readonly bool _canSearch;
    private readonly bool _anime;
    private readonly ObservableCollection<MissingEpisodeRow> _rows = [];

    internal EmbyItem? SearchEpisode { get; private set; }
    internal bool SearchWithAniBt { get; private set; }

    /// <summary>解出来的剧照，按「谁的哪一种哪一版」存。拨开关会整张表重建，不存的话每拨一次都重解一遍。</summary>
    private readonly Dictionary<ArtworkRef, BitmapImage> _decoded = [];

    /// <summary>构造还没走完时那颗开关已经在响了（<c>Toggled</c> 在 <c>IsOn</c> 赋值时就发），见 <see cref="OnToggled"/>。</summary>
    private bool _ready;

    /// <param name="item">这部剧或者这一季本身 —— 抬头要说出对谁做，每行的剧照也要问它要。</param>
    /// <param name="missing">服务器答回来的遗漏集；季那一档由调用方先按季号筛过（见
    /// <see cref="ItemDetail.MissingInSeason"/>）。</param>
    /// <param name="fetch">取一张图的口子。由外壳交进来 —— <c>EmbyImageStore</c> 在容器里，这一层够不到它。</param>
    public MissingEpisodesDialog(
        EmbyItem item,
        IReadOnlyList<EmbyItem> missing,
        Func<ArtworkRef, CancellationToken, Task<byte[]?>> fetch,
        bool canSearch = false,
        bool anime = false)
    {
        InitializeComponent();

        // 对话框摆在 XamlRoot 的浮层根上，从提出它的那棵树上继承不到主题 —— 不点明的话，它会按 Windows 的
        // 应用模式来（见 CollectionDialog 里那段说明）。
        RequestedTheme = ThemeHost.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light;

        _owner = item;
        _all = missing;
        _fetch = fetch;
        _canSearch = canSearch;
        _anime = anime;
        List.ItemsSource = _rows;

        // 特别篇那一季自己的页面上，这颗开关默认是开的 —— 那张表列的本来就是第 0 季，关着等于一张空表。
        // 其余页面上默认关（正片优先），和官方网页端打开时那个样子一致。
        SpecialsSwitch.IsOn = item.Type == EmbyItemType.Season && item.IndexNumber == 0;

        _ready = true;
        Apply();
    }

    /// <summary>两颗开关中的任一颗动了，这张表跟着重列一遍。</summary>
    private void OnToggled(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        Apply();
    }

    /// <summary>
    /// 按两颗开关重列这张表。开关是本地筛，所以这是一件同步的事；只有剧照要等网络，那一趟在
    /// <see cref="LoadStillsAsync"/> 里，回来一张补一张。
    /// </summary>
    private void Apply()
    {
        var shown = ItemDetail.MissingShown(_all, SpecialsSwitch.IsOn, UnairedSwitch.IsOn, DateTimeOffset.Now);
        var rows = shown.Select(episode => new MissingEpisodeRow(episode, _owner, _canSearch, _anime)).ToList();

        Summary.Text = ItemDetail.MissingHeading(_owner, rows.Count);
        _rows.Clear();
        foreach (var row in rows) _rows.Add(row);
        List.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        Paint(rows);
        _ = LoadStillsAsync(rows);
    }

    private void OnSearchMoviePilot(object sender, RoutedEventArgs args) => ChooseSearch(sender, false);
    private void OnSearchAniBt(object sender, RoutedEventArgs args) => ChooseSearch(sender, true);

    private void ChooseSearch(object sender, bool anime)
    {
        if (!_canSearch || sender is not FrameworkElement { Tag: EmbyItem episode }
            || (anime && !_anime && !Momoka.AniBt.AniBtEligibility.HasGenre(episode))) return;
        SearchEpisode = episode;
        SearchWithAniBt = anime;
        Hide();
    }

    /// <summary>
    /// 取每一行那张剧照。**按图去重**：遗漏的单集自己一张图都没有（实测 <c>ImageTags</c> 是空的），一行一张
    /// 借的都是同一个剧的缩略图（见 <see cref="ItemDetail.MissingStill"/>），所以这一圈通常只下一张。
    /// </summary>
    private async Task LoadStillsAsync(IReadOnlyList<MissingEpisodeRow> rows)
    {
        foreach (var art in rows.Select(row => row.Artwork).OfType<ArtworkRef>().Distinct())
        {
            if (_decoded.ContainsKey(art)) continue;

            try
            {
                var bytes = await _fetch(art, CancellationToken.None).ConfigureAwait(true);
                if (bytes is null || bytes.Length == 0) continue;

                var bitmap = await PosterLoader.DecodeAsync(bytes, CardSize.MissingWidth).ConfigureAwait(true);
                if (bitmap is null) continue;

                _decoded[art] = bitmap;
                Paint(rows);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // 一张图取不到不是这张表的失败：那一格留着占位字形，清单本身照旧读得下去。
                Log.Debug(Category, $"遗漏集的剧照没取到（{art.ItemId}/{art.ImageType}）：{error.Message}");
            }
        }
    }

    /// <summary>把已经解出来的剧照铺到这些行上。每次开关一动都重走一遍 —— 缓存在 <see cref="_decoded"/> 里。</summary>
    private void Paint(IReadOnlyList<MissingEpisodeRow> rows)
    {
        foreach (var row in rows)
            if (row.Artwork is { } art && _decoded.TryGetValue(art, out var bitmap))
                row.Poster = bitmap;
    }
}

/// <summary>
/// 遗漏集那一行：左边一张剧照，右边「S1:E1 - 集名」、播出日期和简介。
/// <para>
/// 单独一格而不是直接绑 <see cref="EmbyItem"/>：这几样都不是条目自己的属性，而是 Core 里那几条取名规则
/// （<see cref="ItemDetail.EpisodeLabel"/>、<see cref="ItemDetail.EpisodeRowInfo"/>、
/// <see cref="ItemDetail.MissingStill"/>）的产物，先在这里算好，标记里就只剩几个字符串加一张图。
/// </para>
/// </summary>
public sealed class MissingEpisodeRow : INotifyPropertyChanged
{
    private BitmapImage? _poster;

    public MissingEpisodeRow(EmbyItem episode, EmbyItem owner, bool canSearch = false, bool anime = false)
    {
        Episode = episode;
        anime |= Momoka.AniBt.AniBtEligibility.HasGenre(episode);
        SearchVisibility = canSearch && !anime ? Visibility.Visible : Visibility.Collapsed;
        AnimeSearchVisibility = canSearch && anime ? Visibility.Visible : Visibility.Collapsed;
        Label = ItemDetail.EpisodeLabel(episode);
        Info = ItemDetail.EpisodeRowInfo(episode);
        Overview = episode.Overview?.Trim() ?? "";
        Artwork = ItemDetail.MissingStill(episode, owner);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public EmbyItem Episode { get; }
    public Visibility SearchVisibility { get; }
    public Visibility AnimeSearchVisibility { get; }
    public string SearchName => $"搜索分集资源：{Label}";

    /// <summary>「S1:E1 - 集名」，与集页面上那一行同一个写法。</summary>
    public string Label { get; }

    /// <summary>「2016/4/17」，服务器没给播出日期就是空串（那一行不画）。</summary>
    public string Info { get; }

    /// <summary>简介，服务器没给就是空串（那两行不画）。</summary>
    public string Overview { get; }

    /// <summary>这张剧照取谁的哪一种；null 就是这一行没有图可借。</summary>
    public ArtworkRef? Artwork { get; }

    public Visibility InfoVisibility => Info.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility OverviewVisibility => Overview.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>画出来的宽 —— 也就是解码用的那个宽（<see cref="CardSize.MissingWidth"/>）。</summary>
    public double CardWidth => CardSize.MissingWidth;

    public double PosterHeight => CardSize.HeightFor(CardSize.MissingWidth, wide: true);

    /// <summary>取到并解好的那张剧照；还没到手就是 null，那一格画占位字形。</summary>
    public BitmapImage? Poster
    {
        get => _poster;
        set
        {
            if (ReferenceEquals(_poster, value)) return;
            _poster = value;
            Raise();
        }
    }

    private void Raise([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property ?? string.Empty));
}
