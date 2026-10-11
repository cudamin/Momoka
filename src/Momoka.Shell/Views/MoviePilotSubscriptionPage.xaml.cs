using Momoka.MoviePilot;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Momoka.Shell.Platform;

namespace Momoka.Shell.Views;

internal sealed record MoviePilotSubscriptionRequest(MoviePilotService Service, MoviePilotSubscription? Subscription,
    EmbySessionScope? PlaybackScope = null, Func<MoviePilotPlaybackTarget, Task>? Play = null, Action<string>? SearchVersions = null,
    ISystemLauncher? Launcher = null, Action<MoviePilotMedia>? OpenMedia = null, MoviePilotMedia? Media = null,
    Action<TitleStrip>? SetTitleStrip = null);

public sealed partial class MoviePilotSubscriptionPage : Page, IShellContent
{
    /// <summary>右栏并排时的宽度。</summary>
    private const double FactsSideWidth = 320;

    /// <summary>右栏叠起来时的最宽：再宽下去标签和值会被拉开成两页纸。</summary>
    private const double FactsStackedMaxWidth = 520;

    /// <summary>
    /// 并排所需的最小内容宽度。左栏要放得下集列表那一行（横版剧照 300 + 集名与剧情 + 播放键），
    /// 再加右栏和中间那道 24 的间隔；低于它就让右栏整块落到下面去。
    /// </summary>
    private const double FactsSideBreakpoint = 960;

    /// <summary>叠起来时两块之间的纵向间隔，就是页面的主间距（Theme/Styles.xaml 的 EgSpaceXL）。</summary>
    private const double FactsStackedRowSpacing = 24;

