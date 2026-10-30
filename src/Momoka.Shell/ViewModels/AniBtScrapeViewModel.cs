using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Momoka.AniBt;
using Momoka.Emby;
using Momoka.MoviePilot;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.ViewModels;

public sealed partial class AniBtScrapeRowViewModel : ObservableObject
{
    internal AniBtScrapeRowViewModel(AniBtScrapeRow row)
    {
        TargetPath = row.TargetPath;
        SourceName = row.SourceName;
        Candidates = row.Match.Candidates;
        Selected = row.Match.DefaultCandidate;
        MatchNote = row.Match.AutomaticCandidate is not null
            ? "自动识别"
            : Candidates.Count == 0 ? "未找到匹配"
            : Candidates.Count > 1 ? $"{Candidates.Count} 个候选"
            : "已匹配";
    }

    public string TargetPath { get; }
    public string SourceName { get; }
    public string MatchNote { get; }
    public IReadOnlyList<AniBtAnime> Candidates { get; }
    [ObservableProperty] public partial AniBtAnime? Selected { get; set; }
    [ObservableProperty] public partial string Result { get; set; } = "";
    [ObservableProperty] public partial bool CanSelect { get; set; } = true;
    [ObservableProperty] public partial string PreviewHeading { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewVisibility))]
    public partial string PreviewDetails { get; set; } = "";
    public Visibility PreviewVisibility => PreviewDetails.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    [ObservableProperty] public partial string DestinationTitle { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EpisodeVisibility))]
    public partial string EpisodeLabel { get; set; } = "";
    [ObservableProperty] public partial string TransferMethod { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SourceVisibility))]
    public partial string SourceDirectory { get; set; } = "";
    [ObservableProperty] public partial string DestinationDirectory { get; set; } = "";
    [ObservableProperty] public partial string DestinationFile { get; set; } = "";
    [ObservableProperty] public partial string TargetWarning { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultVisibility))]
    public partial string DisplayResult { get; set; } = "";
    public Visibility EpisodeVisibility => EpisodeLabel.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SourceVisibility => SourceDirectory.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ResultVisibility => DisplayResult.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    internal void ShowTarget(AniBtScrapeConfirmationRow target)
    {
        PreviewHeading = target.Heading;
        PreviewDetails = target.Details;
        DestinationTitle = target.Title;
        EpisodeLabel = target.EpisodeLabel;
        TransferMethod = target.Method;
        SourceDirectory = target.SourcePath.Length > 0 ? MoviePilotTransfer.ParentPath(target.SourcePath) : "";
        DestinationDirectory = target.SourcePath.Length > 0
            ? MoviePilotTransfer.ParentPath(target.DestinationPath) : target.DestinationPath;
        DestinationFile = target.SourcePath.Length > 0 ? MoviePilotTransfer.PathName(target.DestinationPath) : "";
        TargetWarning = target.Warning;
    }

    internal void ClearTarget()
    {
        PreviewHeading = "";
        PreviewDetails = "";
        DestinationTitle = "";
        EpisodeLabel = "";
        TransferMethod = "";
        SourceDirectory = "";
        DestinationDirectory = "";
        DestinationFile = "";
        TargetWarning = "";
    }
}

public sealed partial class AniBtScrapeViewModel : PageViewModel
{
    private AniBtService? _service;
    private EmbySessionScope? _scope;
    private EmbyConnection? _connection;
    private AniBtScrapePlan? _plan;
    private AniBtPreparedScrapePlan? _prepared;
    private bool _attempted;
    private bool _active;
    private int _selectionVersion;

    public override Task ReloadAsync() => Task.CompletedTask;
    public ObservableCollection<AniBtScrapeRowViewModel> Rows { get; } = [];
    public bool CanPreview => _active && !Busy && !_attempted && _plan is not null &&
        _scope is { IsCurrent: true } scope && _connection is not null &&
        scope.CurrentConnection.IsSameIdentityAs(_connection) && Rows.Count > 0 && Rows.All(row => row.Selected is not null);
    public bool CanSubmit => CanPreview && _prepared is { CanSubmit: true } &&
        _prepared.MatchesChoices(Rows.Select(row => row.Selected!).ToArray());
    public bool CanAct => CanSubmit;
    public string PrimaryButtonText => "重新整理";

    /// <summary>这一趟走到哪一步了；比一条不确定进度条更能解释「为什么还没好」。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StageVisibility))]
    public partial string Stage { get; set; } = "";

    public Visibility StageVisibility => Stage.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RetryPrepareVisibility => CanPreview && _prepared is null && NoticeOpen
        ? Visibility.Visible : Visibility.Collapsed;

