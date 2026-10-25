using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.ViewModels;

/// <summary>一个结果筛选维度。选项属于本批结果，不会重新请求站点。</summary>
public sealed partial class MoviePilotResourceFacet(string key, string label, string glyph, Action changed) : ObservableObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    public string Glyph { get; } = glyph;
    public ObservableCollection<MoviePilotResourceChoice> Choices { get; } = [];
    public IReadOnlyList<string> Selected => Choices.Where(choice => choice.Selected).Select(choice => choice.Value).ToArray();
    public string Summary => Selected.Count == 0 ? Label : $"{Label} · {Selected.Count}";
    public Visibility Visibility => Choices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    internal void SetChoices(IEnumerable<(string Value, string Label)> values)
    {
        Choices.Clear();
        foreach (var (value, text) in values)
            Choices.Add(new MoviePilotResourceChoice(value, text, Label, Changed));
        Announce();
    }

    internal void Announce()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Visibility));
    }

    private void Changed()
    {
        Announce();
        changed();
    }
}

public sealed partial class MoviePilotResourceChoice(string value, string label, string category, Action changed) : ObservableObject
{
    public string Value { get; } = value;
    public string Label { get; } = label;
    public string FilterLabel => $"{category} · {Label}  ×";
    public string RemoveName => $"移除筛选：{category} · {Label}";
    [ObservableProperty] public partial bool Selected { get; set; }
    partial void OnSelectedChanged(bool value) => changed();
    [RelayCommand] private void Remove() => Selected = false;
}
