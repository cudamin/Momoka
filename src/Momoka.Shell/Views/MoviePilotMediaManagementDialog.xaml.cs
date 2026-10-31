using System.Collections.ObjectModel;
using System.ComponentModel;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace Momoka.Shell.Views;

/// <summary>先只读收齐范围，再独立确认；结果留在同窗，不能嵌套弹窗或把异常当成取消。</summary>
public sealed partial class MoviePilotMediaManagementDialog : ContentDialog
{
    private readonly MoviePilotService _service;
    private readonly EmbySessionScope _scope;
    private readonly EmbyItem _item;
    private readonly MoviePilotHistoryAction _action;
    private readonly string _stamp;
    private readonly Frame? _frame;
    private readonly object? _page;
    private readonly EmbySession? _sourceSession;
    private readonly CancellationTokenSource _lifetime = new();
    private MoviePilotHistoryPlan? _plan;
    private bool _closed;
    private bool _submitting;
    private int _revoked;
    private bool _updatingSelection;
    public ObservableCollection<MoviePilotHistoryRecordRow> Records { get; } = [];

    internal MoviePilotMediaManagementDialog(MoviePilotService service, EmbySessionScope scope, EmbyItem item,
        MoviePilotHistoryAction action, Frame? frame = null, EmbySession? sourceSession = null)
    {
        _service = service;
        _scope = scope;
        _item = item;
        _action = action;
        _frame = frame;
        _page = frame?.Content;
        _sourceSession = sourceSession;
        _stamp = service.ConnectionStamp;
        InitializeComponent();
        RequestedTheme = ThemeHost.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        Title = action == MoviePilotHistoryAction.SmartReorganize
            ? item.Type is EmbyItemType.Series or EmbyItemType.Season ? "智能批量整理" : "智能整理"
            : "媒体整理";
        PrimaryButtonText = action == MoviePilotHistoryAction.SmartReorganize ? "确认交给智能助手" : "确认删除";
        IsPrimaryButtonEnabled = false;
        CloseButtonText = "取消";
        SummaryText.Text = item.Name;
        ToolTipService.SetToolTip(SummaryText, item.Name);
        CountText.Text = "正在核对整理记录与文件…";
        WarningText.Text = "核对完成后可确认操作。";
        Opened += OnOpened;
        Closing += (_, args) => { if (_submitting && Volatile.Read(ref _revoked) == 0) args.Cancel = true; };
        Closed += OnClosed;
    }

    private bool Owns() => !_closed && Volatile.Read(ref _revoked) == 0 && !_lifetime.IsCancellationRequested &&
        _scope.IsAvailable && (_frame is null || ReferenceEquals(_frame.Content, _page)) && _service.Available && _service.IsCurrentConnection(_stamp);

