using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Diagnostics;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class ShellPage
{
    private MoviePilotService? _moviePilotService;
    private readonly DispatcherTimer _moviePilotTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private CancellationTokenSource? _moviePilotCheck;
    private CancellationTokenSource? _moviePilotCalendarRefresh;
    private bool _moviePilotCalendarRefreshOnLaunch;
    private bool _moviePilotCalendarRefreshStarted;
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
        // 「启动时自动刷新日历」在这一次启动里只按开程序那一刻的设置算：拨开关本身不立刻联网，下次启动才生效。
        _moviePilotCalendarRefreshOnLaunch = Settings.MoviePilot.CalendarEnabled && Settings.MoviePilot.CalendarRefreshOnStartup;
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
            {
                // 「每次打开 momoka 自动在后台刷新日历」：连上之后只跑一趟，结果写进本地缓存，打开日历页先看到它。
                _ = RefreshCalendarOnStartupAsync(service);
                await service.HistoryCapabilitiesAsync(check.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (MoviePilotException error) { Log.Info("moviepilot", "无法读取媒体管理能力：" + Failure.Describe(error)); }
        finally { _moviePilotCheck = null; }
    }

    /// <summary>
    /// 启动后台刷新本月日历。开关（设置 → MoviePilot →「启动时自动刷新日历」）默认关闭；关着时这一趟什么都不做，
    /// 日历缓存只由用户真正打开日历页时的读取积累。
    /// <para>
    /// 每一次启动只跑一趟：可用性检查每 30 秒循环，不设这道门就会反复请求。失败只写日志 —— 这是程序自己动的，
    /// 不该弹提示；旧缓存继续留着，打开日历页仍会自己重读。
    /// </para>
    /// </summary>
    private async Task RefreshCalendarOnStartupAsync(MoviePilotService service)
    {
        if (_moviePilotCalendarRefreshStarted || !_moviePilotCalendarRefreshOnLaunch) return;
        _moviePilotCalendarRefreshStarted = true;
        if (!Settings.MoviePilot.CalendarEnabled) return;

        using var refresh = new CancellationTokenSource();
        _moviePilotCalendarRefresh = refresh;
        try
        {
            var (start, end) = MoviePilotCalendarLayout.Window(
                MoviePilotCalendarLayout.Month(DateOnly.FromDateTime(DateTime.Today)));
            var entries = await service.CalendarAsync(start, end, refresh.Token);
            Log.Info("moviepilot", $"启动时已刷新本月日历缓存：{entries.Count} 项");
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Log.Info("moviepilot", "启动刷新日历缓存失败：" + Failure.Describe(error)); }
        finally { _moviePilotCalendarRefresh = null; }
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
        _moviePilotCalendarRefresh?.Cancel();
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
