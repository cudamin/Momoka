using Microsoft.UI.Xaml.Controls;
using Momoka.AniBt;
using Momoka.Configuration;
using Momoka.MoviePilot;
using Momoka.Shell.Localization;
using Momoka.Shell.Views;

namespace Momoka.Shell.ViewModels;

internal enum CalendarSource { MoviePilot, AniBt }

internal sealed record CalendarCard(DateOnly Date, MoviePilotCalendarEntry? Subscription, AniBtCalendarEntry? Anime);

/// <summary>日历窗口、选择与请求生命周期；翻月和离页都使上一趟读取失效。</summary>
public sealed class MoviePilotCalendarViewModel : PageViewModel
{
    private MoviePilotService? _service;
    private AniBtClient? _aniBt;
    private MoviePilotSettings? _settings;
    private IReadOnlyList<CalendarCard> _entries = [];
    private IReadOnlyList<CalendarCard> _visibleEntries = [];
    private int _mediaType;
    private long _moviePilotRevision;
    private long _aniBtRevision;
    private long _loadedRevision;

    internal event Action? Changed;
    internal DateOnly Month { get; private set; } = MoviePilotCalendarLayout.Month(DateOnly.FromDateTime(DateTime.Today));
    internal DateOnly SelectedDate { get; private set; } = DateOnly.FromDateTime(DateTime.Today);
    internal IReadOnlyList<CalendarCard> Entries => _visibleEntries;
    internal CalendarSource Source { get; private set; }

    internal static bool HasMoviePilot(MoviePilotService? service, MoviePilotSettings settings) =>
        settings.CalendarEnabled && service?.Available == true;

    internal static bool HasAniBt(MoviePilotSettings settings) => settings.CalendarEnabled && settings.AniBtEnabled;

    internal bool MoviePilotAvailable => _settings is { } settings && HasMoviePilot(_service, settings);
    internal bool AniBtAvailable => _aniBt is not null && _settings is { } settings && HasAniBt(settings);
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

    internal void Attach(MoviePilotService? service, MoviePilotSettings settings, AniBtClient? aniBt)
    {
        Release();
        _service = service;
        _aniBt = aniBt;
        _settings = settings;
        settings.ConnectionChanged += OnMoviePilotChanged;
        settings.CalendarChanged += OnCalendarChanged;
        settings.AniBtChanged += OnAniBtChanged;
        if (service is not null) service.AvailabilityChanged += OnMoviePilotChanged;
        Source = MoviePilotAvailable ? CalendarSource.MoviePilot : CalendarSource.AniBt;
        _ = ReloadAsync();
    }

    private void OnMoviePilotChanged(object? sender, EventArgs args) => Interlocked.Increment(ref _moviePilotRevision);
    private void OnAniBtChanged(object? sender, EventArgs args) => Interlocked.Increment(ref _aniBtRevision);

    private void OnCalendarChanged(object? sender, EventArgs args)
    {
        OnMoviePilotChanged(sender, args);
        OnAniBtChanged(sender, args);
    }

    private bool IsAvailable(CalendarSource source) => source == CalendarSource.MoviePilot ? MoviePilotAvailable : AniBtAvailable;

    private long Revision(CalendarSource source) => source == CalendarSource.MoviePilot
        ? Interlocked.Read(ref _moviePilotRevision) : Interlocked.Read(ref _aniBtRevision);

    internal void RefreshSources()
    {
        if (!IsAvailable(Source))
        {
            SelectSource(Source == CalendarSource.MoviePilot ? CalendarSource.AniBt : CalendarSource.MoviePilot);
            return;
        }
        if (_loadedRevision != Revision(Source)) _ = ReloadAsync();
        else Changed?.Invoke();
    }

    internal void SelectSource(CalendarSource source)
    {
        if (Source == source || !IsAvailable(source)) return;
        Source = source;
        _ = ReloadAsync();
    }

    public override async Task ReloadAsync()
    {
        var source = Source;
        if (!IsAvailable(source)) return;
        var token = BeginLoad();
        var revision = _loadedRevision = Revision(source);
        var (start, end) = MoviePilotCalendarLayout.Window(Month);

        // 上次成功读取留下的同一窗口先摆上屏。仍在读最新安排，所以 Busy 保持——用户看到的是旧内容加一条进度，
        // 而不是空白页；网络失败时这份旧内容也继续留着，不会因为一次刷新失败把整月清空。
        _entries = source == CalendarSource.MoviePilot && _service?.CachedCalendar(start, end) is { } cached
            ? cached.Select(entry => new CalendarCard(entry.Date, entry, null)).ToArray()
            : [];
        UpdateEntries();
        Changed?.Invoke();
        try
        {
            if (source == CalendarSource.MoviePilot && _service is { } service)
            {
                var entries = await service.CalendarAsync(start, end, token);
                if (!CanPublish() || entries.Any(entry => !service.IsCurrentConnection(entry.ConnectionStamp))) return;
                _entries = entries.Select(entry => new CalendarCard(entry.Date, entry, null)).ToArray();
            }
            else if (_aniBt is { } client)
            {
                var result = await client.CalendarAsync(start, end, token);
                if (!CanPublish()) return;
                _entries = result.Entries.Select(entry => new CalendarCard(entry.Date, null, entry)).ToArray();
                if (result.PartialFailure || result.Truncated)
                {
                    var message = result.PartialFailure
                        ? "部分季度读取失败，请点击刷新重试。"
                        : "AniBT 返回的季度结果达到上限，部分番剧可能未列出。";
                    Notify(UiText.Translate("AniBT 日历数据不完整"), UiText.Translate(message), InfoBarSeverity.Warning);
                }
            }
            UpdateEntries();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (CanPublish()) Notify(UiText.Translate("日历读取失败"),
                UiText.Format("{0}。请点击刷新重试。", Momoka.Diagnostics.Failure.Describe(error).TrimEnd('。')), InfoBarSeverity.Error);
        }
        finally
        {
            EndLoad(token);
            if (IsCurrent(token)) Changed?.Invoke();
        }

        bool CanPublish() => IsCurrent(token) && revision == Revision(source) && IsAvailable(source);
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

    private void UpdateEntries() => _visibleEntries = MoviePilotCalendarLayout.Filter(_entries, _mediaType,
        entry => entry.Subscription?.Type ?? (entry.Anime?.IsMovie == true ? "电影" : "电视剧"));

    internal void Release()
    {
        Cancel();
        OnCalendarChanged(null, EventArgs.Empty);
        if (_settings is { } settings)
        {
            settings.ConnectionChanged -= OnMoviePilotChanged;
            settings.CalendarChanged -= OnCalendarChanged;
            settings.AniBtChanged -= OnAniBtChanged;
        }
        if (_service is { } service) service.AvailabilityChanged -= OnMoviePilotChanged;
        _service = null;
        _aniBt = null;
        _settings = null;
    }

    public override void Dispose()
    {
        Release();
        base.Dispose();
    }
}
