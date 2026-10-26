using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Momoka.Diagnostics;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Momoka.Shell.ViewModels;

/// <summary>实际订阅状态与过程状态；未知先读取，结果不明禁止重复写入。</summary>
public enum MoviePilotSubscribeState
{
    Idle,
    Working,
    Done,
    Failed,
    Unknown,
    Uncertain
}

/// <summary>
/// 搜索页 MoviePilot 那一片里的一张卡：包一条 <see cref="MoviePilotMedia"/>，加上海报和那颗订阅键要的东西。
/// <para>
/// 仿 <see cref="Views.CardItem"/>／<see cref="FilterChipRow"/>：Core 的模型是「这是什么」，这里补的只有
/// <c>x:Bind</c> 要的可见性和命令。订阅逻辑不在这儿（它要服务或季设置，都不归一张卡管），而是把动作回调给
/// 视图模型 —— 同 <see cref="FilterChipRow"/> 收一个 <c>drop</c> 委托的做法。
/// </para>
/// </summary>
public sealed partial class MoviePilotResult : ObservableObject
{
    /// <summary>海报解码宽度：卡片画得比这个小，解到这个宽够清楚又不费内存。</summary>
    private const int PosterDecodeWidth = 240;

    private bool _posterFailed;
    private readonly Func<MoviePilotResult, Task> _subscribe;
    private readonly Func<MoviePilotResult, Task> _searchResources;
    private readonly Func<MoviePilotResult, Task>? _review;

