using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Momoka.Emby;
using Momoka.Shell.Views;
using CardMetrics = Momoka.Infrastructure.CardSize;

namespace Momoka.Shell.ViewModels;

public sealed partial class RecentSearchRow
{
    private readonly LibraryViewModel _owner;
    internal RecentSearchRow(RecentSearchEntry entry, LibraryViewModel owner, CardItem card)
    {
        Entry = entry;
        _owner = owner;
        Card = card;
    }
    internal RecentSearchEntry Entry { get; }
    public CardItem Card { get; }
    public string Title => Entry.Name;
    public string TypeLabel => Entry.ItemId is null ? "搜索关键词" : EmbyItemType.ToChinese(Entry.Type);
    public string RemoveLabel => $"移除最近搜索：{Title}";
    [RelayCommand] private Task Open() => _owner.OpenRecentAsync(this);
    [RelayCommand] private void Remove() => _owner.RemoveRecent(this);
}

public sealed partial class LibraryViewModel
{
    internal Func<string, Task>? SubmitRecentSearch { get; set; }
    public ObservableCollection<RecentSearchRow> RecentSearches { get; } = [];

    /// <summary>
    /// 最近搜索的海报只在真正展示那一块时才建。普通媒体库和有词的搜索页都看不到它，不该为它发图片请求；
    /// 每次 <see cref="Attach"/> 重置，清空搜索框或切回媒体库来源时再按需恢复。
    /// </summary>
    private bool _recentLoaded;

    /// <summary>当前搜索来源是不是「媒体库」；MoviePilot 与 AniBT 那几段看不到最近搜索。</summary>
    private bool _embySourceActive = true;

    /// <summary>四个分组对象从头到尾不变；卡片对象仍由 <see cref="Cards"/> 持有，这里只是同一批对象的另一份按类型排好的清单。</summary>
    private readonly SearchResultGroup[] _searchGroupBuckets =
        [.. LibrarySearchGroups.Order.Select(kind => new SearchResultGroup(kind))];

    private static readonly IReadOnlyDictionary<LibrarySearchGroupKind, int> SearchGroupRank =
        LibrarySearchGroups.Order.Select((kind, index) => (Kind: kind, Index: index))
            .ToDictionary(entry => entry.Kind, entry => entry.Index);

    /// <summary>
    /// 有搜索词时的媒体库结果，按类型分开摆（见 <see cref="LibrarySearchGroups"/>）。只收现在有条目的组：
    /// 空组不占一条空版面，它的标题也就不会孤零零地出现在屏上。
    /// </summary>
    public ObservableCollection<SearchResultGroup> SearchGroups { get; } = [];

    public Visibility RecentSearchVisibility => Show(_request?.IsSearch == true && SearchText.Length == 0);
    public Visibility ClearRecentVisibility => Show(RecentSearches.Count > 0);
    public Visibility NoRecentVisibility => Show(RecentSearches.Count == 0);
    public Visibility SearchResultsVisibility => Show(_request?.IsSearch != true || SearchText.Length > 0);
    public Visibility SearchCategoriesVisibility => Show(_request?.IsSearch == true && SearchText.Length > 0);

    /// <summary>搜索页有词时，卡片按类型分组的视图取代那一面平铺的墙。</summary>
    public Visibility SearchGroupsVisibility => Show(IsGroupedSearch);

