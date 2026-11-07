using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell.ViewModels;

public sealed partial class LibraryViewModel
{
    private sealed class WindowEpisodesException : IOException
    {
        public WindowEpisodesException() : base("此媒体库返回了单集，请刷新以保留完整播放顺序") { }
    }
    private int _windowStart;
    private bool _windowRejected;
    private bool _windowNeedsPrefix;
    internal event Action? WindowAnchorRequested;
    internal event Action? WindowChanging;
    internal event Action? WindowChanged;
    internal int WindowStart => _windowStart;
    internal int WindowEnd => _windowStart + Cards.Count;
    internal int GridColumns => View is LibraryView.List or LibraryView.Table ? 1 : GridFit.Columns;
    internal bool HasPrevious => WindowEligible && _firstPage && _windowStart > 0;

    /// <summary>
    /// Only ordinary library roots have the stable ranged route this window uses. Emby/Jellyfin named
    /// indexes and child/episode lists retain their own semantics; Plex only implements section roots.
    /// FnNas root types exclude Episode, and an episode filter must keep the complete displayed context.
    /// </summary>
    // Confirmed local omissions break the server's index-to-visible-card mapping. Keep the ordinary
    // raw read cursor until the server index catches up, rather than restoring an invalid window anchor.
    internal bool WindowEligible => _removed.Count == 0 && !_windowRejected && !Aggregated && !UsesQueryCursor && _request is
    {
        IsSearch: false, IsFavorites: false, Row: null, Genre: null, PersonId: null,
        ParentId.Length: > 0, ParentType: null or EmbyItemType.CollectionFolder,
        Section: LibrarySection.Items, BrowseGroup: null
    }
        && _request.CollectionType is "movies" or "tvshows" or "mixed" or "homevideos"
        && SortKey != EmbySortBy.Random
        && _fnNasFilters.Type != "Episode";

    internal bool HasSavedAnchor => WindowEligible && _request is { AnchorIndex: >= 0, AnchorIdentityKey: not null }
        && _snapshotKey is not null && _request.AnchorQueryKey == _snapshotKey;

    internal int LocalIndexOf(int absoluteIndex) => absoluteIndex - _windowStart;

    internal int AnchorLocalIndex()
    {
        if (!HasSavedAnchor || _request is null || Cards.Count == 0) return -1;
        for (var index = 0; index < Cards.Count; index++)
            if (Cards[index].Item.IdentityKey == _request.AnchorIdentityKey) return index;
        return Math.Clamp(_request.AnchorIndex - _windowStart, 0, Cards.Count - 1);
    }

    internal void CaptureAnchor(int localIndex, double relativeY, int columns)
    {
        if (!WindowEligible || _request is null || _snapshotKey is null || localIndex < 0 || localIndex >= Cards.Count) return;
        _request.AnchorQueryKey = _snapshotKey;
        _request.AnchorIndex = _windowStart + localIndex;
        _request.AnchorIdentityKey = Cards[localIndex].Item.IdentityKey;
        _request.AnchorY = double.IsFinite(relativeY) ? relativeY : 0;
        _request.AnchorColumns = Math.Max(1, columns);
        _request.LoadedCount = WindowEnd;
    }

    private void ClearAnchor()
    {
        if (_request is null) return;
        _request.AnchorQueryKey = null;
        _request.AnchorIndex = -1;
        _request.AnchorIdentityKey = null;
        _request.AnchorY = 0;
        _request.ScrollOffset = 0;
        _request.LoadedCount = 0;
    }

    private void ReconcileAnchor()
    {
        if (!HasSavedAnchor || _request is null) return;
        var local = AnchorLocalIndex();
        if (local < 0) return;
        _request.AnchorIndex = _windowStart + local;
        _request.AnchorIdentityKey = Cards[local].Item.IdentityKey;
    }

    /// <summary>At most 400 DTOs, aligned to complete rows before the anchor and inside loaded content.</summary>
    private (int LocalStart, int Count) SnapshotWindow()
    {
        var count = Math.Min(Cards.Count, BrowseSnapshotStore.MaxItemsPerSnapshot);
        if (Cards.Count <= count) return (0, count);
        var columns = HasSavedAnchor ? Math.Max(1, _request!.AnchorColumns) : GridColumns;
        var anchor = HasSavedAnchor ? AnchorLocalIndex() : Cards.Count - 1;
        var local = Math.Clamp(anchor - count / 2, 0, Cards.Count - count);
        var absolute = _windowStart + local;
        absolute -= absolute % columns;
        if (absolute < _windowStart) absolute += columns;
        local = Math.Min(Cards.Count - 1, Math.Max(0, absolute - _windowStart));
        // If aligning down pushed the final anchor outside, advance one row without exceeding the cap.
        if (anchor >= local + count) local += columns;
        count = Math.Min(count, Cards.Count - local);
        return (local, count);
    }

