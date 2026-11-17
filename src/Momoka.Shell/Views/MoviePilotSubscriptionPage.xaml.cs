using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Momoka.AniBt;
using Momoka.Configuration;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Services;
using Momoka.Shell.Platform;
using Momoka.Shell.ViewModels;
using Momoka.Theming;
using Windows.UI;

namespace Momoka.Shell.Views;

internal sealed record MoviePilotSubscriptionRequest(MoviePilotService Service, MoviePilotSubscription? Subscription,
    EmbySessionScope? PlaybackScope = null, Func<MoviePilotPlaybackTarget, Task>? Play = null, Action<string>? SearchVersions = null,
    ISystemLauncher? Launcher = null, Action<MoviePilotMedia>? OpenMedia = null, MoviePilotMedia? Media = null,
    Action<TitleStrip>? SetTitleStrip = null, Action<AniBtSearchQuery>? SearchAniBt = null,
    Action<MoviePilotMedia>? Subscribed = null, ISettingsService? Settings = null,
    Func<EmbyItem, Task>? PlayTrailer = null, Momoka.Infrastructure.AppPaths? Paths = null,
    Momoka.Shell.Media.YouTubeSession? YouTube = null, Action? YouTubeLogin = null);

public sealed partial class MoviePilotSubscriptionPage : Page, IShellContent
{
    private const double FactsSideWidth = 288;
    private const double FactsColumnSpacing = 24;
    private const double FactsStackedMaxWidth = 520;
    private const double FactsSideBreakpoint = 960;

    /// <summary>
    /// 订阅详情封面适度放大，保持原来的 7:10 比例；紧凑版式收小一档，给完整简介留出宽度。
    /// </summary>
    private const double PosterWidth = 238;
    private const double PosterHeight = 340;
    private const double CompactPosterWidth = 154;
    private const double CompactPosterHeight = 220;

    private MoviePilotSubscriptionRequest? _request;
    private CancellationTokenSource? _pageLifetime;
    private bool _appearanceObserved;
    private SurfaceMotion? _detailMotion;
    private SurfaceMotion? _resourcesMotion;
    private bool _resourcesOpen;

    private SurfaceMotion DetailMotion => _detailMotion ??= new SurfaceMotion(DetailScroll);
    private SurfaceMotion ResourcesMotion => _resourcesMotion ??= new SurfaceMotion(MediaResources);

