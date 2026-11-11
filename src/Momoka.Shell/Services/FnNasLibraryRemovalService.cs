using System.Collections.Concurrent;
using Momoka.Emby;
using Momoka.FnNas;

namespace Momoka.Shell.Services;

/// <summary>飞牛保留文件的移出与恢复；身份/权限在发出时再核对，确认写入不夹带可失败的刷新。</summary>
public sealed class FnNasLibraryRemovalService(RemovedMedia removed)
{
    private sealed record Removal(object Ticket, string LibraryGuid, string ItemId, IReadOnlyList<FnNasVersion> Versions);
    private readonly ConcurrentDictionary<string, List<Removal>> _confirmed = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, HashSet<(string Library, string Path)>> _restored = new(StringComparer.Ordinal);

    internal static bool CanManage(EmbySessionScope scope) => scope.IsAvailable && scope.Connection.IsFnNas && scope.IsAdministrator == true;

    internal async Task<FnNasRemovalReceipt> RemoveAsync(EmbySessionScope scope, EmbyItem item, IReadOnlyList<FnNasVersion> versions,
        string? mediaGuid, CancellationToken cancellationToken = default)
    {
        Require(scope);
        if (item.Source?.Key != scope.Key || item.Source.UserStateOwner != scope.UserStateOwner)
            throw new InvalidOperationException("媒体所属账号已改变，请重新打开媒体库");
        if (!item.IsPlayable || versions.Count == 0 || versions.Any(version => string.IsNullOrWhiteSpace(version.MediaGuid) || string.IsNullOrWhiteSpace(version.Path)))
            throw new InvalidOperationException("服务器没有完整文件标识，无法安全移出；请重新读取版本列表");
        if (string.IsNullOrWhiteSpace(item.FnNasLibraryGuid))
            throw new InvalidOperationException("服务器没有返回媒体库标识，请打开详情后再移出");
        if (mediaGuid is not null && !versions.Any(version => version.MediaGuid == mediaGuid))
            throw new InvalidOperationException("所选版本已改变，请重新读取版本列表");
        var selected = versions.Where(version => mediaGuid is null || version.MediaGuid == mediaGuid).ToArray();
        var records = _confirmed.GetOrAdd(scope.Key, static _ => []);
        lock (records)
            if (records.Any(record => record.ItemId == item.Id && record.Versions.Any(known => selected.Any(version => version.MediaGuid == known.MediaGuid))))
                throw new InvalidOperationException("此版本已移出媒体库，请刷新查看；需要恢复时打开已移除文件");
        var revision = scope.PermissionRevision;
        var ticket = new object();
        var confirmed = false;
        void Confirm()
        {
            lock (records) records.Add(new(ticket, item.FnNasLibraryGuid, item.Id, selected));
            var restored = _restored.GetOrAdd(scope.Key, static _ => []);
            lock (restored) restored.RemoveWhere(entry => entry.Library == item.FnNasLibraryGuid && selected.Any(version => version.Path == entry.Path));
            confirmed = true;
        }
        try
        {
            await scope.ExecuteAsync(async (client, token) =>
            {
                await client.FnNas.RemoveItemFromLibraryAsync(ScopedConnection(client, scope, revision), item.Id,
                    mediaGuid is null ? [] : new[] { mediaGuid }, token, Confirm).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (confirmed) { }
        bool subjectRemoved;
        lock (records)
        {
            var confirmedIds = records.Where(record => record.ItemId == item.Id).SelectMany(record => record.Versions)
                .Select(version => version.MediaGuid).ToHashSet(StringComparer.Ordinal);
            subjectRemoved = mediaGuid is null || versions.All(version => confirmedIds.Contains(version.MediaGuid));
        }
        return new(ticket, subjectRemoved);
    }

    internal async Task<FnNasRemovedFileDirectory> ReadAsync(EmbySessionScope scope, string libraryGuid, string? folderGuid,
        CancellationToken cancellationToken = default)
    {
        Require(scope);
        var revision = scope.PermissionRevision;
        var directory = await scope.ExecuteAsync((client, token) => client.FnNas.GetRemovedFileDirectoryAsync(
            ScopedConnection(client, scope, revision), libraryGuid, folderGuid, token), cancellationToken).ConfigureAwait(false);
        var restored = _restored.GetOrAdd(scope.Key, static _ => []);
        lock (restored)
        {
            foreach (var entry in directory.AllEntries.Where(entry => !entry.IsRemoved)) restored.Remove((libraryGuid, entry.Path));
            return directory with { Entries = directory.Entries.Where(entry => !entry.IsRemoved || !restored.Contains((libraryGuid, entry.Path))).ToArray() };
        }
    }

    internal async Task RestoreAsync(EmbySessionScope scope, string libraryGuid, string path, CancellationToken cancellationToken = default)
    {
        Require(scope);
        var restored = _restored.GetOrAdd(scope.Key, static _ => []);
        lock (restored)
            if (restored.Contains((libraryGuid, path))) throw new InvalidOperationException("此文件已提交恢复，请刷新等待索引更新，不必重复恢复");
        var revision = scope.PermissionRevision;
        var confirmed = false;
        try
        {
            await scope.ExecuteAsync(async (client, token) =>
            {
                await client.FnNas.RestoreRemovedFileAsync(ScopedConnection(client, scope, revision), libraryGuid, path, token,
                    () => confirmed = true).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (confirmed) { }
        lock (restored) restored.Add((libraryGuid, path));
        if (!_confirmed.TryGetValue(scope.Key, out var records)) return;
        lock (records)
        {
            var matching = records.Where(record => record.LibraryGuid == libraryGuid && record.Versions.Any(version => version.Path == path
                         || version.Path.StartsWith(path.TrimEnd('/') + "/", StringComparison.Ordinal))).ToArray();
            var itemIds = matching.Select(record => record.ItemId).ToHashSet(StringComparer.Ordinal);
            foreach (var record in records.Where(record => record.LibraryGuid == libraryGuid && itemIds.Contains(record.ItemId)))
                removed.Forget(record.Ticket);
            foreach (var record in matching)
            {
                records.Remove(record);
                var remaining = record.Versions.Where(version => version.Path != path
                    && !version.Path.StartsWith(path.TrimEnd('/') + "/", StringComparison.Ordinal)).ToArray();
                if (remaining.Length > 0) records.Add(record with { Versions = remaining });
            }
        }
    }

    private static FnNasConnection ScopedConnection(EmbyClient client, EmbySessionScope scope, long revision) =>
        client.FnNasConnection with { IsCurrent = () => CanManage(scope) && scope.PermissionRevision == revision };

    private static void Require(EmbySessionScope scope)
    {
        scope.ThrowIfNotAvailable();
        if (!CanManage(scope)) throw new InvalidOperationException("当前账号没有飞牛影视管理权限，请重新打开页面核对");
    }
}
