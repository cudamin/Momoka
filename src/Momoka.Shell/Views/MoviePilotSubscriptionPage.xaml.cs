using Momoka.AniBt;
using Momoka.MoviePilot;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Momoka.Shell.Platform;
using Windows.UI;

namespace Momoka.Shell.Views;

internal sealed record MoviePilotSubscriptionRequest(MoviePilotService Service, MoviePilotSubscription? Subscription,
    EmbySessionScope? PlaybackScope = null, Func<MoviePilotPlaybackTarget, Task>? Play = null, Action<string>? SearchVersions = null,
    ISystemLauncher? Launcher = null, Action<MoviePilotMedia>? OpenMedia = null, MoviePilotMedia? Media = null,
    Action<TitleStrip>? SetTitleStrip = null, Action<AniBtSearchQuery>? SearchAniBt = null);

public sealed partial class MoviePilotSubscriptionPage : Page, IShellContent
{
    /// <summary>资料卡并排时的宽度。</summary>
    private const double FactsSideWidth = 320;

    /// <summary>资料卡叠起来时的最宽：再宽下去标签和值会被拉开成两页纸。</summary>
    private const double FactsStackedMaxWidth = 520;

    /// <summary>
    /// 资料卡并排所需的最小内容宽度。左边那一格要放得下海报、片名与那一排操作键，再加卡片自己和中间
    /// 那道 24 的间隔；低于它就让整张卡落到头部下面，宽度铺满内容区。
    /// <para>
    /// 这一处没用 <c>AdaptiveTrigger</c>：探针实测本页的自适应状态在窗口收窄后没有生效（状态名读回为空），
    /// 而这里的判据本来就该按**内容区**宽度算（页面四边的内缩要先减掉），按窗口宽度反而要另配一个补偿数。
    /// 纯摆位，留在 code-behind。
    /// </para>
    /// </summary>
    private const double FactsSideBreakpoint = 960;

    /// <summary>叠起来时两块之间的纵向间隔，就是页面的主间距（Theme/Styles.xaml 的 EgSpaceXL）。</summary>
    private const double FactsStackedRowSpacing = 24;

    /// <summary>
    /// 头部封面的尺寸，照电影详情页那张竖版封面（<c>DetailViewModel.PosterStillWidth</c>／
    /// <c>PosterStillHeight</c>，210×300）——用户令 2026-10-12：「参考电影页面封面的大小和位置修改订阅
    /// 页面的封面」。紧凑版式按同一比例收小一档，位置仍是头部左侧、与片名同排、顶对齐。
    /// </summary>
    private const double PosterWidth = 210;
    private const double PosterHeight = 300;
    private const double CompactPosterWidth = 140;
    private const double CompactPosterHeight = 200;

