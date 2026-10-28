using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Momoka.MoviePilot;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.ViewModels;

public sealed partial class MoviePilotSeriesReorganizeViewModel : PageViewModel
{
    private MoviePilotService? _service;
    private MoviePilotTransferContext? _context;
    private string _connection = "";
    private readonly CancellationTokenSource _lifetime = new();
    private bool _released;

    public ObservableCollection<MoviePilotSeriesTransferRow> Rows { get; } = [];
    public string Title => _context?.Title ?? "";
    public string Query { get; private set; } = "";
    public string CountLabel => $"共 {Rows.Count} 条资源";
    public string SelectionLabel => $"已选 {SelectedCount} 条";
    public string SubmitLabel => SelectedCount == 0 ? "选择要重新整理的资源" : $"批量重新整理（{SelectedCount}）";
    public bool CanSelect => !_released && !Busy && Rows.Count > 0;
    public bool CanContinue => CanSelect && SelectedCount > 0 && _service?.Available == true && _service.IsCurrentConnection(_connection);
    public Visibility EmptyVisibility => Show(IsReady && !Busy && !NoticeOpen && Rows.Count == 0);
    public Visibility SelectionVisibility => Show(Rows.Count > 0);

    [ObservableProperty]
    public partial int SelectedCount { get; set; }

    internal void Attach(MoviePilotService service, MoviePilotTransferContext context)
    {
        Query = MoviePilotSeriesTransfer.Query(context.MediaId);
        _service = service;
        _context = context;
        _connection = service.ConnectionStamp;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Query));
    }

    public override async Task ReloadAsync()
    {
        if (_released || _service is null || _context is null) return;
        var token = BeginLoad();
        ClearRows();
        ClearNotice();
        Announce();
        try
        {
            var rows = await _service.FindSeriesTransferHistoriesAsync(_context.MediaId, token).ConfigureAwait(true);
            if (!IsCurrent(token) || _released || !_service.Available || !_service.IsCurrentConnection(_connection)) return;
            foreach (var history in rows) Rows.Add(new MoviePilotSeriesTransferRow(history));
            Announce();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (IsCurrent(token) && !_released) Report("读取当前剧的整理资源失败", error);
        }
        finally
        {
            EndLoad(token);
            if (!_released) Announce();
        }
    }

    internal IReadOnlyList<MoviePilotTransferHistory> Select(IEnumerable<MoviePilotSeriesTransferRow> selected)
    {
        if (!CanContinue || _context is null) throw new MoviePilotException("请重新读取当前剧的资源并选择整理记录");
        var selection = selected.ToHashSet();
        var histories = Rows.Where(selection.Contains).Select(row => row.History).ToArray();
        MoviePilotSeriesTransfer.CreateContext(_context, histories);
        return histories;
    }

    internal void SelectionProblem(MoviePilotException error)
    {
        Notify(null, error.Message, Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
        Announce();
    }

    internal Task LoadArtworkAsync(MoviePilotSeriesTransferRow row) => _released || _service is null || !Rows.Contains(row)
        ? Task.CompletedTask : row.LoadArtworkAsync(_service, _connection, _lifetime.Token);

    partial void OnSelectedCountChanged(int value) => Announce();
    protected override void BusyChanged() => Announce();
    private void Announce()
    {
        OnPropertyChanged(nameof(CountLabel));
        OnPropertyChanged(nameof(SelectionLabel));
        OnPropertyChanged(nameof(SubmitLabel));
        OnPropertyChanged(nameof(CanSelect));
        OnPropertyChanged(nameof(CanContinue));
        OnPropertyChanged(nameof(EmptyVisibility));
        OnPropertyChanged(nameof(SelectionVisibility));
    }
    private void ClearRows()
    {
        foreach (var row in Rows) row.Retire();
        Rows.Clear();
        SelectedCount = 0;
    }
    public override void Dispose()
    {
        if (_released) return;
        _released = true;
        _lifetime.Cancel();
        ClearRows();
        base.Dispose();
    }
}
