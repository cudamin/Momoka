using System.Collections.ObjectModel;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.ViewModels;

public sealed partial class LibraryViewModel
{
    private double _favoritesWidth = LibraryFavorites.EpisodeWidth;
    private readonly LoadGeneration _favoriteLoads = new();
    private CancellationToken _favoriteToken;

    internal bool IsFavoritesPage => IsSectionLibrary && Section == LibrarySection.Favorites
        && LibraryFavorites.Rows(_request!.CollectionType, _scope?.Connection.ServerKind ?? MediaServerKind.Emby).Count > 0;
    public Visibility FavoritesVisibility => Show(IsFavoritesPage);
    public ObservableCollection<FavoriteRow> FavoriteRows { get; } = [];
    public ObservableCollection<FavoriteRow> VisibleFavoriteRows { get; } = [];

    /// <summary>标题栏收藏页的账号筛选：两个及以上可收藏的在线账号才出现。</summary>
    public Visibility FavoriteAccountFilterVisibility => Show(_request?.IsFavorites == true && _favoriteAccounts.Count > 1);

    public string FavoriteAccountLabel =>
        _favoriteAccounts.FirstOrDefault(source => source.Key == _request?.FavoriteAccountKey)?.DisplayName ?? "全部账号";

    internal IReadOnlyList<FavoriteAccountEntry> FavoriteAccountEntries()
    {
        var selected = _request?.FavoriteAccountKey;
        return
        [
            new(null, "全部账号", selected is null),
            .. _favoriteAccounts.Select(source => new FavoriteAccountEntry(source.Key, source.DisplayName, source.Key == selected))
        ];
    }

    /// <summary>
    /// 收藏页换账号：只把查询来源换成那一个或全部，重新从这里分页；返回栈里的选择也跟着留下。
    /// </summary>
    internal async Task<bool> ChooseFavoriteAccountAsync(string? key)
    {
        if (_request is not { IsFavorites: true } request || _favoriteAccounts.Count <= 1) return false;
        if (key is not null && _favoriteAccounts.All(source => source.Key != key)) return false;
        if (string.Equals(request.FavoriteAccountKey, key, StringComparison.Ordinal)) return false;

        request.FavoriteAccountKey = key;
        request.ScrollOffset = 0;
        request.LoadedCount = 0;
        _sources = FavoriteAccounts.Select(_favoriteAccounts, key);
        _scope = _sources.Count == 1 ? _sources[0] : _defaultScope;
        foreach (var card in Cards) card.AbandonPoster();
        Cards.Clear();
        _total = NotLoaded;
        ShowEmptyNotice = false;
        // 同页换来源与首次 Attach 使用同一套恢复规则。通用旧条件不能继续进入飞牛查询，
        // 标签与其在飞任务也属于旧来源；不保存、不清除其他库已记住的偏好。
        ResetFnNasFilters();
        RestoreSort();
        RestoreFilters();
        RestoreDisplay();
        RefreshBrowseChrome();
        RowsArrived();
        await ReloadAsync().ConfigureAwait(true);
        return true;
    }