    private async void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        XamlRoot.Changed += OnRootChanged;
        FitPanel();
        _service.AvailabilityChanged += OnAvailabilityChanged;
        _service.HistoryCapabilitiesChanged += OnAvailabilityChanged;
        if (_frame is not null) _frame.Navigating += OnNavigating;
        if (_sourceSession is not null) _sourceSession.SourcesChanged += OnSourcesChanged;
        try
        {
            if (!Owns()) return;
            _plan = await _service.PrepareItemHistoryActionAsync(_scope, _item, _action, _stamp, _lifetime.Token);
            if (!Owns()) return;
            CountText.Text = $"{_plan.Histories.Count} 条记录 · {_plan.ActionLabel}";
            WarningText.Text = _plan.Smart ? _plan.Warning : _plan.DeleteSource || _plan.DeleteDestination
                ? "删除无法撤销，MoviePilot 会按配置清理关联目录。源文件删除可能影响做种；媒体库文件删除后将无法播放。"
                : "两端文件均保留。删除记录后，无法再通过这条记录重新整理。";
            ScopeSummary.Visibility = _plan.Smart ? Visibility.Collapsed : Visibility.Visible;
            HistoryScope.Text = "整理记录 · 删除";
            SourceScope.Text = "源文件 · " + (_plan.DeleteSource ? "删除" : "保留");
            DestinationScope.Text = "媒体库文件 · " + (_plan.DeleteDestination ? "删除" : "保留");
            HistoryScope.Foreground = StatusBrush("将删除");
            SourceScope.Foreground = StatusBrush(_plan.DeleteSource ? "将删除" : "保留");
            DestinationScope.Foreground = StatusBrush(_plan.DeleteDestination ? "将删除" : "保留");
            ShowRecords(_plan);
            SelectAllRecords.Visibility = Visibility.Visible;
            RefreshSelection();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!_closed) ShowProblem("读取确认范围失败，本次没有提交：" + Failure.Describe(error));
        }
        finally { if (!_closed) Activity.Visibility = Visibility.Collapsed; }
    }

    private async void OnConfirm(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (_submitting || _plan is not { Claimed: false } plan || !Owns()) return;
        var selectedIds = Records.Where(row => row.Media.Selected).Select(row => row.Id).ToArray();
        if (selectedIds.Length == 0) return;
        plan = plan.SelectRecords(selectedIds);
        var deferral = args.GetDeferral();
        _submitting = true;
        IsPrimaryButtonEnabled = false;
        Activity.Visibility = Visibility.Visible;
        CloseButtonText = "";
        RecordList.IsEnabled = false;
        SelectAllRecords.Visibility = Visibility.Collapsed;
        WarningText.Text = "正在提交；请勿重复操作。即使窗口关闭，也不会撤销 MP 已接收的操作。";
        try
        {
            // Page/source ownership is checked again immediately before the service consumes this confirmation.
            if (!Owns()) return;
            var result = await _service.SubmitItemHistoryActionAsync(_scope, plan, _lifetime.Token);
            if (_closed) return;
            ShowResult(plan, result);
            PrimaryButtonText = "";
            CloseButtonText = "关闭";
        }
        catch (OperationCanceledException)
        {
            if (!_closed) ShowProblem("本次操作已取消或连接已改变；若请求已发出，请先到 MP 核对，不能重复提交");
        }
        catch (Exception error)
        {
            if (!_closed) ShowProblem(Failure.Describe(error));
        }
        finally
        {
            deferral.Complete();
            _submitting = false;
            if (!_closed)
            {
                Activity.Visibility = Visibility.Collapsed;
                PrimaryButtonText = "";
                CloseButtonText = "关闭";
                RecordList.IsEnabled = true;
            }
        }
    }

    private void ShowRecords(MoviePilotHistoryPlan plan, MoviePilotHistoryResult? result = null)
    {
        var media = Records.ToDictionary(row => row.Id, row => row.Media);
        var ids = plan.Histories.Select(history => history.Id).ToHashSet();
        foreach (var previous in Records)
        {
            previous.Media.PropertyChanged -= OnSelectionChanged;
            if (!ids.Contains(previous.Id)) previous.Media.Retire();
        }
        Records.Clear();
        foreach (var history in plan.Histories)
        {
            media.TryGetValue(history.Id, out var retained);
            var row = MoviePilotHistoryRecordRow.Create(history, plan, result, retained);
            row.Media.PropertyChanged += OnSelectionChanged;
            Records.Add(row);
        }
    }

    private void ShowResult(MoviePilotHistoryPlan plan, MoviePilotHistoryResult result)
    {
        ShowRecords(plan, result);
        Title = result.Smart ? "已提交智能整理" : result.NeedsReview ? "删除结果待核对" : "删除完成";
        CountText.Text = result.Smart ? result.Summary
            : $"已完成 {Records.Count(row => row.Complete)} / {result.RequestedCount} 条记录";
        ScopeSummary.Visibility = Visibility.Collapsed;
        WarningText.Visibility = Visibility.Collapsed;
        Notice.IsOpen = result.NeedsReview;
        Notice.Severity = InfoBarSeverity.Warning;
        Notice.Title = "部分结果待核对";
        Notice.Message = result.Problem.Length > 0 ? result.Problem : "请到 MoviePilot 核对；已完成的操作不会回滚，不要整批重试。";
    }

    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => FitPanel();
    private void FitPanel()
    {
        if (XamlRoot is not { } root) return;
        ManagementPanel.Width = Math.Clamp(root.Size.Width - 80, 240, 1060);
        ManagementPanel.MaxHeight = Math.Clamp(root.Size.Height - 200, 180, 660);
    }

    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!_updatingSelection && args.PropertyName == nameof(MoviePilotSeriesTransferRow.Selected)) RefreshSelection();
    }

    private void RefreshSelection()
    {
        if (_submitting || _plan is not { Claimed: false } plan) return;
        var count = Records.Count(row => row.Media.Selected);
        CountText.Text = $"共 {Records.Count} 条记录 · 已选 {count} 条";
        SelectAllRecords.IsChecked = count == Records.Count ? true : count == 0 ? false : null;
        PrimaryButtonText = plan.Smart ? $"智能整理（{count}）" : $"删除所选（{count}）";
        IsPrimaryButtonEnabled = count > 0 && Owns();
    }

    private void OnSelectAll(object sender, RoutedEventArgs args)
    {
        if (_submitting || _plan is not { Claimed: false }) return;
        var selected = Records.Any(row => !row.Media.Selected);
        _updatingSelection = true;
        try { foreach (var row in Records) row.Media.Selected = selected; }
        finally { _updatingSelection = false; }
        RefreshSelection();
    }

    private void OnRecordLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: MoviePilotHistoryRecordRow row }) LoadArtwork(row);
    }

    private void OnRecordContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (args.NewValue is MoviePilotHistoryRecordRow row) LoadArtwork(row);
    }

    private void LoadArtwork(MoviePilotHistoryRecordRow row)
    {
        if (Owns() && Records.Contains(row)) _ = row.Media.LoadArtworkAsync(_service, _stamp, _lifetime.Token);
    }

    private void OnCardSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (sender is not Grid card) return;
        var wide = args.NewSize.Width >= 820;
        var narrow = args.NewSize.Width < 560;
        card.ColumnSpacing = narrow ? 0 : 18;
        card.ColumnDefinitions[0].Width = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(wide ? 220 : 180);
        card.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        card.ColumnDefinitions[2].Width = new GridLength(wide ? 150 : 0);
        Place("CardIdentity", 0, 0, narrow ? 3 : 1);
        Place("CardPaths", narrow ? 1 : 0, narrow ? 0 : 1, narrow ? 3 : 1);
        Place("CardFacts", narrow ? 2 : wide ? 0 : 1, narrow ? 0 : wide ? 2 : 1, narrow ? 3 : 1);

        void Place(string name, int row, int column, int span)
        {
            var element = (FrameworkElement)card.FindName(name);
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            Grid.SetColumnSpan(element, span);
            element.Margin = new Thickness(0, row > 0 ? 10 : 0, 0, 0);
        }
    }

    public static Visibility MessageVisibility(string message) => message.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility SelectionVisibility(bool selectable) => selectable ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility ResultVisibility(bool selectable) => selectable ? Visibility.Collapsed : Visibility.Visible;
    public static Brush StateBrush(bool review, bool complete) => (Brush)Application.Current.Resources[
        review ? "EgWarningBrush" : complete ? "EgAccentBrush" : "EgTextDimBrush"];
    public static Brush StatusBrush(string status) => (Brush)Application.Current.Resources[status switch
    {
        "将删除" or "删除失败" => "EgDangerBrush",
        "待核对" or "保留待核对" or "未执行" => "EgWarningBrush",
        "已删除" or "已不存在" => "EgAccentBrush",
        _ => "EgTextDimBrush"
    }];

    private void ShowProblem(string message)
    {
        IsPrimaryButtonEnabled = false;
        Notice.Severity = InfoBarSeverity.Error;
        Notice.Message = message;
        Notice.IsOpen = true;
    }

    private void OnNavigating(object sender, NavigatingCancelEventArgs args) => Revoke();
    private void OnSourcesChanged(object? sender, EventArgs args)
    {
        if (!_scope.IsAvailable) Revoke();
    }

    private void Revoke()
    {
        Interlocked.Exchange(ref _revoked, 1);
        _lifetime.Cancel();
        DispatcherQueue.TryEnqueue(() => { if (!_closed) Hide(); });
    }

    private void OnAvailabilityChanged(object? sender, EventArgs args)
    {
        if (_service.Available && _service.IsCurrentConnection(_stamp) && _service.CanManageHistory &&
            (_action != MoviePilotHistoryAction.SmartReorganize || _service.CanSmartReorganize)) return;
        Revoke();
    }

    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        _closed = true;
        foreach (var row in Records)
        {
            row.Media.PropertyChanged -= OnSelectionChanged;
            row.Media.Retire();
        }
        if (XamlRoot is { } root) root.Changed -= OnRootChanged;
        _service.AvailabilityChanged -= OnAvailabilityChanged;
        _service.HistoryCapabilitiesChanged -= OnAvailabilityChanged;
        if (_frame is not null) _frame.Navigating -= OnNavigating;
        if (_sourceSession is not null) _sourceSession.SourcesChanged -= OnSourcesChanged;
        _lifetime.Cancel();
    }
}
