using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Momoka.MoviePilot;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.ViewModels;

public sealed partial class MoviePilotDownloadViewModel : PageViewModel
{
    public const string DefaultDownloader = "默认下载器";
    private MoviePilotService? _service;
    private MoviePilotResource? _resource;
    private MoviePilotDownloadMedia? _resolved;
    private bool _active;
    private bool _syncing;
    private bool _identityEdited;
    private bool _sourcesLoaded;
    private int _identityVersion;

    public ObservableCollection<string> Downloaders { get; } = [DefaultDownloader];
    public ObservableCollection<string> Paths { get; } = [];
    public const string AutomaticType = "自动识别";
    public ObservableCollection<string> Types { get; } = [AutomaticType, "电影", "电视剧"];
    public ObservableCollection<MoviePilotMediaSource> Sources { get; } = [];
    public ObservableCollection<MoviePilotMedia> Matches { get; } = [];
    [ObservableProperty] public partial string Downloader { get; set; } = DefaultDownloader;
    [ObservableProperty] public partial string SavePath { get; set; } = "";
    [ObservableProperty] public partial string? MediaType { get; set; } = AutomaticType;
    [ObservableProperty] public partial MoviePilotMediaSource? Source { get; set; }
    [ObservableProperty] public partial string MediaId { get; set; } = "";
    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AdvancedVisibility))]
    [NotifyPropertyChangedFor(nameof(AdvancedLabel))]
    [NotifyPropertyChangedFor(nameof(AdvancedGlyph))]
    [NotifyPropertyChangedFor(nameof(SearchVisibility))]
    public partial bool Advanced { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchVisibility))]
    public partial bool SearchOpen { get; set; }
    [ObservableProperty] public partial string SearchStatus { get; set; } = "";

    public bool CanEdit => _active && !Busy;
    public bool CanSubmit => CanEdit && _resource?.DownloadProblem is null && (!_identityEdited || _resolved is not null);
    public bool CanResolve => CanEdit && Source is not null && MediaId.Trim().Length > 0 && MediaType is "电影" or "电视剧";
    public bool CanSearch => CanEdit && Source is not null && SearchText.Trim().Length > 0;
    public string Heading => _resolved?.Title ?? _resource?.DisplayTitle ?? "";
    public string ResourceTitle => _resource?.Title ?? "";
    public string Description => _resource?.Description ?? "";
    public string SizeText => _resource?.SizeText ?? "";
    public string SeedersText => $"↑ {_resource?.Seeders ?? 0}";
    public string PeersText => $"↓ {_resource?.Peers ?? 0}";
    public string SiteText => _resource?.SiteName ?? "";
    public string PromotionText => _resource?.PromotionBadge ?? "";
    public string RuleText => _resource?.HitAndRun == true ? "此资源有 HR 考核，请先核对站点做种要求。" : "";
    public string MediaIdLabel => $"{Source?.Name ?? "媒体"} 编号";
    public string AdvancedLabel => Advanced ? "隐藏高级选项" : "显示高级选项";
    public string AdvancedGlyph => Advanced ? "\uE70E" : "\uE70D";
    public string IdentityStatus => _resolved is { } media
        ? $"已识别：{media.Title} · {MoviePilotSourceNames.Label(media.Source)} {media.Id} · {media.Type}"
        : _identityEdited ? "识别信息已修改，请核对编号或按片名选择作品。" : "";
    public Visibility AdvancedVisibility => Show(Advanced);
    public Visibility SearchVisibility => Show(Advanced && SearchOpen);
    public Visibility DescriptionVisibility => Show(Description.Length > 0);
    public Visibility SizeVisibility => Show(SizeText.Length > 0);
    public Visibility SiteVisibility => Show(SiteText.Length > 0);
    public Visibility PromotionVisibility => Show(PromotionText.Length > 0);
    public Visibility RuleVisibility => Show(RuleText.Length > 0);
    public Visibility IdentityVisibility => Show(IdentityStatus.Length > 0);
    public Visibility RefreshVisibility => Show(NoticeOpen);
    internal MoviePilotDownloadOptions Options => new(Downloader == DefaultDownloader ? null : Downloader,
        string.IsNullOrWhiteSpace(SavePath) ? null : SavePath.Trim(), _resolved);

    internal void Attach(MoviePilotService service, MoviePilotResource resource)
    {
        _service = service;
        _resource = resource;
        _active = true;
        ResetIdentity();
        SearchText = resource.DisplayTitle;
        foreach (var name in new[] { nameof(Heading), nameof(ResourceTitle), nameof(Description), nameof(SizeText),
            nameof(SeedersText), nameof(PeersText), nameof(SiteText), nameof(PromotionText), nameof(RuleText),
            nameof(DescriptionVisibility), nameof(SizeVisibility), nameof(SiteVisibility), nameof(PromotionVisibility), nameof(RuleVisibility) })
            OnPropertyChanged(name);
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(NoticeOpen)) OnPropertyChanged(nameof(RefreshVisibility));
        };
    }

    public override async Task ReloadAsync()
    {
        if (!_active || _service is null || _resource is null) return;
        var token = BeginLoad();
        try
        {
            var choices = await _service.DownloadChoicesAsync(_resource, token).ConfigureAwait(true);
            if (!IsCurrent(token)) return;
            var previous = Downloader;
            var path = SavePath;
            Downloaders.Clear();
            Downloaders.Add(DefaultDownloader);
            foreach (var name in choices.Downloaders) Downloaders.Add(name);
            Downloader = Downloaders.Contains(previous) ? previous : DefaultDownloader;
            Paths.Clear();
            foreach (var value in choices.Paths) Paths.Add(value);
            SavePath = path;
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (IsCurrent(token)) Report("读取下载选项失败，可刷新重试或使用默认设置", error);
        }
        finally { EndLoad(token); }
    }

    [RelayCommand]
    private async Task ToggleAdvanced()
    {
        Advanced = !Advanced;
        if (Advanced) await LoadSourcesAsync().ConfigureAwait(true);
    }

    private async Task LoadSourcesAsync()
    {
        if (_sourcesLoaded || !CanEdit || _service is null) return;
        var token = BeginLoad();
        try
        {
            var sources = await _service.MediaSourcesAsync(token).ConfigureAwait(true);
            if (!IsCurrent(token)) return;
            var selected = Source;
            _syncing = true;
            Sources.Clear();
            foreach (var source in sources.Where(source => source.IsVideo)) Sources.Add(source);
            if (selected is not null && Sources.All(source => source.Id != selected.Id)) Sources.Add(selected);
            Source = Sources.FirstOrDefault(source => source.Id == selected?.Id) ?? Sources.FirstOrDefault();
            _sourcesLoaded = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (IsCurrent(token)) Report("读取媒体数据源失败", error);
        }
        finally { _syncing = false; EndLoad(token); }
    }

    [RelayCommand(CanExecute = nameof(CanResolve))]
    private async Task Resolve()
    {
        if (!CanResolve || _service is null || _resource is null || Source is null) return;
        var version = _identityVersion;
        var token = BeginLoad();
        try
        {
            var media = await _service.ResolveDownloadMediaAsync(_resource, Source.Id, MediaId.Trim(), MediaType!, token).ConfigureAwait(true);
            if (!IsCurrent(token) || version != _identityVersion) return;
            _resolved = media;
            SearchOpen = false;
            AnnounceIdentity();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (IsCurrent(token) && version == _identityVersion) Report("无法核对媒体编号", error);
        }
        finally { EndLoad(token); }
    }

    [RelayCommand] private void ToggleSearch() => SearchOpen = !SearchOpen;

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task Search()
    {
        if (!CanSearch || _service is null || Source is null) return;
        var version = _identityVersion;
        var source = Source.Id;
        var term = SearchText.Trim();
        var token = BeginLoad();
        Matches.Clear();
        SearchStatus = "正在搜索…";
        try
        {
            var matches = await _service.SearchAsync(term, token, [source]).ConfigureAwait(true);
            if (!IsCurrent(token) || version != _identityVersion || term != SearchText.Trim()) return;
            foreach (var media in matches.Where(media => media.CanSubscribe && media.MediaSource == source &&
                media.Type is "电影" or "电视剧" && (MediaType is null or AutomaticType || media.Type == MediaType))) Matches.Add(media);
            SearchStatus = Matches.Count > 0 ? $"选择作品以核对编号 · {Matches.Count} 条" : "没有找到匹配作品，请调整片名或类型。";
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (IsCurrent(token)) { SearchStatus = "搜索失败，可重试。"; Report("搜索媒体失败", error); }
        }
        finally { EndLoad(token); }
    }

    internal async Task SelectMatchAsync(MoviePilotMedia media)
    {
        if (!CanEdit || !Matches.Contains(media) || media.MediaSource != Source?.Id) return;
        MediaType = media.Type ?? "";
        MediaId = media.MediaId ?? "";
        await Resolve().ConfigureAwait(true);
    }

    [RelayCommand]
    private void ResetIdentity()
    {
        _syncing = true;
        _resolved = null;
        _identityEdited = false;
        _identityVersion++;
        ClearNotice();
        MediaType = _resource?.MediaType ?? AutomaticType;
        MediaId = _resource?.MediaId ?? "";
        var id = _resource?.MediaSource;
        Source = Sources.FirstOrDefault(source => source.Id == id);
        if (Source is null && id is { Length: > 0 })
        {
            Source = new(MoviePilotSourceNames.Label(id), id, [MediaType ?? AutomaticType]);
            Sources.Add(Source);
        }
        Source ??= Sources.FirstOrDefault();
        Matches.Clear();
        SearchStatus = "";
        _syncing = false;
        AnnounceIdentity();
    }

    partial void OnMediaTypeChanged(string? value)
    {
        if (value is not null) IdentityEdited();
    }
    partial void OnMediaIdChanged(string value) => IdentityEdited();
    partial void OnSourceChanged(MoviePilotMediaSource? value)
    {
        if (!_syncing) MediaId = "";
        OnPropertyChanged(nameof(MediaIdLabel));
        IdentityEdited();
    }
    partial void OnSearchTextChanged(string value)
    {
        Matches.Clear();
        SearchStatus = "";
        SearchCommand.NotifyCanExecuteChanged();
    }

    private void IdentityEdited()
    {
        if (_syncing) return;
        _identityVersion++;
        _resolved = null;
        _identityEdited = true;
        Matches.Clear();
        SearchStatus = "";
        ClearNotice();
        AnnounceIdentity();
    }

    private void AnnounceIdentity()
    {
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(IdentityStatus));
        OnPropertyChanged(nameof(IdentityVisibility));
        BusyChanged();
    }

    protected override void BusyChanged()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanSubmit));
        ResolveCommand.NotifyCanExecuteChanged();
        SearchCommand.NotifyCanExecuteChanged();
    }

    public override void Cancel() { _active = false; base.Cancel(); }
    [RelayCommand]
    private async Task Refresh()
    {
        await ReloadAsync().ConfigureAwait(true);
        if (Advanced && !NoticeOpen) await LoadSourcesAsync().ConfigureAwait(true);
    }
}
