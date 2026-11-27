using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Momoka.AniBt;
using Momoka.Configuration;
using Momoka.MoviePilot;
using Momoka.Shell.Localization;
using Momoka.Shell.Media;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

internal sealed record MoviePilotCalendarRequest(MoviePilotService? Service, MoviePilotSettings Settings, AniBtClient AniBt);

public sealed partial class MoviePilotCalendarPage : Page, IShellContent
{
    internal const string Destination = "moviepilot:calendar";
    private const int MinimumMonthContentWidth = 420;
    private static readonly SemaphoreSlim PosterSlots = new(4);
    private readonly Dictionary<DateOnly, DayCell> _days = [];
    private readonly Dictionary<string, Task<BitmapImage?>> _images = new(StringComparer.Ordinal);
    private MoviePilotCalendarRequest? _request;
    private CancellationTokenSource? _imagesLifetime;
    private Func<string, CancellationToken, Task<byte[]>>? _fetchImage;
    private IReadOnlyList<CalendarCard>? _renderedEntries;
    private CalendarSource _renderedSource;
    private int _renderedType;
    private DateOnly _renderedMonth;
    private long _renderedSourceRevision;
    private bool _syncingSelections;
    private bool? _compactToolbar;
    private CultureInfo Culture => CultureInfo.GetCultureInfo(UiText.Language);
    internal MoviePilotCalendarViewModel ViewModel { get; } = new();
    public object? NavigationRequest => _request;

