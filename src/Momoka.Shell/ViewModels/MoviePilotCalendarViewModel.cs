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
    private (CalendarSource Source, DateOnly Month, long Revision)? _entryWindow;
    private int _mediaType;
    private long _moviePilotRevision;
    private long _aniBtRevision;
    private long _loadedRevision;

    internal event Action? Changed;
    internal DateOnly Month { get; private set; } = MoviePilotCalendarLayout.Month(DateOnly.FromDateTime(DateTime.Today));
    internal DateOnly SelectedDate { get; private set; } = DateOnly.FromDateTime(DateTime.Today);
    internal IReadOnlyList<CalendarCard> Entries => _visibleEntries;
    internal CalendarSource Source { get; private set; }
    internal long SourceRevision => Revision(Source);

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
        var month = Month;
        var revision = Revision(source);
        var token = BeginLoad(_ => _loadedRevision = revision);
        if (!IsCurrent(token)) return;
        try
        {
            if (!CanPublish()) return;
            var (start, end) = MoviePilotCalendarLayout.Window(month);
            var window = (source, month, revision);
            if (_entryWindow != window)
            {
                // 新窗口先显示同连接的缓存。同窗口刷新继续使用已有条目，保留展开状态与滚动位置。
                _entryWindow = window;
                _entries = source == CalendarSource.MoviePilot && _service?.CachedCalendar(start, end) is { } cached
                    ? cached.Select(entry => new CalendarCard(entry.Date, entry, null)).ToArray()
                    : [];
                UpdateEntries();
            }
            Changed?.Invoke();
            if (!CanPublish()) return;

            if (source == CalendarSource.MoviePilot && _service is { } service)
            {
                var first = _entries.Count == 0
                    ? new TaskCompletionSource<IReadOnlyList<MoviePilotCalendarEntry>>(TaskCreationOptions.RunContinuationsAsynchronously)
                    : null;
                var read = service.CalendarAsync(start, end, token, first is null ? null : entries => first.TrySetResult(entries));
                IReadOnlyList<MoviePilotCalendarEntry>? shown = null;
                if (first is not null && !read.IsCompleted && await Task.WhenAny(read, first.Task) == first.Task)
                {
                    var schedule = await first.Task;
                    if (CanPublishMoviePilot(schedule, service))
                    {
                        SetMoviePilotEntries(schedule);
                        shown = schedule;
                        Changed?.Invoke();
                    }
                }
                var entries = await read;
                if (!ReferenceEquals(entries, shown) && CanPublishMoviePilot(entries, service))
                    SetMoviePilotEntries(entries);
            }
            else if (_aniBt is { } client)
            {
                var result = await client.CalendarAsync(start, end, token);
                if (!CanPublish()) return;
                _entries = result.Entries.Select(entry => new CalendarCard(entry.Date, null, entry)).ToArray();
                UpdateEntries();
                if (result.PartialFailure || result.Truncated)
                {
                    var message = result.PartialFailure
                        ? "部分季度读取失败，请点击刷新重试。"
                        : "AniBT 返回的季度结果达到上限，部分番剧可能未列出。";
                    Notify(UiText.Translate("AniBT 日历数据不完整"), UiText.Translate(message), InfoBarSeverity.Warning,
                        result.PartialFailure ? UiText.Translate("重试") : null,
                        result.PartialFailure ? () => _ = ReloadAsync() : null);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (CanPublish()) Notify(UiText.Translate("日历读取失败"),
                UiText.Format("{0}。请点击刷新重试。", Momoka.Diagnostics.Failure.Describe(error).TrimEnd('。')), InfoBarSeverity.Error,
                UiText.Translate("重试"), () => _ = ReloadAsync());
        }
        finally
        {
            if (IsCurrent(token) && !CanPublish()) ClearEntries();
            EndLoad(token);
            if (IsCurrent(token)) Changed?.Invoke();
        }

        bool CanPublish() => IsCurrent(token) && Month == month && Source == source && revision == Revision(source) && IsAvailable(source);

        bool CanPublishMoviePilot(IReadOnlyList<MoviePilotCalendarEntry> entries, MoviePilotService service) =>
            CanPublish() && (entries.Count == 0 || service.IsCurrentConnection(entries[0].ConnectionStamp));
    }

    private void SetMoviePilotEntries(IReadOnlyList<MoviePilotCalendarEntry> entries)
    {
        _entries = entries.Select(entry => new CalendarCard(entry.Date, entry, null)).ToArray();
        UpdateEntries();
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
        var month = MoviePilotCalendarLayout.Month(SelectedDate);
        if (Month == month)
        {
            Changed?.Invoke();
            return;
        }
        Month = month;
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
        ClearEntries();
    }

    private void ClearEntries()
    {
        _entryWindow = null;
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
