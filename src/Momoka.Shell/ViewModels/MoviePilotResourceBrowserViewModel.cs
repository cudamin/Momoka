using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Momoka.MoviePilot;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.ViewModels;

public sealed partial class MoviePilotResourceBrowserViewModel : ObservableObject
{
    public const string AllSites = "全部站点";
    public const string AllResolutions = "全部清晰度";
    public const string AllLabels = "全部标签";
    public const string AllSeasons = "全部季";
    public const string AllEpisodes = "全部集";
    private readonly List<MoviePilotResourceRow> _all = [];
    private readonly Dictionary<MoviePilotResourceRow, MoviePilotResourceGroup> _groups = [];
    private bool _resetting;
    private int? _seededEpisode;

    public ObservableCollection<MoviePilotResourceRow> Rows { get; } = [];
    public ObservableCollection<MoviePilotResourceGroup> Groups { get; } = [];
    public ObservableCollection<string> Sites { get; } = [AllSites];
    public ObservableCollection<string> Resolutions { get; } = [AllResolutions];
    public ObservableCollection<string> Labels { get; } = [AllLabels];
    public ObservableCollection<string> Seasons { get; } = [AllSeasons];
    public ObservableCollection<string> Episodes { get; } = [AllEpisodes];

    [ObservableProperty] public partial string Text { get; set; } = "";
    [ObservableProperty] public partial string SelectedSite { get; set; } = AllSites;
    [ObservableProperty] public partial string SelectedResolution { get; set; } = AllResolutions;
    [ObservableProperty] public partial string SelectedLabel { get; set; } = AllLabels;
    [ObservableProperty] public partial string SelectedSeason { get; set; } = AllSeasons;
    [ObservableProperty] public partial string SelectedEpisode { get; set; } = AllEpisodes;
    [ObservableProperty] public partial int PromotionIndex { get; set; }
    [ObservableProperty] public partial int SortIndex { get; set; }
    [ObservableProperty] public partial int DateIndex { get; set; }
    [ObservableProperty] public partial bool SeededOnly { get; set; }
    [ObservableProperty] public partial bool ExcludeHitAndRun { get; set; }

    public Visibility FiltersVisibility => _all.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoMatchesVisibility => _all.Count > 0 && Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public string CountText => $"{Groups.Count} 组 · {Rows.Count} / {_all.Count} 条站点资源";

    /// <summary>
    /// 换一批结果。<paramref name="autoEpisode"/> 是「这次搜索本来就是为某一集发的」（集页面的版本搜索，见
    /// <see cref="MoviePilotVersionsViewModel"/>）：把它种进集号下拉（meta 没解析出来也在，清掉筛选后还选得回来）
    /// 并自动选中，让结果一出就只剩那一集；用户看得见也随时能改回「全部集」。
    /// </summary>
    internal void SetResults(IEnumerable<MoviePilotResourceRow> rows, int? autoEpisode = null)
    {
        _seededEpisode = autoEpisode;
        _all.Clear();
        _all.AddRange(rows);
        _groups.Clear();
        var byResource = new Dictionary<MoviePilotResource, MoviePilotResourceRow>(ReferenceEqualityComparer.Instance);
        foreach (var row in _all) byResource[row.Resource] = row;
        foreach (var resources in MoviePilotResourceGrouping.Group(_all.Select(row => row.Resource)))
        {
            var members = resources.Select(resource => byResource[resource]).ToArray();
            var group = new MoviePilotResourceGroup(members);
            foreach (var row in members) _groups[row] = group;
        }
        _resetting = true;
        FillChoices(Sites, AllSites, _all.Select(row => row.Resource.SiteName));
        FillChoices(Resolutions, AllResolutions, _all.Select(row => row.Resource.Resolution));
        FillChoices(Labels, AllLabels, _all.SelectMany(row => row.Resource.Labels));
        FillNumbers(Seasons, AllSeasons, "S", _all.SelectMany(row => row.Resource.Seasons));
        _resetting = false;
        Reset();
        if (autoEpisode is { } episode) SelectedEpisode = EpisodeChoice(episode);
        OnPropertyChanged(nameof(FiltersVisibility));
    }

    [RelayCommand]
    private void Reset()
    {
        _resetting = true;
        Text = "";
        SelectedSite = AllSites;
        SelectedResolution = AllResolutions;
        SelectedLabel = AllLabels;
        SelectedSeason = AllSeasons;
        FillNumbers(Episodes, AllEpisodes, "E", EpisodeChoices(_all.SelectMany(row => row.Resource.Episodes)));
        SelectedEpisode = AllEpisodes;
        PromotionIndex = SortIndex = DateIndex = 0;
        SeededOnly = ExcludeHitAndRun = false;
        _resetting = false;
        Apply();
    }