    /// <summary>Ranged ordinary route; BuildQuery still owns every sort, filter and source condition.</summary>
    private async Task<ItemsResult> ReadRangeAsync(int start, int limit, CancellationToken token)
    {
        if (!WindowEligible || !_active || _scope is not { IsAvailable: true } source) throw new OperationCanceledException();
        return await ReadSourceRangeAsync(source, Math.Max(0, start), Math.Max(1, limit), token).ConfigureAwait(true);
    }

    /// <summary>Read the same bounded range again, clamping a shortened tail with at most one extra request.</summary>
    private async Task<(List<EmbyItem> Items, int Total, int Start)> ReadWindowAsync(int start, int wanted, CancellationToken token)
    {
        var limit = Math.Clamp(wanted, 1, BrowseSnapshotStore.MaxItemsPerSnapshot);
        var page = await ReadRangeAsync(start, limit, token).ConfigureAwait(true);
        token.ThrowIfCancellationRequested();
        var total = Math.Max(0, page.TotalRecordCount);
        if (total == 0) return ([], 0, 0);
        if (start >= total)
        {
            start = Math.Max(0, total - limit);
            start -= start % GridColumns;
            page = await ReadRangeAsync(start, limit, token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            total = Math.Max(0, page.TotalRecordCount);
        }
        // An empty positive-total page is a failed read, never proof that the cached content disappeared.
        if (page.Items.Count == 0 && total > 0) throw new IOException("服务器返回空窗口，请稍后刷新重试");
        if (page.Items.Any(item => item.Type == EmbyItemType.Episode)) throw new WindowEpisodesException();
        return (page.Items.Take(limit).ToList(), total, total == 0 ? 0 : start);
    }

    private async Task<bool> RefreshWindowAsync(CancellationToken query, bool background)
    {
        if (!_active || !WindowEligible || !QueryCurrent(query)) return false;
        // Capture the actual viewport before selecting the bounded range, only at this refresh boundary.
        WindowAnchorRequested?.Invoke();
        if (!QueryCurrent(query)) return false;
        var range = SnapshotWindow();
        var start = _windowStart + range.LocalStart;
        var wanted = Math.Max(range.Count, Math.Min(_settings!.PageSize, BrowseSnapshotStore.MaxItemsPerSnapshot));
        var token = background ? BeginBackgroundLoad() : BeginLoad();
        try
        {
            var result = await ReadWindowAsync(start, wanted, token).ConfigureAwait(true);
            if (!QueryCurrent(query) || !IsCurrent(token)) return false;
            // Compare the whole bounded window: changes after its first page must be noticed too.
            if (result.Start == _windowStart && result.Total == _total && result.Items.Count == Cards.Count
                && FirstPageMatches(result.Items))
            {
                CaptureSnapshot();
                return true;
            }
            WindowAnchorRequested?.Invoke();
            if (!QueryCurrent(query) || !IsCurrent(token)) return false;
            // The reader may have scrolled while HTTP was pending. Re-read at their latest anchor once;
            // never replace the live viewport with a stale range from the start of the request.
            if (result.Start == start && HasSavedAnchor && (_request!.AnchorIndex >= result.Start + result.Items.Count
                || _request.AnchorIndex < result.Start))
            {
                var current = SnapshotWindow();
                start = _windowStart + current.LocalStart;
                var requestedAnchor = _request!.AnchorIndex;
                var requestedIdentity = _request.AnchorIdentityKey;
                result = await ReadWindowAsync(start, wanted, token).ConfigureAwait(true);
                if (!QueryCurrent(query) || !IsCurrent(token)) return false;
                WindowAnchorRequested?.Invoke();
                if (!QueryCurrent(query) || !IsCurrent(token)) return false;
                // A second HTTP may outlive another scroll. Keep the live window rather than chase a
                // third range. An unchanged anchor outside a shortened/empty result still applies and
                // clamps normally; only a genuinely moved viewport rejects this second response.
                if (HasSavedAnchor && (_request.AnchorIndex != requestedAnchor
                    || _request.AnchorIdentityKey != requestedIdentity)
                    && (_request.AnchorIndex < result.Start || _request.AnchorIndex >= result.Start + result.Items.Count))
                    return false;
            }
            WindowChanging?.Invoke();
            if (!QueryCurrent(query) || !IsCurrent(token)) return false;
            _windowStart = result.Start;
            ApplyRefreshed(result.Items, result.Total, null);
            if (result.Total == 0) ClearAnchor();
            EndLoad(token);
            PageArrived?.Invoke();
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception error)
        {
            if (!QueryCurrent(query) || !IsCurrent(token)) return false;
            if (error is WindowEpisodesException) _windowNeedsPrefix = true;
            if (FailureDropsContent(error))
            {
                if (_snapshotKey is { } key) _snapshots?.InvalidateLibrary(key);
                foreach (var card in Cards) card.AbandonPoster();
                Cards.Clear();
                _windowStart = 0;
                ClearAnchor();
                _total = NotLoaded;
                _firstPage = false;
                SetPageFailed(true);
                ShowEmptyNotice = false;
                Report($"读取「{Heading}」失败，请刷新重试", error);
            }
            else if (background)
            {
                Log.Warn(Category, "媒体库窗口后台刷新失败，保留原内容", error);
                Notify(null, "后台刷新失败，仍显示上次读取的内容；可稍后刷新重试。",
                    Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
            }
            else
            {
                SetPageFailed(true);
                Report("更新媒体库窗口失败，请刷新重试", error);
            }
            return false;
        }
        finally
        {
            EndLoad(token);
            if (QueryCurrent(query) && IsCurrent(token)) WindowChanged?.Invoke();
        }
    }

    internal async Task LoadPreviousAsync()
    {
        if (!_active || Busy || !HasPrevious || _pageFailed) return;
        var query = _queryToken;
        var previousStart = _windowStart;
        var columns = GridColumns;
        var wanted = Math.Max(columns, Math.Max(1, _settings!.PageSize) / columns * columns);
        if (previousStart % columns != 0) wanted = previousStart % columns;
        var start = Math.Max(0, previousStart - wanted);
        // A restore can come from a different column count. Full row insertion retains the old row.
        var limit = previousStart - start;
        var token = BeginLoad();
        try
        {
            var page = await ReadRangeAsync(start, limit, token).ConfigureAwait(true);
            if (!QueryCurrent(query) || !IsCurrent(token) || _windowStart != previousStart) return;
            if (page.Items.Count != limit)
                throw new IOException("媒体库内容已变化，请刷新后继续向上浏览");
            if (page.Items.Any(item => item.Type == EmbyItemType.Episode)) throw new WindowEpisodesException();
            var identities = Cards.Select(card => card.Item.IdentityKey).ToHashSet(StringComparer.Ordinal);
            if (page.Items.Any(item => !identities.Add(item.IdentityKey)))
                throw new IOException("媒体库顺序已变化，请刷新后继续向上浏览");
            WindowChanging?.Invoke();
            if (!QueryCurrent(query) || !IsCurrent(token)) return;
            _windowStart = start;
            for (var index = page.Items.Count - 1; index >= 0; index--) Cards.Insert(0, NewCard(page.Items[index]));
            _total = page.TotalRecordCount;
            ReconcileAnchor();
            SetPageFailed(false);
            EndLoad(token);
            RowsArrived();
            WindowChanged?.Invoke();
            PageArrived?.Invoke();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!QueryCurrent(query) || !IsCurrent(token)) return;
            if (error is WindowEpisodesException) _windowNeedsPrefix = true;
            Report("向上读取媒体库失败，请刷新重试", error);
        }
        finally { EndLoad(token); }
    }

    /// <summary>Jump or a cold deep return reads only the target range, without replaying prior pages.</summary>
    private async Task LoadWindowAtAsync(int absoluteIndex)
    {
        if (!_active || !WindowEligible || Busy) return;
        var query = _queryToken;
        var columns = GridColumns;
        var size = Math.Min(BrowseSnapshotStore.MaxItemsPerSnapshot, Math.Max(columns, Math.Max(1, _settings!.PageSize)));
        var start = Math.Max(0, absoluteIndex - size / 4);
        start -= start % columns;
        var token = BeginLoad();
        try
        {
            var result = await ReadWindowAsync(start, size, token).ConfigureAwait(true);
            if (!QueryCurrent(query) || !IsCurrent(token)) return;
            _windowStart = result.Start;
            ApplyRefreshed(result.Items, result.Total, null);
            EndLoad(token);
            CaptureSnapshot();
            PageArrived?.Invoke();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!QueryCurrent(query) || !IsCurrent(token)) return;
            if (error is WindowEpisodesException) _windowNeedsPrefix = true;
            SetPageFailed(true);
            Report("读取媒体库位置失败，请刷新重试", error);
        }
        finally { EndLoad(token); }
    }

    /// <summary>Navigation differs from an explicit refresh: retain a query-matched anchor on a cache miss.</summary>
    internal async Task LoadForNavigationAsync()
    {
        if (!HasSavedAnchor)
        {
            await LoadAsync(reset: true).ConfigureAwait(true);
            return;
        }
        _queryToken = _queries.Begin();
        _jumps.Cancel();
        await LoadWindowAtAsync(_request!.AnchorIndex).ConfigureAwait(true);
    }
}
