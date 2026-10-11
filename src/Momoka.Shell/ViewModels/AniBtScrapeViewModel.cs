using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Momoka.AniBt;
using Momoka.Emby;

namespace Momoka.Shell.ViewModels;

public sealed partial class AniBtScrapeRowViewModel : ObservableObject
{
    internal AniBtScrapeRowViewModel(AniBtScrapeRow row)
    {
        TargetPath = row.TargetPath;
        SourceName = row.SourceName;
        Candidates = row.Match.Candidates;
        Selected = row.Match.AutomaticCandidate;
        MatchNote = row.Match.AutomaticCandidate is null
            ? Candidates.Count == 0 ? "没有匹配结果，请在 MoviePilot 手动核对" : "存在多个候选，请选择对应番剧"
            : "AniBT 自动识别";
    }

    public string TargetPath { get; }
    public string SourceName { get; }
    public string MatchNote { get; }
    public IReadOnlyList<AniBtAnime> Candidates { get; }
    [ObservableProperty] public partial AniBtAnime? Selected { get; set; }
    [ObservableProperty] public partial string Result { get; set; } = "";
    [ObservableProperty] public partial bool CanSelect { get; set; } = true;
}

public sealed partial class AniBtScrapeViewModel : PageViewModel
{
    private AniBtService? _service;
    private EmbySessionScope? _scope;
    private AniBtScrapePlan? _plan;
    private bool _attempted;
    public override Task ReloadAsync() => Task.CompletedTask;
    public ObservableCollection<AniBtScrapeRowViewModel> Rows { get; } = [];
    public bool CanSubmit => !Busy && !_attempted && _plan is not null && Rows.Count > 0 && Rows.All(row => row.Selected is not null);

    internal async Task LoadAsync(AniBtService service, EmbySessionScope scope, EmbyItem item)
    {
        _service = service;
        _scope = scope;
        var token = BeginLoad();
        OnPropertyChanged(nameof(CanSubmit));
        try
        {
            var plan = await scope.ExecuteAsync((client, cancellation) => service.PrepareAsync(client, item, cancellation), token);
            if (!IsCurrent(token) || !scope.IsCurrent) return;
            _plan = plan;
            foreach (var row in plan.Rows)
            {
                var model = new AniBtScrapeRowViewModel(row);
                model.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(AniBtScrapeRowViewModel.Selected)) OnPropertyChanged(nameof(CanSubmit));
                };
                Rows.Add(model);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (IsCurrent(token)) Report("AniBT 识别失败", error); }
        finally { EndLoad(token); OnPropertyChanged(nameof(CanSubmit)); }
    }

    internal async Task SubmitAsync()
    {
        if (!CanSubmit || _service is null || _scope is not { IsCurrent: true } scope || _plan is null) return;
        var choices = Rows.Select(row => row.Selected!).ToArray();
        _attempted = true;
        foreach (var row in Rows) row.CanSelect = false;
        var token = BeginLoad();
        OnPropertyChanged(nameof(CanSubmit));
        try
        {
            scope.ThrowIfNotCurrent();
            var results = await _service.SubmitAsync(_plan, choices, token, () => scope.IsCurrent);
            if (!IsCurrent(token)) return;
            foreach (var result in results)
            {
                var row = Rows.FirstOrDefault(row => row.TargetPath == result.TargetPath);
                if (row is not null) row.Result = result.Message;
            }
            Notify(null, $"MoviePilot 已处理 {results.Count(result => result.Success)} / {Rows.Count} 个刮削请求，请查看逐文件结果。",
                results.All(result => result.Success) ? Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success : Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (IsCurrent(token)) Report("刮削未完成，请核对 MoviePilot 后再操作", error); }
        finally { EndLoad(token); OnPropertyChanged(nameof(CanSubmit)); }
    }
}