    private void EnsureFavoriteRows()
    {
        var entries = LibraryFavorites.Rows(_request!.CollectionType, _scope!.Connection.ServerKind);
        if (FavoriteRows.Count == entries.Count && FavoriteRows.Select(row => row.Shelf).SequenceEqual(entries.Select(entry => entry.Shelf))) return;
        FavoriteRows.Clear();
        foreach (var entry in entries)
        {
            var row = new FavoriteRow(entry, LibraryFavorites.WidthFor(entry.Wide, _favoritesWidth));
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FavoriteRow.GroupVisibility)) RefreshVisibleFavoriteRows();
            };
            FavoriteRows.Add(row);
        }
        RefreshVisibleFavoriteRows();
    }

    private void RefreshVisibleFavoriteRows()
    {
        var desired = FavoriteRows.Where(row => row.GroupVisibility == Visibility.Visible).ToArray();
        for (var index = VisibleFavoriteRows.Count - 1; index >= 0; index--)
            if (!desired.Contains(VisibleFavoriteRows[index])) VisibleFavoriteRows.RemoveAt(index);
        for (var index = 0; index < desired.Length; index++)
        {
            var current = VisibleFavoriteRows.IndexOf(desired[index]);
            if (current < 0) VisibleFavoriteRows.Insert(index, desired[index]);
            else if (current != index) VisibleFavoriteRows.Move(current, index);
        }
    }

    internal void ResizeFavorites(double width)
    {
        if (!double.IsFinite(width) || width <= 0) return;
        _favoritesWidth = width;
        if (!IsFavoritesPage || _images is null) return;
        foreach (var row in FavoriteRows)
        {
            var cardWidth = LibraryFavorites.WidthFor(row.Wide, width);
            if (row.CardWidth == cardWidth) continue;
            row.CardWidth = cardWidth;
            for (var index = 0; index < row.Cards.Count; index++)
            {
                var previous = row.Cards[index];
                var replacement = NewFavoriteCard(previous.Item, row);
                var cardIndex = Cards.IndexOf(previous);
                previous.AbandonPoster();
                row.Cards[index] = replacement;
                if (cardIndex >= 0) Cards[cardIndex] = replacement;
            }
        }
    }

    private CardItem NewFavoriteCard(EmbyItem item, FavoriteRow row) => new(item, _images!, row.CardWidth, row.Wide,
        subtitle: LibraryFavorites.Subtitle(item), title: LibraryFavorites.Title(item),
        artwork: row.Wide ? LibraryUpcoming.Artwork(item) : null, indicators: false, centeredText: true,
        containArtwork: row.Wide);

    private async Task LoadFavoritesAsync(bool reset)
    {
        if (!reset || !_active || !SourcesAvailable || _request is null || _images is null) return;
        _queryToken = _queries.Begin();
        _favoriteToken = _favoriteLoads.Begin();
        _jumps.Cancel();
        _firstPage = false;
        _pageFailed = false;
        _total = NotLoaded;
        ShowEmptyNotice = false;
        foreach (var card in Cards) card.AbandonPoster();
        Cards.Clear();
        UpcomingDays.Clear();
        EnsureFavoriteRows();
        foreach (var row in FavoriteRows)
        {
            row.Cards.Clear();
            row.ReadCount = 0;
            row.Total = NotLoaded;
            row.Error = "";
            row.Loaded = false;
            row.Loading = true;
        }
        var queryToken = _queryToken;
        var token = BeginLoad();
        try
        {
            await Task.WhenAll(FavoriteRows.Select(row => ReadFavoriteRowAsync(row, queryToken, token))).ConfigureAwait(true);
            if (!QueryCurrent(queryToken) || !IsCurrent(token)) return;
            _firstPage = true;
            UpdateFavoriteState();
            RowsArrived();
            EndLoad(token);
            PageArrived?.Invoke();
        }
        finally { EndLoad(token); }
    }

    internal async Task LoadFavoriteRowAsync(FavoriteRow row)
    {
        var queryToken = _queryToken;
        if (!QueryCurrent(queryToken) || !IsFavoritesPage || !FavoriteRows.Contains(row) || row.Loading || !row.HasMore) return;
        await ReadFavoriteRowAsync(row, queryToken, _favoriteToken).ConfigureAwait(true);
        if (!QueryCurrent(queryToken)) return;
        UpdateFavoriteState();
        RowsArrived();
    }

    private async Task ReadFavoriteRowAsync(FavoriteRow row, CancellationToken queryToken, CancellationToken token)
    {
        if (_scope is not { IsAvailable: true } source || _request is not { ParentId: { } parentId }) return;
        row.Loading = true;
        row.Error = "";
        ShowEmptyNotice = false;
        try
        {
            var query = LibraryFavorites.Query(parentId, row.Shelf, row.ReadCount);
            var result = await source.ExecuteAsync((client, ct) => client.GetLibraryItemsAsync(
                "tvshows", LibrarySection.Favorites, null, query, ct), token).ConfigureAwait(true);
            if (token.IsCancellationRequested || !QueryCurrent(queryToken) || !IsFavoritesPage || !FavoriteRows.Contains(row) || !source.IsAvailable) return;
            var expected = row.Wide ? EmbyItemType.Episode : EmbyItemType.Series;
            var seen = row.Cards.Select(card => card.Item.IdentityKey).ToHashSet(StringComparer.Ordinal);
            foreach (var item in result.Items)
            {
                if (item.Type != expected || !seen.Add(item.IdentityKey)) continue;
                var card = NewFavoriteCard(item, row);
                row.Cards.Add(card);
                Cards.Add(card);
            }
            row.ReadCount += result.Items.Count;
            row.Total = result.Items.Count == 0 ? row.ReadCount : Math.Max(row.ReadCount, result.TotalRecordCount);
            row.Loaded = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (token.IsCancellationRequested || !QueryCurrent(queryToken) || !IsFavoritesPage || !FavoriteRows.Contains(row)) return;
            row.Error = Failure.Describe(error);
            Log.Warn(Category, $"读取「{row.Title}」失败", error);
        }
        finally
        {
            if (!token.IsCancellationRequested && QueryCurrent(queryToken) && IsFavoritesPage && FavoriteRows.Contains(row))
            {
                row.Loading = false;
                row.RefreshState();
            }
        }
    }

    private void UpdateFavoriteState()
    {
        if (!IsFavoritesPage) return;
        _pageFailed = FavoriteRows.Any(row => row.Error.Length > 0);
        _total = FavoriteRows.Sum(row => Math.Max(0, row.Total));
        EmptyNotice = "暂无收藏节目或集，可在详情页或卡片菜单中添加收藏";
        ShowEmptyNotice = !_pageFailed && FavoriteRows.All(row => row.Loaded && !row.Loading && row.Cards.Count == 0);
    }

    private async Task<bool> RemoveFavoriteAsync(CardItem card, bool deleted)
    {
        var row = FavoriteRows.FirstOrDefault(entry => entry.Cards.Contains(card));
        if (row is null) return false;
        if (deleted && card.Item.Type == EmbyItemType.Series)
        {
            await LoadFavoritesAsync(reset: true).ConfigureAwait(true);
            return QueryCurrent(_queryToken) && !_pageFailed && Cards.All(existing => existing.Item.IdentityKey != card.Item.IdentityKey);
        }
        base.Cancel();
        _favoriteToken = _favoriteLoads.Begin();
        _jumps.Cancel();
        foreach (var entry in FavoriteRows)
        {
            if (entry.Loading) entry.Error = "加载已中断，请重新加载";
            entry.Loading = false;
        }
        card.AbandonPoster();
        row.Cards.Remove(card);
        Cards.Remove(card);
        if (row.ReadCount > 0) row.ReadCount--;
        if (row.Total > 0) row.Total--;
        row.RefreshState();
        UpdateFavoriteState();
        IsReady = true;
        RowsArrived();
        return true;
    }
}
