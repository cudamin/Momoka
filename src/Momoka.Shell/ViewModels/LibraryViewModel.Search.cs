using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Momoka.Emby;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;

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
    public ObservableCollection<RecentSearchRow> RecentSearches { get; } = [];
    public Visibility RecentSearchVisibility => Show(_request?.IsSearch == true && SearchText.Length == 0);
    public Visibility ClearRecentVisibility => Show(RecentSearches.Count > 0);
    public Visibility NoRecentVisibility => Show(RecentSearches.Count == 0);
    public Visibility SearchResultsVisibility => Show(_request?.IsSearch != true || SearchText.Length > 0);
    public Visibility SearchCategoriesVisibility => Show(_request?.IsSearch == true && SearchText.Length > 0);

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(RecentSearchVisibility));
        OnPropertyChanged(nameof(SearchResultsVisibility));
        OnPropertyChanged(nameof(SearchCategoriesVisibility));
        OnPropertyChanged(nameof(EmptyVisibility));
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

    private void LoadRecentSearches()
    {
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
        SaveSearchHistory();
    }

    internal async Task OpenRecentAsync(RecentSearchRow row)
    {
        if (!_active || row.Card.Scope is not { IsAvailable: true } scope || !row.Card.IsCurrent) return;
        if (row.Entry.ItemId is null)
            await SearchAsync(row.Entry.Term).ConfigureAwait(true);
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
