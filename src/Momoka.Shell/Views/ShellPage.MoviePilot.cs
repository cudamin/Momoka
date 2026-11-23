using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class ShellPage
{
    private MoviePilotService? _moviePilotService;
    private readonly DispatcherTimer _moviePilotTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private CancellationTokenSource? _moviePilotCheck;
    private ContentDialog? _moviePilotDialog;
    public event EventHandler? MoviePilotAvailabilityChanged;
    public bool MoviePilotMediaManagementEnabled => _moviePilotService?.CanManageHistory == true;
    public bool MoviePilotSmartReorganizeEnabled => _moviePilotService?.CanSmartReorganize == true;
    private bool CalendarAvailable => MoviePilotCalendarViewModel.HasMoviePilot(_moviePilotService, Settings.MoviePilot)
        || MoviePilotCalendarViewModel.HasAniBt(Settings.MoviePilot);

    private void OpenMoviePilotSubscriptionsAfterSubscribe(MoviePilotMedia media)
    {
        if (_moviePilotService is not { Available: true } service || !service.IsCurrentConnection(media.ConnectionStamp)) return;
        GoTo(media.Type == "电视剧" ? Momoka.Emby.HomeLayout.MoviePilotSeries : Momoka.Emby.HomeLayout.MoviePilotMovies, force: true);
    }

    private void StartMoviePilotMonitoring(MoviePilotService service)
    {
        _moviePilotService = service;
        service.AvailabilityChanged += OnMoviePilotAvailabilityChanged;
        service.HistoryCapabilitiesChanged += OnMoviePilotHistoryCapabilitiesChanged;
        Settings.MoviePilot.ConnectionChanged += OnMoviePilotConfigurationChanged;
        Settings.MoviePilot.AniBtChanged += OnMoviePilotAniBtChanged;
        Settings.MoviePilot.CalendarChanged += OnMoviePilotCalendarChanged;
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
        try
        {
            if (await service.RefreshAvailabilityAsync(check.Token))
                await service.HistoryCapabilitiesAsync(check.Token);
        }
        catch (OperationCanceledException) { }
        catch (MoviePilotException error) { Diagnostics.Log.Info("moviepilot", "无法读取媒体管理能力：" + Diagnostics.Failure.Describe(error)); }
        finally { _moviePilotCheck = null; }
    }

    private void OnMoviePilotHistoryCapabilitiesChanged(object? sender, EventArgs args) => DispatcherQueue.TryEnqueue(() =>
        MoviePilotAvailabilityChanged?.Invoke(this, EventArgs.Empty));

    private void OnMoviePilotConfigurationChanged(object? sender, EventArgs args)
    {
        _moviePilotCheck?.Cancel();
        _moviePilotTimer.Stop();
        _moviePilotTimer.Interval = TimeSpan.FromMilliseconds(700);
        _moviePilotTimer.Start();
    }

    private void OnMoviePilotAniBtChanged(object? sender, EventArgs args) => DispatcherQueue.TryEnqueue(() =>
    {
        MoviePilotAvailabilityChanged?.Invoke(this, EventArgs.Empty);
        SyncCalendarAvailability();
    });

    private void OnMoviePilotCalendarChanged(object? sender, EventArgs args) => DispatcherQueue.TryEnqueue(SyncCalendarAvailability);

    private void SyncCalendarAvailability()
    {
        SyncSidebar();
        if (ContentFrame.Content is MoviePilotCalendarPage calendar)
        {
            if (CalendarAvailable) calendar.ViewModel.RefreshSources();
            else GoTo("home");
        }
        PruneMoviePilotHistory();
        SyncChrome();
    }

    private void OnMoviePilotAvailabilityChanged(object? sender, EventArgs args) => DispatcherQueue.TryEnqueue(() =>
    {
        MoviePilotAvailabilityChanged?.Invoke(this, EventArgs.Empty);
        if (_moviePilotService is { Available: false })
        {
            _moviePilotWindow?.Hide();
            _moviePilotDialog?.Hide();
            if (IsMoviePilotPage(ContentFrame.Content?.GetType())) GoTo("home");
        }
        SyncCalendarAvailability();
    });

    /// <summary>连不上 MoviePilot 时不能再留着的两页：订阅列表（侧边栏那两条入口）和一条订阅的详情。</summary>
    private static bool IsMoviePilotPage(Type? type) =>
        type == typeof(MoviePilotSubscriptionPage) || type == typeof(MoviePilotSubscriptionsPage);

    private bool IsUnavailableMoviePilotPage(Type type) => type == typeof(MoviePilotCalendarPage)
        ? !CalendarAvailable : _moviePilotService?.Available != true && IsMoviePilotPage(type);

    private void PruneMoviePilotHistory()
    {
        for (var index = ContentFrame.BackStack.Count - 1; index >= 0; index--)
        {
            if (!IsUnavailableMoviePilotPage(ContentFrame.BackStack[index].SourcePageType)) continue;
            ContentFrame.BackStack.RemoveAt(index);
            if (index < _backTrails.Count) _backTrails.RemoveAt(index);
        }
        for (var index = ContentFrame.ForwardStack.Count - 1; index >= 0; index--)
        {
            if (!IsUnavailableMoviePilotPage(ContentFrame.ForwardStack[index].SourcePageType)) continue;
            ContentFrame.ForwardStack.RemoveAt(index);
            if (index < _forwardTrails.Count) _forwardTrails.RemoveAt(index);
        }
    }

    private void StopMoviePilotMonitoring()
    {
        _moviePilotEpisodeDelete?.Cancel();
        _moviePilotTimer.Stop();
        _moviePilotTimer.Tick -= CheckMoviePilot;
        _moviePilotCheck?.Cancel();
        Settings.MoviePilot.ConnectionChanged -= OnMoviePilotConfigurationChanged;
        Settings.MoviePilot.AniBtChanged -= OnMoviePilotAniBtChanged;
        Settings.MoviePilot.CalendarChanged -= OnMoviePilotCalendarChanged;
        if (_moviePilotService is { } service)
        {
            service.AvailabilityChanged -= OnMoviePilotAvailabilityChanged;
            service.HistoryCapabilitiesChanged -= OnMoviePilotHistoryCapabilitiesChanged;
        }
        _moviePilotService = null;
    }
}
