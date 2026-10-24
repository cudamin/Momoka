using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Momoka.AniBt;
using Momoka.Emby;
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
            ? "AniBT 自动识别"
            : Candidates.Count == 0 ? "没有匹配结果，请关闭后手动核对"
            : Candidates.Count > 1 ? "有多个候选，已选择第一个，请核对"
            : "已选择识别结果，请核对";
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
    public bool CanAct => CanPreview;
    public string PrimaryButtonText => _prepared is null ? "预览计划" : "确认执行";
    public Visibility CancelPreviewVisibility => _active && !_attempted && _prepared is not null ? Visibility.Visible : Visibility.Collapsed;

    internal async Task LoadAsync(AniBtService service, EmbySessionScope scope, EmbyItem item)
    {
        Cancel();
        _service = service;
        _scope = scope;
        _active = true;
        _attempted = false;
        _plan = null;
        Rows.Clear();
        var token = BeginLoad();
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
                model.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(AniBtScrapeRowViewModel.Selected)) CancelPreview();
                };
                Rows.Add(model);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (IsCurrent(token)) Report("AniBT 识别失败", error); }
        finally { EndLoad(token); AnnounceActions(); }
    }

    internal async Task PreviewAsync()
    {
        if (!CanPreview || _service is null || _scope is not { IsCurrent: true } scope || _plan is null) return;
        CancelPreview();
        var version = _selectionVersion;
        var plan = _plan;
        var connection = scope.CurrentConnection;
        var choices = Rows.Select(row => row.Selected!).ToArray();
        var token = BeginLoad();
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
                Rows[index].PreviewHeading = prepared.Rows[index].Heading;
                Rows[index].PreviewDetails = prepared.Rows[index].Details;
            }
            Notify("请核对实际计划", "上面的最终 Emby 目标或 MoviePilot 源、目标、方式和清理范围已准备好。只有点击“确认执行”才会写入；改选或取消必须重新预览。",
                InfoBarSeverity.Warning);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (IsCurrent(token)) Report("预览失败，本次没有写入", error); }
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
                if (row is not null) row.Result = result.Message;
            }
            Notify(null, $"已处理 {results.Count(result => result.Success)} / {Rows.Count} 个文件，请查看逐文件结果。",
                results.All(result => result.Success) ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }
        catch (OperationCanceledException error)
        {
            if (IsCurrent(token) && _active) Report("操作已取消或连接改变，请核对已提交结果", error);
        }
        catch (Exception error) { if (IsCurrent(token)) Report("重新整理未完成，请核对 MoviePilot 与 Emby 后再操作", error); }
        finally { EndLoad(token); AnnounceActions(); }
    }

    internal void CancelPreview()
    {
        _selectionVersion++;
        _prepared?.Invalidate();
        _prepared = null;
        foreach (var row in Rows)
        {
            row.PreviewHeading = "";
            row.PreviewDetails = "";
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
        OnPropertyChanged(nameof(CancelPreviewVisibility));
    }
}
