using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Momoka.Shell.ViewModels;

public sealed partial class MoviePilotResourceGroup : ObservableObject
{
    public MoviePilotResourceGroup(IReadOnlyList<MoviePilotResourceRow> rows)
    {
        Title = rows[0].Title;
        IsExpanded = rows.Count == 1;
    }

    public string Title { get; }
    public ObservableCollection<MoviePilotResourceRow> Rows { get; } = [];
    [ObservableProperty] public partial bool IsExpanded { get; set; }
    public string Summary => string.Join(" · ", new[]
    {
        $"{Rows.Select(row => row.SiteName).Distinct().Count()} 个站点",
        Rows.FirstOrDefault()?.Resource.Resolution,
        Rows.FirstOrDefault()?.Resource.SizeText,
        string.Join(" / ", Rows.Select(row => row.SiteName).Where(name => name.Length > 0).Distinct())
    }.Where(text => !string.IsNullOrEmpty(text)));

    internal void Update(IReadOnlyList<MoviePilotResourceRow> rows)
    {
        MoviePilotResourceBrowserViewModel.Sync(Rows, rows);
        OnPropertyChanged(nameof(Summary));
    }
}
