using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Momoka.Diagnostics;
using Momoka.Infrastructure;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.Media;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Momoka.Shell.ViewModels;

public sealed partial class MoviePilotSubscriptionCard : ObservableObject
{
    /// <summary>
    /// 详情页头部那张背景图解码到多宽。它铺的是一整块面板（约 900～1400 逻辑像素），按海报那档
    /// 170 解出来再放大就是一团糊；960 在这个宽度上够用，又不至于为一张底图吃掉几 MB。
    /// </summary>
    private const int BackdropDecodeWidth = 960;

    private CancellationTokenSource? _imageLoad;
    private CancellationTokenSource? _backdropLoad;
    public MoviePilotSubscriptionCard(MoviePilotSubscription subscription) => Subscription = subscription;
    public MoviePilotSubscription Subscription { get; private set; }
    public string Title => Subscription.Title;
    public string Status => Subscription.StateLabel;
    public string Progress => Subscription.ProgressLabel;
    public string ProgressCount => Subscription.ProgressCount;
    public Visibility ProgressVisibility => Progress.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string Overview => Subscription.Media.Overview ?? "";
    public string Identity => Subscription.Media.Confirmation;
    public string Glyph => Subscription.IsSeries ? "\uE7F4" : "\uE8B2";
    public double CardWidth => CardSize.PosterWidth;
    public double PosterHeight => CardSize.HeightFor(CardSize.PosterWidth, false);
    public double RowHeight => PosterHeight + CardSize.Chrome;

    /// <summary>
    /// 标题下面那一行：这条订阅认的是哪部作品。来源与编号成对出现（豆瓣的 42 和 TMDB 的 42 不是一部片），
    /// 认不出的来源原样带出，见 <see cref="MoviePilotSourceNames"/>。
    /// </summary>
    public string Meta => string.Join(" · ", new[]
    {
        Subscription.Media.SourceLabel,
        Subscription.Media.MediaId ?? "",
        Subscription.Media.Type ?? "",
        Subscription.Media.Season is { } season ? $"第 {season} 季" : ""
    }.Where(part => !string.IsNullOrWhiteSpace(part)));

    /// <summary>详情页右栏「订阅信息」与左栏「订阅规则」两串事实，投影在 Core（见 <see cref="MoviePilotSubscriptionFacts"/>）。</summary>
    public IReadOnlyList<MoviePilotSubscriptionFact> InfoFacts => MoviePilotSubscriptionFacts.Info(Subscription);
    public IReadOnlyList<MoviePilotSubscriptionFact> RuleFacts => MoviePilotSubscriptionFacts.Rules(Subscription);
    public bool HasRules => RuleFacts.Count > 0;
    public string RulesNote => HasRules ? "「编辑订阅」可修改以上规则；未列出的项沿用 MoviePilot 当前配置。" : "未设置自定义搜索与过滤规则，沿用 MoviePilot 当前配置。";

    /// <summary>评分是 0～10 的读数，五颗星按 10 分制的一半折算。</summary>
    public bool HasRating => Subscription.Vote is > 0;
    public double RatingValue => (Subscription.Vote ?? 0) / 2;
    public string RatingText => HasRating ? $"{Subscription.Vote:0.0} / 10" : "暂无评分";

