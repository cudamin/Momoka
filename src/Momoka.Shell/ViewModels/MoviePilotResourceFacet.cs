using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.ViewModels;

/// <summary>一个结果筛选维度。选项属于本批结果，不会重新请求站点。</summary>
public sealed partial class MoviePilotResourceFacet(string key, string label, string glyph, Action changed) : ObservableObject
{
    private bool _settingAll;
    public string Key { get; } = key;
    public string Label { get; } = label;
    public string Glyph { get; } = glyph;
    public ObservableCollection<MoviePilotResourceChoice> Choices { get; } = [];
    public IReadOnlyList<string> Selected => Choices.Where(choice => choice.Selected).Select(choice => choice.Value).ToArray();
    public string Summary => Selected.Count == 0 ? Label : $"{Label} · {Selected.Count}";
    public Visibility Visibility => Choices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    /// <summary>分组卡片上的「清除」只在这一组有选中时出现，和网页端一致。</summary>
    public Visibility SelectionVisibility => Selected.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    internal void SetChoices(IEnumerable<(string Value, string Label)> values)
    {
        var desired = values.Select(item => Choices.FirstOrDefault(choice =>
            string.Equals(choice.Value, item.Value, StringComparison.OrdinalIgnoreCase))
            ?? new MoviePilotResourceChoice(item.Value, item.Label, Label, Changed)).ToArray();
        MoviePilotResourceBrowserViewModel.Sync(Choices, desired);
        Announce();
    }

    /// <summary>「全选」「清除」是整组动作，批量改完只让外层重筛一次。</summary>
    internal void SetAll(bool selected)
    {
        _settingAll = true;
        foreach (var choice in Choices) choice.Selected = selected;
        _settingAll = false;
        Announce();
        changed();
    }

    [RelayCommand] private void SelectAll() => SetAll(true);
    [RelayCommand] private void Clear() => SetAll(false);

    internal void Announce()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Visibility));
        OnPropertyChanged(nameof(SelectionVisibility));
    }

    private void Changed()
    {
        if (_settingAll) return;
        Announce();
        changed();
    }
}

public sealed partial class MoviePilotResourceChoice(string value, string label, string category, Action changed) : ObservableObject
{
    public string Value { get; } = value;
    public string Label { get; } = label;
    public string FilterLabel => $"{category} · {Label}";
    public string RemoveName => $"移除筛选：{category} · {Label}";
    public Visibility CheckVisibility => Selected ? Visibility.Visible : Visibility.Collapsed;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CheckVisibility))]
    public partial bool Selected { get; set; }
    partial void OnSelectedChanged(bool value) => changed();
    [RelayCommand] private void Remove() => Selected = false;
}