    /// <summary>上一次算正文两列时 BodyRow 的宽；只按宽度重算，避免高度变化回灌成布局回路。</summary>
    private double _bodyLayoutWidth = double.NaN;
    internal MoviePilotSubscriptionsViewModel ViewModel { get; } = new();
    internal MoviePilotMediaViewModel MediaViewModel { get; } = new();
    public Visibility SubscriptionVisibility => SubscriptionSectionVisibility(ViewModel.Deleted);
    public Visibility SubscriptionSectionVisibility(bool deleted) => _request?.Subscription is not null && !deleted ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// 这一页不画细进度条（用户令 2026-10-12：「不要显示那个加载条」）。原先这里有一对
    /// <c>BusyVisibility</c> / <c>RefreshBusy</c> 驱动头部下面那颗 <c>ProgressBar</c>（订阅与媒体详情
    /// 两份数据谁在读取都算页面在读取）—— 它落在「简介」与「演职员」之间那块空地方，看着像那一块在
    /// 加载东西。整条撤掉：本页不再有进度线，两份数据的加载态仍各自住在自己的视图模型里，由头部与
    /// 各段自己的说明交代（入库徽标、文件统计那句「正在读取文件统计…」、季信息那句），失败走 InfoBar。
    /// 2026-10-28 起首次读取媒体详情时页面另盖一层带流光的骨架占位（<c>DetailLoading</c>），判据在
    /// <see cref="MoviePilotMediaViewModel.InitialLoading"/>，读完即收起。
    /// </summary>
    public MoviePilotSubscriptionPage()
    {
        InitializeComponent();
        PaintBackdropFade();
        PrepareShowcase();
        PrepareActionAppearance();
        WatchTrailerPreferences();
        WatchShowcaseTheme();
        WatchAppearance();
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MoviePilotSubscriptionsViewModel.SelectedIdentity)
                && ViewModel.Selected is { } selected)
                MediaViewModel.SyncSubscriptionIdentity(selected.Subscription);
        };
        MediaViewModel.PropertyChanged += OnShowcaseChanged;
        MediaViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MediaViewModel.Backdrop) or nameof(MediaViewModel.DetailErrorVisibility)
                or nameof(MediaViewModel.Immersive))
                SyncTitleStrip();
        };
    }

    /// <summary>
    /// 图下沿那道渐变 —— 与媒体详情页 <c>DetailPage.PaintBackdropFade</c> 的 <c>BackdropEdgeFade</c> 同一支：
    /// 整张图从上面透明，最后 20% 用 smoothstep 收到主题窗口色，末色与正文纸面同色，图被视口或纸面截断时
    /// 不再留下一条硬边。
    /// <para>
    /// 在代码里画而不是在标记里写停点：这一页此前一处硬编码颜色都没有，写三十几个色值会往「Shell XAML 里的
    /// 硬编码颜色只减不增」那条基线上加一大串。颜色跟主题与高对比度走（同媒体详情页）。
    /// </para>
    /// </summary>
    private void PaintBackdropFade()
    {
        var color = _accessibility.HighContrast
            ? _systemAppearance.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background)
            : ThemeHost.ToColor(ThemeHost.Current.Colors.Window);
        BackdropFadeBrush.GradientStops.Clear();
        BackdropFadeBrush.GradientStops.Add(new GradientStop { Offset = 0, Color = Color.FromArgb(0, color.R, color.G, color.B) });
        for (var step = 0; step <= 32; step++)
        {
            var t = step / 32d;
            var alpha = (byte)Math.Round(255 * t * t * (3 - 2 * t));
            BackdropFadeBrush.GradientStops.Add(new GradientStop
            {
                Offset = 0.8 + 0.2 * t,
                Color = Color.FromArgb(alpha, color.R, color.G, color.B)
            });
        }
    }

    public object? NavigationRequest => _request is { Subscription: { } subscription } request
        ? request with { Subscription = ViewModel.Deleted ? null : ViewModel.Selected?.Subscription ?? subscription, Media = subscription.Media }
        : _request;

    private void OnDetailLoaded(object sender, RoutedEventArgs args)
    {
        LiftBackdrop();
        UpdateStoreTrailer();
    }
    private void OnDetailSizeChanged(object sender, SizeChangedEventArgs args)
    {
        LiftBackdrop();
        UpdateStoreTrailer();
    }

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
        // 宽高各自写回视图模型，派生值（背景盒高、头部下限）跟着它们自己重算。正文两列的宽高由
        // BodyRow 自己的 SizeChanged 处理（见 OnBodyRowSizeChanged），这里不再顺手调版面。
        if (args.NewSize.Width > 0) MediaViewModel.PageWidth = args.NewSize.Width;
        ApplyMaterialAppearance();
        if (args.NewSize.Height > 0) MediaViewModel.Viewport = args.NewSize.Height;
    }

    /// <summary>
    /// 头部那一叠（封面、片名、操作）本来多高 —— 背景那张至少垫住它，否则头部下半截会落到
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
        // 简洁档不再有页面级的局部材质要挪（2026-11-12 起底是外壳那块画布，它本来就铺满窗口）；
        // 只有背景图那一层还要顶到窗口上沿。
        if (Math.Abs(DetailBackdropRoot.Margin.Top + lift) > 0.5)
            DetailBackdropRoot.Margin = new Thickness(0, -lift, 0, 0);
    }

    public static Thickness FactBorder(bool last) => last ? default : new Thickness(0, 0, 0, 1);
    public static Visibility FactVisibility(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility TextVisibility(string text) => FactVisibility(!string.IsNullOrWhiteSpace(text));
    public static Visibility ReleaseCountryVisibility(string country) => FactVisibility(country.Length > 0);
    public static HorizontalAlignment FactAlignment(bool cloud, string country) => cloud || country.Length > 0
        ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
    public static Windows.Foundation.Rect StarClip(double fill) => new(0, 0, 16 * fill, 20);

    private void OnBodyRowSizeChanged(object sender, SizeChangedEventArgs args)
    {
        // 只认宽度：ApplyBodyLayout 会把资料卡在行 0／行 1 之间搬，正文高度跟着变；要是高度变化也回
        // 到这里，就成了「自己改版面 → 自己再算一遍」的回路，拖边框或切模式时能打成 LayoutCycle。
        if (args.NewSize.Width <= 0) return;
        if (!double.IsNaN(_bodyLayoutWidth) && Math.Abs(args.NewSize.Width - _bodyLayoutWidth) < 0.5) return;
        _bodyLayoutWidth = args.NewSize.Width;
        ApplyBodyLayout(args.NewSize.Width);
    }

    private void ApplyBodyLayout(double width)
    {
        var side = width >= FactsSideBreakpoint;
        // 宽版式：左列季与三段货架，右列评分与资料卡（同商店条目页「发现更多」那一格）。
        // 并排不下时资料卡换到正文最上面，左列顺次下移 —— 评分仍紧接头部，不必滚到页尾才看得到。
        Grid.SetColumn(BodyFlow, 0);
        Grid.SetRow(BodyFlow, side ? 0 : 1);
        Grid.SetColumn(HeroFacts, side ? 1 : 0);
        Grid.SetRow(HeroFacts, 0);
        FactsColumn.Width = side ? new GridLength(FactsSideWidth) : new GridLength(0);
        // 窄窗右列宽 0 时列间距也跟着归零：留 24 的话左列会比改动前窄一截，600 宽下的集行会被挤到换行。
        BodyRow.ColumnSpacing = side ? FactsColumnSpacing : 0;
        HeroFacts.HorizontalAlignment = side ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        HeroFacts.Width = side ? FactsSideWidth : Math.Min(FactsStackedMaxWidth, width);
        HeroFacts.Margin = side ? default : new Thickness(0, 0, 0, 8);
        MediaViewModel.WideLayout = side;
    }

    private void OnHeroSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var compact = args.NewSize.Width < 580;
        Grid.SetRow(HeroContent, compact ? 1 : 0);
        Grid.SetColumn(HeroContent, compact ? 0 : 1);
        Grid.SetColumnSpan(HeroContent, compact ? 2 : 1);
        DetailPoster.Width = MediaViewModel.Plain ? 140 : compact ? CompactPosterWidth : PosterWidth;
        DetailPoster.Height = MediaViewModel.Plain ? 210 : compact ? CompactPosterHeight : PosterHeight;
        ApplyMaterialAppearance();
    }

    /// <summary>头部那一行在卡面里外两种模式下的外边距 —— 沉浸档照旧（压在图上，上面多让一截），卡面里归 0。</summary>
    private const double HeroRowTop = 12;
    private const double HeroRowBottom = 12;

    /// <summary>
    /// 设置 → 详情页外观里那条「显示模式」改了：这一页和电影／剧／季详情页一样当场跟上，不要求重开页面。
    /// 设置窗口在另一条线上喊这一声，所以照详情页那一支把落定排回 UI 线程。
    /// </summary>
    private void WatchAppearance()
    {
        Loaded += (_, _) =>
        {
            _appearanceObserved = true;
            StartMaterialWatch();
            SettleShowcase();
            ShellPrefs.AppearanceChanged -= OnAppearanceChanged;
            ShellPrefs.AppearanceChanged += OnAppearanceChanged;
        };
        Unloaded += (_, _) =>
        {
            _appearanceObserved = false;
            _appearanceSystemTimer?.Stop();
            ShellPrefs.AppearanceChanged -= OnAppearanceChanged;
        };
    }

    private void OnAppearanceChanged(UiSettings ui)
    {
        if (!_appearanceObserved) return;
        if (DispatcherQueue.HasThreadAccess)
        {
            ApplyDisplayMode(ui);
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (_appearanceObserved) ApplyDisplayMode(ui);
        });
    }

    /// <summary>把显示模式写进视图模型，并让头部卡面、纸面与标题条跟上 —— 三样都是这一个开关的下游。</summary>
    private void ApplyDisplayMode(UiSettings ui)
    {
        _appearanceSettings = ui;
        MediaViewModel.Immersive = ui.EnableImmersive;
        ApplyHeroSizing();
        ApplyMaterialAppearance();
        SettleShowcase();
        SyncTitleStrip();
    }

    /// <summary>
    /// 头部那一叠的尺寸：取消卡面之后这里只剩海报档位与上下留白（2026-11-14 用户令
    /// 「去掉订阅详情页的卡片」—— 海报、片名与操作直接压在剧照上，不再垫卡底）。
    /// </summary>
    private void ApplyHeroSizing()
    {
        var plain = MediaViewModel.Plain;
        HeroGrid.MinHeight = plain ? 0 : PosterHeight;
        if (plain) { DetailPoster.Width = 140; DetailPoster.Height = 210; }
        else
        {
            var compact = HeroGrid.ActualWidth < 580;
            DetailPoster.Width = compact ? CompactPosterWidth : PosterWidth;
            DetailPoster.Height = compact ? CompactPosterHeight : PosterHeight;
        }
        HeroRow.Margin = plain ? default : new Thickness(0, HeroRowTop, 0, HeroRowBottom);
        HeroOverview.VerticalAlignment = VerticalAlignment.Top;
    }

    /// <summary>
    /// 标题栏那一行字压不压在图上：有图、沉浸模式、详情没出错时用浅墨；商店模式不覆盖标题栏，回到页面字色。
    /// </summary>
    private void SyncTitleStrip() => _request?.SetTitleStrip?.Invoke(
        MediaViewModel.Plain ? TitleStrip.PagePainted
            : MediaViewModel.DetailErrorVisibility == Visibility.Visible || MediaViewModel.Backdrop is null
                ? TitleStrip.Plain : TitleStrip.OnScrim);

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is MoviePilotSubscriptionRequest request) Attach(request);
    }

    internal void Attach(MoviePilotSubscriptionRequest request)
    {
        _pageLifetime?.Cancel(); _pageLifetime?.Dispose(); _pageLifetime = new();
        _request = request;
        MediaViewModel.PropertyChanged -= OnShowcaseChanged;
        MediaViewModel.PropertyChanged += OnShowcaseChanged;
        var media = request.Subscription?.Media ?? request.Media;
        if (media is null) return;
        if (request.Subscription is not null) ViewModel.Attach(request.Service, request.Subscription);
        ViewModel.AttachPlayback(request.PlaybackScope, request.Play);
        ViewModel.AttachSearch(request.SearchVersions);
        ViewModel.UseConfirm(ConfirmDialog.For(this, dialog => MoviePilotDialog.ShowAsync(dialog, request.Service)));
        MediaViewModel.Attach(request.Service, media, request.Subscription?.EpisodeGroup ?? "",
            search: (season, episode) => OpenResources(season, episode), subscribe: season => _ = ChooseSeasonsAsync(season));
        MediaViewModel.AttachPlayback(request.PlaybackScope);
        // 只有订阅详情才接这一颗；从搜索/推荐打开的纯媒体页没有订阅，也没有可交给 AniBT 的那条路。
        MediaViewModel.AniBtSearch = request.Subscription is not null && request.SearchAniBt is not null;
        MediaViewModel.OpenMedia = request.OpenMedia;
        MediaViewModel.OpenPerson = url => request.Launcher?.OpenUrl(url);
        // 显示模式跟着设置走；没带设置（探针、旧入口）时按出厂的商店模式。写在 Bindings.Update 之前，
        // 第一帧就是用户选的那一档。
        _appearanceSettings = request.Settings?.Settings.Ui ?? new UiSettings();
        MediaViewModel.Immersive = _appearanceSettings.EnableImmersive;
        // 订阅详情才把订阅视图模型交给季列表：匹配季号的折叠栏改装文件统计；纯媒体详情的季浏览照旧。
        MediaSeasons.Attach(MediaViewModel, request.Subscription is null ? null : ViewModel);
        Bindings.Update();
        // 尺寸没变时 SizeChanged 不会再发一次，这里按当前实际宽度补算一遍。尚未排版时读到 0，
        // 随后的 SizeChanged 会把它纠正过来。
        ApplyBodyLayout(BodyRow.ActualWidth);
        _bodyLayoutWidth = BodyRow.ActualWidth > 0 ? BodyRow.ActualWidth : double.NaN;
        ApplyHeroSizing();
        ApplyMaterialAppearance();
        SettleShowcase();
        SyncTitleStrip();
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
            MoviePilotMenuBrand.Apply(item);
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
            _ = ViewModel.EditAsync(card,
                (subscription, save, remove) => MoviePilotSubscriptionEditor.ShowAsync(this, subscription, _request?.Service, save, remove));
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
        _resourcesOpen = true;
        // 资源面板淡入的这 120ms 里底层详情仍画着，但不能接输入。
        DetailScroll.IsHitTestVisible = false;
        MediaResources.IsHitTestVisible = true;
        MediaResources.Visibility = Visibility.Visible;
        ResourcesMotion.Enter(200, 12);
        // Exit 对从未显示过的元素会立即落定；先 Settle 到显示档，淡出才有起始帧。
        DetailMotion.Settle();
        DetailMotion.Exit(120, 8, () => DetailScroll.Visibility = Visibility.Collapsed);
    }

    private void OnResourcesClosed(object? sender, EventArgs args)
    {
        _resourcesOpen = false;
        MediaResources.IsHitTestVisible = false;
        DetailScroll.IsHitTestVisible = true;
        ResourcesMotion.Settle();
        ResourcesMotion.Exit(120, 8, () =>
        {
            MediaResources.Visibility = Visibility.Collapsed;
            MediaResources.Release();
        });
        DetailScroll.Visibility = Visibility.Visible;
        DetailMotion.Enter(160, 8);
    }

    private void OnChooseSeasons(object sender, RoutedEventArgs args) => _ = ChooseSeasonsAsync(null);
    private bool _choosingSeasons;
    private async Task ChooseSeasonsAsync(int? season)
    {
        if (_choosingSeasons || _request is not { } request || !MediaViewModel.CanInteract || MediaViewModel.Detail is not { } detail) return;
        if (_pageLifetime is not { } lifetime) return;
        var token = lifetime.Token;
        _choosingSeasons = true;
        try
        {
            var subscribed = false;
            if (detail.Media.Type == "电影")
            {
                var subscriptions = await request.Service.SubscriptionsAsync(token);
                if (!ReferenceEquals(_request, request) || token.IsCancellationRequested || !MediaViewModel.CanInteract) return;
                var current = MoviePilotSubscriptionPlan.Find(detail.Media with { Season = null }, "", subscriptions);
                await request.Service.ApplyMovieSubscriptionAsync(detail.Media, current, token);
                subscribed = current is null;
            }
            else
            {
                var before = MediaViewModel.Seasons.Where(row => row.CurrentSubscription is not null)
                    .Select(row => row.CurrentSubscription!.Id).ToHashSet();
                var report = await MoviePilotSeasonPicker.ShowAsync(this, request.Service, detail.Media, MediaViewModel.GroupId, season, token);
                if (!ReferenceEquals(_request, request) || token.IsCancellationRequested) return;
                // 弹窗一点提交就收起来了，成败由它带回来（2026-11-13 用户令）；先把这句话放到本页提示条上。
                if (report.NoticeMessage.Length > 0) MediaViewModel.Announce(report);
                // 失败再弹一条系统通知：用户切走或者收起窗口以后也看得见结果（2026-11-13 用户令）。
                if (report.Kind == MoviePilotSubscribeReportKind.Failed) SystemNotification.Show(report.NoticeTitle ?? "订阅未完成", report.NoticeMessage);
                if (report.Succeeded && request.Subscribed is not null)
                {
                    var after = await request.Service.SubscriptionsAsync(token);
                    subscribed = after.Any(item => MoviePilotSubscriptionPlan.SameMedia(item.Media, detail.Media) && !before.Contains(item.Id));
                }
            }
            if (!ReferenceEquals(_request, request) || token.IsCancellationRequested
                || !request.Service.Available || !request.Service.IsCurrentConnection(detail.Media.ConnectionStamp)) return;
            if (subscribed && request.Subscribed is { } openSubscriptions)
            {
                openSubscriptions(detail.Media);
                return;
            }
            await RefreshSubscriptionStateAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!ReferenceEquals(_request, request)) return;
            if (request.Service.Available)
            {
                try { await RefreshSubscriptionStateAsync(); }
                catch (OperationCanceledException) { }
                catch (Exception) { /* 保留原失败；未取得新状态时不冒充成功。 */ }
            }
            if (ReferenceEquals(_request, request))
            {
                MediaViewModel.OperationFailed(error);
                // 电影这条没有季弹窗，失败也只在这里；系统通知跟页面提示一起发出去（2026-11-13 用户令）。
                SystemNotification.Show("订阅操作未完成", Failure.Describe(error));
            }
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
        _trailerMenu?.Hide();
        StopStoreTrailer();
        _trailerFailed = null;
        _storeAmbientBrush.Clear();
        _storeTint.Clear();
        StoreBackdropSides.Background = null;
        _request?.SetTitleStrip?.Invoke(TitleStrip.Plain);
        _pageLifetime?.Cancel(); _pageLifetime?.Dispose(); _pageLifetime = null;
        ThemeHost.Changed -= OnShowcaseThemeChanged;
        _appearanceObserved = false;
        _bodyLayoutWidth = double.NaN;
        _appearanceSystemTimer?.Stop();
        ShellPrefs.AppearanceChanged -= OnAppearanceChanged;
        MediaViewModel.PropertyChanged -= OnShowcaseChanged;
        // 离页不播淡出：停掉进出动画并立即落定，按离开时所处的那一档写死可见性，最后状态不变。
        _detailMotion?.Settle();
        _resourcesMotion?.Collapse();
        DetailScroll.Visibility = _resourcesOpen ? Visibility.Collapsed : Visibility.Visible;
        MediaResources.Visibility = _resourcesOpen ? Visibility.Visible : Visibility.Collapsed;
        MediaResources.IsHitTestVisible = true;
        DetailScroll.IsHitTestVisible = true;
        _request = null; ViewModel.Cancel(); MediaViewModel.Cancel(); MediaResources.Release();
    }
    protected override void OnNavigatedFrom(NavigationEventArgs e) { Release(); base.OnNavigatedFrom(e); }
}
