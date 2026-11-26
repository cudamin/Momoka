using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Momoka.Scrobbling;
using Momoka.Shell.Localization;

namespace Momoka.Shell.Views;

internal sealed record ScrobblingRecordRow(string Timestamp, string Outcome, string EpisodeTitle, string Detail);

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

    private readonly (ScrobblingRecordOutcome Outcome, ScrobblingRecordRow Row)[] _rows;
    private XamlRoot? _layoutRoot;

    internal ScrobblingRecordsView(IReadOnlyList<ScrobblingRecord> records)
    {
        _rows = records.Select(record => (record.Outcome, ToRow(record))).ToArray();
        InitializeComponent();
        AutomationProperties.SetName(ScrobblingRecordsFilter, UiText.Translate("按同步结果筛选"));
        RecordsSummary.Text = string.Format(UiText.Translate("本次运行已保留 {0} / {1} 条，退出应用后清空。"),
            records.Count, ScrobblingService.RecordLimitPerProvider);
        foreach (var (outcome, label) in Filters)
        {
            var count = outcome is null ? _rows.Length : _rows.Count(entry => entry.Outcome == outcome);
            ScrobblingRecordsFilter.Items.Add($"{UiText.Translate(label)} ({count})");
        }
        ScrobblingRecordsFilter.Visibility = records.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ScrobblingRecordsFilter.SelectedIndex = 0;
    }

    private static ScrobblingRecordRow ToRow(ScrobblingRecord record)
    {
        var outcome = Filters.FirstOrDefault(filter => filter.Outcome == record.Outcome).Label ?? "未知";
        var episode = record.EpisodeNumber is { } number
            ? string.Format(UiText.Translate("第 {0} 集"), number)
            : UiText.Translate("集数未知");
        return new ScrobblingRecordRow(record.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            UiText.Translate(outcome), $"{record.Title} · {episode}", UiText.Translate(record.Detail));
    }

    private void OnFilterChanged(object sender, SelectionChangedEventArgs args)
    {
        if (ScrobblingRecordsFilter.SelectedIndex < 0) return;
        var outcome = Filters[ScrobblingRecordsFilter.SelectedIndex].Outcome;
        var rows = _rows.Where(entry => outcome is null || entry.Outcome == outcome).Select(entry => entry.Row).ToArray();
        ScrobblingRecordsList.ItemsSource = rows;
        ScrobblingRecordsList.Visibility = rows.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        RecordsEmpty.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (rows.Length > 0) ScrobblingRecordsList.ScrollIntoView(rows[0]);
        else RecordsEmpty.Text = UiText.Translate(_rows.Length == 0
            ? "还没有自动标记记录。"
            : "这个分类下还没有同步记录。");
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _layoutRoot = XamlRoot;
        if (_layoutRoot is not { } root) return;
        ApplySize(root);
        root.Changed += OnRootChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (_layoutRoot is { } root) root.Changed -= OnRootChanged;
        _layoutRoot = null;
    }

    private void OnRootChanged(XamlRoot root, XamlRootChangedEventArgs args) => ApplySize(root);

    private void ApplySize(XamlRoot root)
    {
        Width = Math.Clamp(root.Size.Width - 96, 1, 620);
        ScrobblingRecordsList.MaxHeight = Math.Clamp(root.Size.Height - 220, 80, 440);
    }
}
