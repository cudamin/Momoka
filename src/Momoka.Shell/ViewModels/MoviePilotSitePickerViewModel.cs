using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Momoka.Configuration;
using Momoka.MoviePilot;

namespace Momoka.Shell.ViewModels;

public sealed partial class MoviePilotSearchSiteRow(MoviePilotSearchSite site, Action<MoviePilotSearchSiteRow> changed) : ObservableObject
{
    public int Id => site.Id;
    public string Name => site.Name;
    [ObservableProperty] public partial bool Selected { get; set; }
    public Visibility SelectedVisibility => Selected ? Visibility.Visible : Visibility.Collapsed;
    public Visibility UnselectedVisibility => Selected ? Visibility.Collapsed : Visibility.Visible;
    partial void OnSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(SelectedVisibility));
        OnPropertyChanged(nameof(UnselectedVisibility));
        changed(this);
    }
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
    private MoviePilotSitePickerViewModel? _draftOwner;
    private int _editVersion;
    private int _ownerEditVersion;
    private int _preferenceVersion;
    private int _ownerPreferenceVersion;
    private MoviePilotSitePickerViewModel? _committingDraft;
    private bool _ownSaveObserved;
    private bool _editing = true;

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
    /// <summary>目录没读出来时不显示「0 个站点」冒充成功。</summary>
    public string CatalogueText => _loaded ? $"{Sites.Count} 个站点" : "";
    public int SelectedCount => Sites.Count(site => site.Selected);
    public string SelectionText => _loaded ? $"已选择 {SelectedCount}/{Sites.Count}" : Busy ? "正在读取站点" : "尚未读取站点目录";
    // 默认模式不锁住卡片：按任意卡片即明确退出默认模式。
    public bool CanPick => CanComplete && _loaded;
    public bool CanComplete => _editing && !Busy && IsCurrentConnection && DraftIsCurrent;
    public bool CanSearch => CanComplete && HasSelection;
    private bool CanSelect => CanPick && Sites.Count > 0;
    private bool CanRefresh => _editing && !Busy && IsCurrentConnection && DraftIsCurrent;
    private bool DraftIsCurrent => _draftOwner is null || (_draftOwner._editing && _draftOwner._editVersion == _ownerEditVersion
        && _draftOwner._preferenceVersion == _ownerPreferenceVersion
        && ReferenceEquals(_draftOwner._service, _service) && _draftOwner._mediaType == _mediaType
        && _draftOwner._connectionStamp == _connectionStamp);
    public Visibility EmptyVisibility => Show(_loaded && Sites.Count == 0 && !Busy);
    internal bool HasSelection => UseDefault || (_service?.IsCurrentConnection(_connectionStamp) == true && Sites.Any(site => site.Selected));
    internal MoviePilotSiteSelection? Selection => UseDefault ? null : new(_connectionStamp, [.. Sites.Where(site => site.Selected).Select(site => site.Id)]);
    internal bool IsDraftOwnerCurrent => DraftIsCurrent && IsCurrentConnection;

    /// <summary>弹窗有自己的选择草稿；X、Esc、遮罩取消不写盘，也不通知搜索宿主。</summary>
    internal MoviePilotSitePickerViewModel CreateDraft()
    {
        var draft = new MoviePilotSitePickerViewModel
        {
            _draftOwner = this,
            _ownerEditVersion = _editVersion,
            _ownerPreferenceVersion = _preferenceVersion,
            _service = _service,
            _mediaType = _mediaType,
            _connectionStamp = _connectionStamp,
            _loaded = _loaded
        };
        draft.Apply(new() { UseDefault = UseDefault, SiteIds = [.. _preferredIds] });
        draft.CopySites(Sites.Select(site => new MoviePilotSearchSite(site.Id, site.Name)));
        draft.AnnounceSelection();
        return draft;
    }

    /// <summary>只有确认才保存；失败留在弹窗里，不把未保存的选择当成搜索确认。</summary>
    internal bool CommitSelection()
    {
        if (_draftOwner is not { } owner || !CanComplete) return false;
        var preference = new MoviePilotSearchSitePreference { UseDefault = UseDefault, SiteIds = [.. _preferredIds] };
        _ownSaveObserved = false;
        owner._committingDraft = this;
        try
        {
            _service!.SaveSearchSitePreference(_connectionStamp, _mediaType, UseDefault, _preferredIds);
            if (!IsDraftOwnerCurrent) return false;
            if (_loaded)
            {
                owner.CopySites(Sites.Select(site => new MoviePilotSearchSite(site.Id, site.Name)));
                owner._loaded = true;
            }
            owner.Apply(preference, notify: true);
            return true;
        }
        catch (Exception error)
        {
            Report("搜索站点未能保存，请重试或取消", error);
            return false;
        }
        finally { owner._committingDraft = null; }
    }

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
        _editing = true;
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
        if (changed) _preferenceVersion++;
        // SaveSearchSitePreference 同步广播：只认本次写入的第一份同值回执。
        // 别处先改、通知中再改或 A→B→A 都不借这个豁免更新草稿的归属。
        if (_committingDraft is { _ownSaveObserved: false } draft)
        {
            draft._ownSaveObserved = true;
            if (_preferenceVersion == draft._ownerPreferenceVersion + (changed ? 1 : 0)
                && preference.UseDefault == draft.UseDefault && draft._preferredIds.SetEquals(preference.SiteIds))
                draft._ownerPreferenceVersion = _preferenceVersion;
        }
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

    /// <summary>
    /// 控件在页面初始化时可能先接在一个尚未连接的服务上；连接恢复后空戳不能挡住用户打开选择表。
    /// 只有服务此刻可用且戳确实变了才重新接一次，目录与草稿照常按新连接重读。
    /// </summary>
    internal void RefreshConnection()
    {
        if (_service is not { Available: true } service || service.IsCurrentConnection(_connectionStamp)) return;
        Attach(service, _mediaType);
    }

    internal Task PrepareSelectionAsync() => UseDefault ? Task.CompletedTask : EnsureLoadedAsync();

    public override void Cancel()
    {
        _editing = false;
        _editVersion++;
        _catalogueLoad = null;
        Observe(null);
        base.Cancel();
    }

    public override void Dispose()
    {
        _editing = false;
        _editVersion++;
        Observe(null);
        base.Dispose();
    }

    public override Task ReloadAsync() => _catalogueLoad = LoadCatalogueAsync();

    private async Task LoadCatalogueAsync()
    {
        if (_service is null || (_draftOwner is not null && (!DraftIsCurrent || !IsCurrentConnection))) return;
        var token = BeginLoad();
        try
        {
            var stamp = _service.ConnectionStamp;
            var sites = await _service.SearchSitesAsync(_mediaType, token).ConfigureAwait(true);
            if (!IsCurrent(token) || !_service.IsCurrentConnection(stamp) || !DraftIsCurrent) return;
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
                CopySites(sites);
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

    private void CopySites(IEnumerable<MoviePilotSearchSite> sites)
    {
        var wasUpdating = _updating;
        _updating = true;
        try
        {
            Sites.Clear();
            foreach (var site in sites)
                Sites.Add(new MoviePilotSearchSiteRow(site, SiteChanged) { Selected = _preferredIds.Contains(site.Id) });
        }
        finally { _updating = wasUpdating; }
    }

    private void SiteChanged(MoviePilotSearchSiteRow row)
    {
        if (_updating || !Sites.Contains(row) || !CanPick) return;
        _updating = true;
        try { UseDefault = false; }
        finally { _updating = false; }
        Changed();
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))] private Task Refresh() => ReloadAsync();
    [RelayCommand(CanExecute = nameof(CanSelect))] private void SelectAll() => Select(true);
    [RelayCommand(CanExecute = nameof(CanSelect))] private void ClearSelection() => Select(false);

    private void Select(bool selected)
    {
        if (!CanSelect) return;
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
        _preferenceVersion++;
        _preferredIds.ExceptWith(Sites.Select(site => site.Id));
        _preferredIds.UnionWith(Sites.Where(site => site.Selected).Select(site => site.Id));
        Exception? saveError = null;
        if (_draftOwner is null && _service?.IsCurrentConnection(_connectionStamp) == true)
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
        OnPropertyChanged(nameof(CanComplete));
        OnPropertyChanged(nameof(CanSearch));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(EmptyVisibility));
        RefreshCommand.NotifyCanExecuteChanged();
        SelectAllCommand.NotifyCanExecuteChanged();
        ClearSelectionCommand.NotifyCanExecuteChanged();
    }
    protected override void BusyChanged() => AnnounceSelection();
}