    private MoviePilotSubscriptionRequest? _request;
    private CancellationTokenSource? _pageLifetime;
    internal MoviePilotSubscriptionsViewModel ViewModel { get; } = new();
    internal MoviePilotMediaViewModel MediaViewModel { get; } = new();
    public Visibility SubscriptionVisibility => SubscriptionSectionVisibility(ViewModel.Deleted);
    public Visibility SubscriptionSectionVisibility(bool deleted) => _request?.Subscription is not null && !deleted ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// 这一页不画加载条（用户令 2026-10-12：「不要显示那个加载条」）。原先这里有一对
    /// <c>BusyVisibility</c> / <c>RefreshBusy</c> 驱动头部下面那颗 <c>ProgressBar</c>（订阅与媒体详情
    /// 两份数据谁在读取都算页面在读取）—— 它落在「简介」与「演职员」之间那块空地方，看着像那一块在
    /// 加载东西。整条撤掉：本页不再有进度线，两份数据的加载态仍各自住在自己的视图模型里，由头部与
    /// 各段自己的说明交代（入库徽标、文件统计那句「正在读取文件统计…」、季信息那句），失败走 InfoBar。
    /// </summary>
    public MoviePilotSubscriptionPage()
    {
        InitializeComponent();
        PaintBackdropFade();
        MediaViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MediaViewModel.Backdrop))
                _request?.SetTitleStrip?.Invoke(MediaViewModel.Backdrop is null ? TitleStrip.Plain : TitleStrip.OnScrim);
        };
    }

    /// <summary>
    /// 图下沿那道黑色渐变（用户令 2026-10-13「下方组件背景也参考电影页面做下滑遮罩」）—— 同电影详情页
    /// <c>DetailPage.BackdropFade</c> 那一支：先爬后收，最后一段压到满墨，藏住画面被截断的那条硬边。
    /// <para>
    /// 在构造里画而不是在标记里写四个色值：这一页此前一处硬编码颜色都没有，写进去会往「Shell XAML 里的
    /// 硬编码颜色只减不增」那条基线上加四行。停点与电影页逐档相同，色由 Core 那支墨给
    /// （<see cref="DetailHero.ScrimInk"/>），所以两页的渐变是同一个色。
    /// </para>
    /// <para>
    /// 不跟主题走：它压的是一张剧照，剧照不会因为用户挑了浅色主题就变亮（同电影页那一支，理由写在
    /// <c>DetailHero.ScrimInk</c> 上）。
    /// </para>
    /// </summary>
    private void PaintBackdropFade()
    {
        var ink = DetailHero.ScrimInk;
        BackdropFadeBrush.GradientStops.Clear();
        foreach (var (along, alpha) in BackdropFadeStops)
            BackdropFadeBrush.GradientStops.Add(new GradientStop
            {
                Offset = along,
                Color = Color.FromArgb(alpha, ink.R, ink.G, ink.B)
            });
    }

    /// <summary>图下沿那道渐变的停点，与 <c>DetailPage.xaml</c> 里 BackdropFade 那一支逐档相同。</summary>
    private static readonly (double Along, byte Alpha)[] BackdropFadeStops =
        [(0, 0x00), (0.42, 0xA6), (0.78, 0xE8), (1, 0xFF)];

    public object? NavigationRequest => _request is { Subscription: { } subscription } request
        ? request with { Subscription = ViewModel.Deleted ? null : ViewModel.Selected?.Subscription ?? subscription, Media = subscription.Media }
        : _request;

    private void OnDetailLoaded(object sender, RoutedEventArgs args) => LiftBackdrop();
    private void OnDetailSizeChanged(object sender, SizeChangedEventArgs args) => LiftBackdrop();

    /// <summary>
    /// 这一页看得见的那一段有多宽、多高 —— 背景那张画多高（<c>MoviePilotMediaViewModel.BackdropHeight</c>）
    /// 和头部那一段让开多少（<c>HeroMinHeight</c>）都问它。只有布好的滚动视图知道自己的视口，怎么用这个数
    /// 是 Core 那两支纯函数的事，不在这个处理器里。
    /// <para>
    /// 0 不写回去：这一层在资源面板打开时是收着的（<c>OpenResources</c> 把 <c>DetailScroll</c> 收起来），
    /// 收着的元素量出来是 0，写进去会让背景图当场消失；面板关掉时再量一次就对了。
    /// </para>
    /// </summary>
    private void OnDetailScrollSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (args.NewSize.Width > 0) MediaViewModel.PageWidth = args.NewSize.Width;
        if (args.NewSize.Height > 0) MediaViewModel.Viewport = args.NewSize.Height;
    }

    /// <summary>
    /// 头部那一叠（封面、片名、操作、资料卡、简介）本来多高 —— 背景那张至少垫住它，否则头部下半截会落到
    /// 图外面那段页底色上。量的是让开高度那一层里面这一叠，所以「让开多少」不会回头改这个数。
    /// </summary>
    private void OnHeroContentSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (args.NewSize.Height > 0) MediaViewModel.HeroBandHeight = args.NewSize.Height;
    }

    private void LiftBackdrop()
    {
        if (XamlRoot?.Content is not UIElement root) return;
        var lift = Math.Max(0, TransformToVisual(root).TransformPoint(default).Y);

        // 头部那一段让开的高度是「图的下沿折进滚动内容坐标」，所以要减掉这一截（赶在提前返回之前写：
        // 换主题、面包屑显隐都会挪它，而那些时候边距未必变）。
        MediaViewModel.HeaderLift = lift;

        if (Math.Abs(DetailBackdropRoot.Margin.Top + lift) > 0.5)
            DetailBackdropRoot.Margin = new Thickness(0, -lift, 0, 0);
    }

    /// <summary>
    /// 头部那一行的资料卡怎么摆：内容区够宽就并排在海报与片名的右侧（页面右上角），不够就落到头部下面。
    /// </summary>
    private void OnHeroRowSizeChanged(object sender, SizeChangedEventArgs args) => ApplyHeroLayout(args.NewSize.Width);

    private void ApplyHeroLayout(double width)
    {
        var side = width >= FactsSideBreakpoint;
        Grid.SetColumn(HeroFacts, side ? 1 : 0);
        Grid.SetRow(HeroFacts, side ? 0 : 1);
        HeroFactColumn.Width = side ? new GridLength(FactsSideWidth) : new GridLength(0);
        HeroRow.RowSpacing = side ? 0 : FactsStackedRowSpacing;
        HeroFacts.Width = side ? FactsSideWidth : Math.Min(FactsStackedMaxWidth, width);
    }

    private void OnHeroSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var compact = args.NewSize.Width < 580;
        Grid.SetRow(HeroContent, compact ? 1 : 0);
        Grid.SetColumn(HeroContent, compact ? 0 : 1);
        Grid.SetColumnSpan(HeroContent, compact ? 2 : 1);
        DetailPoster.Width = compact ? CompactPosterWidth : PosterWidth;
        DetailPoster.Height = compact ? CompactPosterHeight : PosterHeight;
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
        // 只有订阅详情才接这一颗；从搜索/推荐打开的纯媒体页没有订阅，也没有可交给 AniBT 的那条路。
        MediaViewModel.AniBtSearch = request.Subscription is not null && request.SearchAniBt is not null;
        MediaViewModel.OpenMedia = request.OpenMedia;
        MediaViewModel.OpenPerson = url => request.Launcher?.OpenUrl(url);
        // 订阅详情才把订阅视图模型交给季列表：匹配季号的折叠栏改装文件统计；纯媒体详情的季浏览照旧。
        MediaSeasons.Attach(MediaViewModel, request.Subscription is null ? null : ViewModel);
        Bindings.Update();
        // 尺寸没变时 SizeChanged 不会再发一次，这里按当前实际宽度补算一遍。尚未排版时读到 0，
        // 随后的 SizeChanged 会把它纠正过来。
        ApplyHeroLayout(HeroRow.ActualWidth);
        if (request.Subscription is not null) _ = ViewModel.ReloadAsync();
        _ = MediaViewModel.ReloadAsync();
    }

    /// <summary>
    /// 操作排右端那颗「更多」（用户令 2026-10-12：「只留这四个按钮 + 一个编辑订阅按钮，其他的折叠到更多按钮
    /// （参考其他页新增）里」「复刻电影页面的更多按钮」）。收进去的是排上原来那四颗 —— 暂停／恢复订阅、重置
    /// 订阅、管理订阅、刷新 —— 加上封面更多键上的编辑、搜索与取消订阅：文案、显隐、可用性与动作逐条沿用它们
    /// 原来的那几条，只是从一排键换成菜单项。
    /// <para>
    /// **这一层不再折叠**（用户令 2026-10-12：「订阅页面的更多按钮不要折叠，去掉重复的选项」，随后指明说的是
    /// 「管理订阅」那一条）：原先「管理订阅」是一个 <c>MenuFlyoutSubItem</c>，摊开之后里面还有一条「暂停」一条
    /// 「重置」与外面那两条是同一个动作，而「媒体详情」在这页上与「文件统计」同去一处（都滚到订阅季的折叠栏）。
    /// 现在整组摊平成一层：暂停／恢复订阅、重置订阅、编辑、搜索、文件统计（只电视剧）、刷新，末尾一条分隔线后
    /// 是取消订阅 —— 重复的两条去掉，「媒体详情」由落点准确的「文件统计」替掉（电影上它本来无处可去）。
    /// </para>
    /// <para>
    /// 摊平之后这一份与封面更多键上的那一份（<see cref="MoviePilotSubscriptionMenu.Fill"/>）不再是同一组项：
    /// 封面那边点「媒体详情」是打开这一页，这儿是滚到折叠栏，两处的落点本来就不同，不再共用一个集合。
    /// </para>
    /// <para>
    /// 菜单现开现造：暂停那颗的文案跟着当前订阅状态走（「暂停订阅」／「恢复订阅」），可用性跟着
    /// <see cref="MoviePilotSubscriptionsViewModel.CanManage"/>，两者都会在页面停留期间变；菜单是一次性的
    /// 浮层，没必要挂一套绑定去追它。「刷新」什么时候都在 —— 纯媒体详情（没有订阅）也要靠它重读。
    /// </para>
    /// <para>
    /// 单独留一个方法给探针走：那颗键点下去弹出来的就是这一份，探针据此断言收起来的是哪几项，不必去按浮层。
    /// </para>
    /// </summary>
    internal MenuFlyout BuildMoreMenu()
    {
        var menu = new MenuFlyout();
        MoviePilotSubscriptionMenu.Watch(menu, _request?.Service);
        var manage = ViewModel.CanWriteSelected;
        if (SubscriptionVisibility == Visibility.Visible)
        {
            if (ViewModel.Selected is { } selected && ViewModel.NeedsReview(selected))
                Add("核对后继续", "\uE946", ViewModel.CanManage, OnReviewSubscription);
            Add(ViewModel.DetailPauseLabel, ViewModel.DetailPaused ? "\uE768" : "\uE769", manage, OnTogglePause);
            Add("重置订阅", "\uE777", manage, OnReset);
            Add("编辑", "\uE70F", manage, OnEdit);
            Add("搜索", "\uE721", manage, OnSearchSubscription);
            // 「文件统计」只在电视剧上有落点：它滚到订阅季折叠栏里那份统计。电影那块独立面板已按用户令
            // （2026-10-12）删掉，这一项在电影上无处可去，跟着一起收起（不是置灰）。
            if (MediaSeasons.HasSubscriptionFiles) Add("文件统计", "\uE9F9", true, OnOpenFilesSection);
            Add("刷新", "\uE72C", true, OnRefreshDetails);
            menu.Items.Add(new MenuFlyoutSeparator());
            Add("取消订阅", "\uE74D", manage, OnCancelSubscription);
        }
        else Add("刷新", "\uE72C", true, OnRefreshDetails);
        return menu;

        void Add(string text, string glyph, bool enabled, RoutedEventHandler handler)
        {
            var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph }, IsEnabled = enabled };
            // 句柄跟封面更多键上那一份同一个写法：这两处从前共用 Fill，摊平之后各自建项，读屏名不能跟着丢。
            AutomationProperties.SetAutomationId(item, "Subscription" + text);
            item.Click += handler;
            menu.Items.Add(item);
        }
    }

    private void OnMoreActions(object sender, RoutedEventArgs args)
    {
        if (sender is Button anchor) BuildMoreMenu().ShowAt(anchor);
    }

    /// <summary>「更多」菜单的「文件统计」：展开订阅季并滚到它的折叠栏。菜单只在电视剧上给这一项 ——
    /// 电影那块独立面板已按用户令（2026-10-12）删掉，没有可滚过去的地方。</summary>
    private void OpenFilesSection() => MediaSeasons.ShowSubscriptionFiles();

    private void OnOpenFilesSection(object sender, RoutedEventArgs args) => OpenFilesSection();

    private void OnEdit(object sender, RoutedEventArgs args)
    {
        if (ViewModel.Selected is { } card)
            _ = ViewModel.EditAsync(card, subscription => MoviePilotSubscriptionEditor.ShowAsync(this, subscription, _request?.Service));
    }

    /// <summary>「更多」菜单的「搜索」：立即搜这条订阅（服务端动作，可能自动加入下载，有确认）。
    /// 它与排上那颗「搜索资源」不是一回事 —— 那颗只读地打开资源窗口，不会触发订阅搜索。</summary>
    private void OnSearchSubscription(object sender, RoutedEventArgs args)
    {
        _ = ManageSubscriptionAsync(MoviePilotSubscriptionAction.Search);
    }

    /// <summary>「更多」菜单的「取消订阅」：与封面更多键上那一项同一个动作，保留已入库文件。</summary>
    private void OnCancelSubscription(object sender, RoutedEventArgs args)
    {
        _ = ManageSubscriptionAsync(MoviePilotSubscriptionAction.Delete);
    }

    private void OnReviewSubscription(object sender, RoutedEventArgs args)
    {
        if (ViewModel.Selected is { } card) _ = ViewModel.ReviewAsync(card);
    }

    internal async Task ManageSubscriptionAsync(MoviePilotSubscriptionAction action)
    {
        if (_request is not { } request || ViewModel.Selected is not { } card) return;
        if (await ViewModel.ManageAsync(card, action) && ReferenceEquals(_request, request)
            && action == MoviePilotSubscriptionAction.Delete) await RefreshSubscriptionStateAsync();
    }

    internal async Task RefreshSubscriptionStateAsync()
    {
        if (_request is not { } request) return;
        try
        {
            await MediaViewModel.ReloadAsync();
            if (ReferenceEquals(_request, request)) await ViewModel.RefreshAfterMediaChangesAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (ReferenceEquals(_request, request)) MediaViewModel.OperationFailed(error); }
    }

    private void OnSearch(object sender, RoutedEventArgs args)
    {
        OpenResources(null, null);
    }

    /// <summary>
    /// 订阅详情里的番剧名交给外壳的 AniBT 窗口。这里不判断媒体类型 —— 是否显示这颗键由
    /// <see cref="MoviePilotMediaViewModel.AniBtVisibility"/> 决定，执行前再核对一次详情身份。
    /// </summary>
    private void OnSearchAniBt(object sender, RoutedEventArgs args)
    {
        if (_request is not { SearchAniBt: { } search, Subscription: not null } || MediaViewModel.Detail is not { } detail) return;
        search(new AniBtSearchQuery(detail.Media.Title, null));
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
            if (detail.Media.Type == "电影")
            {
                var subscriptions = await request.Service.SubscriptionsAsync(_pageLifetime!.Token);
                if (!ReferenceEquals(_request, request) || !MediaViewModel.CanInteract) return;
                var current = MoviePilotSubscriptionPlan.Find(detail.Media with { Season = null }, "", subscriptions);
                await request.Service.ApplyMovieSubscriptionAsync(detail.Media, current, _pageLifetime.Token);
            }
            else await MoviePilotSeasonPicker.ShowAsync(this, request.Service, detail.Media, MediaViewModel.GroupId, season, _pageLifetime!.Token);
            if (!ReferenceEquals(_request, request)) return;
            await RefreshSubscriptionStateAsync();
        }
        catch (Exception error)
        {
            if (!ReferenceEquals(_request, request)) return;
            if (request.Service.Available)
            {
                try { await RefreshSubscriptionStateAsync(); }
                catch (OperationCanceledException) { }
                catch (Exception) { /* 保留原失败；未取得新状态时不冒充成功。 */ }
            }
            if (ReferenceEquals(_request, request)) MediaViewModel.OperationFailed(error);
        }
        finally { _choosingSeasons = false; }
    }

    private void OnRefreshDetails(object sender, RoutedEventArgs args)
    {
        _ = RefreshSubscriptionStateAsync();
    }

    /// <summary>「更多」菜单里的暂停那一项：同一个动作按当前状态换说法，暂停过的订阅上它说的是「恢复订阅」
    /// （见 <c>DetailPauseLabel</c>，菜单项的文案就在 <see cref="BuildMoreMenu"/> 里现取）。</summary>
    private void OnTogglePause(object sender, RoutedEventArgs args)
    {
        if (ViewModel.Selected is { } card)
            _ = ManageSubscriptionAsync(card.Subscription.Paused ? MoviePilotSubscriptionAction.Resume : MoviePilotSubscriptionAction.Pause);
    }

    private void OnReset(object sender, RoutedEventArgs args)
    {
        _ = ManageSubscriptionAsync(MoviePilotSubscriptionAction.Reset);
    }

    public void Release()
    {
        _request?.SetTitleStrip?.Invoke(TitleStrip.Plain);
        _pageLifetime?.Cancel(); _pageLifetime?.Dispose(); _pageLifetime = null;
        _request = null; ViewModel.Cancel(); MediaViewModel.Cancel(); MediaResources.Release();
    }
    protected override void OnNavigatedFrom(NavigationEventArgs e) { Release(); base.OnNavigatedFrom(e); }
}