    internal bool IsGroupedSearch => _request?.IsSearch == true && SearchText.Length > 0;

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(RecentSearchVisibility));
        OnPropertyChanged(nameof(SearchResultsVisibility));
        OnPropertyChanged(nameof(SearchCategoriesVisibility));
        OnPropertyChanged(nameof(SearchGroupsVisibility));
        OnPropertyChanged(nameof(GridVisibility));
        OnPropertyChanged(nameof(EmptyVisibility));
        if (value.Length == 0) EnsureRecentSearches();
    }

    /// <summary>搜索页切来源：只有「媒体库」那一段会显示最近搜索。</summary>
    internal void SetEmbySourceActive(bool active)
    {
        _embySourceActive = active;
        if (active) EnsureRecentSearches();
    }

    /// <summary>Cards 的所有增删都经过这里，分组清单因此不会与平铺清单走散。</summary>
    private void OnSearchCardsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (_request?.IsSearch != true) return;

        switch (args.Action)
        {
            case NotifyCollectionChangedAction.Add when args.NewItems is { } added:
                foreach (CardItem card in added)
                {
                    var group = GroupOf(card.Item.Type);
                    group.Cards.Add(card);
                    ShowGroup(group);
                }
                break;
            case NotifyCollectionChangedAction.Remove when args.OldItems is { } removed:
                foreach (CardItem card in removed)
                {
                    var group = GroupOf(card.Item.Type);
                    group.Cards.Remove(card);
                    HideEmptyGroup(group);
                }
                break;
            case NotifyCollectionChangedAction.Reset:
                foreach (var group in _searchGroupBuckets) group.Cards.Clear();
                SearchGroups.Clear();
                break;
            default:
                // 删除后的重排会连着发 Move；只有这几条路会在搜索页改次序，整体按平铺次序重排一次最稳。
                RefreshSearchGroups();
                break;
        }
    }

    private SearchResultGroup GroupOf(string? type) =>
        _searchGroupBuckets.First(group => group.Kind == LibrarySearchGroups.KindOf(type));

    /// <summary>把分组清单整份对齐到 <see cref="Cards"/> 的次序；删除后的重排走这一支。</summary>
    private void RefreshSearchGroups()
    {
        foreach (var group in _searchGroupBuckets)
        {
            var desired = Cards
                .Where(card => LibrarySearchGroups.KindOf(card.Item.Type) == group.Kind)
                .ToArray();
            UpcomingListDiff.Apply(group.Cards, desired);
            if (desired.Length == 0) HideEmptyGroup(group);
            else ShowGroup(group);
        }
    }

    /// <summary>一组从无到有（或翻页时第一次出现）：按类型次序插进来，而不是都堆到末尾。</summary>
    private void ShowGroup(SearchResultGroup group)
    {
        if (group.Cards.Count == 0 || SearchGroups.Contains(group)) return;

        var rank = SearchGroupRank[group.Kind];
        var index = 0;
        while (index < SearchGroups.Count && SearchGroupRank[SearchGroups[index].Kind] < rank) index++;
        SearchGroups.Insert(index, group);
    }

    private void HideEmptyGroup(SearchResultGroup group)
    {
        if (group.Cards.Count == 0) SearchGroups.Remove(group);
    }

    /// <summary>卡片格宽或行高变了，组里那一排的版面要按同一档重排。</summary>
    private void UpdateSearchGroupMetrics()
    {
        var posterHeight = RowHeight;
        foreach (var group in _searchGroupBuckets)
        {
            group.CardWidth = CardWidth;
            // 单集结果按 16:9 的剧照摆（用户令 2026-11-01），和其它组的竖版海报不是同一档高度。
            group.RowHeight = group.Kind == LibrarySearchGroupKind.Episode
                ? CardMetrics.HeightFor(CardWidth, wide: true) + CardMetrics.Chrome + CardMetrics.MediaTitleExtra
                : posterHeight;
        }
    }

    internal void PrepareSearch(string text)
    {
        if (!_active || _request?.IsSearch != true) return;
        _queries.Cancel();
        _jumps.Cancel();
        base.Cancel();
        _firstPage = false;
        _total = 0;
        _request.SearchTerm = text.Trim();
        _request.LoadedCount = 0;
        _request.ScrollOffset = 0;
        SearchText = text;
        ShowEmptyNotice = false;
        foreach (var card in Cards) card.AbandonPoster();
        Cards.Clear();
    }

    internal Task ChooseSearchCategoryAsync(LibrarySearchCategory category)
    {
        if (!_active || _request is not { IsSearch: true } request || request.SearchCategory == category)
            return Task.CompletedTask;
        request.SearchCategory = category;
        request.ScrollOffset = 0;
        request.LoadedCount = 0;
        return ReloadAsync();
    }

    /// <summary>
    /// 空搜索页真正要展示最近搜索时才建。条件不满足时什么都不做 —— 普通媒体库和有词搜索页都走这里，
    /// 重复调用也不会重复请求。
    /// </summary>
    private void EnsureRecentSearches()
    {
        if (_recentLoaded || _request?.IsSearch != true || SearchText.Length != 0 || !_embySourceActive
            || _settings is null || _images is null)
            return;

        _recentLoaded = true;
        LoadRecentSearches();
    }

    private void LoadRecentSearches()
    {
        _recentLoaded = true;
        foreach (var row in RecentSearches) row.Card.AbandonPoster();
        RecentSearches.Clear();
        if (_settings is not null && _images is not null)
            foreach (var scope in _sources.Where(source => source.IsAvailable))
                foreach (var entry in RecentSearch.Read(_settings.Settings.Ui, scope.Connection))
                {
                    var item = entry.ToItem();
                    scope.Bind(item);
                    var card = new CardItem(item, _images, 56, indicators: false);
                    RecentSearches.Add(new RecentSearchRow(entry, this, card));
                    _ = card.EnsurePosterAsync();
                }
        OnPropertyChanged(nameof(ClearRecentVisibility));
        OnPropertyChanged(nameof(NoRecentVisibility));
        OnSearchTextChanged(SearchText);
    }

    internal void ReleaseSearchPosters()
    {
        foreach (var row in RecentSearches) row.Card.AbandonPoster();
    }

    internal void RememberSearch(EmbyItem? item = null)
    {
        var scope = item?.Source ?? _scope;
        if (!_active || scope is not { IsAvailable: true } || _request?.IsSearch != true || _settings is null) return;
        RecentSearch.Remember(_settings.Settings.Ui, scope.Connection, SearchText, item);
        _recentLoaded = false;
        SaveSearchHistory();
    }

    internal async Task OpenRecentAsync(RecentSearchRow row)
    {
        if (!_active || row.Card.Scope is not { IsAvailable: true } scope || !row.Card.IsCurrent) return;
        if (row.Entry.ItemId is null)
            await (SubmitRecentSearch?.Invoke(row.Entry.Term) ?? SearchAsync(row.Entry.Term)).ConfigureAwait(true);
        else
        {
            RecentSearch.Remember(_settings!.Settings.Ui, scope.Connection, row.Entry.Term, row.Card.Item);
            SaveSearchHistory();
            _actions?.OpenItem(row.Card.Item);
        }
    }

    internal void RemoveRecent(RecentSearchRow row)
    {
        if (!_active || row.Card.Scope is not { IsAvailable: true } scope || _settings is null) return;
        RecentSearch.Remove(_settings.Settings.Ui, scope.Connection, row.Entry);
        row.Card.AbandonPoster();
        RecentSearches.Remove(row);
        OnPropertyChanged(nameof(ClearRecentVisibility));
        OnPropertyChanged(nameof(NoRecentVisibility));
        SaveSearchHistory();
    }

    [RelayCommand]
    private void ClearRecentSearches()
    {
        if (!_active || _settings is null) return;
        foreach (var scope in _sources) RecentSearch.Clear(_settings.Settings.Ui, scope.Connection);
        LoadRecentSearches();
        SaveSearchHistory();
    }

    private void SaveSearchHistory()
    {
        if (_settings?.TrySave() == false)
            Notify(null, "最近搜索暂未保存，重启后可能丢失。", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
    }
}
