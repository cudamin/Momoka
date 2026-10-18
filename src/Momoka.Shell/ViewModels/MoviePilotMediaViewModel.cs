using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.AniBt;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.Media;

namespace Momoka.Shell.ViewModels;

public sealed partial class MoviePilotMediaEpisodeRow(MoviePilotMediaEpisode episode, bool? exists, Action search) : ObservableObject
{
    public MoviePilotMediaEpisode Episode { get; } = episode;
    public string Title => Episode.Title;
    public string Overview => Episode.Overview;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Meta))]
    public partial bool? Exists { get; set; } = exists;
    public string Meta => string.Join(" · ", new[] { Episode.AirDate, Episode.Vote is > 0 ? $"★ {Episode.Vote:0.0}" : "", Exists switch { true => "已入库", false => "缺失", _ => "入库状态未知" } }.Where(value => value.Length > 0));
    [ObservableProperty] public partial BitmapImage? Artwork { get; set; }
    public IRelayCommand SearchCommand { get; } = new RelayCommand(search);
}

public sealed partial class MoviePilotMediaSeasonRow(MoviePilotMediaSeason season, MoviePilotSubscription? subscription,
    Action<MoviePilotMediaSeasonRow> expand, Action<MoviePilotMediaSeasonRow> subscribe) : ObservableObject
{
    public MoviePilotMediaSeason Season { get; } = season;
    public MoviePilotSubscription? Subscription { get; } = subscription;
    public bool IsMovie { get; init; }
    public string Title => IsMovie ? Season.Name : Season.Label;
    public string Summary => (IsMovie ? "电影" : $"{Season.EpisodeCount} 集") + (Season.AirDate.Length > 0 ? $" · 首播于 {Season.AirDate}" : "") + (Season.Vote is > 0 ? $" · ★ {Season.Vote:0.0}" : "");
    public string SubscribeLabel => Subscription is null ? "订阅" : "已订阅 · " + MoviePilotSubscriptionPlan.ModeLabel(MoviePilotSubscriptionPlan.Mode(Subscription));
    public string HeartGlyph => Subscription is null ? "\uEB51" : "\uEB52";
    public ObservableCollection<MoviePilotMediaEpisodeRow> Episodes { get; } = [];
    [ObservableProperty] public partial BitmapImage? Poster { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderStatus))]
    public partial string Status { get; set; } = "入库状态未知";

    /// <summary>
    /// 订阅季的折叠栏直接装这条订阅的文件统计（用户令 2026-10-12）。订阅详情的视图模型由
    /// <see cref="MoviePilotMediaSeasonsView"/> 挂到匹配季号的这一行上：它既是「这一季装文件统计」的标志
    /// （媒体侧据此跳过通用集列表），也带来下面几项显示快照。不走嵌套 x:Bind（bind 基线只许减），
    /// 数值由视图推进来，<see cref="FilesEpisodes"/> 与订阅视图模型共享同一个集合实例，筛选原地生效。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilesHostVisibility))]
    [NotifyPropertyChangedFor(nameof(MediaEpisodesVisibility))]
    [NotifyPropertyChangedFor(nameof(MediaEpisodeNoticeVisibility))]
    [NotifyPropertyChangedFor(nameof(HeaderStatus))]
    public partial MoviePilotSubscriptionsViewModel? Files { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderStatus))]
    public partial string FilesSummary { get; set; } = "";
    [ObservableProperty] public partial string FilesPlaybackStatus { get; set; } = "";
    [ObservableProperty] public partial int FilesFilterIndex { get; set; }
    [ObservableProperty] public partial ObservableCollection<MoviePilotSubscriptionEpisodeRow>? FilesEpisodes { get; set; }
    public Visibility FilesHostVisibility => Files is not null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MediaEpisodesVisibility => Files is null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MediaEpisodeNoticeVisibility => Files is null && EpisodeNotice.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    // 订阅按自己的目标范围统计；只在季标题显示一次，加载和失败提示也沿用同一来源。
    public string HeaderStatus => Files is null ? Status : FilesSummary;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EpisodeNoticeVisibility))]
    [NotifyPropertyChangedFor(nameof(MediaEpisodeNoticeVisibility))]
    public partial string EpisodeNotice { get; set; } = "展开后加载剧集";
    public Visibility EpisodeNoticeVisibility => EpisodeNotice.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    [ObservableProperty] public partial bool Expanded { get; set; }
    [ObservableProperty] public partial bool Selected { get; set; } = subscription is not null;
    [ObservableProperty] public partial int ModeIndex { get; set; } = (int)(subscription is null ? MoviePilotSubscribeMode.Normal : MoviePilotSubscriptionPlan.Mode(subscription));
    public string ModeGroup => "mode-" + GetHashCode().ToString(System.Globalization.CultureInfo.InvariantCulture);
    public bool NormalMode { get => ModeIndex == 0; set { if (value) ModeIndex = 0; } }
    public bool EpisodesMode { get => ModeIndex == 1; set { if (value) ModeIndex = 1; } }
    public bool FullMode { get => ModeIndex == 2; set { if (value) ModeIndex = 2; } }
    partial void OnModeIndexChanged(int value) { OnPropertyChanged(nameof(NormalMode)); OnPropertyChanged(nameof(EpisodesMode)); OnPropertyChanged(nameof(FullMode)); }
    [ObservableProperty] public partial bool Editable { get; set; } = true;
    public Visibility SelectedVisibility => Selected ? Visibility.Visible : Visibility.Collapsed;
    partial void OnSelectedChanged(bool value) => OnPropertyChanged(nameof(SelectedVisibility));
    partial void OnExpandedChanged(bool value) { if (value) expand(this); }
    public IRelayCommand SubscribeCommand => new RelayCommand(() => subscribe(this));
}