    public MoviePilotCalendarPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is MoviePilotCalendarRequest request) Attach(request);
    }

    internal void Attach(MoviePilotCalendarRequest request)
    {
        Release();
        _request = request;
        Tag = Destination;
        ViewModel.Changed += OnChanged;
        ViewModel.Attach(request.Service, request.Settings, request.AniBt);
        OnChanged();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        Release();
        base.OnNavigatedFrom(e);
    }

    public void Release()
    {
        ViewModel.Changed -= OnChanged;
        ViewModel.Release();
        ClearMonth();
        ReleaseImages();
        _request = null;
        _renderedEntries = null;
    }

    private void OnChanged()
    {
        var month = ViewModel.Month;
        var source = ViewModel.Source;
        var monthChanged = _days.Count == 0 || _renderedMonth != month;
        var ownershipChanged = monthChanged || _renderedSource != source || _renderedSourceRevision != ViewModel.SourceRevision;
        var filterChanged = _renderedType != ViewModel.MediaType;
        var entries = ViewModel.Entries;
        SyncToolbar(month, source);
        if (ownershipChanged)
        {
            ClearMonth();
            ReleaseImages();
            _imagesLifetime = new CancellationTokenSource();
            if (_request is { } request)
                _fetchImage = source == CalendarSource.AniBt ? request.AniBt.CalendarCoverAsync : request.Service?.CaptureImageFetcher();
            BuildMonth(month);
            if (monthChanged) CalendarScroll.ChangeView(null, 0, null, disableAnimation: true);
        }

        if (ownershipChanged || filterChanged || !ReferenceEquals(_renderedEntries, entries))
        {
            var groups = entries.ToLookup(entry => entry.Date);
            foreach (var (date, cell) in _days)
            {
                if (filterChanged) cell.Expanded = false;
                SyncDay(date, cell, groups[date].ToArray());
            }
            _renderedEntries = entries;
            _renderedType = ViewModel.MediaType;
            _renderedMonth = month;
            _renderedSource = source;
            _renderedSourceRevision = ViewModel.SourceRevision;
        }

        var count = entries.Count(entry => MoviePilotCalendarLayout.Month(entry.Date) == month);
        var summary = count > 0 ? UiText.Format("本月共 {0} 项播出安排", count)
            : ViewModel.Busy ? UiText.Translate("正在读取日历…")
            : ViewModel.NoticeOpen ? ""
            : UiText.Translate("本月暂无符合条件的播出安排。");
        if (MonthSummary.Text != summary) MonthSummary.Text = summary;
        MonthSummary.Visibility = summary.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SyncToolbar(DateOnly month, CalendarSource source)
    {
        var title = month.ToString(Culture.DateTimeFormat.YearMonthPattern, Culture);
        if (MonthTitle.Text != title) MonthTitle.Text = title;
        _syncingSelections = true;
        try
        {
            MoviePilotCalendarSource.Visibility = ViewModel.MoviePilotAvailable ? Visibility.Visible : Visibility.Collapsed;
            AniBtCalendarSource.Visibility = ViewModel.AniBtAvailable ? Visibility.Visible : Visibility.Collapsed;
            CalendarSources.Visibility = ViewModel.MoviePilotAvailable && ViewModel.AniBtAvailable ? Visibility.Visible : Visibility.Collapsed;
            SingleCalendarSource.Text = (source == CalendarSource.AniBt ? AniBtCalendarSource : MoviePilotCalendarSource).Content?.ToString();
            SingleCalendarSource.Visibility = CalendarSources.Visibility == Visibility.Collapsed
                && (ViewModel.MoviePilotAvailable || ViewModel.AniBtAvailable) ? Visibility.Visible : Visibility.Collapsed;
            CalendarSources.SelectedIndex = (int)source;
            SeriesMedia.Content = UiText.Translate(source == CalendarSource.AniBt ? "番剧" : "电视剧");
            MediaType.SelectedIndex = ViewModel.MediaType;
            RefreshCalendar.IsEnabled = !ViewModel.Busy;
        }
        finally { _syncingSelections = false; }
    }

    private void ClearMonth()
    {
        foreach (var cell in _days.Values)
            foreach (var card in cell.Cards.Children.Concat(cell.ExtraCards.Children).OfType<MoviePilotCalendarEventCard>()) card.Release();
        _days.Clear();
        MonthGrid.Children.Clear();
        MonthGrid.RowDefinitions.Clear();
        MonthGrid.ColumnDefinitions.Clear();
    }

    private void ReleaseImages()
    {
        _imagesLifetime?.Cancel();
        _imagesLifetime?.Dispose();
        _imagesLifetime = null;
        _images.Clear();
        _fetchImage = null;
    }

    private void BuildMonth(DateOnly month)
    {
        // MoviePilot-Frontend e6a30ad4, src/views/subscribe/FullCalendarView.vue (MIT, jxxghp).
        // CalendarViewDayItem has no event template, so the 42 dates retain their own in-cell schedules.
        for (var column = 0; column < 7; column++) MonthGrid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var row = 0; row < 7; row++) MonthGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var column = 0; column < 7; column++)
        {
            var weekday = (DayOfWeek)((column + 1) % 7);
            var label = new TextBlock
            {
                Text = Culture.DateTimeFormat.GetAbbreviatedDayName(weekday),
                Style = (Style)Resources["CalendarWeekdayStyle"],
                HorizontalAlignment = HorizontalAlignment.Center
            };
            AutomationProperties.SetName(label, Culture.DateTimeFormat.GetDayName(weekday));
            var header = Cell(label, new Thickness(0, 7, 0, 7));
            Grid.SetColumn(header, column);
            MonthGrid.Children.Add(header);
        }

        var (start, end) = MoviePilotCalendarLayout.Window(month);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var dateCorners = (CornerRadius)Application.Current.Resources["EgPill28CornerRadius"];
        for (var date = start; date < end; date = date.AddDays(1))
        {
            var day = date;
            var isToday = day == today;
            var dayText = new TextBlock
            {
                Text = day.Day.ToString(Culture),
                Style = (Style)Resources[isToday ? "CalendarTodayDateStyle"
                    : day.Month == month.Month ? "CalendarDateStyle" : "CalendarAdjacentDateStyle"]
            };
            var dateName = day.ToString("D", Culture);
            if (isToday) dateName = UiText.Format("今天，{0}", dateName);
            AutomationProperties.SetName(dayText, dateName);
            AutomationProperties.SetHeadingLevel(dayText, AutomationHeadingLevel.Level2);
            var badge = new Border
            {
                Child = dayText,
                MinWidth = 28,
                MinHeight = 28,
                Padding = new Thickness(3, 0, 3, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                CornerRadius = dateCorners
            };
            if (isToday) badge.Style = (Style)Resources["CalendarTodayBadgeStyle"];
            var cards = new StackPanel { Spacing = 4, Visibility = Visibility.Collapsed };
            var more = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                MinWidth = 0,
                MinHeight = 32,
                Padding = new Thickness(2),
                Visibility = Visibility.Collapsed
            };
            AutomationProperties.SetAutomationId(more, "MoviePilotCalendarMore" + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            var extraCards = new StackPanel { Spacing = 4, Visibility = Visibility.Collapsed };
            var panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(badge);
            panel.Children.Add(cards);
            panel.Children.Add(more);
            panel.Children.Add(extraCards);
            var border = Cell(panel, new Thickness(3));
            border.MinHeight = 58;
            AutomationProperties.SetAutomationId(border, "CalendarDay" + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            AutomationProperties.SetName(border, dateName);
            var cell = new DayCell(border, cards, more, extraCards);
            more.Click += (_, _) =>
            {
                cell.Expanded = !cell.Expanded;
                SyncDay(day, cell, cell.Entries);
            };
            var offset = day.DayNumber - start.DayNumber;
            Grid.SetColumn(border, offset % 7);
            Grid.SetRow(border, 1 + (offset / 7));
            MonthGrid.Children.Add(border);
            _days.Add(day, cell);
        }
    }

    private Border Cell(UIElement child, Thickness padding) => new()
    {
        Style = (Style)Resources["CalendarDayCellStyle"],
        Child = child,
        Padding = padding,
        BorderThickness = new Thickness(0, 0, 1, 1)
    };

    private void SyncDay(DateOnly date, DayCell cell, IReadOnlyList<CalendarCard> entries)
    {
        cell.Entries = entries;
        cell.Expanded &= entries.Count > MoviePilotCalendarLayout.VisibleEventCount;
        var preview = MoviePilotCalendarLayout.VisibleCount(entries.Count, false);
        SyncCards(cell.Cards, entries, 0, preview);
        SyncCards(cell.ExtraCards, entries, preview, cell.Expanded ? entries.Count - preview : 0);

        var remaining = entries.Count - MoviePilotCalendarLayout.VisibleEventCount;
        if (remaining <= 0)
        {
            if (cell.More.FocusState != FocusState.Unfocused) FocusCalendar();
            cell.More.Visibility = Visibility.Collapsed;
            return;
        }

        var dayName = date.ToString("M", Culture);
        var action = cell.Expanded ? UiText.Format("收起{0}的其余安排", dayName)
            : UiText.Format("展开{0}的其余 {1} 项安排", dayName, remaining);
        cell.More.Content = cell.Expanded ? "−" : "+" + remaining.ToString(Culture);
        AutomationProperties.SetName(cell.More, action);
        ToolTipService.SetToolTip(cell.More, action);
        cell.More.Visibility = Visibility.Visible;
    }

    private void SyncCards(StackPanel host, IReadOnlyList<CalendarCard> entries, int start, int count)
    {
        var children = host.Children;
        var token = _imagesLifetime?.Token ?? new CancellationToken(canceled: true);
        for (var index = 0; index < count; index++)
        {
            var entry = entries[start + index];
            if (index < children.Count && children[index] is MoviePilotCalendarEventCard existing && existing.Matches(entry))
            {
                existing.Update(entry, LoadImageAsync, token);
                continue;
            }
            var card = new MoviePilotCalendarEventCard();
            card.Update(entry, LoadImageAsync, token);
            children.Insert(index, card);
        }
        while (children.Count > count)
        {
            ((MoviePilotCalendarEventCard)children[count]).Release();
            children.RemoveAt(count);
        }
        host.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FocusCalendar()
    {
        var tabStop = CalendarScroll.IsTabStop;
        CalendarScroll.IsTabStop = true;
        CalendarScroll.Focus(FocusState.Programmatic);
        CalendarScroll.IsTabStop = tabStop;
    }

    private Task<BitmapImage?> LoadImageAsync(string url, CancellationToken token)
    {
        if (_imagesLifetime is null || _fetchImage is null || !Uri.TryCreate(url, UriKind.Absolute, out var image)
            || image.Scheme is not ("http" or "https")) return Task.FromResult<BitmapImage?>(null);
        if (!_images.TryGetValue(url, out var task))
        {
            task = ReadImageAsync(url, _fetchImage, _imagesLifetime.Token);
            _images.Add(url, task);
        }
        return task.WaitAsync(token);
    }

    private static async Task<BitmapImage?> ReadImageAsync(string url, Func<string, CancellationToken, Task<byte[]>> fetch, CancellationToken token)
    {
        try
        {
            await PosterSlots.WaitAsync(token);
            try
            {
                var bytes = await fetch(url, token);
                token.ThrowIfCancellationRequested();
                return await PosterLoader.DecodeAsync(bytes, 56);
            }
            finally { PosterSlots.Release(); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return null; }
        catch (Exception) { return null; } // A missing cover leaves the date and schedule readable.
    }

    private void OnToolbarSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < MinimumMonthContentWidth;
        if (compact == _compactToolbar) return;
        _compactToolbar = compact;
        Grid.SetRow(MonthActions, compact ? 1 : 0);
        Grid.SetColumn(MonthActions, compact ? 0 : 1);
        Grid.SetColumnSpan(MonthTitle, compact ? 2 : 1);
    }

    private void OnPreviousMonth(object sender, RoutedEventArgs e) => ViewModel.MoveMonth(-1);
    private void OnNextMonth(object sender, RoutedEventArgs e) => ViewModel.MoveMonth(1);

    private void OnToday(object sender, RoutedEventArgs e)
    {
        ViewModel.Today();
        if (!_days.TryGetValue(ViewModel.SelectedDate, out var cell)) return;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (_days.TryGetValue(ViewModel.SelectedDate, out var current) && ReferenceEquals(cell, current))
                cell.Surface.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        });
    }

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Busy) return;
        foreach (var (url, task) in _images.ToArray())
            if (task.IsCompletedSuccessfully && task.Result is null) _images.Remove(url);
        foreach (var cell in _days.Values)
            foreach (var card in cell.Cards.Children.Concat(cell.ExtraCards.Children).OfType<MoviePilotCalendarEventCard>()) card.RetryPoster();
        _ = ViewModel.ReloadAsync();
    }

    private void OnRetry(object sender, RoutedEventArgs e) => ViewModel.InvokeNoticeAction();

    private void OnSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_request is not null && !_syncingSelections && CalendarSources.SelectedIndex >= 0)
            ViewModel.SelectSource((CalendarSource)CalendarSources.SelectedIndex);
    }

    private void OnMediaTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_request is not null && !_syncingSelections && MediaType.SelectedIndex >= 0) ViewModel.MediaType = MediaType.SelectedIndex;
    }

    private sealed class DayCell(Border surface, StackPanel cards, Button more, StackPanel extraCards)
    {
        internal Border Surface { get; } = surface;
        internal StackPanel Cards { get; } = cards;
        internal Button More { get; } = more;
        internal StackPanel ExtraCards { get; } = extraCards;
        internal IReadOnlyList<CalendarCard> Entries { get; set; } = [];
        internal bool Expanded { get; set; }
    }
}
