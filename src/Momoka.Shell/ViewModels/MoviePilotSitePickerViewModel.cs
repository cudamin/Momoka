using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Momoka.Configuration;
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
    private MoviePilotService? _observed;
    private string? _mediaType;
    private string _connectionStamp = "";
    private bool _loaded;
    private bool _updating;
    private HashSet<int> _preferredIds = [];
    private Task? _catalogueLoad;

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
    /// <summary>面板抬头右端那行读数；目录没读出来时收起，不显示「0 个站点」冒充读到了。</summary>
    public string CatalogueText => _loaded ? $"{Sites.Count} 个站点" : "";
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
            try { Sites.Clear(); }
            finally { _updating = false; }
            _loaded = false;
            _connectionStamp = service.Available ? service.ConnectionStamp : "";
            _catalogueLoad = null;
        }
        _service = service;
        _mediaType = mediaType;
        Observe(service);
        Apply(service.SearchSitePreference(mediaType));
    }

    /// <summary>
    /// 重读一遍存下来的选择（设置页、搜索页或另一个窗口刚改过）。只读本机设置，不读目录、不写盘；
    /// 已经铺出来的站点目录留在原地，只把勾挪到新的那一份上。
    /// </summary>
    internal void RefreshSelection()
    {
        if (_service is null || !_service.IsCurrentConnection(_connectionStamp)) return;
        Apply(_service.SearchSitePreference(_mediaType), notify: true);
    }

    /// <summary>
    /// 把一份选择装上。<paramref name="notify"/> 只给「别处改的」那两条路用 —— 真的换了选择才喊
    /// <see cref="SelectionChanged"/>，让正开着的那一页按「下一次搜索才生效」处理；接上来的头一次不算更改。
    /// </summary>
    private void Apply(MoviePilotSearchSitePreference preference, bool notify = false)
    {
        var changed = UseDefault != preference.UseDefault || !_preferredIds.SetEquals(preference.SiteIds);
        _updating = true;
        try
        {
            UseDefault = preference.UseDefault;
            _preferredIds = preference.SiteIds.ToHashSet();
            foreach (var site in Sites) site.Selected = _preferredIds.Contains(site.Id);
        }
        finally { _updating = false; }
        AnnounceSelection();
        if (notify && changed) SelectionChanged?.Invoke();
    }

    /// <summary>跟着一个服务听「站点选择变了」；换服务或离页时把这根线拆掉。</summary>
    private void Observe(MoviePilotService? service)
    {
        if (ReferenceEquals(_observed, service)) return;
        if (_observed is not null) _observed.SearchSitesChanged -= OnSearchSitesChanged;
        _observed = service;
        if (service is not null) service.SearchSitesChanged += OnSearchSitesChanged;
    }

    /// <summary>
    /// 别处改了选择：本选择器还在台上就当场跟过去。自己存的那一下也会走到这里，代价是一次重读 ——
    /// 值本来就一样，属性通知不会白喊（<see cref="Apply"/> 里有 <see cref="_updating"/> 挡着写盘）。
    /// </summary>
    private void OnSearchSitesChanged()
    {
        if (_service is null || !_service.IsCurrentConnection(_connectionStamp)) return;
        Apply(_service.SearchSitePreference(_mediaType), notify: true);
    }

    internal Task EnsureLoadedAsync() => _loaded && _service?.IsCurrentConnection(_connectionStamp) == true
        ? Task.CompletedTask : _catalogueLoad is { IsCompleted: false } ? _catalogueLoad : ReloadAsync();

    /// <summary>手里这份目录与勾选属于此刻这条连接。设置页据此判断「连接刚恢复、要重新接一次」。</summary>
    internal bool IsCurrentConnection => _service?.IsCurrentConnection(_connectionStamp) == true;

    internal Task PrepareSelectionAsync() => UseDefault ? Task.CompletedTask : EnsureLoadedAsync();

    public override void Cancel()
    {
        _catalogueLoad = null;
        Observe(null);
        base.Cancel();
    }

    public override void Dispose()
    {
        Observe(null);
        base.Dispose();
    }

    public override Task ReloadAsync() => _catalogueLoad = LoadCatalogueAsync();

    private async Task LoadCatalogueAsync()
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
                if (!sameIdentity)
                {
                    var preference = _service.SearchSitePreference(_mediaType);
                    UseDefault = preference.UseDefault;
                    _preferredIds = preference.SiteIds.ToHashSet();
                }
                Sites.Clear();
                foreach (var site in sites)
                    Sites.Add(new MoviePilotSearchSiteRow(site, Changed) { Selected = _preferredIds.Contains(site.Id) });
                _connectionStamp = stamp;
                _loaded = true;
            }
            finally { _updating = false; }
            AnnounceSelection();
            if (hadCatalogue && (!sameIdentity || (!UseDefault && !selected.SetEquals(Sites.Where(site => site.Selected).Select(site => site.Id)))))
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
            _preferredIds.Clear();
            foreach (var site in Sites) site.Selected = selected;
        }
        finally { _updating = false; }
        Changed();
    }

    partial void OnUseDefaultChanged(bool value) => Changed();
    /// <summary>
    /// 勾选变了。保存失败的那句提示要最后说 —— 消费者收到 <see cref="SelectionChanged"/> 后会写一句
    /// 「站点已更改」的信息条，先报错就会被它盖掉，用户只看到一句「已更改」而不知道没存进去。
    /// </summary>
    private void Changed()
    {
        if (_updating) return;
        _preferredIds.ExceptWith(Sites.Select(site => site.Id));
        _preferredIds.UnionWith(Sites.Where(site => site.Selected).Select(site => site.Id));
        Exception? saveError = null;
        if (_service?.IsCurrentConnection(_connectionStamp) == true)
        {
            try { _service.SaveSearchSitePreference(_connectionStamp, _mediaType, UseDefault, _preferredIds); }
            catch (Exception error) { saveError = error; }
        }
        AnnounceSelection();
        SelectionChanged?.Invoke();
        if (saveError is not null) Report("搜索站点已更改，但未能保存，下次启动可能恢复原选择", saveError);
    }
    private void AnnounceSelection()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(CatalogueText));
        OnPropertyChanged(nameof(CanPick));
        OnPropertyChanged(nameof(EmptyVisibility));
    }
    protected override void BusyChanged() => AnnounceSelection();
}