    internal async Task LoadAsync(AniBtService service, EmbySessionScope scope, EmbyItem item)
    {
        Cancel();
        _service = service;
        _scope = scope;
        _active = true;
        _attempted = false;
        _plan = null;
        _connection = null;
        Rows.Clear();
        var token = BeginLoad();
        Stage = "正在收集可整理的视频文件…";
        AnnounceActions();
        try
        {
            scope.ThrowIfNotCurrent();
            var connection = scope.CurrentConnection;
            var plan = await scope.ExecuteAsync((client, cancellation) => service.PrepareAsync(client, item, cancellation), token);
            if (!IsCurrent(token) || !scope.IsCurrent || !_active ||
                !scope.CurrentConnection.IsSameIdentityAs(connection)) return;
            _connection = connection;
            _plan = plan;
            foreach (var row in plan.Rows)
            {
                var model = new AniBtScrapeRowViewModel(row);
                model.PropertyChanged += async (_, args) =>
                {
                    if (args.PropertyName != nameof(AniBtScrapeRowViewModel.Selected)) return;
                    CancelPreview();
                    await PreviewAsync();
                };
                Rows.Add(model);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (IsCurrent(token)) { Stage = ""; Report("AniBT 识别失败", error); } }
        finally { EndLoad(token); AnnounceActions(); }
        // 默认候选已经选中，自动读取同一份实际整理目标；点击主按钮只负责提交。
        if (IsCurrent(token) && _active)
        {
            Stage = "正在读取整理目标和刮削设置…";
            await PreviewAsync();
        }
        else if (IsCurrent(token)) Stage = "";
    }

    internal async Task PreviewAsync()
    {
        if (!CanPreview || _service is null || _scope is not { IsCurrent: true } scope || _plan is null)
        {
            Stage = "";
            return;
        }
        CancelPreview();
        var version = _selectionVersion;
        var plan = _plan;
        var connection = scope.CurrentConnection;
        var choices = Rows.Select(row => row.Selected!).ToArray();
        var token = BeginLoad();
        Stage = "正在读取整理目标和刮削设置…";
        AnnounceActions();
        try
        {
            var prepared = await scope.ExecuteAsync((client, cancellation) =>
                _service.PrepareForConfirmationAsync(client, plan, choices, cancellation,
                    () => Current(scope, connection, version)), token);
            if (!IsCurrent(token) || !Current(scope, connection, version) ||
                !prepared.MatchesChoices(Rows.Select(row => row.Selected!).ToArray()))
            {
                prepared.Invalidate();
                return;
            }
            _prepared = prepared;
            for (var index = 0; index < Rows.Count; index++)
            {
                Rows[index].ShowTarget(prepared.Rows[index]);
            }
            ClearNotice();
            Stage = "";
        }
        catch (OperationCanceledException) { if (IsCurrent(token)) Stage = ""; }
        catch (Exception error) { if (IsCurrent(token)) { Stage = ""; Report("整理目标读取失败，本次没有写入", error); } }
        finally { EndLoad(token); AnnounceActions(); }
    }

    internal async Task SubmitAsync()
    {
        if (!CanSubmit || _service is null || _scope is not { IsCurrent: true } scope || _prepared is null) return;
        var prepared = _prepared;
        var connection = scope.CurrentConnection;
        var version = _selectionVersion;
        _attempted = true;
        foreach (var row in Rows) row.CanSelect = false;
        var token = BeginLoad();
        Stage = "正在重新整理并自动刮削，MoviePilot 可能需要一段时间…";
        AnnounceActions();
        try
        {
            scope.ThrowIfNotCurrent();
            var results = await scope.ExecuteAsync((client, cancellation) =>
                _service.SubmitAsync(client, prepared, cancellation,
                    () => Current(scope, connection, version) &&
                        prepared.MatchesChoices(Rows.Select(row => row.Selected!).ToArray())), token);
            if (!IsCurrent(token)) return;
            foreach (var result in results)
            {
                var row = Rows.FirstOrDefault(row => row.TargetPath == result.TargetPath);
                if (row is not null)
                {
                    row.Result = result.Message;
                    row.DisplayResult = result.DisplayMessage.Length > 0 ? result.DisplayMessage : result.Message;
                }
            }
            Notify(null, $"已处理 {results.Count(result => result.Success)} / {Rows.Count} 个文件，请查看逐文件结果。",
                results.All(result => result.Success) ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }
        catch (OperationCanceledException error)
        {
            if (IsCurrent(token) && _active) Report("操作已取消或连接改变，请核对已提交结果", error);
        }
        catch (Exception error) { if (IsCurrent(token)) Report("重新整理未完成，请核对 MoviePilot 与 Emby 后再操作", error); }
        finally { Stage = ""; EndLoad(token); AnnounceActions(); }
    }

    internal void CancelPreview()
    {
        _selectionVersion++;
        _prepared?.Invalidate();
        _prepared = null;
        // 改选/关闭立即取消在途只读请求，迟到回话不能恢复旧目标或提交按钮。
        base.Cancel();
        Stage = "";
        foreach (var row in Rows)
        {
            row.ClearTarget();
        }
        ClearNotice();
        AnnounceActions();
    }

    public override void Cancel()
    {
        _active = false;
        CancelPreview();
        base.Cancel();
        AnnounceActions();
    }

    private bool Current(EmbySessionScope scope, EmbyConnection connection, int version) =>
        _active && scope.IsCurrent && scope.CurrentConnection.IsSameIdentityAs(connection) && version == _selectionVersion;

    internal void RefreshActions() => AnnounceActions();

    protected override void BusyChanged() => AnnounceActions();

    private void AnnounceActions()
    {
        OnPropertyChanged(nameof(CanPreview));
        OnPropertyChanged(nameof(CanSubmit));
        OnPropertyChanged(nameof(CanAct));
        OnPropertyChanged(nameof(PrimaryButtonText));
        OnPropertyChanged(nameof(RetryPrepareVisibility));
    }
}
