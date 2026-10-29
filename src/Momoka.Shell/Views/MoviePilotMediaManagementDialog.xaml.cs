using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.MoviePilot;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
            : "媒体管理";
        PrimaryButtonText = action == MoviePilotHistoryAction.SmartReorganize ? "确认交给智能助手" : "确认删除";
        IsPrimaryButtonEnabled = false;
        CloseButtonText = "取消";
        SummaryText.Text = $"正在核对「{item.Name}」的 MP 整理记录…";
        WarningText.Text = "当前只读取记录，不会删除文件或提交整理。";
        Opened += OnOpened;
        Closing += (_, args) => { if (_submitting && Volatile.Read(ref _revoked) == 0) args.Cancel = true; };
        Closed += OnClosed;
    }

    private bool Owns() => !_closed && Volatile.Read(ref _revoked) == 0 && !_lifetime.IsCancellationRequested &&
        _scope.IsAvailable && (_frame is null || ReferenceEquals(_frame.Content, _page)) && _service.Available && _service.IsCurrentConnection(_stamp);

    private async void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        _service.AvailabilityChanged += OnAvailabilityChanged;
        _service.HistoryCapabilitiesChanged += OnAvailabilityChanged;
        if (_frame is not null) _frame.Navigating += OnNavigating;
        if (_sourceSession is not null) _sourceSession.SourcesChanged += OnSourcesChanged;
        try
        {
            if (!Owns()) return;
            _plan = await _service.PrepareItemHistoryActionAsync(_scope, _item, _action, _stamp, _lifetime.Token);
            if (!Owns()) return;
            SummaryText.Text = _plan.Summary;
            WarningText.Text = _plan.Warning;
            RecordDetails.Text = _plan.Details;
            IsPrimaryButtonEnabled = true;
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
        var deferral = args.GetDeferral();
        _submitting = true;
        IsPrimaryButtonEnabled = false;
        Activity.Visibility = Visibility.Visible;
        CloseButtonText = "";
        WarningText.Text = "正在提交；请勿重复操作。即使窗口关闭，也不会撤销 MP 已接收的操作。";
        try
        {
            // Page/source ownership is checked again immediately before the service consumes this confirmation.
            if (!Owns()) return;
            var result = await _service.SubmitItemHistoryActionAsync(_scope, plan, _lifetime.Token);
            if (_closed) return;
            SummaryText.Text = result.Summary;
            WarningText.Text = result.Problem;
            RecordDetails.Text = result.Smart ? plan.Details : string.Join("\n\n", result.Receipts.Select(receipt => receipt.Display));
            RecordDetails.Header = "MP 操作回执";
            Notice.Severity = result.NeedsReview ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
            Notice.Message = result.Smart ? "任务已提交，文件处理完成情况请到 MP 核对" : result.NeedsReview ? "部分结果待核对；不会整批重试" : "删除范围已确认完成";
            Notice.IsOpen = true;
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
            }
        }
    }

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
        _service.AvailabilityChanged -= OnAvailabilityChanged;
        _service.HistoryCapabilitiesChanged -= OnAvailabilityChanged;
        if (_frame is not null) _frame.Navigating -= OnNavigating;
        if (_sourceSession is not null) _sourceSession.SourcesChanged -= OnSourcesChanged;
        _lifetime.Cancel();
    }
}