    public MoviePilotResult(
        MoviePilotMedia media,
        Func<MoviePilotResult, Task> subscribe,
        Func<MoviePilotResult, Task> searchResources,
        Func<MoviePilotResult, Task>? review = null)
    {
        Media = media;
        _subscribe = subscribe;
        _searchResources = searchResources;
        _review = review;

        // 海报是外站 URL（TMDB/豆瓣的图，或 MoviePilot 代理的），直接交给 BitmapImage 下载 —— 这一处不走
        // EmbyImageStore，那是 Emby 会话域的。只认绝对 http(s)：相对路径或空的就留空，卡片显示占位字形而不是抛。
        if (Uri.TryCreate(media.PosterUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            Poster = new BitmapImage(uri) { DecodePixelWidth = PosterDecodeWidth };
            Poster.ImageFailed += (_, _) =>
            {
                _posterFailed = true;
                OnPropertyChanged(nameof(PosterVisibility));
                OnPropertyChanged(nameof(GlyphVisibility));
            };
        }
    }

    public MoviePilotMedia Media { get; }

    public string Title => Media.Display;
    public string PosterTitle => Media.Title;
    public string RatingText => Media.Rating is > 0 and <= 10 and var rating
        ? rating.ToString("0.#", CultureInfo.InvariantCulture) : "";
    public Visibility RatingVisibility => RatingText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ActionsVisibility => Media.CanSubscribe ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SubscribeActionVisibility => CanSubscribe ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SubscribeStatusVisibility => Media.CanSubscribe && !CanSubscribe ? Visibility.Visible : Visibility.Collapsed;
    public string SubscribeGlyph => State switch
    {
        MoviePilotSubscribeState.Done => "\uEB52",
        MoviePilotSubscribeState.Working => "\uE895",
        MoviePilotSubscribeState.Uncertain => "\uE7BA",
        MoviePilotSubscribeState.Failed => "\uE72C",
        _ => "\uEB51"
    };

    public string TypeLabel => Media.Type ?? "";

    public string SourceLabel => Media.SourceLabel;

    public Visibility SourceVisibility => SourceLabel.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>类型和来源那一行的可见性：两样都没有就不留一行空的。</summary>
    public Visibility IdentityVisibility => SourceLabel.Length > 0 || Media.Type is { Length: > 0 }
        ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TypeVisibility => Media.Type is { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;

    public string Overview => Media.Overview ?? "";

    public Visibility OverviewVisibility =>
        string.IsNullOrWhiteSpace(Media.Overview) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>海报，取不到就是 null；这时显示字形占位（见下面两个可见性）。构造时定好，不再变。</summary>
    public BitmapImage? Poster { get; }

    public Visibility PosterVisibility => Poster is null || _posterFailed ? Visibility.Collapsed : Visibility.Visible;

    public Visibility GlyphVisibility => Poster is null || _posterFailed ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>订阅到哪一步。改了之后那颗键的字、能不能按都跟着变。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubscribeLabel))]
    [NotifyPropertyChangedFor(nameof(CanSubscribe))]
    [NotifyPropertyChangedFor(nameof(SubscribeActionVisibility))]
    [NotifyPropertyChangedFor(nameof(SubscribeStatusVisibility))]
    [NotifyPropertyChangedFor(nameof(SubscribeGlyph))]
    [NotifyPropertyChangedFor(nameof(SubscribeAutomationName))]
    [NotifyPropertyChangedFor(nameof(ReviewVisibility))]
    [NotifyPropertyChangedFor(nameof(CanReview))]
    [NotifyCanExecuteChangedFor(nameof(ReviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(SubscribeCommand))]
    public partial MoviePilotSubscribeState State { get; set; }

    internal MoviePilotSubscription? Subscription { get; set; }
    internal bool ConnectionAvailable { get; private set; } = true;
    internal void RefreshAvailability(bool available)
    {
        ConnectionAvailable = available;
        OnPropertyChanged(nameof(CanSubscribe));
        OnPropertyChanged(nameof(CanSearchResources));
        SubscribeCommand.NotifyCanExecuteChanged();
        SearchResourcesCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanReview));
        ReviewCommand.NotifyCanExecuteChanged();
    }

    /// <summary>已订阅电影再次点击即取消；电视剧始终进入按季设置。未知状态先只读核对。</summary>
    public bool CanSubscribe => ConnectionAvailable && Media.CanSubscribe && State != MoviePilotSubscribeState.Working;

    public string SubscribeLabel => State switch
    {
        MoviePilotSubscribeState.Working => "处理中…",
        MoviePilotSubscribeState.Done => Media.Type == "电影" ? "取消订阅" : "已订阅 · 选择季",
        MoviePilotSubscribeState.Failed => "重试订阅",
        MoviePilotSubscribeState.Unknown => "重新读取状态",
        MoviePilotSubscribeState.Uncertain => Media.Type == "电视剧" ? "核对订阅季" : "请先核对",
        _ => "订阅"
    };

    internal static MoviePilotSubscribeState StateOf(MoviePilotOperationState state) => state switch
    {
        MoviePilotOperationState.Working => MoviePilotSubscribeState.Working,
        MoviePilotOperationState.Submitted => MoviePilotSubscribeState.Done,
        MoviePilotOperationState.Uncertain => MoviePilotSubscribeState.Uncertain,
        _ => MoviePilotSubscribeState.Idle
    };

    /// <summary>读屏软件念出来的那一句：光一个「订阅」念不出订的是哪一部；来源对了才不至于订错库。</summary>
    public string SubscribeAutomationName => SourceLabel.Length > 0
        ? $"{SubscribeLabel}（{SourceLabel}）：{Media.Title}" : $"{SubscribeLabel}：{Media.Title}";

    /// <summary>同上，给「搜索资源」那颗键。</summary>
    public string SearchResourcesAutomationName => SourceLabel.Length > 0
        ? $"搜索资源（{SourceLabel}）：{Media.Title}" : $"搜索资源：{Media.Title}";

    public Visibility ReviewVisibility => Media.Type == "电影" && State == MoviePilotSubscribeState.Uncertain ? Visibility.Visible : Visibility.Collapsed;
    public bool CanReview => ConnectionAvailable && ReviewVisibility == Visibility.Visible && _review is not null;
    [RelayCommand(CanExecute = nameof(CanReview))]
    private Task Review() => _review?.Invoke(this) ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanSubscribe))]
    private Task Subscribe() => _subscribe(this);

    /// <summary>能不能搜资源：和订阅同一个条件（都要身份对齐）。</summary>
    public bool CanSearchResources => ConnectionAvailable && Media.CanSubscribe;

    [RelayCommand(CanExecute = nameof(CanSearchResources))]
    private Task SearchResources() => _searchResources(this);
}

