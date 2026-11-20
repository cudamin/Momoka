using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Momoka.Configuration;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

internal sealed record MoviePilotCalendarRequest(MoviePilotService Service, MoviePilotSettings Settings);

public sealed partial class MoviePilotCalendarPage : Page, IShellContent
{
    internal const string Destination = "moviepilot:calendar";
    private MoviePilotCalendarRequest? _request;
    private readonly Dictionary<DateOnly, Button> _days = [];
    private IReadOnlyList<MoviePilotCalendarEntry>? _renderedEntries;
    private DateOnly _renderedMonth;
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
        ViewModel.Attach(request.Service, request.Settings);
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
        DayEvents.Children.Clear();
        MonthGrid.Children.Clear();
        _days.Clear();
        _renderedEntries = null;
    }

    private void OnChanged()
    {
        MonthTitle.Text = ViewModel.Month.ToString("yyyy 年 M 月", CultureInfo.InvariantCulture);
        var entries = ViewModel.Entries;
        var count = entries.Count(entry => entry.Date.Month == ViewModel.Month.Month && entry.Date.Year == ViewModel.Month.Year);
        MonthSummary.Text = ViewModel.Busy ? "正在读取日历…" : ViewModel.NoticeOpen ? "读取未完成，请点击刷新重试。"
            : count == 0 ? "本月暂无符合条件的播出安排。" : $"本月共 {count} 项播出安排";
        if (!ReferenceEquals(_renderedEntries, entries) || _renderedMonth != ViewModel.Month)
        {
            RenderMonth(entries);
            _renderedEntries = entries;
            _renderedMonth = ViewModel.Month;
        }
        foreach (var (day, button) in _days)
        {
            button.Style = (Style)Application.Current.Resources[day == ViewModel.SelectedDate ? "EgPrimaryButtonStyle" : "EgSecondaryButtonStyle"];
            AutomationProperties.SetHelpText(button, day == ViewModel.SelectedDate ? "已选中" : "");
        }
        SelectedDayTitle.Text = ViewModel.SelectedDate.ToString("M 月 d 日 dddd", CultureInfo.GetCultureInfo("zh-CN"));
        var selected = entries.Where(entry => entry.Date == ViewModel.SelectedDate).ToArray();
        DaySummary.Text = ViewModel.Busy ? "正在读取…" : ViewModel.NoticeOpen ? "日历暂不可用。"
            : selected.Length == 0 ? "当天暂无符合条件的播出安排。" : $"共 {selected.Length} 项";
        DayEvents.Children.Clear();
        foreach (var entry in selected)
        {
            var panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(Body(entry.Title, "EgBodyStyle"));
            var season = entry.Season is { } number ? $" · 第 {number} 季" : "";
            var runtime = entry.RuntimeMinutes is { } minutes ? $" · {minutes} 分钟" : "";
            panel.Children.Add(Body(entry.Type + season + runtime, "EgCaptionStyle"));
            foreach (var episode in entry.Episodes)
            {
                var state = episode.IsInLibrary switch { true => "已入库", false => "未入库", null => "入库状态未知" };
                panel.Children.Add(Body($"第 {episode.Number} 集  {episode.Title} · {state}", "EgBodyStyle"));
            }
            DayEvents.Children.Add(panel);
        }
    }

    private void RenderMonth(IReadOnlyList<MoviePilotCalendarEntry> entries)
    {
        // Gallery 6de91feccadfbb0a796b71caedead5a2284f730e Samples/CalendarView only selects dates.
        // CalendarViewDayItem cannot host event text; compose existing Grid/Button controls for summaries.
        var (start, end) = MoviePilotCalendarLayout.Window(ViewModel.Month);
        if (_days.Count == 0 || _renderedMonth != ViewModel.Month)
        {
            MonthGrid.Children.Clear();
            _days.Clear();
            MonthGrid.RowDefinitions.Clear();
            MonthGrid.ColumnDefinitions.Clear();
            for (var column = 0; column < 7; column++) MonthGrid.ColumnDefinitions.Add(new ColumnDefinition());
            var weeks = (end.DayNumber - start.DayNumber) / 7;
            for (var row = 0; row <= weeks; row++) MonthGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var weekdays = new[] { "一", "二", "三", "四", "五", "六", "日" };
            for (var column = 0; column < 7; column++)
            {
                var text = Body(weekdays[column], "EgCaptionStyle");
                text.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetColumn(text, column);
                MonthGrid.Children.Add(text);
            }
        }
        var groups = entries.GroupBy(entry => entry.Date).ToDictionary(group => group.Key, group => group.ToArray());
        for (var date = start; date < end; date = date.AddDays(1))
        {
            var day = date;
            var items = groups.GetValueOrDefault(day) ?? [];
            var today = day == DateOnly.FromDateTime(DateTime.Today) ? "今天 · " : "";
            if (!_days.TryGetValue(day, out var button))
            {
                button = new Button
                {
                    MinWidth = 0,
                    MinHeight = 88,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(4),
                    Opacity = day.Month == ViewModel.Month.Month ? 1 : 0.55
                };
                AutomationProperties.SetAutomationId(button, "MoviePilotCalendarDay" + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                button.Click += (_, _) => ViewModel.SelectDate(day);
                _days.Add(day, button);
                var index = day.DayNumber - start.DayNumber;
                Grid.SetColumn(button, index % 7);
                Grid.SetRow(button, 1 + (index / 7));
                MonthGrid.Children.Add(button);
            }
            button.Content = DayContent(day, items, button);
            AutomationProperties.SetName(button, today + day.ToString("yyyy年M月d日", CultureInfo.InvariantCulture) + $"，{items.Length} 项安排，点击查看详情");
        }
    }

    private StackPanel DayContent(DateOnly day, IReadOnlyList<MoviePilotCalendarEntry> items, Button button)
    {
        var content = new StackPanel { Spacing = 4, DataContext = button };
        void Add(string text, string style, bool trim = false)
        {
            var label = Body(text, style);
            label.SetBinding(TextBlock.ForegroundProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Foreground") });
            if (trim)
            {
                label.TextWrapping = TextWrapping.NoWrap;
                label.TextTrimming = TextTrimming.CharacterEllipsis;
            }
            content.Children.Add(label);
        }
        var today = day == DateOnly.FromDateTime(DateTime.Today) ? "今天 · " : "";
        Add(today + day.Day.ToString(CultureInfo.InvariantCulture), "EgBodyStyle");
        if (items.Count > 0) Add(items[0].Title, "EgCaptionStyle", trim: true);
        if (items.Count > 1) Add($"另有 {items.Count - 1} 项", "EgCaptionStyle");
        return content;
    }

    private TextBlock Body(string text, string style) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources[style],
        TextWrapping = TextWrapping.Wrap
    };

    private void OnPreviousMonth(object sender, RoutedEventArgs e) => ViewModel.MoveMonth(-1);
    private void OnNextMonth(object sender, RoutedEventArgs e) => ViewModel.MoveMonth(1);
    private void OnToday(object sender, RoutedEventArgs e) => ViewModel.Today();
    private void OnRefresh(object sender, RoutedEventArgs e) => _ = ViewModel.ReloadAsync();
    private void OnMediaTypeChanged(object sender, SelectionChangedEventArgs e) => ViewModel.MediaType = MediaType.SelectedIndex;
}