    /// <summary>
    /// 五颗星的字形：够到的实心（FavoriteStarFill），不够的空心（FavoriteStar）。取整走「四舍五入、.5 向上」——
    /// 9 分是四颗半，画成五颗实心才不至于把「很好」读成「还行」。
    /// </summary>
    public IReadOnlyList<string> Stars => Enumerable.Range(0, 5)
        .Select(index => index < Math.Round((Subscription.Vote ?? 0) / 2, MidpointRounding.AwayFromZero) ? "\uE735" : "\uE734")
        .ToArray();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyPosterVisibility))]
    public partial BitmapImage? Poster { get; set; }
    public Visibility EmptyPosterVisibility => Poster is null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>头部那张横版底图。缺图时那一层不画，标题与操作照旧站在面板底色上。</summary>
    [ObservableProperty]
    public partial BitmapImage? Backdrop { get; set; }

    public void Update(MoviePilotSubscription subscription)
    {
        if (Subscription.Media.PosterUrl != subscription.Media.PosterUrl) Release();
        if (Subscription.Media.BackdropUrl != subscription.Media.BackdropUrl) { _backdropLoad?.Cancel(); _backdropLoad = null; Backdrop = null; }
        Subscription = subscription;
        OnPropertyChanged(string.Empty);
    }

    public async Task LoadPosterAsync(MoviePilotService service)
    {
        if (Poster is not null || _imageLoad is not null || !service.IsCurrentSubscription(Subscription)
            || !Uri.TryCreate(Subscription.Media.PosterUrl, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https")) return;
        using var pending = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        _imageLoad = pending;
        try
        {
            var bytes = await service.FetchImageAsync(url.AbsoluteUri, pending.Token);
            var image = await PosterLoader.DecodeAsync(bytes, CardSize.PosterWidth);
            if (!pending.IsCancellationRequested && ReferenceEquals(_imageLoad, pending) && service.IsCurrentSubscription(Subscription)) Poster = image;
        }
        catch (Exception) { /* 缺封面仍保留可访问的标题和操作。 */ }
        finally { if (ReferenceEquals(_imageLoad, pending)) _imageLoad = null; }
    }

    /// <summary>
    /// 头部底图单独一趟：它比海报大得多（见 <see cref="BackdropDecodeWidth"/>），失败与缺图都只是不画这一层，
    /// 不影响标题、规则和操作。
    /// </summary>
    public async Task LoadBackdropAsync(MoviePilotService service)
    {
        if (Backdrop is not null || _backdropLoad is not null || !service.IsCurrentSubscription(Subscription)
            || !Uri.TryCreate(Subscription.Media.BackdropUrl, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https")) return;
        using var pending = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        _backdropLoad = pending;
        try
        {
            var bytes = await service.FetchImageAsync(url.AbsoluteUri, pending.Token);
            var image = await PosterLoader.DecodeAsync(bytes, BackdropDecodeWidth);
            if (!pending.IsCancellationRequested && ReferenceEquals(_backdropLoad, pending) && service.IsCurrentSubscription(Subscription)) Backdrop = image;
        }
        catch (Exception) { /* 底图不可用时不画那一层。 */ }
        finally { if (ReferenceEquals(_backdropLoad, pending)) _backdropLoad = null; }
    }

    public void Release() { _imageLoad?.Cancel(); _imageLoad = null; Poster = null; _backdropLoad?.Cancel(); _backdropLoad = null; Backdrop = null; }
}

public sealed partial class MoviePilotSubscriptionsViewModel : PageViewModel
{
    private MoviePilotService? _service;
    private CancellationTokenSource? _lifetime;
    private string _connection = "";
    private bool _detail;
    private bool Live => _lifetime is { IsCancellationRequested: false } && _service?.IsCurrentConnection(_connection) == true;
    private IReadOnlyList<MoviePilotSubscriptionEpisodeRow> _episodes = [];
    private EmbySessionScope? _playbackScope;
    private Func<MoviePilotPlaybackTarget, Task>? _play;
    private Action<string>? _search;

    public ObservableCollection<MoviePilotSubscriptionCard> Movies { get; } = [];
    public ObservableCollection<MoviePilotSubscriptionCard> Series { get; } = [];
    public ObservableCollection<MoviePilotSubscriptionEpisodeRow> Episodes { get; } = [];

    public string MoviesNote => Movies.Count == 0 ? "暂无电影订阅" : $"{Movies.Count} 部电影";
    public string SeriesNote => Series.Count == 0 ? "暂无电视剧订阅" : $"{Series.Count} 条订阅";

    /// <summary>连接可用且已有内容时显示；业务提示只在服务可连接时保留，断线不撑起空板块。</summary>
    public Visibility Visible => Show(_service?.Available == true && (Movies.Count > 0 || Series.Count > 0 || NoticeOpen));
    public Visibility MoviesVisibility => Show(Movies.Count > 0);
    public Visibility SeriesVisibility => Show(Series.Count > 0);
    public double RowHeight => CardSize.HeightFor(CardSize.PosterWidth, false) + CardSize.Chrome;

    [ObservableProperty]
    public partial MoviePilotSubscriptionCard? Selected { get; set; }
    public string DetailTitle => Selected?.Title ?? "订阅媒体详情";
    public string DetailStatus => Selected?.Status ?? "";
    public string DetailMeta => Selected?.Meta ?? "";
    public string DetailOverview => Selected?.Overview ?? "";
    public BitmapImage? DetailPoster => Selected?.Poster;
    public BitmapImage? DetailBackdrop => Selected?.Backdrop;
    public Visibility DetailPosterEmptyVisibility => Show(Selected?.Poster is null);
    public Visibility DetailBackdropVisibility => Show(Selected?.Backdrop is not null);
    public IReadOnlyList<MoviePilotSubscriptionFact> DetailInfoFacts => Selected?.InfoFacts ?? [];
    public IReadOnlyList<MoviePilotSubscriptionFact> DetailRuleFacts => Selected?.RuleFacts ?? [];
    public string DetailRulesNote => Selected?.RulesNote ?? "";
    public string DetailRatingText => Selected?.RatingText ?? "";
    public IReadOnlyList<string> DetailStars => Selected?.Stars ?? [];
    public bool DetailPaused => Selected?.Subscription.Paused == true;
    public string DetailPauseLabel => DetailPaused ? "恢复订阅" : "暂停订阅";

    /// <summary>
    /// 标题旁边那颗徽标：这条订阅的入库情况。文件统计读不到时是空串（不显示），而不是「未入库」——
    /// 拿不到统计不等于库里没有。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LibraryBadgeVisibility))]
    public partial string LibraryBadge { get; set; } = "";
    public Visibility LibraryBadgeVisibility => Show(LibraryBadge.Length > 0);

    partial void OnSelectedChanged(MoviePilotSubscriptionCard? oldValue, MoviePilotSubscriptionCard? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= SelectedChanged;
        if (newValue is not null) newValue.PropertyChanged += SelectedChanged;
        SelectedChanged(null, null);
    }
    private void SelectedChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs? args)
    {
        OnPropertyChanged(nameof(DetailTitle)); OnPropertyChanged(nameof(DetailStatus));
        OnPropertyChanged(nameof(DetailMeta));
        OnPropertyChanged(nameof(DetailOverview)); OnPropertyChanged(nameof(DetailPoster)); OnPropertyChanged(nameof(DetailBackdrop));
        OnPropertyChanged(nameof(DetailPosterEmptyVisibility)); OnPropertyChanged(nameof(DetailBackdropVisibility));
        OnPropertyChanged(nameof(DetailInfoFacts)); OnPropertyChanged(nameof(DetailRuleFacts)); OnPropertyChanged(nameof(DetailRulesNote));
        OnPropertyChanged(nameof(DetailRatingText)); OnPropertyChanged(nameof(DetailStars));
        OnPropertyChanged(nameof(DetailPaused)); OnPropertyChanged(nameof(DetailPauseLabel));
    }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanManage))]
    public partial bool Working { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanManage))]
    public partial bool Deleted { get; set; }
    [ObservableProperty]
    public partial string FileSummary { get; set; } = "正在读取文件统计…";
    [ObservableProperty]
    public partial string PlaybackStatus { get; set; } = "";
    [ObservableProperty]
    public partial int FilterIndex { get; set; }
    public bool CanManage => Live && _service?.Available == true && !Working && !Busy && !Deleted;
    partial void OnFilterIndexChanged(int value) => ApplyFilter();
    protected override void BusyChanged() { OnPropertyChanged(nameof(CanManage)); RefreshCommands(); }
    partial void OnWorkingChanged(bool value) => RefreshCommands();
    partial void OnDeletedChanged(bool value) => RefreshCommands();

    /// <summary>忙碌、忙碌解除、订阅被取消之后，把两处按命令可用性渲染的行各问一遍（集列表与剧集组卡片）。</summary>
    private void RefreshCommands()
    {
        foreach (var row in _episodes) row.Refresh();
        OnPropertyChanged(nameof(PlayFirstVisibility));
        PlayFirstCommand.NotifyCanExecuteChanged();
    }

    public Visibility PlayFirstVisibility => Show(_episodes.Any(row => row.CanPlay));
    private bool CanPlayFirst => _episodes.Any(row => row.CanPlay);
    [RelayCommand(CanExecute = nameof(CanPlayFirst))]
    private Task PlayFirstAsync() => _episodes.FirstOrDefault(row => row.CanPlay) is { } row ? PlayEpisodeAsync(row) : Task.CompletedTask;

    public MoviePilotSubscriptionsViewModel() =>
        // Visible 也看 NoticeOpen（见它的说明），而那条属性住在基类上 —— 生成器的 On…Changed 只长在声明它的
        // 那个类里，这里够不着，就听 PropertyChanged 这一路。提示条一开一关，整块板块跟着立起来或塌下去。
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(NoticeOpen)) OnPropertyChanged(nameof(Visible));
        };

    internal void AttachPlayback(EmbySessionScope? scope, Func<MoviePilotPlaybackTarget, Task>? play)
    { _playbackScope = scope; _play = play; }

    /// <summary>
    /// 「搜索这一集的资源」的出口。外壳递进来（打开 MoviePilot 资源窗口是外壳的事，同「搜索其他版本」），
    /// 页面只负责按订阅快照算出关键词。
    /// </summary>
    internal void AttachSearch(Action<string>? search) => _search = search;

    internal void Attach(MoviePilotService service, MoviePilotSubscription? selected = null)
    {
        Cancel();
        _service = service;
        _detail = selected is not null;
        _lifetime = new CancellationTokenSource();
        Deleted = false;
        Selected = selected is null ? null : new(selected);
        try { _connection = selected?.ConnectionStamp ?? service.ConnectionStamp; }
        catch (MoviePilotException error) { Report("MoviePilot 未连接", error); }

        // 进这一页/返回主页时先把上一批卡片收干净，整块板块跟着塌下来；真的取到订阅时再由 NotifyRows 立起来。
        // 不能在这里凭 Enabled 就把整块摆上屏 —— 那正是「先看到两块空牌子」的那一帧（见 Visible）。
        ClearCards();
    }

    public override async Task ReloadAsync()
    {
        if (Working || _service is null || _lifetime is not { IsCancellationRequested: false }) return;
        if (!_service.Enabled) { ClearCards(); OnPropertyChanged(nameof(CanManage)); return; }
        var token = BeginLoad();
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        try
        {
            if (!Live)
            {
                ClearCards();
                if (_detail) throw new MoviePilotException("MoviePilot 连接已改变，请返回主页重新打开订阅");
                _connection = _service.ConnectionStamp;
            }
            if (_detail && Selected is { } selected)
            {
                var current = await _service.SubscriptionAsync(selected.Subscription, pending.Token);
                if (!IsCurrent(token) || !Live) return;
                selected.Update(current);
                _ = selected.LoadPosterAsync(_service);
                _ = selected.LoadBackdropAsync(_service);
                foreach (var row in _episodes) row.Retire();
                _episodes = [];
                Episodes.Clear();
                PlaybackStatus = "";
                LibraryBadge = "";
                FileSummary = "正在读取文件统计…";
                var files = await _service.SubscriptionFilesAsync(current, pending.Token);
                if (!IsCurrent(token) || !Live) return;
                var airDates = await AirDatesAsync(current, pending.Token);
                if (!IsCurrent(token) || !Live) return;
                _episodes = files.Episodes.Select(episode => new MoviePilotSubscriptionEpisodeRow(
                    episode with { AirDate = airDates.TryGetValue(episode.Number, out var aired) ? aired : null },
                    () => CanManage && _playbackScope?.IsCurrent == true && _play is not null, PlayEpisodeAsync,
                    () => CanManage && _search is not null, SearchEpisode)).ToArray();
                FileSummary = current.IsSeries ? files.Summary
                    : files.Episodes.Count == 0 ? files.Summary : files.LibraryCount > 0 ? "电影已入库" : "电影尚未入库";
                // 徽标只在统计真的读到时给结论：空回话与接口错误都不说「未入库」。
                LibraryBadge = files.Episodes.Count == 0 ? "" : files.LibraryCount == files.Episodes.Count ? "已入库"
                    : files.LibraryCount > 0 ? "部分入库" : "未入库";
                ApplyFilter();
                await LoadPlaybackAsync(current, files, token, pending.Token);
            }
            else
            {
                var items = await _service.SubscriptionsAsync(pending.Token);
                if (!IsCurrent(token) || !Live) return;
                Apply(Movies, items.Where(item => !item.IsSeries).ToArray());
                Apply(Series, items.Where(item => item.IsSeries).ToArray());
                NotifyRows();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (IsCurrent(token))
            {
                if (_detail) { FileSummary = "文件统计未读取成功，不能据此判断是否入库"; LibraryBadge = ""; }
                Report("读取 MoviePilot 订阅失败", error);
            }
        }
        finally { EndLoad(token); OnPropertyChanged(nameof(CanManage)); }
    }

    internal void RefreshAvailability()
    {
        if (_service?.Available != true)
        {
            base.Cancel();
            ClearNotice();
            ClearCards();
        }
        OnPropertyChanged(nameof(Visible));
        OnPropertyChanged(nameof(CanManage));
        RefreshCommands();
    }

    internal async Task RefreshAfterMediaChangesAsync()
    {
        if (!Live || _service is null || _lifetime is null || Selected is not { } selected) return;
        var token = _lifetime.Token;
        var subscriptions = await _service.SubscriptionsAsync(token);
        if (!Live || token.IsCancellationRequested) return;
        Deleted = subscriptions.All(item => item.Id != selected.Subscription.Id);
        if (!Deleted) await ReloadAsync();
        else { FileSummary = "这条订阅已取消，已入库文件保留。"; }
    }

    private static void Apply(ObservableCollection<MoviePilotSubscriptionCard> cards, IReadOnlyList<MoviePilotSubscription> items)
    {
        foreach (var old in cards.Where(card => items.All(item => item.Id != card.Subscription.Id)).ToArray())
        { old.Release(); cards.Remove(old); }
        for (var index = 0; index < items.Count; index++)
        {
            var existing = cards.FirstOrDefault(card => card.Subscription.Id == items[index].Id);
            if (existing is null) cards.Insert(index, new(items[index]));
            else { existing.Update(items[index]); if (cards.IndexOf(existing) != index) cards.Move(cards.IndexOf(existing), index); }
        }
    }

    public bool Owns(MoviePilotSubscriptionCard card) => Live && (_detail ? ReferenceEquals(Selected, card) : Movies.Contains(card) || Series.Contains(card));
    public bool NeedsReview(MoviePilotSubscriptionCard card) => _service?.SubscriptionNeedsReview(card.Subscription) == true;
    public Task LoadPosterAsync(MoviePilotSubscriptionCard card) => Owns(card) && _service is { } service ? card.LoadPosterAsync(service) : Task.CompletedTask;
    public Task LoadEpisodeArtworkAsync(MoviePilotSubscriptionEpisodeRow row) => Live && _episodes.Contains(row)
        && _service is { } service && _lifetime is { } lifetime ? row.LoadArtworkAsync(service, lifetime.Token) : Task.CompletedTask;

    internal async Task ReviewAsync(MoviePilotSubscriptionCard card)
    {
        if (!CanManage || !Owns(card) || _service is null || _lifetime is null) return;
        var token = _lifetime.Token;
        Working = true;
        try
        {
            if (await ConfirmAsync("核对订阅操作", card.Identity + "\n\n上次操作的结果不明确。请先核对 MoviePilot 中的订阅状态和下载任务，确认不会重复执行后再继续。", "已核对，允许继续")
                && !token.IsCancellationRequested && Owns(card))
            {
                _service.ConfirmSubscriptionReviewed(card.Subscription);
                ClearNotice();
            }
        }
        finally { if (!token.IsCancellationRequested) Working = false; }
    }

    public async Task ManageAsync(MoviePilotSubscriptionCard card, MoviePilotSubscriptionAction action)
    {
        if (!CanManage || !Owns(card) || _service is null || _lifetime is null) return;
        var subscription = card.Subscription;
        var token = _lifetime.Token;
        var verb = action switch
        {
            MoviePilotSubscriptionAction.Pause => "暂停订阅",
            MoviePilotSubscriptionAction.Resume => "恢复订阅",
            MoviePilotSubscriptionAction.Search => "搜索订阅",
            MoviePilotSubscriptionAction.Reset => "重置订阅",
            _ => "取消订阅"
        };
        var note = action switch
        {
            MoviePilotSubscriptionAction.Delete => "将取消这条 MoviePilot 订阅，已入库文件保留。",
            MoviePilotSubscriptionAction.Reset => "将重置订阅进度并重新处理，可能触发下载。",
            MoviePilotSubscriptionAction.Search => "将立即搜索这条订阅，并可能自动加入下载。",
            MoviePilotSubscriptionAction.Resume => "恢复后 MoviePilot 将继续处理这条订阅。",
            _ => "暂停后 MoviePilot 将停止处理这条订阅。"
        };
        Working = true;
        var changed = false;
        try
        {
            if (!await ConfirmAsync(verb, subscription.Media.Confirmation + "\n\n" + note, verb)) return;
            if (token.IsCancellationRequested || !Owns(card)) return;
            await _service.ManageSubscriptionAsync(subscription, action, token);
            if (token.IsCancellationRequested || !Owns(card)) return;
            changed = true;
            if (action == MoviePilotSubscriptionAction.Delete)
            {
                Deleted = _detail;
                if (!_detail) { card.Release(); Movies.Remove(card); Series.Remove(card); }
            }
            Notify(verb, action == MoviePilotSubscriptionAction.Search ? "搜索请求已提交，可稍后刷新查看进度。" : "MoviePilot 已确认操作成功。", InfoBarSeverity.Success);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!token.IsCancellationRequested && Live) Report(verb, error); }
        finally { if (!token.IsCancellationRequested) Working = false; }
        if (changed && !Deleted && Live && action != MoviePilotSubscriptionAction.Search) await ReloadAsync();
        NotifyRows();
    }

    internal async Task EditAsync(MoviePilotSubscriptionCard card, Func<MoviePilotSubscription, Task<MoviePilotSubscriptionEdit?>> edit)
    {
        if (!CanManage || !Owns(card) || _service is null || _lifetime is null) return;
        var token = _lifetime.Token;
        Working = true;
        var changed = false;
        try
        {
            var original = await _service.SubscriptionAsync(card.Subscription, token);
            if (token.IsCancellationRequested || !Owns(card)) return;
            var draft = await edit(original);
            if (draft is null || token.IsCancellationRequested || !Owns(card)) return;
            await _service.UpdateSubscriptionAsync(original, draft, token);
            changed = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!token.IsCancellationRequested && Live) Report("保存订阅失败", error); }
        finally { if (!token.IsCancellationRequested) Working = false; }
        if (changed && Live) await ReloadAsync();
    }

    private void ApplyFilter()
    {
        Episodes.Clear();
        foreach (var episode in _episodes.Where(episode => FilterIndex switch { 1 => episode.InLibrary, 2 => !episode.InLibrary, _ => true })) Episodes.Add(episode);
    }

    private async Task LoadPlaybackAsync(MoviePilotSubscription subscription, MoviePilotSubscriptionFiles files,
        CancellationToken load, CancellationToken cancellationToken)
    {
        PlaybackStatus = files.LibraryCount == 0 ? "" : "正在匹配当前 Emby 的播放条目…";
        if (files.LibraryCount == 0) return;
        if (_playbackScope is not { IsCurrent: true } scope || _play is null)
        { PlaybackStatus = "连接对应的 Emby 服务器后即可播放已入库内容。"; return; }
        try
        {
            var targets = await scope.ExecuteAsync((client, token) => MoviePilotLibraryPlayback.FindAsync(client, subscription, files, token), cancellationToken);
            if (!IsCurrent(load) || !Live || !scope.IsCurrent) return;
            foreach (var row in _episodes) row.SetTarget(targets.GetValueOrDefault(row.Episode.Number));
            RefreshCommands();
            PlaybackStatus = $"当前 Emby 可播放 {targets.Count} / {files.LibraryCount} 项";
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        { if (IsCurrent(load) && Live) PlaybackStatus = "播放条目匹配失败，请刷新重试；文件统计仍保留。"; }
    }

    /// <summary>
    /// 每集的播出日期是**附加读取**（见 <see cref="MoviePilotService.SubscriptionAirDatesAsync"/>）：只有 TMDB 来源的
    /// 电视剧才真发一趟。取不到就当没有日期 —— 不因为它把整页的入库统计和播放匹配一起拖红。
    /// </summary>
    private async Task<IReadOnlyDictionary<int, DateTimeOffset>> AirDatesAsync(
        MoviePilotSubscription subscription, CancellationToken cancellationToken)
    {
        if (_service is null) return new Dictionary<int, DateTimeOffset>();
        try { return await _service.SubscriptionAirDatesAsync(subscription, cancellationToken).ConfigureAwait(true); }
        // 连接改变时服务会以取消的形式拒绝旧请求，这一条要往外抛（同页其它读取），不能吞成「没有日期」。
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            Log.Info("moviepilot", $"读取订阅播出日期失败：{error.Message}");
            return new Dictionary<int, DateTimeOffset>();
        }
    }

    /// <summary>某一集的「搜索资源」：用订阅快照里的剧名与季集号拼词，交给外壳开 MoviePilot 资源窗口。</summary>
    private void SearchEpisode(MoviePilotSubscriptionEpisodeRow row)
    {
        if (_search is null || Selected is not { } card || !_episodes.Contains(row)) return;
        _search(MoviePilotVersionQuery.Keyword(card.Subscription.Media.Title, card.Subscription.Media.Season, row.Episode.Number));
    }

    private async Task PlayEpisodeAsync(MoviePilotSubscriptionEpisodeRow row)
    {
        if (!row.CanPlay || !_episodes.Contains(row) || row.Target is not { } target || _play is null || _lifetime is null) return;
        var token = _lifetime.Token;
        Working = true;
        try
        {
            if (!token.IsCancellationRequested && Live && _playbackScope?.IsCurrent == true) await _play(target);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!token.IsCancellationRequested && Live) Report("播放失败", error); }
        finally { if (!token.IsCancellationRequested) Working = false; }
    }

    private void ClearCards()
    {
        foreach (var card in Movies.Concat(Series)) card.Release();
        Movies.Clear(); Series.Clear();
        Selected?.Release();
        NotifyRows();
    }

    private void NotifyRows()
    {
        // Visible 是 Movies/Series 的派生读数（见它的说明），卡片一增一减这里必须一起报 —— 漏掉它的症状是
        // 「数据回来了，板块却不出现」，比闪现更难查。
        OnPropertyChanged(nameof(Visible));
        OnPropertyChanged(nameof(MoviesNote)); OnPropertyChanged(nameof(SeriesNote));
        OnPropertyChanged(nameof(MoviesVisibility)); OnPropertyChanged(nameof(SeriesVisibility));
    }

    [RelayCommand]
    private Task RefreshAsync() => ReloadAsync();

    public override void Cancel()
    {
        _lifetime?.Cancel(); _lifetime?.Dispose(); _lifetime = null;
        _playbackScope = null; _play = null;
        foreach (var row in _episodes) row.Retire();
        _episodes = []; Episodes.Clear();
        Working = false;
        LibraryBadge = "";

        // 离页/换连接时把上一次的报错一起收掉：那条提示条也在 Visible 的判据里（见它），留着就是「返回主页
        // 又弹出一块已经过时的空板块」。这一句放在 ClearCards 前面，收起卡片那一下按的是同一条新判据。
        ClearNotice();
        ClearCards();
        base.Cancel();
        OnPropertyChanged(nameof(CanManage));
    }
}