/// <summary>媒体详情与季选择共用读取流程，弹窗持有独立实例，草稿不会改动详情页或远程订阅。</summary>
public sealed partial class MoviePilotMediaViewModel : PageViewModel
{
    private MoviePilotService? _service;
    private MoviePilotMedia? _media;
    private CancellationTokenSource? _lifetime;
    private CancellationTokenSource? _groupLoad;
    private readonly HashSet<MoviePilotMediaSeasonRow> _loadingEpisodes = [];
    private IReadOnlyList<MoviePilotSubscription> _subscriptions = [];
    private IReadOnlyDictionary<int, IReadOnlySet<int>>? _existing;
    private Action<int?, int?>? _search;
    private Action<int?>? _subscribe;
    private bool _picker;
    private int? _preselect;
    private bool Live => _service is { Available: true } && _media is not null && _lifetime is { IsCancellationRequested: false }
        && _service.IsCurrentConnection(_media.ConnectionStamp);

    public ObservableCollection<MoviePilotEpisodeGroupRow> Groups { get; } = [];
    public ObservableCollection<MoviePilotMediaSeasonRow> Seasons { get; } = [];
    public ObservableCollection<MoviePilotSubscriptionFact> Facts { get; } = [];
    public ObservableCollection<MoviePilotMediaLink> Links { get; } = [];
    public MoviePilotMediaDetail? Detail { get; private set; }
    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string Overview { get; set; } = "";
    [ObservableProperty] public partial string Tagline { get; set; } = "";
    [ObservableProperty] public partial string Meta { get; set; } = "";
    [ObservableProperty] public partial string Rating { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PosterEmptyVisibility))]
    public partial BitmapImage? Poster { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackdropVisibility))]
    [NotifyPropertyChangedFor(nameof(BackdropHeight))]
    [NotifyPropertyChangedFor(nameof(BackdropFadeMargin))]
    [NotifyPropertyChangedFor(nameof(HeroMinHeight))]
    public partial BitmapImage? Backdrop { get; set; }
    public Visibility PosterEmptyVisibility => Show(Poster is null);
    public Visibility BackdropVisibility => Show(Backdrop is not null);

    /// <summary>
    /// 背景那张位图的高 ÷ 宽（16:9 是 0.5625），图解出来时跟着 <see cref="Backdrop"/> 一起换。它喂背景
    /// 盒高（<see cref="BackdropHeight"/>）：用户令 2026-10-13「参考电影页面的背景图修改订阅页面的背景图」
    /// —— 电影详情页那张是「宽的窗口上铺满第一屏、窄下来整张等比缩小」，按的就是图自己的形状
    /// （<c>DetailViewModel.HeroHeightRatio</c> 同一套账）。图还没解出来时按 16:9 兜底：背景那几张全是横图，
    /// 第一帧按它布、真图到了再修正。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackdropHeight))]
    [NotifyPropertyChangedFor(nameof(BackdropFadeMargin))]
    [NotifyPropertyChangedFor(nameof(HeroMinHeight))]
    public partial double BackdropRatio { get; set; } = 9d / 16;

    /// <summary>
    /// 这一页看得见的那一段有多宽／多高，由页面在每次改尺寸时量给
    /// （<c>MoviePilotSubscriptionPage.OnDetailScrollSizeChanged</c>）。背景那张画多高、尾部让开多少都问它。
    /// 0 是「还没量」（第一次布局之前），<see cref="DetailHero.PictureHeight"/> 按那个档给。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackdropHeight))]
    [NotifyPropertyChangedFor(nameof(BackdropFadeMargin))]
    [NotifyPropertyChangedFor(nameof(HeroMinHeight))]
    public partial double PageWidth { get; set; }

    /// <inheritdoc cref="PageWidth"/>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackdropHeight))]
    [NotifyPropertyChangedFor(nameof(BackdropFadeMargin))]
    [NotifyPropertyChangedFor(nameof(HeroMinHeight))]
    public partial double Viewport { get; set; }

    /// <summary>
    /// 头部那一叠（封面、片名、操作、资料卡、简介）连上下留白实测有多高，由页面量给
    /// （<c>MoviePilotSubscriptionPage.OnHeroContentSizeChanged</c>）。它是背景盒高的下限：图比这一叠矮的话，
    /// 头部的下半截就落到图外面那段页底色上，而压在图上那几行字用的是固定浅墨（<c>EgOnScrim*</c>），
    /// 在那里读不出来。同电影详情页 <c>DetailViewModel.HeroRoom</c>。
    /// <para>
    /// 量的是头部里面那一叠、不是让它让开高度的那一层（<c>HeroSection</c>）—— 那一层的高由
    /// <see cref="HeroMinHeight"/> 定，拿它当自变量就成了自己喂自己。
    /// </para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackdropHeight))]
    [NotifyPropertyChangedFor(nameof(BackdropFadeMargin))]
    [NotifyPropertyChangedFor(nameof(HeroMinHeight))]
    public partial double HeroBandHeight { get; set; }

    /// <summary>
    /// 背景那一层被顶上窗口上沿的那一截（标题栏加页面在窗口里的偏移，见
    /// <c>MoviePilotSubscriptionPage.LiftBackdrop</c>）。滚动内容的坐标比窗口坐标低这一截，所以「图的下沿」
    /// 折进内容坐标要减掉它 —— 不减的话尾部永远多让出一截标题栏高的空画面（同电影详情页
    /// <c>DetailViewModel.HeaderLift</c>）。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeroMinHeight))]
    public partial double HeaderLift { get; set; }

    /// <summary>
    /// 背景那一张这次画多高 —— <see cref="DetailHero.PictureHeight"/>：宽的窗口上被视口封顶、照旧铺满第一屏；
    /// 窄到「页宽 × 图的高宽比」够不着视口之后整张等比缩小、一像素不裁。下限是头部那一叠的实测高，别让压在
    /// 图上那几行字落到图外面去。
    /// </summary>
    public double BackdropHeight => DetailHero.PictureHeight(PageWidth, BackdropRatio, Viewport, HeroBandHeight);

    /// <summary>
    /// 图下沿那道黑色渐变摆多高 —— 盒子的下沿减去渐变自己的高，渐变钉在画面的下沿上、不钉在视口上：往下滚它
    /// 跟着那条边待在原地，纸面盖过来把它收走（同电影详情页 <c>DetailViewModel.PictureFadeMargin</c>）。
    /// </summary>
    public Thickness BackdropFadeMargin => new(0, Math.Max(0, BackdropHeight - FadeHeight), 0, 0);

    /// <summary>
    /// 头部那一段自己至少要多高：把正文纸面的上沿推到图的下沿上，第一屏因此是「图 + 一条压暗的尾巴」，而不是
    /// 纸面切在画面中间（同电影详情页的 <c>HeroTail</c>）。头部那一叠本身比这还高时就按它自己的高走。
    /// </summary>
    public double HeroMinHeight => Math.Max(0, BackdropHeight - HeaderLift);

    /// <summary>图下沿那道黑色渐变的高度 —— 同电影详情页 <c>DetailViewModel.PictureFadeHeight</c>。</summary>
    private const double FadeHeight = 64;

    /// <summary>解出来的那张图的高宽比上屏（同电影详情页 <c>DetailViewModel.ShowHero</c> 那一手）。量不成话
    /// （0 或负）就不动兜底的那 0.5625。</summary>
    partial void OnBackdropChanged(BitmapImage? value)
    {
        if (value is { PixelWidth: > 0, PixelHeight: > 0 })
            BackdropRatio = value.PixelHeight / (double)value.PixelWidth;
    }
    [ObservableProperty] public partial string SubscribeLabel { get; set; } = "选择订阅季";
    [ObservableProperty] public partial string GroupId { get; set; } = "";
    [ObservableProperty] public partial string GroupSummary { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SeasonNoticeVisibility))]
    public partial string SeasonNotice { get; set; } = "正在读取季信息…";
    public Visibility SeasonNoticeVisibility => Show(SeasonNotice.Length > 0);
    [ObservableProperty] public partial bool Submitting { get; set; }
    [ObservableProperty] public partial bool Submitted { get; set; }
    [ObservableProperty] public partial bool ReviewConfirmed { get; set; }
    [ObservableProperty] public partial string SubmitSummary { get; set; } = "选择季和订阅模式，提交后 MoviePilot 将按配置处理订阅。取消订阅会保留已入库文件。";
    public bool CanInteract => Live && !Busy && !Submitting && !Submitted;
    public bool CanSubmit => CanInteract && Detail is not null && Seasons.Count > 0;

    /// <summary>
    /// 订阅详情页上「在 AniBT 中搜索」那一颗。只有订阅详情（不是从搜索或推荐打开的纯媒体页）
    /// 才把这一位打开；具体是否日本动画要等媒体详情回来才知道，见
    /// <see cref="AniBtEligibility.IsJapaneseAnime"/>。
    /// </summary>
    [ObservableProperty] public partial bool AniBtSearch { get; set; }
    public Visibility AniBtVisibility => Show(AniBtSearch && Live && Detail is not null && AniBtEligibility.IsJapaneseAnime(Detail));
    public bool CanSearchAniBt => CanInteract && AniBtVisibility == Visibility.Visible;
    public Visibility SeasonsVisibility => Show(Detail?.SupportsSeasons == true);
    public Visibility GroupsVisibility => Show(Groups.Count > 1);
    public Visibility SubmitReviewVisibility => Show(Submitted);
    public Visibility ReviewVisibility => Show(Submitted || Seasons.Any(row => _service?.SeasonSubscribeState(Detail!.Media with
    { Season = Detail.Media.Type == "电视剧" ? row.Season.Number : null }) == MoviePilotOperationState.Uncertain
        || row.Subscription is { } subscription && _service?.SubscriptionNeedsReview(subscription) == true));
    partial void OnSubmittingChanged(bool value) => NotifyState();
    partial void OnSubmittedChanged(bool value) => NotifyState();
    partial void OnAniBtSearchChanged(bool value) => NotifyState();
    protected override void BusyChanged() => NotifyState();
    private void NotifyState()
    {
        OnPropertyChanged(nameof(CanInteract)); OnPropertyChanged(nameof(CanSubmit)); OnPropertyChanged(nameof(SubmitReviewVisibility));
        OnPropertyChanged(nameof(ReviewVisibility)); OnPropertyChanged(nameof(AniBtVisibility)); OnPropertyChanged(nameof(CanSearchAniBt));
        foreach (var row in Groups) row.Refresh();
        foreach (var row in Seasons) row.Editable = CanInteract;
    }

    internal void Attach(MoviePilotService service, MoviePilotMedia media, string group = "", bool picker = false,
        int? preselect = null, Action<int?, int?>? search = null, Action<int?>? subscribe = null, CancellationToken lifetimeToken = default)
    {
        Cancel();
        _service = service; _media = media; GroupId = group; _picker = picker; _preselect = preselect;
        _search = search; _subscribe = subscribe; _lifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        Title = media.Display; Overview = media.Overview ?? ""; Meta = media.SourceLabel + " · " + media.MediaId + " · " + media.Type;
        Submitted = false;
        AniBtSearch = false;
    }

    public override async Task ReloadAsync()
    {
        if (!Live || _media is null || _service is null || Submitting) return;
        var load = BeginLoad();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(load, _lifetime!.Token);
        try
        {
            var detail = await _service.MediaDetailAsync(_media, linked.Token);
            var subscriptions = await _service.SubscriptionsAsync(linked.Token);
            if (!Live || !IsCurrent(load)) return;
            Detail = detail; _subscriptions = subscriptions.Where(item => MoviePilotSubscriptionPlan.SameMedia(item.Media, _media)).ToArray();
            OnPropertyChanged(nameof(AniBtVisibility)); OnPropertyChanged(nameof(CanSearchAniBt));
            _ = LoadHeroImagesAsync(detail, linked.Token);
            Title = detail.Media.Display; Overview = detail.Media.Overview ?? ""; Tagline = detail.Tagline;
            Meta = string.Join(" · ", new[] { detail.Media.Type, detail.Genres, detail.Media.SourceLabel + " " + detail.Media.MediaId }.Where(value => !string.IsNullOrEmpty(value)));
            Rating = detail.Vote is > 0 ? $"★ {detail.Vote:0.0} / 10" : "暂无评分";
            Facts.Clear(); foreach (var fact in detail.Facts) Facts.Add(fact);
            Links.Clear(); foreach (var link in detail.Links) Links.Add(link);
            SubscribeLabel = detail.Media.Type == "电视剧" ? (_subscriptions.Count > 0 ? $"已订阅 {_subscriptions.Count} 季" : "选择订阅季")
                : _subscriptions.Count > 0 ? "取消订阅" : "订阅";
            var groups = new List<MoviePilotEpisodeGroup>();
            if (detail.SupportsEpisodeGroups)
            {
                try { groups.AddRange(await _service.MediaGroupsAsync(detail, linked.Token)); }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { groups.Add(new("", "默认", detail.Seasons.Count, detail.Seasons.Sum(season => season.EpisodeCount))); }
            }
            if (!Live || !IsCurrent(load)) return;
            FillGroups(groups);
            await LoadSeasonsAsync(linked.Token);
            if (!_picker) await LoadRelatedAsync(linked.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (Live && IsCurrent(load)) { SeasonNotice = "媒体详情读取失败，可刷新重试。"; Report("媒体详情读取失败", error); } }
        finally { EndLoad(load); OnPropertyChanged(nameof(SeasonsVisibility)); NotifyState(); }
    }

    private async Task LoadHeroImagesAsync(MoviePilotMediaDetail detail, CancellationToken token)
    {
        if (_service is null) return;
        async Task LoadAsync(string? url, bool backdrop)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                var bytes = await _service.FetchImageAsync(url, token);
                var image = await PosterLoader.DecodeAsync(bytes, backdrop ? 1200 : 240);
                if (!Live || token.IsCancellationRequested || !ReferenceEquals(detail, Detail)) return;
                if (backdrop) Backdrop = image; else Poster = image;
            }
            catch (Exception) { }
        }
        await Task.WhenAll(LoadAsync(detail.Media.PosterUrl, false), LoadAsync(detail.Media.BackdropUrl, true));
    }

    private void FillGroups(IEnumerable<MoviePilotEpisodeGroup> groups)
    {
        var options = groups.ToArray();
        Groups.Clear();
        foreach (var group in options) Groups.Add(new(group, group.Id == GroupId, () => Live && !Submitting && !Submitted, SelectGroupAsync));
        GroupSummary = MoviePilotEpisodeGroups.Current(options, GroupId);
        OnPropertyChanged(nameof(GroupsVisibility));
    }

    internal async Task SelectGroupAsync(MoviePilotEpisodeGroupRow row)
    {
        if (!Live || Submitting || Submitted || !Groups.Contains(row) || row.IsCurrent) return;
        GroupId = row.Group.Id;
        FillGroups(Groups.Select(item => item.Group).ToArray());
        await LoadSeasonsAsync(_lifetime!.Token);
    }

    private async Task LoadSeasonsAsync(CancellationToken cancellationToken)
    {
        if (_service is null || Detail is null) return;
        _groupLoad?.Cancel(); _groupLoad?.Dispose();
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _groupLoad = pending;
        var group = GroupId;
        Seasons.Clear(); _existing = null; _loadingEpisodes.Clear();
        SeasonNotice = "正在读取季信息…";
        try
        {
            var seasons = Detail.SupportsSeasons ? await _service.MediaSeasonsAsync(Detail, group, pending.Token)
                : _picker && Detail.Media.Type == "电影" ? new[] { new MoviePilotMediaSeason(0, Detail.Media.Title, 0, "", Detail.Media.PosterUrl ?? "", Detail.Vote) } : [];
            if (!Live || pending.IsCancellationRequested || !ReferenceEquals(_groupLoad, pending)) return;
            foreach (var season in seasons)
            {
                var subscription = _subscriptions.FirstOrDefault(item => item.Media.Season == (Detail.Media.Type == "电视剧" ? season.Number : null) && item.EpisodeGroup == group);
                var row = new MoviePilotMediaSeasonRow(season, subscription, row => _ = LoadEpisodesAsync(row), row =>
                { if (CanInteract && Seasons.Contains(row)) _subscribe?.Invoke(row.Season.Number); })
                { IsMovie = Detail.Media.Type == "电影" };
                if (_picker && _preselect == season.Number) row.Selected = true;
                Seasons.Add(row);
                row.PropertyChanged += (_, args) => { if (_picker && args.PropertyName is nameof(row.Selected) or nameof(row.ModeIndex)) UpdatePlanSummary(); };
                _ = LoadSeasonImageAsync(row, pending.Token);
            }
            _preselect = null;
            if (_picker) UpdatePlanSummary();
            SeasonNotice = seasons.Count > 0 ? "" : "此剧集组没有可用的季信息。";
            if (Detail.SupportsSeasons)
            {
                IReadOnlyDictionary<int, IReadOnlySet<int>>? existing = null;
                try { existing = await _service.MediaExistingEpisodesAsync(Detail, group, pending.Token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { SeasonNotice = "入库状态暂不可用；仍可浏览季集与管理订阅。"; }
                if (!Live || pending.IsCancellationRequested || !ReferenceEquals(_groupLoad, pending)) return;
                _existing = existing;
                foreach (var row in Seasons)
                {
                    var count = _existing?.GetValueOrDefault(row.Season.Number)?.Count ?? 0;
                    row.Status = _existing is null ? "入库状态未知" : count == 0 ? "缺失" : count >= row.Season.EpisodeCount && row.Season.EpisodeCount > 0 ? "已入库" : $"部分入库 · {count} / {row.Season.EpisodeCount} 集";
                    foreach (var episode in row.Episodes) episode.Exists = _existing is null ? null : _existing.GetValueOrDefault(row.Season.Number)?.Contains(episode.Episode.Number) == true;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (Live && ReferenceEquals(_groupLoad, pending)) { SeasonNotice = "季列表读取失败，请重新选择或刷新。"; Report("季列表读取失败", error); } }
        finally { if (ReferenceEquals(_groupLoad, pending)) _groupLoad = null; NotifyState(); }
    }

    private async Task LoadSeasonImageAsync(MoviePilotMediaSeasonRow row, CancellationToken token)
    {
        if (_service is null || row.Season.PosterUrl.Length == 0) return;
        try
        {
            var bytes = await _service.FetchImageAsync(row.Season.PosterUrl, token);
            var image = await PosterLoader.DecodeAsync(bytes, 120);
            if (Live && !token.IsCancellationRequested && Seasons.Contains(row)) row.Poster = image;
        }
        catch (Exception) { }
    }

    internal async Task LoadEpisodesAsync(MoviePilotMediaSeasonRow row)
    {
        // 订阅季的展开内容由文件统计渲染（row.Files 由 MoviePilotMediaSeasonsView 挂上），不再拉通用集列表。
        if (row.Files is not null || !Live || _service is null || Detail is null || !Seasons.Contains(row) || row.Episodes.Count > 0 || !_loadingEpisodes.Add(row)) return;
        var group = GroupId;
        var token = _lifetime!.Token;
        row.EpisodeNotice = "正在读取剧集…";
        try
        {
            var episodes = await _service.MediaEpisodesAsync(Detail, group, row.Season.Number, token);
            if (!Live || group != GroupId || !Seasons.Contains(row)) return;
            foreach (var episode in episodes)
            {
                var item = new MoviePilotMediaEpisodeRow(episode, _existing is null ? null : _existing.GetValueOrDefault(row.Season.Number)?.Contains(episode.Number) == true,
                    () => { if (CanInteract && Seasons.Contains(row)) _search?.Invoke(row.Season.Number, episode.Number); });
                row.Episodes.Add(item);
                _ = LoadEpisodeImageAsync(row, item, token);
            }
            row.EpisodeNotice = episodes.Count == 0 ? "暂无剧集信息" : "";
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (Live && Seasons.Contains(row)) row.EpisodeNotice = "读取失败，收起后重新展开可重试。"; }
        finally { _loadingEpisodes.Remove(row); }
    }

    private async Task LoadEpisodeImageAsync(MoviePilotMediaSeasonRow season, MoviePilotMediaEpisodeRow row, CancellationToken token)
    {
        if (_service is null || row.Episode.ImageUrl.Length == 0) return;
        try
        {
            var bytes = await _service.FetchImageAsync(row.Episode.ImageUrl, token);
            var image = await PosterLoader.DecodeAsync(bytes, 220);
            if (Live && Seasons.Contains(season) && !token.IsCancellationRequested) row.Artwork = image;
        }
        catch (Exception) { }
    }

    internal IReadOnlyList<MoviePilotSubscribeChange> Plan() => Detail is null ? [] : MoviePilotSubscriptionPlan.Build(Detail.Media, GroupId,
        Seasons.Select(row => new MoviePilotSeasonChoice(Detail.Media.Type == "电视剧" ? row.Season.Number : null, row.Selected, (MoviePilotSubscribeMode)row.ModeIndex)).ToArray(), _subscriptions);

    private void UpdatePlanSummary()
    {
        try
        {
            var plan = Plan();
            SubmitSummary = plan.Count == 0 ? "订阅没有变化。" : "提交后将执行：\n" + string.Join("\n", plan.Select(change => change.Description));
        }
        catch (Exception error) { SubmitSummary = error.Message; }
    }

    internal async Task<bool> SubmitAsync()
    {
        if (!CanSubmit || _service is null) return false;
        var lifetime = _lifetime!;
        var token = lifetime.Token;
        Submitting = true;
        var completed = new List<string>();
        try
        {
            var plan = Plan();
            if (plan.Count == 0) { SubmitSummary = "订阅没有变化。"; return true; }
            foreach (var change in plan)
            {
                token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(lifetime, _lifetime) || !Live) return false;
                await _service.ApplySubscriptionChangeAsync(change, token);
                token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(lifetime, _lifetime) || !Live) return false;
                completed.Add(change.Description);
            }
            SubmitSummary = string.Join("\n", completed.Select(value => "已提交：" + value));
            Submitted = true;
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception error)
        {
            if (!ReferenceEquals(lifetime, _lifetime) || !Live) return false;
            Submitted = true;
            SubmitSummary = string.Join("\n", completed.Select(value => "已提交：" + value)) + (completed.Count > 0 ? "\n\n" : "") + error.Message + "\n未继续提交后续季。关闭后重新打开可读取服务器现状。";
            if (Live) Report("部分订阅未提交", error);
            return false;
        }
        finally { if (ReferenceEquals(lifetime, _lifetime)) Submitting = false; }
    }

    internal async Task ReviewAsync()
    {
        if (!Live || !ReviewConfirmed || Submitting || _service is null || Detail is null) return;
        foreach (var row in Seasons)
        {
            var media = Detail.Media with { Season = Detail.Media.Type == "电视剧" ? row.Season.Number : null };
            _service.ConfirmSeasonSubscriptionReviewed(media);
            if (row.Subscription is { } subscription) _service.ConfirmSubscriptionReviewed(subscription);
        }
        Submitted = false; ReviewConfirmed = false; ClearNotice();
        await ReloadAsync();
    }

    internal void OperationFailed(Exception error) => Report("订阅操作失败", error);

    public override void Cancel()
    {
        _lifetime?.Cancel(); _lifetime?.Dispose(); _lifetime = null;
        _groupLoad?.Cancel(); _groupLoad = null;
        Seasons.Clear(); Groups.Clear(); Facts.Clear(); Links.Clear(); Detail = null;
        Poster = Backdrop = null; ClearRelated(); Submitting = false;
        _loadingEpisodes.Clear();
        base.Cancel(); NotifyState();
        OnPropertyChanged(nameof(AniBtVisibility)); OnPropertyChanged(nameof(CanSearchAniBt));
    }
}
