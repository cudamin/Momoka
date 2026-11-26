using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Momoka.Scrobbling;
using Momoka.Shell.Localization;

namespace Momoka.Shell.Views;

internal sealed class ScrobblingRecordRow(ScrobblingRecord record, string timestamp, string outcome, string episodeTitle, string detail)
{
    internal ScrobblingRecord Record { get; } = record;
    public string Timestamp { get; } = timestamp;
    public string Outcome { get; } = outcome;
    public string EpisodeTitle { get; } = episodeTitle;
    public string Detail { get; } = detail;
}

internal sealed partial class ScrobblingRecordsFilterOption : ObservableObject
{
    [ObservableProperty]
    public partial string Text { get; set; } = "";
}

public sealed partial class ScrobblingRecordsView : UserControl
{
    private static readonly (ScrobblingRecordOutcome? Outcome, string Label)[] Filters =
    [
        (null, "全部结果"),
        (ScrobblingRecordOutcome.Watching, "在看"),
        (ScrobblingRecordOutcome.Watched, "已看"),
        (ScrobblingRecordOutcome.Skipped, "已跳过"),
        (ScrobblingRecordOutcome.Failed, "失败")
    ];

    private readonly ScrobblingRecordsFilterOption[] _filterOptions = Filters.Select(_ => new ScrobblingRecordsFilterOption()).ToArray();
    private readonly ObservableCollection<ScrobblingRecordRow> _visibleRows = [];
    private ScrobblingRecordRow[] _rows = [];

    internal ScrobblingRecordsView(IReadOnlyList<ScrobblingRecord> records, ScrobblingRecordOutcome? initialOutcome = null)
    {
        InitializeComponent();
        AutomationProperties.SetName(ScrobblingRecordsFilter, UiText.Translate("按同步结果筛选"));
        ScrobblingRecordsList.ItemsSource = _visibleRows;
        ScrobblingRecordsFilter.ItemsSource = _filterOptions;
        ScrobblingRecordsFilter.SelectedIndex = Math.Max(0, Array.FindIndex(Filters, filter => filter.Outcome == initialOutcome));
        ShowRecords(records);
    }

    internal void UpdateRecords(IReadOnlyList<ScrobblingRecord> records)
    {
        if (_rows.Select(row => row.Record).SequenceEqual(records, ReferenceEqualityComparer.Instance)) return;
        ShowRecords(records);
    }

    private void ShowRecords(IReadOnlyList<ScrobblingRecord> records)
    {
        var existing = new Dictionary<ScrobblingRecord, ScrobblingRecordRow>(ReferenceEqualityComparer.Instance);
        foreach (var row in _rows) existing[row.Record] = row;
        _rows = records.Select(record => existing.TryGetValue(record, out var row) ? row : ToRow(record)).ToArray();

        RecordsSummary.Text = string.Format(UiText.Translate("本次运行已保留 {0} / {1} 条，退出应用后清空。"),
            records.Count, ScrobblingService.RecordLimitPerProvider);
        for (var index = 0; index < Filters.Length; index++)
        {
            var (outcome, label) = Filters[index];
            var count = outcome is null ? _rows.Length : _rows.Count(row => row.Record.Outcome == outcome);
            _filterOptions[index].Text = $"{UiText.Translate(label)} ({count})";
        }
        ScrobblingRecordsFilter.Visibility = records.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter(resetScroll: false);
    }

    private static ScrobblingRecordRow ToRow(ScrobblingRecord record)
    {
        var outcome = Filters.FirstOrDefault(filter => filter.Outcome == record.Outcome).Label ?? "未知";
        var title = record.Provider == ScrobblingProvider.Douban ? record.Title
            : record.EpisodeNumber is { } number ? $"{record.Title} · {string.Format(UiText.Translate("第 {0} 集"), number)}"
            : $"{record.Title} · {UiText.Translate("集数未知")}";
        return new ScrobblingRecordRow(record, record.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            UiText.Translate(outcome), title, UiText.Translate(record.Detail));
    }

    private void OnFilterChanged(object sender, SelectionChangedEventArgs args) => ApplyFilter(resetScroll: true);

    private void ApplyFilter(bool resetScroll)
    {
        var index = ScrobblingRecordsFilter.SelectedIndex;
        if (index < 0 || index >= Filters.Length) return;
        var outcome = Filters[index].Outcome;
        var rows = _rows.Where(row => outcome is null || row.Record.Outcome == outcome).ToArray();
        if (!_visibleRows.SequenceEqual(rows))
        {
            // At the top, show incoming rows immediately. Farther down, keep the item being read in view.
            if (ScrobblingRecordsList.ItemsPanelRoot is ItemsStackPanel panel)
            {
                var offset = ScrobblingRecordsList.FindDescendant<ScrollViewer>()?.VerticalOffset ?? 0;
                panel.ItemsUpdatingScrollMode = resetScroll || offset < 1
                    ? ItemsUpdatingScrollMode.KeepScrollOffset : ItemsUpdatingScrollMode.KeepItemsInView;
            }

            var desired = rows.ToHashSet();
            for (var remove = _visibleRows.Count - 1; remove >= 0; remove--)
                if (!desired.Contains(_visibleRows[remove])) _visibleRows.RemoveAt(remove);

            // Records only arrive at the head or leave at the retention limit. A filter switch
            // adds back rows at their positions without recreating the rows that remain visible.
            for (var insert = 0; insert < rows.Length; insert++)
                if (insert >= _visibleRows.Count || !ReferenceEquals(_visibleRows[insert], rows[insert]))
                    _visibleRows.Insert(insert, rows[insert]);
        }
        ScrobblingRecordsList.Visibility = rows.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        RecordsEmpty.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (resetScroll && rows.Length > 0) ScrobblingRecordsList.ScrollIntoView(rows[0], ScrollIntoViewAlignment.Leading);
        if (rows.Length == 0) RecordsEmpty.Text = UiText.Translate(_rows.Length == 0
            ? "还没有自动标记记录。" : "这个分类下还没有同步记录。");
    }

    internal void ApplySize(XamlRoot root)
    {
        Width = Math.Clamp(root.Size.Width - 96, 1, 620);
        ScrobblingRecordsList.MaxHeight = Math.Clamp(root.Size.Height - 220, 80, 440);
    }
}