/// <summary>
/// 搜索页里 MoviePilot 那一段的视图模型：一个搜索词进来，去 MoviePilot 找片、把结果铺成卡片，并把每张卡的
/// 「订阅」接到电视剧按季设置或电影直接切换上。
/// <para>
/// 和 Emby 那半（<see cref="LibraryViewModel"/>）分开，不塞进去：两类结果性质不同 —— 那半是库里的
/// <c>EmbyItem</c> 网格（查询/翻页/排序/筛选），这半是「库里还没有、可去订阅」的片子。共用的只有
/// <see cref="PageViewModel"/> 的忙/通知条/载入代次。页面用分段切换在两半之间切显隐。
/// </para>
/// </summary>
public sealed partial class MoviePilotSearchViewModel : PageViewModel
{
    private const string Category = "moviepilot";

    /// <summary>
    /// 还没搜时页面中间那句话。这一来源**不随输入更新**（2026-10-15 用户令：随输入只属于「媒体库」），所以这句
    /// 得把回车说出来 —— 另外两半（资源、AniBT）的空态早就是这么写的。
    /// </summary>
    private const string IdlePrompt = "输入关键字，按回车或点击搜索图标，在 MoviePilot 上找库里还没有的片子";

    /// <summary>来源下拉的第一条哨兵：Id 为空表示不加 media_source，全来源都搜。</summary>
    internal static readonly MoviePilotMediaSource AllSources = new("全部来源", "", []);

    /// <summary>media/source 问不到时的兜底清单 —— 内置影视来源，别让来源筛选整个消失。</summary>
    private static readonly IReadOnlyList<MoviePilotMediaSource> FallbackSources =
    [
        new("TMDB", "themoviedb", ["电影", "电视剧"]),
        new("豆瓣", "douban", ["电影", "电视剧"]),
        new("IMDb", "imdb", ["电影", "电视剧"]),
        new("Bangumi", "bangumi", ["电视剧"]),
        new("AniList", "anilist", ["电视剧"]),
        new("TVDB", "tvdb", ["电视剧"])
    ];

    private MoviePilotService? _service;
    private readonly LoadGeneration _sourceLoads = new();
    private CancellationToken _query;
    private bool _updatingSources;

    /// <summary>上一次搜的词，供 <see cref="ReloadAsync"/> 重跑。</summary>
    private string _lastTerm = "";

    /// <summary>「搜资源」交给视图去开那层覆盖面板（要 XamlRoot），视图模型只管把媒体递出去。</summary>
    private Func<MoviePilotMedia, Task>? _openResources;
    private Func<MoviePilotMedia, CancellationToken, Task>? _openSeasonPicker;
    private readonly HashSet<MoviePilotSubscriptionIdentity> _subscriptionActions = [];

    internal void UseSeasonPicker(Func<MoviePilotMedia, CancellationToken, Task> open) => _openSeasonPicker = open;

