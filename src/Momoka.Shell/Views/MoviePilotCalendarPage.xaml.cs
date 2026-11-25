using System.Globalization;
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
    private MoviePilotCalendarRequest? _request;
    private readonly HashSet<DateOnly> _expandedDays = [];
    private readonly List<MoviePilotCalendarEventCard> _cards = [];
    private readonly Dictionary<string, Task<BitmapImage?>> _images = new(StringComparer.Ordinal);
    private CancellationTokenSource? _imagesLifetime;
    private Func<string, CancellationToken, Task<byte[]>>? _fetchImage;
    private IReadOnlyList<CalendarCard>? _renderedEntries;
    private CalendarSource _renderedSource;
    private int _renderedType;
    private DateOnly _renderedMonth;
    private int _toolbarMode = -1;
    private long _calendarRevision;
    internal MoviePilotCalendarViewModel ViewModel { get; } = new();
    public object? NavigationRequest => _request;

    public MoviePilotCalendarPage()
    {
        InitializeComponent();
        CalendarToolbar.SizeChanged += OnSizeChanged;
    }

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
        _expandedDays.Clear();
        _renderedEntries = null;
    }

    private void OnChanged()
    {
        MonthTitle.Text = ViewModel.Month.ToString("yyyy年M月", CultureInfo.InvariantCulture);
        var entries = ViewModel.Entries;
        var count = entries.Count(entry => entry.Date.Month == ViewModel.Month.Month && entry.Date.Year == ViewModel.Month.Year);
        var isAniBt = ViewModel.Source == CalendarSource.AniBt;
        MonthSummary.Text = ViewModel.Busy ? UiText.Translate("正在读取日历…")
            : ViewModel.NoticeOpen ? ""
            : count == 0 ? UiText.Translate("本月暂无符合条件的播出安排。") : UiText.Format("本月共 {0} 项播出安排", count);
        MonthSummary.Visibility = MonthSummary.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        MoviePilotCalendarSource.Visibility = ViewModel.MoviePilotAvailable ? Visibility.Visible : Visibility.Collapsed;
        AniBtCalendarSource.Visibility = ViewModel.AniBtAvailable ? Visibility.Visible : Visibility.Collapsed;
        CalendarSources.Visibility = ViewModel.MoviePilotAvailable || ViewModel.AniBtAvailable ? Visibility.Visible : Visibility.Collapsed;
        SetSelected(MoviePilotCalendarSource, !isAniBt);
        SetSelected(AniBtCalendarSource, isAniBt);
        SeriesMedia.Content = UiText.Translate(isAniBt ? "番剧" : "电视剧");
        var filters = new[] { AllMedia, MovieMedia, SeriesMedia };
        for (var index = 0; index < filters.Length; index++)
            SetSelected(filters[index], index == ViewModel.MediaType);
        if (ViewModel.Busy || _imagesLifetime is null)
        {
            ReleaseImages();
            _imagesLifetime = new CancellationTokenSource();
            if (_request is { } request)
                _fetchImage = isAniBt ? request.AniBt.CalendarCoverAsync : request.Service?.CaptureImageFetcher();
        }
        if (ReferenceEquals(_renderedEntries, entries) && _renderedMonth == ViewModel.Month && _renderedSource == ViewModel.Source) return;
        if (_renderedMonth != ViewModel.Month || _renderedType != ViewModel.MediaType || _renderedSource != ViewModel.Source) _expandedDays.Clear();
        RenderMonth(entries);
        _renderedEntries = entries;
        _renderedMonth = ViewModel.Month;
        _renderedType = ViewModel.MediaType;
        _renderedSource = ViewModel.Source;
    }

    private static void SetSelected(Button button, bool selected)
    {
        button.Style = (Style)Application.Current.Resources[selected ? "EgPrimaryButtonStyle" : "EgSecondaryButtonStyle"];
        AutomationProperties.SetName(button, selected ? UiText.Format("{0}，已选中", button.Content) : button.Content?.ToString());
    }

    private void ClearMonth()
    {
        _calendarRevision++;
        foreach (var card in _cards) card.Release();
        _cards.Clear();
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

    private void RenderMonth(IReadOnlyList<CalendarCard> entries)
    {
        // MoviePilot-Frontend e6a30ad4: FullCalendarView.vue, 6-week month, Monday first,
        // five cards per day, inline expansion. MIT, Copyright (c) 2023 jxxghp.
        // Gallery CalendarViewDayItem cannot contain event cards; use existing Grid/Border controls.
        ClearMonth();
        for (var column = 0; column < 7; column++) MonthGrid.ColumnDefinitions.Add(new ColumnDefinition());
        MonthGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var row = 0; row < 6; row++) MonthGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var weekdays = new[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
        for (var column = 0; column < weekdays.Length; column++)
        {
            var label = Text(weekdays[column], "EgBodyStyle");
            label.HorizontalAlignment = HorizontalAlignment.Center;
            var header = Cell(label, new Thickness(0, 1, 0, 1));
            Grid.SetColumn(header, column);
            MonthGrid.Children.Add(header);
        }
        var (start, end) = MoviePilotCalendarLayout.Window(ViewModel.Month);
        var groups = entries.GroupBy(entry => entry.Date).ToDictionary(group => group.Key, group => group.ToArray());
        for (var date = start; date < end; date = date.AddDays(1))
        {
            var day = date;
            var events = groups.GetValueOrDefault(day) ?? [];
            var panel = new StackPanel { Spacing = 5 };
            var dayText = Text(day.Day.ToString(CultureInfo.InvariantCulture) + "日", "EgBodyStyle");
            dayText.HorizontalAlignment = HorizontalAlignment.Right;
            if (day.Month != ViewModel.Month.Month) dayText.Opacity = 0.4;
            panel.Children.Add(dayText);
            var visible = MoviePilotCalendarLayout.VisibleCount(events.Length, _expandedDays.Contains(day));
            for (var index = 0; index < visible; index++) panel.Children.Add(EventCard(events[index]));
            if (events.Length > visible)
            {
                var more = new Button
                {
                    Content = $"+{events.Length - visible}",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Style = (Style)Application.Current.Resources["EgSecondaryButtonStyle"],
                    MinWidth = 0,
                    Padding = new Thickness(6)
                };
                AutomationProperties.SetAutomationId(more, "MoviePilotCalendarMore" + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                AutomationProperties.SetName(more, $"展开{day:M月d日}的其余{events.Length - visible}项安排");
                more.Click += (_, _) => ExpandDay(day, panel, more, events, visible);
                panel.Children.Add(more);
            }
            var cell = Cell(panel, new Thickness(date.DayOfWeek == DayOfWeek.Monday ? 1 : 0, 0, 1, 1), day == DateOnly.FromDateTime(DateTime.Today));
            cell.MinHeight = 180;
            var offset = date.DayNumber - start.DayNumber;
            Grid.SetColumn(cell, offset % 7);
            Grid.SetRow(cell, 1 + (offset / 7));
            MonthGrid.Children.Add(cell);
        }
    }

    private void ExpandDay(DateOnly day, StackPanel panel, Button more, IReadOnlyList<CalendarCard> entries, int visible)
    {
        if (!_expandedDays.Add(day)) return;
        var revision = _calendarRevision;
        var horizontal = CalendarScroll.HorizontalOffset;
        var vertical = CalendarScroll.VerticalOffset;
        panel.Children.Remove(more);
        for (var index = visible; index < entries.Count; index++) panel.Children.Add(EventCard(entries[index]));
        // Match the existing header-scroll focus handoff without inventing an action for the expanded state.
        var expanded = Text("已显示全部", "EgCaptionStyle");
        panel.Children.Add(expanded);
        AutomationProperties.SetLiveSetting(expanded, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(expanded, $"{day:M月d日}已显示全部{entries.Count}项安排");
        var tabStop = CalendarScroll.IsTabStop;
        CalendarScroll.IsTabStop = true;
        CalendarScroll.Focus(FocusState.Programmatic);
        CalendarScroll.IsTabStop = tabStop;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (revision == _calendarRevision && _expandedDays.Contains(day) && _cards.Count > 0)
                CalendarScroll.ChangeView(horizontal, vertical, null, disableAnimation: true);
        });
    }

    private MoviePilotCalendarEventCard EventCard(CalendarCard entry)
    {
        var card = new MoviePilotCalendarEventCard();
        var token = _imagesLifetime?.Token ?? new CancellationToken(canceled: true);
        if (entry.Subscription is { } subscription) card.Attach(subscription, LoadImageAsync, token);
        else if (entry.Anime is { } anime) card.Attach(anime, LoadImageAsync, token);
        _cards.Add(card);
        return card;
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
            var bytes = await fetch(url, token);
            token.ThrowIfCancellationRequested();
            var image = await PosterLoader.DecodeAsync(bytes, 56);
            token.ThrowIfCancellationRequested();
            return image;
        }
        catch (Exception) { return null; }
    }

    private Border Cell(UIElement content, Thickness border, bool today = false)
    {
        var cell = new Border
        {
            Style = (Style)Application.Current.Resources["EgFrameStyle"],
            Child = content,
            Padding = new Thickness(5),
            BorderThickness = border,
            CornerRadius = new CornerRadius(0)
        };
        if (!today) cell.Background = null;
        return cell;
    }

    private static TextBlock Text(string text, string style) => new() { Text = text, Style = (Style)Application.Current.Resources[style] };

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var mode = e.NewSize.Width < 500 ? 2 : e.NewSize.Width < 820 ? 1 : 0;
        if (mode != _toolbarMode)
        {
            _toolbarMode = mode;
            CalendarToolbar.RowDefinitions.Clear();
            for (var row = 0; row <= mode; row++) CalendarToolbar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumnSpan(MediaType, mode > 0 ? 3 : 1);
            Grid.SetRow(MonthTitle, mode > 0 ? 1 : 0);
            Grid.SetColumn(MonthTitle, mode > 0 ? 0 : 1);
            Grid.SetColumnSpan(MonthTitle, mode == 2 ? 3 : 1);
            Grid.SetRow(MonthActions, mode);
            Grid.SetColumn(MonthActions, mode == 2 ? 0 : mode == 1 ? 1 : 2);
            Grid.SetColumnSpan(MonthActions, mode == 2 ? 3 : mode == 1 ? 2 : 1);
            MonthTitle.HorizontalAlignment = mode == 1 ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            MonthActions.HorizontalAlignment = mode == 2 ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        }
    }

    private void OnPreviousMonth(object sender, RoutedEventArgs e) => ViewModel.MoveMonth(-1);
    private void OnNextMonth(object sender, RoutedEventArgs e) => ViewModel.MoveMonth(1);
    private void OnToday(object sender, RoutedEventArgs e) => ViewModel.Today();
    private void OnRefresh(object sender, RoutedEventArgs e) => _ = ViewModel.ReloadAsync();
    private void OnMoviePilotSource(object sender, RoutedEventArgs e) => ViewModel.SelectSource(CalendarSource.MoviePilot);
    private void OnAniBtSource(object sender, RoutedEventArgs e) => ViewModel.SelectSource(CalendarSource.AniBt);
    private void OnMediaTypeChanged(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var type)) ViewModel.MediaType = type;
    }
}
