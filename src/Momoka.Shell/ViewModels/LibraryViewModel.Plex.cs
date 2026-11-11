using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Momoka.Emby;
using Momoka.Plex;

namespace Momoka.Shell.ViewModels;

public sealed partial class LibraryViewModel
{
    private PlexFilterSelection _plexFilters = new();
    private CancellationTokenSource? _plexLifetime;
    private Task? _plexCatalogTask;
    private long _plexEpoch;
    private bool _plexTypeRestoring;
    private readonly Dictionary<string, long> _plexSelectionOperations = new(StringComparer.Ordinal);
    private bool _plexRefreshCatalog;
    internal bool IsPlex => _scope?.Connection.IsPlex == true;
    internal PlexLibraryCatalog? PlexCatalog { get; private set; }
    public ObservableCollection<PlexFilterChoice> PlexTypes { get; } = [];
    public ObservableCollection<PlexFilterRow> PlexFilterRows { get; } = [];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlexFilterVisibility))]
    public partial bool PlexFilterOpen { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlexFilterLoadingVisibility))]
    public partial bool PlexFilterLoading { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlexFilterErrorVisibility))]
    public partial string PlexFilterError { get; set; } = "";
    [ObservableProperty]
    public partial PlexFilterChoice? PlexSelectedType { get; set; }
    public Visibility PlexFilterVisibility => Show(IsPlex && PlexFilterOpen && _active);
    public Visibility PlexFilterLoadingVisibility => Show(PlexFilterLoading);
    public Visibility PlexFilterErrorVisibility => Show(PlexFilterError.Length > 0);
    public Visibility PlexTypeVisibility => Show(PlexTypes.Count > 1);
    private bool PlexHasSorts => IsPlexSection && PlexType?.Sorts.Count > 0;
    private bool IsPlexSection => IsPlex && _request is
    {
        IsSearch: false, Row: null, PersonId: null, Genre: null,
        ParentId.Length: > 0, ParentType: null or EmbyItemType.CollectionFolder, CollectionType: "movies" or "tvshows" or "homevideos" or "mixed"
    };
    private PlexLibraryType? PlexType => PlexCatalog?.Types.FirstOrDefault(type => type.Id == _plexFilters.Type);
    private bool PlexHasFilters => IsPlexSection && (PlexType?.Filters.Count > 0 || PlexFilterError.Length > 0 || _plexFilters.HasConditions || PlexTypes.Count > 1);
    private bool PlexHasAlphabet => IsPlexSection && _active && _scope?.IsAvailable == true
        && PlexType?.Sorts.Any(sort => sort.Key == SortKey && sort.FirstCharacterKey.Length > 0) == true;

    private void ResetPlex()
    {
        _plexEpoch++;
        _plexLifetime?.Cancel();
        _plexLifetime?.Dispose();
        _plexLifetime = new CancellationTokenSource();
        _plexCatalogTask = null;
        PlexCatalog = null;
        _plexRefreshCatalog = true;
        PlexFilterRows.Clear();
        PlexTypes.Clear();
        PlexFilterOpen = false;
        PlexFilterLoading = false;
        PlexFilterError = "";
        _plexFilters = IsPlex && MemoryKey() is { } key && Settings.Ui.PlexFilters.TryGetValue(key, out var saved)
            ? saved.Clone() : new();
        _plexSelectionOperations.Clear();
    }

    /// <summary>Cancel/Resume is also used by LibraryPage.ApplySource, before its first load.</summary>
    private void ResumePlex()
    {
        _plexEpoch++;
        _plexLifetime?.Cancel();
        _plexLifetime?.Dispose();
        _plexLifetime = new CancellationTokenSource();
        _plexCatalogTask = null;
        _plexSelectionOperations.Clear();
        PlexFilterLoading = false;
        _plexRefreshCatalog = true;
    }

    internal Task EnsurePlexCatalogAsync(CancellationToken token = default, bool refresh = false)
    {
        if (!IsPlexSection || _scope is not { IsAvailable: true } scope || _request?.ParentId is not { } section) return Task.CompletedTask;
        if (!refresh && _plexCatalogTask is not null) return _plexCatalogTask.WaitAsync(token);
        refresh |= _plexRefreshCatalog;
        if (!refresh && PlexCatalog is not null) return Task.CompletedTask;
        if (refresh)
        {
            _plexEpoch++;
            _plexLifetime?.Cancel();
            _plexLifetime?.Dispose();
            _plexLifetime = new CancellationTokenSource();
            PlexCatalog = null;
        }
        _plexLifetime ??= new CancellationTokenSource();
        var epoch = _plexEpoch;
        var request = _request;
        var lifetime = _plexLifetime.Token;
        var loading = LoadPlexCatalogAsync(scope, section, epoch, request, lifetime, refresh);
        _plexCatalogTask = loading.IsCompleted ? null : loading;
        return loading.WaitAsync(token);
    }

    private async Task LoadPlexCatalogAsync(EmbySessionScope scope, string section, long epoch,
        Views.LibraryRequest request, CancellationToken token, bool refresh)
    {
        PlexFilterLoading = true;
        PlexFilterError = "";
        try
        {
            var catalog = await scope.ExecuteAsync((client, ct) => client.GetPlexLibraryCatalogAsync(section, ct, refresh), token).ConfigureAwait(true);
            if (!PlexCurrent(scope, request, epoch, token)) return;
            PlexCatalog = catalog;
            _plexRefreshCatalog = false;
            if (_plexFilters.Type.Length == 0)
            {
                _plexFilters.Type = catalog.Types.FirstOrDefault()?.Id ?? "";
                _plexFilters.TypeIsExplicit = false;
            }
            PlexTypes.Clear();
            foreach (var type in catalog.Types) PlexTypes.Add(new(type.Id, type.Title));
            _plexTypeRestoring = true;
            PlexSelectedType = PlexTypes.FirstOrDefault(type => type.Value == _plexFilters.Type);
            _plexTypeRestoring = false;
            if (_plexFilters.Type.Length > 0 && PlexType is null && _plexFilters.HasConditions)
                PlexFilterError = "保存的媒体类型已失效，请清除筛选或重试。";
            RestorePlexSort();
            await LoadPlexRowsAsync(scope, request, epoch, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (PlexCurrent(scope, request, epoch, token))
            {
                PlexFilterError = "读取 Plex 筛选目录失败，可重试。";
                PlexFilterRows.Clear();
                if (PlexCatalog is not null) BuildPlexRecoveryRows();
                Report("读取 Plex 筛选目录失败", error);
            }
        }
        finally
        {
            if (PlexCurrent(scope, request, epoch, token))
            {
                PlexFilterLoading = false;
                _plexCatalogTask = null;
                RaisePlexChrome();
            }
        }
    }

    private async Task LoadPlexRowsAsync(EmbySessionScope scope, Views.LibraryRequest request, long epoch, CancellationToken token)
    {
        var rows = new List<PlexFilterRow>();
        if (PlexType is { } type)
            foreach (var field in type.Filters)
            {
                var options = await scope.ExecuteAsync((client, ct) => client.GetPlexLibraryFilterOptionsAsync(
                    request.ParentId!, type.Id, field.Field, _plexFilters.Clone(), ct), token).ConfigureAwait(true);
                if (!PlexCurrent(scope, request, epoch, token)) return;
                var row = new PlexFilterRow(field, RowChanged);
                row.Options.Add(new("", "全部"));
                foreach (var option in options.Where(option => option.Value.Length > 0)) row.Options.Add(new(option.Value, option.Title));
                row.Restore(_plexFilters.Values.GetValueOrDefault(field.Field)?.FirstOrDefault() ?? "");
                rows.Add(row);
            }
        if (!PlexCurrent(scope, request, epoch, token)) return;
        PlexFilterRows.Clear();
        foreach (var row in rows) PlexFilterRows.Add(row);
        ShowPlexFilterState();

        void RowChanged(PlexFilterRow row) => _ = SelectPlexFilterAsync(row.Field, row.SelectedOption?.Value ?? "");
    }

    private void BuildPlexRecoveryRows()
    {
        foreach (var filter in PlexType?.Filters ?? [])
        {
            var row = new PlexFilterRow(filter, row => _ = SelectPlexFilterAsync(row.Field, row.SelectedOption?.Value ?? ""));
            row.Options.Add(new("", "全部"));
            row.Restore(_plexFilters.Values.GetValueOrDefault(filter.Field)?.FirstOrDefault() ?? "");
            PlexFilterRows.Add(row);
        }
    }

    private bool PlexCurrent(EmbySessionScope scope, Views.LibraryRequest request, long epoch, CancellationToken token) =>
        _active && !token.IsCancellationRequested && _plexEpoch == epoch && ReferenceEquals(_scope, scope)
        && ReferenceEquals(_request, request) && scope.IsAvailable;

    internal async Task TogglePlexFilterAsync()
    {
        if (!IsPlexSection || !_active || !SourcesAvailable) return;
        PlexFilterOpen = !PlexFilterOpen;
        if (PlexFilterOpen)
        {
            await EnsurePlexCatalogAsync().ConfigureAwait(true);
            if (_scope is { IsAvailable: true } scope && _request is { } request && _plexLifetime is { } lifetime && PlexCatalog is not null)
                try { await LoadPlexRowsAsync(scope, request, _plexEpoch, lifetime.Token).ConfigureAwait(true); }
                catch (OperationCanceledException) { }
                catch (Exception error) { PlexFilterError = "读取筛选选项失败，可重试或清除筛选。"; Report("读取筛选选项失败", error); }
        }
    }
    internal void ClosePlexFilter() => PlexFilterOpen = false;

    internal async Task SelectPlexFilterAsync(string field, string value)
    {
        if (!IsPlexSection || !_active || _scope?.IsAvailable != true || PlexType is not { } type) return;
        if (!type.Filters.Any(filter => filter.Field == field)) return;
        var operation = _plexSelectionOperations.GetValueOrDefault(field) + 1;
        _plexSelectionOperations[field] = operation;
        var row = PlexFilterRows.FirstOrDefault(row => row.Field == field);
        if (value.Length > 0 && (row is null || !row.Options.Any(option => option.Value == value))) return;
        if (value.Length > 0 && _scope is { } currentScope && _request?.ParentId is { } section)
        {
            var epoch = _plexEpoch;
            var request = _request;
            var selection = _plexFilters.Clone();
            var token = _plexLifetime?.Token ?? default;
            try
            {
                var options = await currentScope.ExecuteAsync((client, ct) => client.GetPlexLibraryFilterOptionsAsync(section, type.Id, field, selection, ct), token).ConfigureAwait(true);
                if (!PlexCurrent(currentScope, request, epoch, token) || _plexFilters.Type != type.Id
                    || _plexSelectionOperations.GetValueOrDefault(field) != operation) return;
                if (!options.Any(option => option.Value == value)) { PlexFilterError = "筛选选项已改变，请重试或清除筛选。"; return; }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception error)
            {
                if (PlexCurrent(currentScope, request, epoch, token) && _plexSelectionOperations.GetValueOrDefault(field) == operation)
                { PlexFilterError = "读取筛选选项失败，可重试。"; Report("读取筛选选项失败", error); }
                return;
            }
        }
        if ((_plexFilters.Values.GetValueOrDefault(field)?.FirstOrDefault() ?? "") == value) return;
        if (value.Length == 0) _plexFilters.Values.Remove(field); else _plexFilters.Values[field] = [value];
        row?.Restore(value);
        PlexFilterError = "";
        RememberPlexFilters();
        ShowPlexFilterState();
        await ReloadAsync().ConfigureAwait(true);
    }

    internal async Task SelectPlexTypeAsync(string type)
    {
        if (!IsPlexSection || !_active || PlexCatalog?.Types.Any(entry => entry.Id == type) != true || _plexFilters.Type == type) return;
        _plexFilters.Type = type;
        _plexFilters.TypeIsExplicit = true;
        _plexFilters.Values.Clear();
        _plexEpoch++;
        _plexLifetime?.Cancel();
        _plexLifetime?.Dispose();
        _plexLifetime = new CancellationTokenSource();
        _plexCatalogTask = null;
        _plexSelectionOperations.Clear();
        RememberPlexFilters();
        RestorePlexSort();
        _plexTypeRestoring = true;
        PlexSelectedType = PlexTypes.FirstOrDefault(entry => entry.Value == type);
        _plexTypeRestoring = false;
        if (_scope is { } scope && _request is { } request && _plexLifetime is { } lifetime)
            try { await LoadPlexRowsAsync(scope, request, _plexEpoch, lifetime.Token).ConfigureAwait(true); }
            catch (OperationCanceledException) { return; }
            catch (Exception error) { PlexFilterError = "读取筛选选项失败，可重试。"; Report("读取筛选选项失败", error); }
        await ReloadAsync().ConfigureAwait(true);
    }
    partial void OnPlexSelectedTypeChanged(PlexFilterChoice? value)
    {
        if (!_plexTypeRestoring && value is not null) _ = SelectPlexTypeAsync(value.Value);
    }

    internal async Task ClearPlexFiltersAsync()
    {
        if (!IsPlexSection || !_active) return;
        _plexFilters = new() { Type = PlexCatalog?.Types.FirstOrDefault()?.Id ?? "", TypeIsExplicit = false };
        PlexFilterError = "";
        RememberPlexFilters();
        await EnsurePlexCatalogAsync(refresh: true).ConfigureAwait(true);
        await ReloadAsync().ConfigureAwait(true);
    }

    internal async Task RetryPlexFiltersAsync()
    {
        await EnsurePlexCatalogAsync(refresh: true).ConfigureAwait(true);
        await ReloadAsync().ConfigureAwait(true);
    }

    private void RestorePlexSort()
    {
        var options = PlexType?.Sorts;
        if (options is null || options.Count == 0)
        {
            // Empty directories may still allow ordinary browsing. Its legacy query accepts shared
            // sort names, whereas a retained native key (titleSort) belongs only to the typed route.
            SortKey = EmbySortBy.Name;
            ShowSort("标题", false);
            return;
        }
        var fallback = options.FirstOrDefault(option => option.Key == "titleSort") ?? options[0];
        if (MemoryKey() is { } key && Settings.Ui.Sort.TryGetValue(key, out var saved)
            && options.FirstOrDefault(option => option.Key == saved.By) is { } option)
        {
            SortKey = option.Key;
            ShowSort(option.Title, saved.Descending && option.DescKey.Length > 0);
        }
        else
        {
            SortKey = fallback.Key;
            ShowSort(fallback.Title, fallback.DefaultDescending && fallback.DescKey.Length > 0);
        }
    }

    private IReadOnlyList<SortEntry> PlexSortEntries() => PlexType is { } type
        ? type.Sorts.Select(option => new SortEntry(new EmbySortOption
        { Value = option.Key, Label = option.Title, DescendingByDefault = option.DefaultDescending && option.DescKey.Length > 0 },
            option.Key == SortKey, SortDescending, option.DescKey.Length > 0)).ToArray() : [];

    private void RememberPlexFilters()
    {
        if (MemoryKey() is { } key) Settings.Ui.PlexFilters[key] = _plexFilters.Clone();
    }
    private void ShowPlexFilterState()
    {
        FilterCount = _plexFilters.Count;
        var parts = _plexFilters.Values.Where(pair => pair.Value.Count > 0).Select(pair =>
            (PlexType?.Filters.FirstOrDefault(field => field.Field == pair.Key)?.Title ?? pair.Key) + "："
            + string.Join('、', pair.Value.Select(value => PlexFilterRows.FirstOrDefault(row => row.Field == pair.Key)?.Options.FirstOrDefault(option => option.Value == value)?.Title ?? value)));
        FilterTooltip = FilterCount == 0 ? "筛选" : string.Join("；", parts);
        FilterChips.Clear();
        foreach (var pair in _plexFilters.Values.Where(pair => pair.Value.Count > 0))
        {
            var field = pair.Key;
            var title = PlexType?.Filters.FirstOrDefault(filter => filter.Field == field)?.Title ?? field;
            var value = string.Join('、', pair.Value.Select(value => PlexFilterRows.FirstOrDefault(row => row.Field == field)?.Options.FirstOrDefault(option => option.Value == value)?.Title ?? value));
            FilterChips.Add(new FilterChipRow(title + "：" + value, () => _ = SelectPlexFilterAsync(field, "")));
        }
        RaisePlexChrome();
    }
    private void RaisePlexChrome()
    {
        foreach (var name in new[] { nameof(FilterVisibility), nameof(SortFilterVisibility), nameof(AlphaVisibility), nameof(PlexFilterVisibility),
            nameof(PlexTypeVisibility), nameof(FilterChipsVisibility) }) OnPropertyChanged(name);
        RefreshSnapshotKey();
    }

    private async Task<(int Index, string Letter)?> JumpPlexAsync(string letter)
    {
        if (!PlexHasAlphabet || SortDescending || _scope is not { IsAvailable: true } scope || _request?.ParentId is not { } section) return null;
        var query = _queryToken;
        var jump = _jumps.Begin();
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(query, jump);
        try
        {
            var alphabet = await scope.ExecuteAsync((client, ct) => client.GetPlexLibraryFirstCharactersAsync(section, _plexFilters.Clone(), ct), stopping.Token).ConfigureAwait(true);
            if (!QueryCurrent(query) || stopping.IsCancellationRequested) return null;
            var target = alphabet.FirstOrDefault(bucket => bucket.Key.Equals(letter, StringComparison.OrdinalIgnoreCase));
            if (target is null) return null;
            await LoadUntilAsync(target.Index).ConfigureAwait(true);
            var local = LocalIndexOf(target.Index);
            return QueryCurrent(query) && !stopping.IsCancellationRequested && local >= 0 && local < Cards.Count ? (local, target.Title) : null;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception error) { if (QueryCurrent(query)) Report("Plex 字母跳转失败", error); return null; }
    }
}
