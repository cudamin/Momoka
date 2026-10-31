using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Momoka.MoviePilot;

namespace Momoka.Shell.ViewModels;

public sealed partial class MoviePilotSubscriptionEditorChoiceRow(string label, string value, bool selected, Action changed) : ObservableObject
{
    public string Label { get; } = label;
    public string Value { get; } = value;
    [ObservableProperty] public partial bool Selected { get; set; } = selected;
    partial void OnSelectedChanged(bool value) => changed();
}

public sealed partial class MoviePilotSubscriptionEditorViewModel : PageViewModel
{
    private MoviePilotSubscription? _subscription;
    private MoviePilotService? _service;
    private bool _active;
    public ObservableCollection<MoviePilotSubscriptionChoice> Qualities { get; } = [];
    public ObservableCollection<MoviePilotSubscriptionChoice> Resolutions { get; } = [];
    public ObservableCollection<MoviePilotSubscriptionChoice> Effects { get; } = [];
    public ObservableCollection<MoviePilotSubscriptionChoice> Downloaders { get; } = [];
    public ObservableCollection<MoviePilotSubscriptionChoice> Paths { get; } = [];
    public ObservableCollection<MoviePilotSubscriptionChoice> EpisodeGroups { get; } = [];
    public ObservableCollection<MoviePilotSubscriptionEditorChoiceRow> Sites { get; } = [];
    public ObservableCollection<MoviePilotSubscriptionEditorChoiceRow> FilterGroups { get; } = [];
    [ObservableProperty] public partial string Keyword { get; set; } = "";
    [ObservableProperty] public partial string Include { get; set; } = "";
    [ObservableProperty] public partial string Exclude { get; set; } = "";
    [ObservableProperty] public partial string CustomWords { get; set; } = "";
    [ObservableProperty] public partial string MediaCategory { get; set; } = "";
    [ObservableProperty] public partial string SavePath { get; set; } = "";
    [ObservableProperty] public partial double TotalEpisodes { get; set; }
    [ObservableProperty] public partial double StartEpisode { get; set; }
    [ObservableProperty] public partial double Season { get; set; }
    [ObservableProperty] public partial double SearchInterval { get; set; } = 24;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IntervalVisibility))] public partial int IntervalMode { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(FullUpgradeVisibility))] public partial bool BestVersion { get; set; }
    [ObservableProperty] public partial bool BestVersionFull { get; set; }
    [ObservableProperty] public partial bool SearchImdbId { get; set; }
    [ObservableProperty] public partial MoviePilotSubscriptionChoice? Quality { get; set; }
    [ObservableProperty] public partial MoviePilotSubscriptionChoice? Resolution { get; set; }
    [ObservableProperty] public partial MoviePilotSubscriptionChoice? Effect { get; set; }
    [ObservableProperty] public partial MoviePilotSubscriptionChoice? Downloader { get; set; }
    [ObservableProperty] public partial MoviePilotSubscriptionChoice? EpisodeGroup { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CanEdit))] public partial bool Working { get; set; }
    public bool CanEdit => !Working;
    public bool IsSeries => _subscription?.IsSeries == true;
    public Visibility SeriesVisibility => Show(IsSeries);
    public Visibility GroupVisibility => Show(IsSeries && _subscription?.Media.MediaSource == "themoviedb");
    public Visibility IntervalVisibility => Show(IntervalMode == 1);
    public Visibility FullUpgradeVisibility => Show(IsSeries && BestVersion);
    public string SitesSummary => Summary(Sites, "跟随系统");
    public string FilterSummary => Summary(FilterGroups, "跟随系统");

    internal void Attach(MoviePilotSubscription subscription, MoviePilotService? service)
    {
        _subscription = subscription; _service = service; _active = true;
        var edit = MoviePilotSubscriptionEdit.From(subscription);
        Keyword = edit.Keyword; Include = edit.Include; Exclude = edit.Exclude;
        CustomWords = edit.CustomWords; MediaCategory = edit.MediaCategory; SavePath = edit.SavePath;
        TotalEpisodes = edit.TotalEpisodes; StartEpisode = edit.StartEpisode; Season = edit.Season ?? double.NaN;
        IntervalMode = edit.SearchInterval.HasValue ? 1 : 0; SearchInterval = edit.SearchInterval ?? 24;
        BestVersion = edit.BestVersion; BestVersionFull = edit.BestVersionFull; SearchImdbId = edit.SearchImdbId;
        Quality = Fill(Qualities, MoviePilotSubscriptionPresets.Qualities, edit.Quality);
        Resolution = Fill(Resolutions, MoviePilotSubscriptionPresets.Resolutions, edit.Resolution);
        Effect = Fill(Effects, MoviePilotSubscriptionPresets.Effects, edit.Effect);
        Downloader = Fill(Downloaders, [new("默认", "")], edit.Downloader);
        EpisodeGroup = Fill(EpisodeGroups, [new("默认", "")], edit.EpisodeGroup);
        FillRows(Sites, edit.Sites.Select(id => new MoviePilotSubscriptionChoice($"站点 {id}", id.ToString())), edit.Sites.Select(id => id.ToString()));
        FillRows(FilterGroups, edit.FilterGroups.Select(name => new MoviePilotSubscriptionChoice(name, name)), edit.FilterGroups);
        OnPropertyChanged(nameof(SeriesVisibility)); OnPropertyChanged(nameof(GroupVisibility)); OnPropertyChanged(nameof(FullUpgradeVisibility));
    }

    internal MoviePilotSubscriptionEdit Read()
    {
        if (_subscription is null) throw new InvalidOperationException("订阅尚未附加");
        var original = MoviePilotSubscriptionEdit.From(_subscription);
        var edit = original with
        {
            Keyword = Keyword,
            Include = Include,
            Exclude = Exclude,
            CustomWords = CustomWords,
            MediaCategory = MediaCategory,
            Quality = Quality?.Value ?? original.Quality,
            Resolution = Resolution?.Value ?? original.Resolution,
            Effect = Effect?.Value ?? original.Effect,
            TotalEpisodes = IsSeries ? Integer(TotalEpisodes, "总集数") : original.TotalEpisodes,
            StartEpisode = IsSeries ? Integer(StartEpisode, "开始集数") : original.StartEpisode,
            Season = IsSeries && !double.IsNaN(Season) ? Integer(Season, "季号") : original.Season,
            SearchInterval = IntervalMode == 1 ? Integer(SearchInterval, "搜索周期") : null,
            Sites = Sites.Where(row => row.Selected).Select(row => int.Parse(row.Value, System.Globalization.CultureInfo.InvariantCulture)).ToArray(),
            FilterGroups = FilterGroups.Where(row => row.Selected).Select(row => row.Value).ToArray(),
            Downloader = Downloader?.Value ?? original.Downloader,
            SavePath = SavePath,
            EpisodeGroup = EpisodeGroup?.Value ?? original.EpisodeGroup,
            BestVersion = BestVersion,
            BestVersionFull = BestVersionFull,
            SearchImdbId = SearchImdbId
        };
        _ = edit.Changes(_subscription);
        return edit;
    }

    public override async Task ReloadAsync()
    {
        if (!_active || _service is null || _subscription is null) return;
        var token = BeginLoad();
        try
        {
            var choices = await _service.SubscriptionEditorChoicesAsync(_subscription, token);
            if (!_active || !IsCurrent(token)) return;
            var sites = Sites.Where(row => row.Selected).Select(row => row.Value).ToArray();
            var filters = FilterGroups.Where(row => row.Selected).Select(row => row.Value).ToArray();
            FillRows(Sites, choices.Sites.Select(site => new MoviePilotSubscriptionChoice(site.Name, site.Id.ToString())), sites);
            FillRows(FilterGroups, choices.FilterGroups.Select(name => new MoviePilotSubscriptionChoice(name, name)), filters);
            Downloader = Fill(Downloaders, new[] { new MoviePilotSubscriptionChoice("默认", "") }.Concat(choices.Downloaders.Select(name => new MoviePilotSubscriptionChoice(name, name))), Downloader?.Value ?? "");
            EpisodeGroup = Fill(EpisodeGroups, new[] { new MoviePilotSubscriptionChoice("默认", "") }.Concat(choices.EpisodeGroups.Where(group => !group.IsDefault).Select(group => new MoviePilotSubscriptionChoice(group.Label + (group.Summary.Length == 0 ? "" : " · " + group.Summary), group.Id))), EpisodeGroup?.Value ?? "");
            Paths.Clear(); foreach (var path in choices.Paths) Paths.Add(path);
            if (choices.Notice.Length > 0) Notify("部分选项未读取", choices.Notice + "读取失败，已有设置已保留，可重试。", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (_active && IsCurrent(token)) Report("读取订阅选项失败", error); }
        finally { EndLoad(token); }
    }

    internal void ReportSave(Exception error) => Report("保存订阅失败", error);
    public override void Cancel() { _active = false; base.Cancel(); }
    private static int Integer(double value, string label) => double.IsFinite(value) && value == Math.Truncate(value) && value is >= int.MinValue and <= int.MaxValue
        ? (int)value : throw new MoviePilotException(label + "必须是整数");
    private static string Summary(IEnumerable<MoviePilotSubscriptionEditorChoiceRow> rows, string fallback)
    {
        var names = rows.Where(row => row.Selected).Select(row => row.Label).ToArray();
        return names.Length == 0 ? fallback : string.Join("、", names);
    }
    private static MoviePilotSubscriptionChoice Fill(ObservableCollection<MoviePilotSubscriptionChoice> target, IEnumerable<MoviePilotSubscriptionChoice> choices, string value)
    {
        target.Clear(); foreach (var item in choices.DistinctBy(item => item.Value)) target.Add(item);
        var selected = target.FirstOrDefault(item => item.Value == value);
        if (selected is not null) return selected;
        selected = new(value, value); target.Add(selected); return selected;
    }
    private void FillRows(ObservableCollection<MoviePilotSubscriptionEditorChoiceRow> target, IEnumerable<MoviePilotSubscriptionChoice> choices, IEnumerable<string> selected)
    {
        var saved = selected.ToHashSet(StringComparer.Ordinal);
        var previous = target.ToDictionary(row => row.Value, row => row.Label, StringComparer.Ordinal);
        target.Clear();
        foreach (var choice in choices.DistinctBy(choice => choice.Value))
            target.Add(new(choice.Label, choice.Value, saved.Contains(choice.Value), AnnounceRows));
        foreach (var value in saved.Where(value => target.All(row => row.Value != value)))
            target.Add(new(previous.GetValueOrDefault(value, value), value, true, AnnounceRows));
        AnnounceRows();
    }
    private void AnnounceRows() { OnPropertyChanged(nameof(SitesSummary)); OnPropertyChanged(nameof(FilterSummary)); }
}
