using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

/// <summary>
/// 订阅墙共用的纯展示映射：执行色调 → 角色笔刷、bool → 显隐。住在视图层，不往视图模型加 UI 笔刷属性。
/// 色调对齐网页端 VAlert 的 color token：info／primary／success／secondary／error／warning。
/// </summary>
internal static class MoviePilotWallUi
{
    internal static Brush Tone(MoviePilotExecutionTone tone)
    {
        var key = tone switch
        {
            MoviePilotExecutionTone.Info => "EgInfoBrush",
            MoviePilotExecutionTone.Primary => "EgAccentBrush",
            MoviePilotExecutionTone.Success => "EgAccentBrush",
            MoviePilotExecutionTone.Error => "EgDangerBrush",
            MoviePilotExecutionTone.Warning => "EgWarningBrush",
            _ => "EgTextDimBrush"
        };
        return (Brush)Application.Current.Resources[key];
    }

    internal static Brush Role(string key) => (Brush)Application.Current.Resources[key];

    internal static Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>
/// MoviePilot「我的订阅」墙（电影／电视剧分类页）。状态与行为都在 <see cref="MoviePilotSubscriptionsViewModel"/>
/// 的墙成员里，这里只绑定、接事件，并持有执行轮询的定时器（外壳惯例：DispatcherTimer 只在 View）。
/// 与横排共用同一份订阅数据与确认框；汇总库不立墙，由宿主页面收起来。
/// </summary>
public sealed partial class MoviePilotSubscriptionWallView : UserControl
{
    private readonly MoviePilotHomeSubscriptions _standalone = new();
    private readonly DispatcherTimer _pollTimer;
    private MoviePilotHomeSubscriptions? _owner;
    private MoviePilotService? _service;
    private Action<MoviePilotSubscription>? _open;
    private bool _attached;
    private bool _loaded;

    private MoviePilotHomeSubscriptions Source => _owner ?? _standalone;
    internal MoviePilotSubscriptionsViewModel ViewModel => Source.ViewModel;

    public MoviePilotSubscriptionWallView()
    {
        InitializeComponent();
        // 网页端 scheduleExecutionPoll 的 2500ms 节拍；单趟在途收口由视图模型负责，这里只管按时敲。
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
        _pollTimer.Tick += (_, _) => _ = ViewModel.PollWallExecutionAsync();
        Loaded += (_, _) => { _loaded = true; UpdatePolling(); };
        Unloaded += (_, _) => { _loaded = false; UpdatePolling(); };
    }

    /// <summary>
    /// 宿主页面（分类页）把墙接到自己那份订阅数据上。Row 不需要：墙的电影／电视剧判断走视图模型的
    /// UsePageCategory。确认框与横排同一套（MoviePilotDialog）。
    /// </summary>
    internal void Attach(MoviePilotHomeSubscriptions owner, MoviePilotService service, Action<MoviePilotSubscription>? open)
    {
        _owner = owner;
        _service = service;
        _open = open;
        _attached = true;
        ViewModel.UseConfirm(ConfirmDialog.For(this, dialog => MoviePilotDialog.ShowAsync(dialog, service)));
        Bindings.Update();
        UpdatePolling();
    }

    /// <summary>松开墙：停轮询、清属主。离页／换连接／汇总库都由宿主调它。</summary>
    internal void Detach()
    {
        _attached = false;
        UpdatePolling();
        _owner = null;
        _service = null;
        _open = null;
        Bindings.Update();
    }

    /// <summary>附加且在树上才轮询；离开/卸载/松开任一条都停（网页端 props.active ＋ document.hidden）。</summary>
    private void UpdatePolling()
    {
        var active = _attached && _loaded;
        ViewModel.SetWallActive(active);
        if (active) _pollTimer.Start();
        else _pollTimer.Stop();
    }

    private void Open(MoviePilotSubscriptionCard card)
    {
        if (ViewModel.Owns(card)) _open?.Invoke(card.Subscription);
    }

    // ── 工具栏 ────────────────────────────────────────────────────────────────────────────────────────

