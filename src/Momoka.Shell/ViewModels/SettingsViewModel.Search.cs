using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.ViewModels;

/// <summary>搜索只索引当前可用的页面和选项，不过滤或重建侧栏，不索引凭据或选项值。</summary>
public sealed record SettingsSearchResult(string Label, string Location, string CategoryKey,
    SettingSection? Section = null, SettingRow? Row = null)
{
    public override string ToString() => Label + " — " + Location;
}

public sealed partial class SettingsViewModel
{
    private readonly HashSet<SettingSection> _searchSections = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SettingsSearchEmptyVisibility))]
    public partial string SettingsSearchQuery { get; set; } = "";

    public ObservableCollection<SettingsSearchResult> SettingsSearchResults { get; } = [];

    public Visibility SettingsSearchEmptyVisibility => !string.IsNullOrWhiteSpace(SettingsSearchQuery) && SettingsSearchResults.Count == 0
        ? Visibility.Visible : Visibility.Collapsed;

    partial void OnSettingsSearchQueryChanged(string value) => RefreshSettingsSearch();

    private void InitializeSettingsSearch()
    {
        SettingsSearchResults.CollectionChanged += (_, _) => OnPropertyChanged(nameof(SettingsSearchEmptyVisibility));
        Sections.CollectionChanged += OnSearchSectionsChanged;
        Categories.CollectionChanged += (_, _) => RefreshSettingsSearch();
    }

    private void OnSearchSectionsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        foreach (var section in _searchSections.Where(section => !Sections.Contains(section)).ToArray())
        {
            section.Rows.CollectionChanged -= OnSearchRowsChanged;
            _searchSections.Remove(section);
        }
        foreach (var section in Sections)
            if (_searchSections.Add(section)) section.Rows.CollectionChanged += OnSearchRowsChanged;
        RefreshSettingsSearch();
    }

    private void OnSearchRowsChanged(object? sender, NotifyCollectionChangedEventArgs args) => RefreshSettingsSearch();

    /// <summary>只读当前行表；从属功能隐藏后即从索引消失，恢复后可再次搜索且偏好不变。</summary>
    internal void RefreshSettingsSearch()
    {
        SettingsSearchResults.Clear();
        var words = SettingsSearchQuery.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return;

        var matches = new List<(SettingsSearchResult Result, int Rank)>();
        foreach (var entry in Categories.Where(entry => !entry.IsSection))
        {
            Add(new(entry.Label, entry.Group.Name, entry.Key), entry.Label, entry.PickerLabel);
            if (IsHostedEntry(entry)) continue;
            foreach (var section in Sections.Where(section => section.Category == entry.CardCategory && section.Rows.Count > 0))
                foreach (var row in section.Rows)
                {
                    var location = section.Title == entry.Label ? entry.PickerLabel : entry.PickerLabel + " / " + section.Title;
                    if (row is SettingToggleGroupRow group)
                    {
                        foreach (var toggle in group.Toggles)
                            Add(new(toggle.Label, location, entry.Key, section, row), toggle.Label, location + " " + toggle.Note);
                    }
                    else Add(new(row.Label, location, entry.Key, section, row), row.Label, location + " " + row.Note);
                }
        }
        foreach (var match in matches.OrderBy(match => match.Rank).Take(40)) SettingsSearchResults.Add(match.Result);

        void Add(SettingsSearchResult result, string label, string context)
        {
            var searchable = label + " " + context;
            if (!words.All(word => searchable.Contains(word, StringComparison.CurrentCultureIgnoreCase))) return;
            var query = SettingsSearchQuery.Trim();
            var rank = label.Equals(query, StringComparison.CurrentCultureIgnoreCase) ? 0
                : label.StartsWith(query, StringComparison.CurrentCultureIgnoreCase) ? 1
                : words.All(word => label.Contains(word, StringComparison.CurrentCultureIgnoreCase)) ? 2 : 3;
            matches.Add((result, rank));
        }
    }

    /// <summary>再次核实当前目录和行，旧建议不能导航到已失去权限或刚隐藏的选项。</summary>
    internal bool SelectSettingsSearchResult(SettingsSearchResult result)
    {
        var entry = Categories.FirstOrDefault(entry => !entry.IsSection && entry.Key == result.CategoryKey);
        if (entry is null || result.Section is { } section
            && (IsHostedEntry(entry) || section.Category != entry.CardCategory || !Sections.Contains(section)
                || result.Row is null || !section.Rows.Contains(result.Row))) return false;
        SelectedEntry = entry;
        return true;
    }
}