    private MoviePilotSubscriptionRequest? _request;
    private CancellationTokenSource? _pageLifetime;
    internal MoviePilotSubscriptionsViewModel ViewModel { get; } = new();
    internal MoviePilotMediaViewModel MediaViewModel { get; } = new();
    public Visibility SubscriptionVisibility => _request?.Subscription is not null ? Visibility.Visible : Visibility.Collapsed;
    public MoviePilotSubscriptionPage()
    {
        InitializeComponent();
        MediaViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MediaViewModel.Backdrop))
                _request?.SetTitleStrip?.Invoke(MediaViewModel.Backdrop is null ? TitleStrip.Plain : TitleStrip.OnScrim);
        };
    }
    public object? NavigationRequest => _request;

    private void OnDetailLoaded(object sender, RoutedEventArgs args) => LiftBackdrop();
    private void OnDetailSizeChanged(object sender, SizeChangedEventArgs args) => LiftBackdrop();
    private void LiftBackdrop()
    {
        if (XamlRoot?.Content is not UIElement root) return;
        var lift = Math.Max(0, TransformToVisual(root).TransformPoint(default).Y);
        if (Math.Abs(DetailBackdropRoot.Margin.Top + lift) > 0.5)
            DetailBackdropRoot.Margin = new Thickness(0, -lift, 0, 0);
    }

    /// <summary>
    /// 「简介」和「订阅信息」怎么摆：内容区够宽就并排，不够就把右栏整块落到简介下面。
    /// <para>
    /// 这一处没用 <c>AdaptiveTrigger</c>：探针实测本页的自适应状态在窗口收窄后没有生效（状态名读回为空），
    /// 而这里的判据本来就该按**内容区**宽度算（页面四边的内缩要先减掉），按窗口宽度反而要另配一个补偿数。
    /// 纯摆位，留在 code-behind。
    /// </para>
    /// </summary>
    private void OnOverviewSizeChanged(object sender, SizeChangedEventArgs args) => ApplyOverviewLayout(args.NewSize.Width);

    private void ApplyOverviewLayout(double width)
    {
        var stacked = width < FactsSideBreakpoint;
        Grid.SetColumn(DetailFacts, stacked ? 0 : 1);
        Grid.SetRow(DetailFacts, stacked ? 1 : 0);
        FactColumn.Width = stacked ? new GridLength(0) : new GridLength(FactsSideWidth);
        OverviewGrid.RowSpacing = stacked ? FactsStackedRowSpacing : 0;
        DetailFacts.Width = stacked ? Math.Min(FactsStackedMaxWidth, width) : FactsSideWidth;
    }

    private void OnHeroSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var compact = args.NewSize.Width < 580;
        Grid.SetRow(HeroContent, compact ? 1 : 0);
        Grid.SetColumn(HeroContent, compact ? 0 : 1);
        Grid.SetColumnSpan(HeroContent, compact ? 2 : 1);
        DetailPoster.Width = compact ? 120 : 170;
        DetailPoster.Height = compact ? 180 : 255;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is MoviePilotSubscriptionRequest request) Attach(request);
    }

    internal void Attach(MoviePilotSubscriptionRequest request)
    {
        _pageLifetime?.Cancel(); _pageLifetime?.Dispose(); _pageLifetime = new();
        _request = request;
        var media = request.Subscription?.Media ?? request.Media;
        if (media is null) return;
        if (request.Subscription is not null) ViewModel.Attach(request.Service, request.Subscription);
        ViewModel.AttachPlayback(request.PlaybackScope, request.Play);
        ViewModel.AttachSearch(request.SearchVersions);
        ViewModel.UseConfirm(ConfirmDialog.For(this, dialog => MoviePilotDialog.ShowAsync(dialog, request.Service)));
        MediaViewModel.Attach(request.Service, media, request.Subscription?.EpisodeGroup ?? "",
            search: (season, episode) => OpenResources(season, episode), subscribe: season => _ = ChooseSeasonsAsync(season));
        MediaViewModel.OpenMedia = request.OpenMedia;
        MediaViewModel.OpenPerson = url => request.Launcher?.OpenUrl(url);
        MediaSeasons.Attach(MediaViewModel);
        Bindings.Update();
        if (request.Subscription is not null) _ = ViewModel.ReloadAsync();
        _ = MediaViewModel.ReloadAsync();
    }

    private void OnManage(object sender, RoutedEventArgs args)
    {
        if (ViewModel.Selected is { } card)
            MoviePilotSubscriptionMenu.Create(this, ViewModel, card, () => FilesHeading.StartBringIntoView(), _request?.Service).ShowAt(ManageSubscription);
    }

    private void OnEdit(object sender, RoutedEventArgs args)
    {
        if (ViewModel.Selected is { } card)
            _ = ViewModel.EditAsync(card, subscription => MoviePilotSubscriptionEditor.ShowAsync(this, subscription, _request?.Service));
    }

    private void OnSearch(object sender, RoutedEventArgs args)
    {
        OpenResources(null, null);
    }

    private void OnSearchImdb(object sender, RoutedEventArgs args) => OpenResources(null, null, "imdbid");
    private void OnSearchSubtitles(object sender, RoutedEventArgs args) => OpenResources(null, null, subtitles: true);

    private void OpenResources(int? season, int? episode, string area = "title", bool subtitles = false)
    {
        if (_request is not { } request || !MediaViewModel.CanInteract || MediaViewModel.Detail is not { } detail) return;
        if (request.Launcher is not { } launcher)
        {
            request.SearchVersions?.Invoke(MoviePilotVersionQuery.Keyword(detail.Media.Title, season, episode));
            return;
        }
        MediaResources.Open(request.Service, detail.Media with { Season = season }, launcher, new(area, episode, subtitles));
        DetailScroll.Visibility = Visibility.Collapsed;
        MediaResources.Visibility = Visibility.Visible;
    }

    private void OnResourcesClosed(object? sender, EventArgs args)
    {
        MediaResources.Release();
        MediaResources.Visibility = Visibility.Collapsed;
        DetailScroll.Visibility = Visibility.Visible;
    }

    private void OnChooseSeasons(object sender, RoutedEventArgs args) => _ = ChooseSeasonsAsync(null);
    private bool _choosingSeasons;
    private async Task ChooseSeasonsAsync(int? season)
    {
        if (_choosingSeasons || _request is not { } request || !MediaViewModel.CanInteract || MediaViewModel.Detail is not { } detail) return;
        _choosingSeasons = true;
        try
        {
            await MoviePilotSeasonPicker.ShowAsync(this, request.Service, detail.Media, MediaViewModel.GroupId, season, _pageLifetime!.Token);
            if (!ReferenceEquals(_request, request)) return;
            await MediaViewModel.ReloadAsync();
            await ViewModel.RefreshAfterMediaChangesAsync();
        }
        catch (Exception error) { if (_request is not null) MediaViewModel.OperationFailed(error); }
        finally { _choosingSeasons = false; }
    }

    private void OnRefreshDetails(object sender, RoutedEventArgs args)
    {
        _ = ViewModel.ReloadAsync();
        _ = MediaViewModel.ReloadAsync();
    }

    /// <summary>同一颗键按当前状态换动作：暂停过的订阅上它说的是「恢复订阅」（见 <c>DetailPauseLabel</c>）。</summary>
    private void OnTogglePause(object sender, RoutedEventArgs args)
    {
        if (ViewModel.Selected is { } card)
            _ = ViewModel.ManageAsync(card, card.Subscription.Paused ? MoviePilotSubscriptionAction.Resume : MoviePilotSubscriptionAction.Pause);
    }

    private void OnReset(object sender, RoutedEventArgs args)
    {
        if (ViewModel.Selected is { } card) _ = ViewModel.ManageAsync(card, MoviePilotSubscriptionAction.Reset);
    }

    private void OnEpisodeLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: MoviePilotSubscriptionEpisodeRow row }) _ = ViewModel.LoadEpisodeArtworkAsync(row);
    }

    public void Release()
    {
        _request?.SetTitleStrip?.Invoke(TitleStrip.Plain);
        _pageLifetime?.Cancel(); _pageLifetime?.Dispose(); _pageLifetime = null;
        _request = null; ViewModel.Cancel(); MediaViewModel.Cancel(); MediaResources.Release();
    }
    protected override void OnNavigatedFrom(NavigationEventArgs e) { Release(); base.OnNavigatedFrom(e); }
}