    private void OnToggleSortMode(object sender, RoutedEventArgs e) => ViewModel.ToggleWallSortMode();
    private void OnToggleBatchMode(object sender, RoutedEventArgs e) => ViewModel.ToggleWallBatchMode();
    private void OnExitSort(object sender, RoutedEventArgs e) => ViewModel.ToggleWallSortMode();
    private void OnExitBatch(object sender, RoutedEventArgs e) => ViewModel.ExitWallBatchMode();
    private void OnSelectAll(object sender, RoutedEventArgs e) => ViewModel.ToggleWallSelectAll();
    private void OnSearchAll(object sender, RoutedEventArgs e) => _ = ViewModel.WallSearchAllAsync();
    private void OnRefreshResources(object sender, RoutedEventArgs e) => _ = ViewModel.WallRefreshResourcesAsync();
    private void OnRefreshMetadata(object sender, RoutedEventArgs e) => _ = ViewModel.WallRefreshMetadataAsync();
    private void OnBatchResume(object sender, RoutedEventArgs e) => _ = ViewModel.WallBatchResumeAsync();
    private void OnBatchPause(object sender, RoutedEventArgs e) => _ = ViewModel.WallBatchPauseAsync();
    private void OnBatchDelete(object sender, RoutedEventArgs e) => _ = ViewModel.WallBatchDeleteAsync();
    private void OnCancelBatch(object sender, RoutedEventArgs e) => _ = ViewModel.CancelWallBatchAsync();
    private void OnDismissBatch(object sender, RoutedEventArgs e) => ViewModel.DismissWallBatch();

    private void OnFilterFlyoutOpening(object sender, object e) => BuildFilterPanel();

    /// <summary>清掉名称筛选；输入框自己那一颗清除键，和网页端可清空的输入框一致。</summary>
    private void OnClearKeyword(object sender, RoutedEventArgs e) => ViewModel.WallKeyword = "";

    /// <summary>
    /// 筛选浮层的内容（网页端 subscribe.vue 那颗筛选键的 VMenu）：名称搜索、状态列表、排序列表。
    /// 每次开浮层按当前选择重建；选完排序不收起面板，勾选要当场挪过去，所以选完再重建一遍。
    /// </summary>
    private void BuildFilterPanel()
    {
        WallFilterOptions.Children.Clear();
        foreach (var filter in ViewModel.WallFilterOptions)
        {
            var current = filter;
            WallFilterOptions.Children.Add(OptionRow(
                MoviePilotSubscriptionWall.FilterLabel(current),
                FilterGlyph(current),
                FilterToneKey(current),
                ViewModel.WallFilter == current,
                "WallFilter" + current,
                () => { ViewModel.SelectWallFilter(current); WallFilterFlyout.Hide(); }));
        }
        WallSortOptions.Children.Clear();
        foreach (var sort in ViewModel.WallSortOptions)
        {
            var current = sort;
            WallSortOptions.Children.Add(OptionRow(
                MoviePilotSubscriptionWall.SortLabel(current),
                glyph: null,
                toneKey: null,
                ViewModel.EffectiveWallSort == current,
                "WallSort" + current,
                // 网页端 selectSubscribeSort 不动弹层：勾选当场挪过去，面板留着。
                () => { ViewModel.SelectWallSort(current); BuildFilterPanel(); }));
        }
    }

    /// <summary>状态行的小图标（网页端 filterOptions 的 mdi 图标，换成 Segoe 的同义字形）。</summary>
    private static string FilterGlyph(MoviePilotSubscriptionWallFilter filter) => filter switch
    {
        MoviePilotSubscriptionWallFilter.BestVersion => "\uE72C",   // Refresh：洗版
        MoviePilotSubscriptionWallFilter.NotStarted => "\uE823",    // Recent：未开始
        MoviePilotSubscriptionWallFilter.Subscribing => "\uE896",   // Download：订阅中
        MoviePilotSubscriptionWallFilter.Pending => "\uE897",       // Help：待定
        MoviePilotSubscriptionWallFilter.Paused => "\uE769",        // Pause：暂停
        MoviePilotSubscriptionWallFilter.Completed => "\uE930",     // Completed：订阅完成
        _ => "\uE71C"                                              // Filter：全部
    };

