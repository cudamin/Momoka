using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.ViewModels;

/// <summary>标题分组只折叠显示行，成员快照与勾选状态仍由资源清单持有。</summary>
public sealed partial class MoviePilotSeriesTransferGroup : ObservableCollection<MoviePilotSeriesTransferRow>
{
    public string Title { get; }
    public IReadOnlyList<MoviePilotSeriesTransferRow> Members { get; }
    public Visibility HeaderVisibility { get; }
    public string CountLabel => $"{Members.Count} 条资源";
    public string AutomationName => $"{Title}，{CountLabel}";
    public string ChevronGlyph => Expanded ? "\uE70D" : "\uE76C";
    public bool Expanded { get; private set; } = true;

    internal MoviePilotSeriesTransferGroup(string title, IReadOnlyList<MoviePilotSeriesTransferRow> members, bool grouped)
    {
        Title = title.Length == 0 ? "未命名资源" : title;
        Members = members;
        HeaderVisibility = grouped ? Visibility.Visible : Visibility.Collapsed;
        foreach (var row in members) Add(row);
    }

    internal void Toggle()
    {
        Expanded = !Expanded;
        if (Expanded) foreach (var row in Members) Add(row);
        else Clear();
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(ChevronGlyph)));
    }
}