    partial void OnTextChanged(string value) => Apply();
    partial void OnSelectedSiteChanged(string value) => Apply();
    partial void OnSelectedResolutionChanged(string value) => Apply();
    partial void OnSelectedLabelChanged(string value) => Apply();
    partial void OnSelectedSeasonChanged(string value)
    {
        if (_resetting) return;
        _resetting = true;
        var season = SelectedNumber(value);
        FillNumbers(Episodes, AllEpisodes, "E", EpisodeChoices(_all
            .Where(row => season is null || row.Resource.Seasons.Contains(season.Value))
            .SelectMany(row => row.Resource.Episodes)));
        SelectedEpisode = AllEpisodes;
        _resetting = false;
        Apply();
    }
    partial void OnSelectedEpisodeChanged(string value) => Apply();
    partial void OnPromotionIndexChanged(int value) => Apply();
    partial void OnSortIndexChanged(int value) => Apply();
    partial void OnDateIndexChanged(int value) => Apply();
    partial void OnSeededOnlyChanged(bool value) => Apply();
    partial void OnExcludeHitAndRunChanged(bool value) => Apply();

    private void Apply()
    {
        if (_resetting) return;
        var filter = new MoviePilotResourceFilter
        {
            Text = Text,
            Site = SelectedSite == AllSites ? "" : SelectedSite ?? "",
            Resolution = SelectedResolution == AllResolutions ? "" : SelectedResolution ?? "",
            Label = SelectedLabel == AllLabels ? "" : SelectedLabel ?? "",
            Season = SelectedNumber(SelectedSeason),
            Episode = SelectedNumber(SelectedEpisode),
            Promotion = (MoviePilotPromotionFilter)PromotionIndex,
            Sort = (MoviePilotResourceSort)SortIndex,
            SeededOnly = SeededOnly,
            ExcludeHitAndRun = ExcludeHitAndRun,
            PublishedWithinDays = DateIndex switch { 1 => 1, 2 => 7, 3 => 30, 4 => 90, _ => null }
        };
        var byResource = new Dictionary<MoviePilotResource, MoviePilotResourceRow>(ReferenceEqualityComparer.Instance);
        foreach (var row in _all) byResource[row.Resource] = row;
        var desired = filter.Apply(_all.Select(row => row.Resource), DateTimeOffset.Now)
            .Select(resource => byResource[resource]).ToList();
        Sync(Rows, desired);
        var grouped = desired.GroupBy(row => _groups[row]).ToArray();
        foreach (var group in grouped) group.Key.Update(group.ToArray());
        Sync(Groups, grouped.Select(group => group.Key).ToArray());
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(NoMatchesVisibility));
    }

    internal static void Sync<T>(ObservableCollection<T> rows, IReadOnlyList<T> desired) where T : class
    {
        var keep = desired.ToHashSet();
        for (var index = rows.Count - 1; index >= 0; index--)
            if (!keep.Contains(rows[index])) rows.RemoveAt(index);
        for (var index = 0; index < desired.Count; index++)
        {
            if (index < rows.Count && ReferenceEquals(rows[index], desired[index])) continue;
            var existing = rows.IndexOf(desired[index]);
            if (existing >= 0) rows.Move(existing, index);
            else rows.Insert(index, desired[index]);
        }
    }

    private static int? SelectedNumber(string? value) => value is { Length: > 1 } &&
        int.TryParse(value.AsSpan(1), out var number) ? number : null;

    /// <summary>集号下拉的候选：meta 解析出来的集号，再补上这次搜索种下的目标集（没有就原样）——
    /// 自动筛选被清除或重置后，那个集号还得选得回来。</summary>
    private IEnumerable<int> EpisodeChoices(IEnumerable<int> parsed) =>
        _seededEpisode is { } seeded ? parsed.Append(seeded) : parsed;

    /// <summary>集号在下拉里的写法，和 FillNumbers 的「E」前缀同一格式。</summary>
    private static string EpisodeChoice(int episode) => $"E{episode:00}";

    private static void FillNumbers(ObservableCollection<string> choices, string all, string prefix, IEnumerable<int> values)
    {
        choices.Clear();
        choices.Add(all);
        foreach (var value in values.Distinct().Order()) choices.Add($"{prefix}{value:00}");
    }

    private static void FillChoices(ObservableCollection<string> choices, string all, IEnumerable<string?> values)
    {
        choices.Clear();
        choices.Add(all);
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase))
            choices.Add(value);
    }
}