    /// <summary>状态行的图标色（网页端 filterOptions 的 color）：洗版警示、订阅中信息、暂停危险、完成强调。</summary>
    private static string FilterToneKey(MoviePilotSubscriptionWallFilter filter) => filter switch
    {
        MoviePilotSubscriptionWallFilter.BestVersion => "EgWarningBrush",
        MoviePilotSubscriptionWallFilter.Subscribing => "EgInfoBrush",
        MoviePilotSubscriptionWallFilter.Paused => "EgDangerBrush",
        MoviePilotSubscriptionWallFilter.Completed => "EgAccentBrush",
        _ => "EgTextDimBrush"
    };

    /// <summary>
    /// 筛选／排序浮层里的一条：可选图标＋标签＋选中勾（网页端 VListItem）。
    /// 选中的一条铺强调色底、字与图标转强调色；每次开浮层按当前选择重建。
    /// </summary>
    private Button OptionRow(string label, string? glyph, string? toneKey, bool selected, string automationId, Action action)
    {
        var resources = Application.Current.Resources;
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (glyph is { Length: > 0 })
        {
            grid.Children.Add(new FontIcon
            {
                Glyph = glyph,
                FontSize = (double)resources["EgCaptionFontSize"],
                Foreground = (Brush)resources[selected ? "EgAccentBrush" : toneKey ?? "EgTextDimBrush"],
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        var text = new TextBlock
        {
            Text = label,
            Style = (Style)resources["EgBodyStyle"],
            Foreground = (Brush)resources[selected ? "EgAccentBrush" : "EgTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (selected)
        {
            var check = new FontIcon
            {
                Glyph = "\uE73E",
                FontSize = (double)resources["EgCaptionFontSize"],
                Foreground = (Brush)resources["EgAccentBrush"],
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(check, 2);
            grid.Children.Add(check);
        }
        var button = new Button
        {
            // 行样式住在这个视图自己的 Resources 里（它只服务这块面板），别处的角色样式仍在 App 上。
            Style = (Style)Resources["WallOptionRowStyle"],
            Content = grid
        };
        if (selected) button.Background = (Brush)resources["EgAccentSoftBrush"];
        AutomationProperties.SetAutomationId(button, automationId);
        button.Click += (_, _) => action();
        return button;
    }

    // ── 卡片网格 ──────────────────────────────────────────────────────────────────────────────────────

    private void OnWallGridLoaded(object sender, RoutedEventArgs e) => ResizeWall();

    private void OnWallGridSizeChanged(object sender, SizeChangedEventArgs e) => ResizeWall();

    /// <summary>
    /// 版面还没立起来时（清单是空的、或者面板正在换）<c>ItemsPanelRoot</c> 是 null，量不到就没法算；
    /// 第一格容器准备好时再算一次 —— 那时面板一定在，而且这是「卡片已经上屏」最早的一拍。
    /// </summary>
    private void OnWallContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args) => ResizeWall();

    /// <summary>
    /// 一格铺多宽按这一页的可用宽度算（<see cref="MoviePilotWallMetrics"/>，与媒体库同一条算术）。
    /// <para>
    /// 卡片本身写死过 340、格子写死过 352：窗口拖宽只多出空档，拖窄把卡片裁掉一截（横向滚动是关着的）。
    /// 2026-11-16 用户令「订阅媒体库怎么不会跟媒体库一样自动调整封面大小」之后改成跟着窗口走 —— 卡片横向
    /// 撑满格子（见 WallCard 的 Root），这里只负责把格子的宽交出去。
    /// </para>
    /// <para>
    /// 三个入口都走到这里（Loaded／SizeChanged／第一格容器），所以要幂等：宽度没变就退出，别在拖窗口的
    /// 每一拍都让版面重排一次。设完 <c>ItemWidth</c> 自己会失效重排；显式再敲一下 measure，是因为这个属性
    /// 不经过子元素的尺寸变化，某些一拍里面板不会自己重算（探针量到过「格宽换了、卡片还是上一档」）。
    /// </para>
    /// </summary>
    private void ResizeWall()
    {
        if (WallGrid.ItemsPanelRoot is not ItemsWrapGrid panel) return;
        var available = WallGrid.ActualWidth;
        if (!(available > 0) || !double.IsFinite(available)) return;

        var (_, itemWidth) = MoviePilotWallMetrics.Fit(available);
        if (Math.Abs(panel.ItemWidth - itemWidth) < 0.5) return;

        panel.ItemWidth = itemWidth;
        panel.InvalidateMeasure();
    }

    private void OnWallItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not MoviePilotSubscriptionCard card) return;
        if (ViewModel.WallBatchMode) { ViewModel.ToggleWallSelected(card); return; }
        // 与横排封面点击一致：打开编辑订阅（MoviePilotSubscriptionsView.OnPosterOpen 同一条路）。
        _ = ViewModel.EditAsync(card,
            (subscription, save, remove) => MoviePilotSubscriptionEditor.ShowAsync(this, subscription, _service, save, remove));
    }

    private void OnCardImage(object? sender, EventArgs e)
    {
        if (sender is not MoviePilotSubscriptionWallCard { Card: { } card }) return;
        _ = ViewModel.LoadPosterAsync(card);
        if (_service is { } service) _ = card.LoadBackdropAsync(service);
    }

    private void OnCardMenu(object? sender, EventArgs e)
    {
        if (sender is not MoviePilotSubscriptionWallCard { Card: { } card } control) return;
        if (_service is { } service)
            ViewModel.UseConfirm(ConfirmDialog.For(this, dialog => MoviePilotDialog.ShowAsync(dialog, service)));
        // Gallery 6de91feccadfbb0a796b71caedead5a2284f730e：
        // WinUIGallery/Samples/MenuFlyout/MenuFlyoutPage.xaml 把菜单挂在触发按钮上；这里沿用动态菜单和页面属主。
        MoviePilotSubscriptionMenu.Create(this, ViewModel, card, () => Open(card), _service).ShowAt(control.MenuAnchor);
    }

    private void OnWallDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) => _ = ViewModel.SaveWallOrderAsync();

    // ── 展示映射（x:Bind 函数）──────────────────────────────────────────────────────────────────────────

    private Brush ToneBrush(MoviePilotExecutionTone tone) => MoviePilotWallUi.Tone(tone);
    private Visibility VisibleWhen(bool value) => MoviePilotWallUi.Show(value);
    private Visibility KeywordClearVisibility(string keyword) => MoviePilotWallUi.Show(keyword.Length > 0);
    private bool NotBusy(bool busy) => !busy;
    private string SelectAllLabel(bool all) => all ? "取消全选订阅" : "选择全部订阅";

    /// <summary>
    /// 筛选键的图标色（网页端 filterButtonColor）：有名称或非「全部」的状态筛选时是强调色，否则跟随正文。
    /// 网页端那颗键是 text 变体，靠颜色而不是底色说明「筛过」。
    /// </summary>
    private Brush FilterTone(MoviePilotSubscriptionWallFilter filter, string keyword) => MoviePilotWallUi.Role(
        keyword.Trim().Length > 0 || filter != MoviePilotSubscriptionWallFilter.All ? "EgAccentBrush" : "EgTextBrush");

    /// <summary>拖拽排序键的图标色（网页端 subscribeSortMode ? warning : gray）。</summary>
    private Brush SortModeTone(bool active) => MoviePilotWallUi.Role(active ? "EgWarningBrush" : "EgTextBrush");

    /// <summary>批量键的图标色（网页端 subscribeBatchState.enabled ? primary : gray）。</summary>
    private Brush BatchModeTone(bool active) => MoviePilotWallUi.Role(active ? "EgAccentBrush" : "EgTextBrush");
}
