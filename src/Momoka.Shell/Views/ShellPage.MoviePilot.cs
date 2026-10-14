using Momoka.MoviePilot;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

public sealed partial class ShellPage
{
    private MoviePilotService? _moviePilotService;
    private readonly DispatcherTimer _moviePilotTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private CancellationTokenSource? _moviePilotCheck;
    private ContentDialog? _moviePilotDialog;
    public event EventHandler? MoviePilotAvailabilityChanged;

    private void StartMoviePilotMonitoring(MoviePilotService service)
    {
        _moviePilotService = service;
        service.AvailabilityChanged += OnMoviePilotAvailabilityChanged;
        Settings.MoviePilot.ConnectionChanged += OnMoviePilotConfigurationChanged;
        _moviePilotTimer.Tick += CheckMoviePilot;
        _moviePilotTimer.Start();
        CheckMoviePilot(null, null);
    }

    private async void CheckMoviePilot(object? sender, object? args)
    {
        if (_moviePilotService is not { } service || _moviePilotCheck is not null) return;
        _moviePilotTimer.Interval = TimeSpan.FromSeconds(30);
        using var check = new CancellationTokenSource();
        _moviePilotCheck = check;
        try { await service.RefreshAvailabilityAsync(check.Token); }
        catch (OperationCanceledException) { }
        finally { _moviePilotCheck = null; }
    }

    private void OnMoviePilotConfigurationChanged(object? sender, EventArgs args)
    {
        _moviePilotCheck?.Cancel();
        _moviePilotTimer.Stop();
        _moviePilotTimer.Interval = TimeSpan.FromMilliseconds(700);
        _moviePilotTimer.Start();
    }

    private void OnMoviePilotAvailabilityChanged(object? sender, EventArgs args) => DispatcherQueue.TryEnqueue(() =>
    {
        MoviePilotAvailabilityChanged?.Invoke(this, EventArgs.Empty);
        // 侧边栏那两条订阅入口跟的是可用性，而**两个方向都要重算**：连上时它们要出现，断线时它们要收起。
        // 所以这一句不能挪到下面那道「不可用才继续」的门里面 —— 那道门管的是「收起已经打开的附属界面」，
        // 只走一个方向。
        SyncSidebar();
        if (_moviePilotService is null || _moviePilotService.Available) return;
        _moviePilotWindow?.Hide();
        _moviePilotDialog?.Hide();
        if (IsMoviePilotPage(ContentFrame.Content?.GetType())) GoTo("home");
        PruneMoviePilotHistory();
        SyncChrome();
    });

    /// <summary>连不上 MoviePilot 时不能再留着的两页：订阅列表（侧边栏那两条入口）和一条订阅的详情。</summary>
    private static bool IsMoviePilotPage(Type? type) =>
        type == typeof(MoviePilotSubscriptionPage) || type == typeof(MoviePilotSubscriptionsPage);

    private void PruneMoviePilotHistory()
    {
        if (_moviePilotService?.Available == true) return;
        for (var index = ContentFrame.BackStack.Count - 1; index >= 0; index--)
        {
            if (!IsMoviePilotPage(ContentFrame.BackStack[index].SourcePageType)) continue;
            ContentFrame.BackStack.RemoveAt(index);
            if (index < _backTrails.Count) _backTrails.RemoveAt(index);
        }
        for (var index = ContentFrame.ForwardStack.Count - 1; index >= 0; index--)
        {
            if (!IsMoviePilotPage(ContentFrame.ForwardStack[index].SourcePageType)) continue;
            ContentFrame.ForwardStack.RemoveAt(index);
            if (index < _forwardTrails.Count) _forwardTrails.RemoveAt(index);
        }
    }

    private void StopMoviePilotMonitoring()
    {
        _moviePilotTimer.Stop();
        _moviePilotTimer.Tick -= CheckMoviePilot;
        _moviePilotCheck?.Cancel();
        Settings.MoviePilot.ConnectionChanged -= OnMoviePilotConfigurationChanged;
        if (_moviePilotService is { } service) service.AvailabilityChanged -= OnMoviePilotAvailabilityChanged;
        _moviePilotService = null;
    }
}
