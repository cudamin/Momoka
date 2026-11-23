using Momoka.Configuration;
using Momoka.MoviePilot;
using Momoka.Shell.Views;

namespace Momoka.Shell.ViewModels;

/// <summary>日历窗口、选择与请求生命周期；翻月和离页都使上一趟读取失效。</summary>
public sealed class MoviePilotCalendarViewModel : PageViewModel
{
    private MoviePilotService? _service;
    private MoviePilotSettings? _settings;
    private IReadOnlyList<MoviePilotCalendarEntry> _entries = [];
    private IReadOnlyList<MoviePilotCalendarEntry> _visibleEntries = [];
    private int _mediaType;
    private long _connectionRevision;

    internal event Action? Changed;
    internal DateOnly Month { get; private set; } = MoviePilotCalendarLayout.Month(DateOnly.FromDateTime(DateTime.Today));
    internal DateOnly SelectedDate { get; private set; } = DateOnly.FromDateTime(DateTime.Today);
    internal IReadOnlyList<MoviePilotCalendarEntry> Entries => _visibleEntries;
    internal int MediaType
    {
        get => _mediaType;
        set
        {
            if (_mediaType == value) return;
            _mediaType = value;
            UpdateEntries();
            Changed?.Invoke();
        }
    }

    internal void Attach(MoviePilotService service, MoviePilotSettings settings)
    {
        Release();
        _service = service;
        _settings = settings;
        settings.ConnectionChanged += OnConnectionChanged;
        settings.CalendarChanged += OnConnectionChanged;
        service.AvailabilityChanged += OnConnectionChanged;
        _ = ReloadAsync();
    }

    private void OnConnectionChanged(object? sender, EventArgs args) => Interlocked.Increment(ref _connectionRevision);

    public override async Task ReloadAsync()
    {
        if (_service is not { Available: true } service || _settings?.CalendarEnabled != true) return;
        var token = BeginLoad();
        var revision = Interlocked.Read(ref _connectionRevision);
        var (start, end) = MoviePilotCalendarLayout.Window(Month);

        // 上次成功读取留下的同一窗口先摆上屏。仍在读最新安排，所以 Busy 保持——用户看到的是旧内容加一条进度，
        // 而不是空白页；网络失败时这份旧内容也继续留着，不会因为一次刷新失败把整月清空。
        _entries = service.CachedCalendar(start, end) ?? [];
        UpdateEntries();
        Changed?.Invoke();
        try
        {
            var entries = await service.CalendarAsync(start, end, token);
            if (!IsCurrent(token) || revision != Interlocked.Read(ref _connectionRevision) || !service.Available || _settings?.CalendarEnabled != true
                || entries.Any(entry => !service.IsCurrentConnection(entry.ConnectionStamp))) return;
            _entries = entries;
            UpdateEntries();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (IsCurrent(token) && revision == Interlocked.Read(ref _connectionRevision)) Report("日历读取失败", error);
        }
        finally
        {
            EndLoad(token);
            if (IsCurrent(token)) Changed?.Invoke();
        }
    }

    internal void MoveMonth(int delta)
    {
        var target = ((long)Month.Year * 12) + Month.Month + delta;
        if (target < 25 || target > (9998 * 12) + 12) return;
        Month = Month.AddMonths(delta);
        SelectedDate = Month;
        _ = ReloadAsync();
    }

    internal void Today()
    {
        SelectedDate = DateOnly.FromDateTime(DateTime.Today);
        Month = MoviePilotCalendarLayout.Month(SelectedDate);
        _ = ReloadAsync();
    }

    internal void SelectDate(DateOnly date)
    {
        SelectedDate = date;
        Changed?.Invoke();
    }

    public override void Cancel()
    {
        base.Cancel();
        _entries = [];
        UpdateEntries();
    }

    private void UpdateEntries() => _visibleEntries = MoviePilotCalendarLayout.Filter(_entries, _mediaType);

    internal void Release()
    {
        Cancel();
        Interlocked.Increment(ref _connectionRevision);
        if (_settings is { } settings)
        {
            settings.ConnectionChanged -= OnConnectionChanged;
            settings.CalendarChanged -= OnConnectionChanged;
        }
        if (_service is { } service) service.AvailabilityChanged -= OnConnectionChanged;
        _service = null;
        _settings = null;
    }

    public override void Dispose()
    {
        Release();
        base.Dispose();
    }
}