    public MoviePilotSearchViewModel()
    {
        EmptyNotice = IdlePrompt;
        ShowEmptyNotice = true;
        Results.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ResultCount));
    }

    public string Heading => _lastTerm.Length == 0 ? "电影、电视剧" : $"电影、电视剧：{_lastTerm}";
    public string ResultCount => Results.Count == 0 ? "" : $"{Results.Count} 个结果";

    public ObservableCollection<MoviePilotResult> Results { get; } = [];

    /// <summary>来源下拉：全部来源打底，其余按 media/source 的目录来（只留影视可用的）。</summary>
    public ObservableCollection<MoviePilotMediaSource> SourceChoices { get; } = [AllSources];

    [ObservableProperty]
    public partial MoviePilotMediaSource SelectedSource { get; set; } = AllSources;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyVisibility))]
    public partial bool ShowEmptyNotice { get; set; }

    public Visibility EmptyVisibility => Show(ShowEmptyNotice);

    [ObservableProperty]
    public partial string EmptyNotice { get; set; }

    private Action<Action>? _dispatch;
    internal void Attach(MoviePilotService service, Action<Action>? dispatch = null)
    {
        if (_service is not null) _service.AvailabilityChanged -= OnAvailabilityChanged;
        _service = service;
        _dispatch = dispatch;
        service.AvailabilityChanged += OnAvailabilityChanged;
    }

    private void OnAvailabilityChanged(object? sender, EventArgs args)
    {
        if (!ReferenceEquals(sender, _service)) return;
        var available = _service?.Available == true;
        var query = _query;
        void Update()
        {
            if (!ReferenceEquals(sender, _service)) return;
            if (!available || _service?.Available != true)
            {
                if (query == _query) { _sourceLoads.Cancel(); base.Cancel(); Results.Clear(); }
                return;
            }
            foreach (var result in Results) result.RefreshAvailability(true);
        }
        if (_dispatch is { } dispatch) dispatch(Update); else Update();
    }

    /// <summary>
    /// 来源目录装进下拉：media/source 现问一遍、只留影视可用的（<see cref="MoviePilotMediaSource.IsVideo"/>）；
    /// 问不到（没连上、接口不在）就退到内置清单。第一项「全部来源」永远在。
    /// </summary>
    internal async Task LoadSourcesAsync()
    {
        var token = _sourceLoads.Begin();
        if (_service is null || !_service.Enabled) return;

        IReadOnlyList<MoviePilotMediaSource> sources;
        try
        {
            sources = await _service.MediaSourcesAsync(token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception error)
        {
            if (!_sourceLoads.IsCurrent(token)) return;
            Log.Warn(Category, "读取 MoviePilot 来源目录失败，退到内置来源", error);
            sources = FallbackSources;
        }
        if (!_sourceLoads.IsCurrent(token)) return;
        var choices = sources.Where(source => source.IsVideo)
            .DistinctBy(source => source.Id, StringComparer.OrdinalIgnoreCase).ToList();

        var selectedId = SelectedSource?.Id ?? "";
        _updatingSources = true;
        try
        {
            // 保留已有来源对象与选中项。Clear 后重建会让 ComboBox 的延迟选择通知把恢复的值再次清空。
            var desired = new[] { AllSources }.Concat(choices).ToArray();
            for (var index = 0; index < desired.Length; index++)
            {
                var existing = SourceChoices.FirstOrDefault(source => source.Id.Equals(desired[index].Id, StringComparison.OrdinalIgnoreCase));
                if (existing is null) SourceChoices.Insert(index, desired[index]);
                else if (SourceChoices.IndexOf(existing) != index) SourceChoices.Move(SourceChoices.IndexOf(existing), index);
            }
            while (SourceChoices.Count > desired.Length) SourceChoices.RemoveAt(SourceChoices.Count - 1);
            SelectedSource = SourceChoices.FirstOrDefault(source => source.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase)) ?? AllSources;
        }
        finally { _updatingSources = false; }
        if (!SelectedSource.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase) && _lastTerm.Length > 0)
            await SearchAsync(_lastTerm).ConfigureAwait(true);
    }

    /// <summary>换来源筛选：有上一次的词就重搜一遍，没有就只记住选择，下次搜索时生效。</summary>
    internal Task ApplySourceFilterAsync() =>
        !_updatingSources && SelectedSource is not null && _lastTerm.Length > 0 ? SearchAsync(_lastTerm) : Task.CompletedTask;

    /// <summary>这次搜索带不带来源筛选：选了具体来源就只搜那一家。</summary>
    private IReadOnlyList<string>? Sources =>
        SelectedSource is { Id: { Length: > 0 } id } ? [id] : null;

    /// <summary>视图交给它的「开资源面板」入口，见 <see cref="SearchResourcesAsync"/>。</summary>
    internal void UseResourceOpener(Func<MoviePilotMedia, Task> open) => _openResources = open;

    /// <summary>外壳刷新会调这一条；这一页的刷新就是把上一次的词重搜一遍。</summary>
    public override Task ReloadAsync() => SearchAsync(_lastTerm);

    /// <summary>搜一个词。空词回到「还没搜」的样子，不发请求。结果整片重填（集合实例保持稳定）。</summary>
    internal async Task SearchAsync(string term)
    {
        if (_service is null) return;

        var keyword = term.Trim();
        _lastTerm = keyword;
        OnPropertyChanged(nameof(Heading));

        if (keyword.Length == 0)
        {
            base.Cancel();
            ClearNotice();
            IsReady = true;
            Results.Clear();
            EmptyNotice = IdlePrompt;
            ShowEmptyNotice = true;
            return;
        }

        var sources = Sources;
        var token = BeginLoad(value => _query = value);
        if (!IsCurrent(token)) return;
        Results.Clear();
        ShowEmptyNotice = false;

        try
        {
            var found = await _service.SearchAsync(keyword, token, sources).ConfigureAwait(true);
            if (!IsCurrent(token) || !_service.Available) return;
            IReadOnlyList<MoviePilotSubscription>? subscriptions = null;
            try { subscriptions = await _service.SubscriptionsAsync(token).ConfigureAwait(true); }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                if (IsCurrent(token)) Report("订阅状态暂不可用", error);
            }
            if (!IsCurrent(token) || !_service.Available) return;

            Results.Clear();
            foreach (var media in found)
            {
                var result = new MoviePilotResult(media, SubscribeAsync, SearchResourcesAsync, ReviewSubscriptionAsync);
                RefreshResult(result, subscriptions);
                Results.Add(result);
            }

            EmptyNotice = found.Count == 0 ? $"MoviePilot 上没搜到「{keyword}」" : IdlePrompt;
            ShowEmptyNotice = found.Count == 0;
            EndLoad(token);
        }
        catch (OperationCanceledException)
        {
            EndLoad(token);
        }
        catch (Exception error)
        {
            if (!IsCurrent(token)) return;

            Log.Warn(Category, $"MoviePilot 搜「{keyword}」失败", error);
            Report("搜索 MoviePilot 失败", error);
            Results.Clear();
            ShowEmptyNotice = false;
            EndLoad(token);
        }
    }

    private void RefreshResult(MoviePilotResult result, IReadOnlyList<MoviePilotSubscription>? subscriptions)
    {
        if (_service is null) return;
        result.RefreshAvailability(_service.Available && _service.IsCurrentConnection(result.Media.ConnectionStamp));
        if (subscriptions is null) { result.Subscription = null; result.State = MoviePilotSubscribeState.Unknown; return; }
        try
        {
            result.Subscription = result.Media.Type == "电影" ? MoviePilotSubscriptionPlan.Find(result.Media with { Season = null }, "", subscriptions) : null;
            var subscribed = result.Media.Type == "电影" ? result.Subscription is not null
                : subscriptions.Any(item => MoviePilotSubscriptionPlan.SameMedia(result.Media, item.Media));
            var operation = _service.SubscribeState(result.Media);
            result.State = operation is MoviePilotOperationState.Uncertain or MoviePilotOperationState.Working
                ? MoviePilotResult.StateOf(operation) : subscribed ? MoviePilotSubscribeState.Done : MoviePilotSubscribeState.Idle;
        }
        catch (MoviePilotException error)
        {
            result.Subscription = null;
            result.State = MoviePilotSubscribeState.Unknown;
            Report("订阅状态需核对", error);
        }
    }

    /// <summary>电视剧开季设置；电影不用第二次确认，按当前明确状态执行一次新增或取消，然后重读。</summary>
    private async Task SubscribeAsync(MoviePilotResult result)
    {
        if (_service is null || !result.CanSubscribe || !IsCurrent(_query) || !_service.Available
            || !_service.IsCurrentConnection(result.Media.ConnectionStamp) || !Results.Contains(result)) return;
        var identity = MoviePilotSubscriptionIdentity.Of(result.Media, "");
        if (!_subscriptionActions.Add(identity)) return;
        var token = _query;
        var previous = result.State;
        var original = result.Subscription;
        result.State = MoviePilotSubscribeState.Working;
        try
        {
            if (result.Media.Type == "电视剧")
            {
                if (_openSeasonPicker is null) throw new MoviePilotOperationBlockedException("订阅季设置尚未就绪");
                await _openSeasonPicker(result.Media, token).ConfigureAwait(true);
            }
            else if (previous == MoviePilotSubscribeState.Unknown)
            {
                // 列表读取失败或重复身份时只核对，不把未知误认成未订阅而发送 POST。
            }
            else if (previous == MoviePilotSubscribeState.Uncertain)
                throw new MoviePilotOperationBlockedException("上次操作结果不明，请先在 MoviePilot 核对订阅与下载任务");
            else
            {
                await _service.ApplyMovieSubscriptionAsync(result.Media, original, token).ConfigureAwait(true);
                if (!IsCurrent(token)) return;
                Notify(null, original is null ? $"已在 MoviePilot 订阅《{result.Media.Title}》" : $"已取消《{result.Media.Title}》的订阅", InfoBarSeverity.Success);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (IsCurrent(token) && Results.Contains(result)) Report($"《{result.Media.Title}》订阅操作未完成", error);
        }
        finally
        {
            try
            {
                if (IsCurrent(token) && _service.Available && _service.IsCurrentConnection(result.Media.ConnectionStamp))
                {
                    IReadOnlyList<MoviePilotSubscription>? subscriptions = null;
                    try { subscriptions = await _service.SubscriptionsAsync(token).ConfigureAwait(true); }
                    catch (OperationCanceledException) { }
                    catch (Exception error) { if (IsCurrent(token)) Report("订阅状态读取失败", error); }
                    if (IsCurrent(token) && _service.Available)
                        foreach (var current in Results.Where(item => MoviePilotSubscriptionPlan.SameMedia(item.Media, result.Media))) RefreshResult(current, subscriptions);
                }
            }
            finally { _subscriptionActions.Remove(identity); }
        }
    }

    /// <summary>用户在独立恢复键明确声明已核对；先读取当前服务器，再解当前电影身份的锁，绝不写入。</summary>
    private async Task ReviewSubscriptionAsync(MoviePilotResult result)
    {
        if (_service is null || !result.CanReview || !IsCurrent(_query) || !_service.Available || !Results.Contains(result)
            || !_service.IsCurrentConnection(result.Media.ConnectionStamp)) return;
        var identity = MoviePilotSubscriptionIdentity.Of(result.Media, "");
        if (!_subscriptionActions.Add(identity)) return;
        var token = _query;
        result.State = MoviePilotSubscribeState.Working;
        try
        {
            var subscriptions = await _service.SubscriptionsAsync(token).ConfigureAwait(true);
            if (!IsCurrent(token) || !_service.Available || !Results.Contains(result)) return;
            var current = MoviePilotSubscriptionPlan.Find(result.Media, "", subscriptions);
            _service.ConfirmSeasonSubscriptionReviewed(result.Media);
            if (current is not null) _service.ConfirmSubscriptionReviewed(current);
            foreach (var row in Results.Where(item => MoviePilotSubscriptionPlan.SameMedia(item.Media, result.Media))) RefreshResult(row, subscriptions);
            Notify(null, "已读取并解除当前电影的待核对状态；未发送订阅或取消请求", InfoBarSeverity.Informational);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (IsCurrent(token) && Results.Contains(result)) { result.State = MoviePilotSubscribeState.Uncertain; Report("订阅核对未完成", error); }
        }
        finally { _subscriptionActions.Remove(identity); }
    }

    public override void Cancel()
    {
        _sourceLoads.Cancel();
        base.Cancel();
    }

    public override void Dispose()
    {
        if (_service is not null) _service.AvailabilityChanged -= OnAvailabilityChanged;
        _sourceLoads.Dispose();
        base.Dispose();
    }

    /// <summary>一张卡的「搜索资源」按下去：交给视图去开资源覆盖面板（那要 XamlRoot），视图模型只把媒体递出去。</summary>
    private Task SearchResourcesAsync(MoviePilotResult result) =>
        _openResources?.Invoke(result.Media) ?? Task.CompletedTask;
}
