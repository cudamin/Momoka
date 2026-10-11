using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Momoka.MoviePilot;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.ViewModels;

public sealed partial class MoviePilotSearchSiteRow(MoviePilotSearchSite site, Action changed) : ObservableObject
{
    public int Id => site.Id;
    public string Name => site.Name;
    [ObservableProperty] public partial bool Selected { get; set; }
    partial void OnSelectedChanged(bool value) => changed();
}

public sealed partial class MoviePilotSitePickerViewModel : PageViewModel
{
    private MoviePilotService? _service;
    private string? _mediaType;
    private string _connectionStamp = "";
    private bool _loaded;
    private bool _updating;

    public ObservableCollection<MoviePilotSearchSiteRow> Sites { get; } = [];
    internal event Action? SelectionChanged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyPropertyChangedFor(nameof(CanPick))]
    public partial bool UseDefault { get; set; } = true;

    public string Summary => UseDefault ? "默认站点" : Sites.Count(site => site.Selected) switch
    {
        0 => "未选择站点",
        1 => Sites.First(site => site.Selected).Name,
        var count => $"已选 {count} 个站点"
    };
    public bool CanPick => !UseDefault && !Busy;
    public Visibility EmptyVisibility => Show(_loaded && Sites.Count == 0 && !Busy);
    internal bool HasSelection => UseDefault || (_service?.IsCurrentConnection(_connectionStamp) == true && Sites.Any(site => site.Selected));
    internal MoviePilotSiteSelection? Selection => UseDefault ? null : new(_connectionStamp, [.. Sites.Where(site => site.Selected).Select(site => site.Id)]);

    internal void Attach(MoviePilotService service, string? mediaType = null)
    {
        Cancel();
        if (!ReferenceEquals(_service, service) || _mediaType != mediaType || !service.IsCurrentConnection(_connectionStamp))
        {
            _updating = true;
            Sites.Clear();
            UseDefault = true;
            _updating = false;
            _loaded = false;
            _connectionStamp = "";
        }
        _service = service;
        _mediaType = mediaType;
        AnnounceSelection();
    }

    internal Task EnsureLoadedAsync() => _loaded && _service?.IsCurrentConnection(_connectionStamp) == true
        ? Task.CompletedTask : ReloadAsync();

    public override async Task ReloadAsync()
    {
        if (_service is null) return;
        var token = BeginLoad();
        try
        {
            var stamp = _service.ConnectionStamp;
            var sites = await _service.SearchSitesAsync(_mediaType, token).ConfigureAwait(true);
            if (!IsCurrent(token) || !_service.IsCurrentConnection(stamp)) return;
            var sameIdentity = stamp == _connectionStamp;
            var hadCatalogue = _loaded;
            var selected = Sites.Where(site => site.Selected).Select(site => site.Id).ToHashSet();
            _updating = true;
            try
            {
                Sites.Clear();
                foreach (var site in sites)
                    Sites.Add(new MoviePilotSearchSiteRow(site, Changed) { Selected = sameIdentity && selected.Contains(site.Id) });
                if (hadCatalogue && !sameIdentity) UseDefault = true;
                _connectionStamp = stamp;
                _loaded = true;
            }
            finally { _updating = false; }
            AnnounceSelection();
            if ((hadCatalogue && !sameIdentity) || (!UseDefault && !selected.SetEquals(Sites.Where(site => site.Selected).Select(site => site.Id))))
                SelectionChanged?.Invoke();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (IsCurrent(token)) Report("读取搜索站点失败，可点击刷新重试", error);
        }
        finally { EndLoad(token); }
    }

    [RelayCommand] private Task Refresh() => ReloadAsync();
    [RelayCommand] private void SelectAll() => Select(true);
    [RelayCommand] private void ClearSelection() => Select(false);

    private void Select(bool selected)
    {
        _updating = true;
        try
        {
            UseDefault = false;
            foreach (var site in Sites) site.Selected = selected;
        }
        finally { _updating = false; }
        Changed();
    }

    partial void OnUseDefaultChanged(bool value) => Changed();
    private void Changed()
    {
        if (_updating) return;
        AnnounceSelection();
        SelectionChanged?.Invoke();
    }
    private void AnnounceSelection()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(CanPick));
        OnPropertyChanged(nameof(EmptyVisibility));
    }
    protected override void BusyChanged() => AnnounceSelection();
}
