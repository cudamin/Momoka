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

    private void OnFilterFlyoutOpening(object sender, object e)
    {
        WallFilterOptions.Children.Clear();
        foreach (var filter in ViewModel.WallFilterOptions)
        {
            var current = filter;
            WallFilterOptions.Children.Add(OptionButton(
                MoviePilotSubscriptionWall.FilterLabel(current),
                ViewModel.WallFilter == current,
                "WallFilter" + current,
                () => { ViewModel.SelectWallFilter(current); WallFilterFlyout.Hide(); }));
        }
    }

    private void OnSortFlyoutOpening(object sender, object e)
    {
        WallSortOptions.Children.Clear();
        foreach (var sort in ViewModel.WallSortOptions)
        {
            var current = sort;
            WallSortOptions.Children.Add(OptionButton(
                MoviePilotSubscriptionWall.SortLabel(current),
                ViewModel.EffectiveWallSort == current,
                "WallSort" + current,
                () => { ViewModel.SelectWallSort(current); WallSortFlyout.Hide(); }));
        }
    }

    /// <summary>筛选／排序浮层里的一条：标签＋选中勾，点选即生效并收起浮层。每次开浮层按当前选项重建。</summary>
    private static Button OptionButton(string label, bool selected, string automationId, Action action)
    {
        var resources = Application.Current.Resources;
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new TextBlock { Text = label, Style = (Style)resources["EgBodyStyle"], VerticalAlignment = VerticalAlignment.Center };
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
            Grid.SetColumn(check, 1);
            grid.Children.Add(check);
        }
        var button = new Button
        {
            Style = (Style)resources["EgSecondaryButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = grid
        };
        AutomationProperties.SetAutomationId(button, automationId);
        button.Click += (_, _) => action();
        return button;
    }

    // ── 卡片网格 ──────────────────────────────────────────────────────────────────────────────────────

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
        MoviePilotSubscriptionMenu.Create(this, ViewModel, card, () => Open(card), _service).ShowAt(control);
    }

    private void OnWallDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) => _ = ViewModel.SaveWallOrderAsync();

    // ── 展示映射（x:Bind 函数）──────────────────────────────────────────────────────────────────────────

    private Brush ToneBrush(MoviePilotExecutionTone tone) => MoviePilotWallUi.Tone(tone);
    private Visibility VisibleWhen(bool value) => MoviePilotWallUi.Show(value);
    private bool NotBusy(bool busy) => !busy;
    private string SelectAllLabel(bool all) => all ? "取消全选订阅" : "选择全部订阅";
    private Brush SortModeForeground(bool active) => MoviePilotWallUi.Role(active ? "EgWarningBrush" : "EgTextBrush");
    private Brush BatchModeForeground(bool active) => MoviePilotWallUi.Role(active ? "EgAccentBrush" : "EgTextBrush");
}
