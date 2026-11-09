using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.FnNas;
using Momoka.Infrastructure;

namespace Momoka.Shell.ViewModels;

public sealed partial class HomeViewModel
{
    private readonly FnNasTaskWatch _fnNasTasks = new();
    private readonly HashSet<string> _directorySources = new(StringComparer.Ordinal);
    private readonly HashSet<string> _directoryPendingShelves = new(StringComparer.Ordinal);
    private Action<EmbySessionScope, IReadOnlyList<EmbyItem>>? _directoryUpdated;
    private bool _directoryBusy;
    private long _directoryVersion;
    private EventHandler<EmbySessionScope>? _taskPermissionsChanged;
    private CancellationTokenSource? _directoryRead;

    private void StartFnNasTasks()
    {
        var visit = _visit;
        var eligible = _sources.Where(source => source.Connection.IsFnNas && source.IsAvailable && source.IsAdministrator == true).ToArray();
        _fnNasTasks.Attach(eligible, key =>
        {
            if (Volatile.Read(ref _visit) == visit) OnFnNasDirectoryChanged(key);
        });
        if (_session is null) return;
        if (_taskPermissionsChanged is not null) _session.PermissionsChanged -= _taskPermissionsChanged;
        _taskPermissionsChanged = (_, scope) =>
        {
            if (!scope.Connection.IsFnNas) return;
            _dispatcher?.Post(() =>
            {
                if (visit == _visit && Live) StartFnNasTasks();
            });
        };
        _session.PermissionsChanged += _taskPermissionsChanged;
    }

    internal void UseLibraryDirectory(Action<EmbySessionScope, IReadOnlyList<EmbyItem>>? updated) =>
        _directoryUpdated = updated;

    private void OnFnNasDirectoryChanged(string sourceKey)
    {
        lock (_realtimeSources)
        {
            if (!_active) return;
            _directorySources.Add(sourceKey);
            _realtimeDirty = true;
        }
        ScheduleRealtime();
    }

    /// <summary>官方任务完成只刷新目录和计数；已有非空货架不会因此重读。</summary>
    private async Task RefreshFnNasDirectoryAsync(string sourceKey)
    {
        var scope = _sources.FirstOrDefault(source => source.Key == sourceKey);
        if (!Live || scope is not { IsAvailable: true, IsAdministrator: true } || !scope.Connection.IsFnNas) return;
        if (Busy || _watchBusy || _directoryBusy)
        {
            OnFnNasDirectoryChanged(sourceKey);
            return;
        }
        var visit = _visit;
        var version = _directoryVersion;
        var permission = scope.PermissionRevision;
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(scope.Lifetime);
        _directoryRead = reading;
        _directoryBusy = true;
        try
        {
            var views = await scope.ExecuteAsync((client, token) => client.GetViewsAsync(token), reading.Token).ConfigureAwait(true);
            if (visit != _visit || !Live || !scope.IsAvailable || scope.IsAdministrator != true) return;
            if (version != _directoryVersion || permission != scope.PermissionRevision)
            {
                OnFnNasDirectoryChanged(sourceKey);
                return;
            }
            views = views.Where(item => !EmbyItemType.IsMusicLibrary(item.CollectionType)).ToList();
            var previous = _libraryViews.Where(item => item.Source?.Key == sourceKey)
                .ToDictionary(item => HomeLayout.LibraryKey(item), StringComparer.Ordinal);
            var merged = _libraryViews.ToList();
            var index = merged.FindIndex(item => item.Source?.Key == sourceKey);
            merged.RemoveAll(item => item.Source?.Key == sourceKey);
            merged.InsertRange(index < 0 ? merged.Count : Math.Min(index, merged.Count), views);
            _libraryViews = merged;
            _directoryUpdated?.Invoke(scope, views);

            var known = views.Select(HomeLayout.LibraryKey).ToHashSet(StringComparer.Ordinal);
            var old = _all.ToDictionary(entry => entry.Row.Key, StringComparer.Ordinal);
            var plan = HomeLayout.ForSources(HomeLayout.Plan(_settings?.Settings.Ui.HomeRows, _libraryViews, _banner),
                _sources.Where(source => source.IsAvailable).Select(source => source.Key),
                aggregateMode: _settings?.Settings.AggregateMode == true)
                .Where(row => !previous.ContainsKey(row.Key) || known.Contains(row.Key)).ToArray();
            var next = new List<(HomeRowPlan Row, CardShelf Shelf)>();
            var newShelves = new List<(EmbyItem Library, CardShelf Shelf)>();
            foreach (var row in plan)
            {
                var library = _libraryViews.FirstOrDefault(item => HomeLayout.LibraryKey(item) == row.Key);
                var shelf = old.TryGetValue(row.Key, out var retained) ? retained.Shelf
                    : new CardShelf(SourceCaption.Prefix(row.Title, library?.Source, ShowServerNames), _images!,
                        CardSize.PosterWidth, false, libraryId: HomeLayout.LibraryId(row.Key), target: HomeLayout.TargetOf(row.Key))
                    { HomeKey = row.Key, Source = library?.Source };
                next.Add((row, shelf));
                if (library?.Source?.Key != sourceKey) continue;
                shelf.Title = SourceCaption.Prefix(row.Title, scope, ShowServerNames);
                if (library.RecursiveItemCount == 0)
                {
                    shelf.Clear();
                    shelf.NoteOverride = "0 项";
                    _directoryPendingShelves.Remove(row.Key);
                }
                else
                {
                    shelf.NoteOverride = library.RecursiveItemCount is > 0 and var count ? $"{count} 项" : null;
                    if (row.Visible && library.RecursiveItemCount > 0
                        && (_directoryPendingShelves.Contains(row.Key)
                            || !previous.TryGetValue(row.Key, out var oldLibrary) || oldLibrary.RecursiveItemCount is not > 0))
                    {
                        _directoryPendingShelves.Add(row.Key);
                        newShelves.Add((library, shelf));
                    }
                }
            }
            foreach (var (row, shelf) in _all)
                if (!next.Any(entry => ReferenceEquals(entry.Shelf, shelf))) shelf.Clear();
            _all = next.ToArray();
            var directory = _all.FirstOrDefault(entry => entry.Row.Key == HomeLayout.Libraries);
            if (directory.Shelf is not null && directory.Row.Visible)
                directory.Shelf.Apply(_libraryViews, ShelfSubtitle(HomeLayout.Libraries), ShelfTitle(HomeLayout.Libraries));
            PublishShelves();

            // 官方 sum 从空到非空或新库才挂载货架；正数到正数不重读现有 item/list。
            foreach (var (library, shelf) in newShelves)
            {
                var items = await scope.ExecuteAsync((client, token) =>
                    client.GetLatestAsync(library.Id, LatestSize, token, HomeLatest.LibraryTypes(library)), reading.Token).ConfigureAwait(true);
                if (visit != _visit || !Live || !scope.IsAvailable || scope.IsAdministrator != true) return;
                if (version != _directoryVersion || permission != scope.PermissionRevision)
                {
                    OnFnNasDirectoryChanged(sourceKey);
                    return;
                }
                if (!_all.Any(entry => ReferenceEquals(entry.Shelf, shelf))) return;
                shelf.Apply(WithoutMusic(Shown(items)));
                _directoryPendingShelves.Remove(shelf.HomeKey);
                PublishShelves();
            }
            ReconcileRows();
            RefreshSnapshotKey();
            if (_settings is not null && IsReady && !NoticeOpen)
                _retainedFingerprint = RetainedFingerprint(_libraryViews, _settings, _sources);
            CaptureSnapshot();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (visit == _visit && Live) Log.Debug(Category, $"飞牛任务完成后的目录更新失败（{error.GetType().Name}）");
        }
        finally
        {
            if (visit == _visit)
            {
                if (ReferenceEquals(_directoryRead, reading)) _directoryRead = null;
                _directoryBusy = false;
                ScheduleRealtime();
            }
        }
    }
}
