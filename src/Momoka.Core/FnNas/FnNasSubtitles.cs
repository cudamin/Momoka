using System.Net;
using Momoka.Emby;

namespace Momoka.FnNas;

/// <summary>A dialog visit owns one file and one online identity. Confirmed writes and read-back are separate operations.</summary>
public sealed class FnNasSubtitles
{
    private readonly EmbySessionScope _scope;
    private readonly EmbyItem _item;
    private readonly MediaSource _source;
    private readonly string _itemId;
    private readonly string _mediaGuid;
    private readonly string _path;
    private readonly Func<ItemMenuPermissions> _permissions;
    private readonly Func<bool> _isCurrent;
    private readonly HashSet<string> _downloaded = new(StringComparer.Ordinal);
    private IReadOnlyList<FnNasSubtitleCandidate> _candidates = [];
    private FnNasSubtitleSnapshot? _snapshot;
    private bool _writing;
    private bool _requiresRead;
    private bool _unconfirmed;
    private long _searchRevision;

    public FnNasSubtitles(EmbySessionScope scope, EmbyItem item, MediaSource source,
        Func<ItemMenuPermissions>? permissions = null, Func<bool>? isCurrent = null)
    {
        if (!scope.Connection.IsFnNas || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(source.Id)
            || string.IsNullOrWhiteSpace(source.Path) || !item.MediaSources.Contains(source)
            || item.Source?.UserStateOwner != scope.UserStateOwner || item.Source.Key != scope.Key)
            throw new InvalidOperationException("字幕管理缺少同一账号的完整媒体文件，请重新打开详情");
        _ = FnNasEndpoint.Segment(item.Id);
        _ = FnNasEndpoint.Segment(source.Id);
        _scope = scope;
        _item = item;
        _source = source;
        _itemId = item.Id;
        _mediaGuid = source.Id;
        _path = source.Path;
        _permissions = permissions ?? (() => new(scope.IsAdministrator, CanManageSubtitles: scope.CurrentConnection.CanManageSubtitles));
        _isCurrent = isCurrent ?? (() => true);
    }

    public bool IsCurrent => _scope.IsAvailable && _isCurrent() && _item.Id == _itemId
        && _item.Source?.UserStateOwner == _scope.UserStateOwner && _item.Source.Key == _scope.Key
        && _item.MediaSources.Contains(_source) && _source.Id == _mediaGuid && _source.Path == _path;
    public bool CanManage => IsCurrent && _permissions().CanManageSubtitles != false;
    public bool CanMarkServerFile => CanManage && _scope.IsAdministrator == true && _permissions().IsAdministrator == true;
    public bool CanWrite => CanManage && !_writing && !_requiresRead && !_unconfirmed && _snapshot is not null;
    public bool Touched { get; private set; }
    public bool NeedsRefresh { get; private set; }
    public bool IsUnconfirmed => _unconfirmed;
    public string MediaGuid => _mediaGuid;
    public string Path => _path;

    public bool CanDownload(FnNasSubtitleCandidate candidate) => CanWrite
        && _candidates.Any(known => ReferenceEquals(known, candidate)) && candidate.CanDownload
        && !_downloaded.Contains(candidate.TrimId) && !_snapshot!.Tracks.Any(track => track.TrimId == candidate.TrimId);

    public async Task<FnNasSubtitleSnapshot> ReloadAsync(CancellationToken cancellationToken = default)
    {
        RequireCurrent();
        if (_writing) throw new InvalidOperationException("字幕写入尚未完成，请稍后刷新");
        var current = await ReadAsync(cancellationToken).ConfigureAwait(false);
        RequireCurrent();
        _snapshot = current;
        _requiresRead = false;
        return current;
    }

    public async Task<IReadOnlyList<FnNasSubtitleCandidate>> SearchAsync(string language, CancellationToken cancellationToken = default)
    {
        RequireCurrent();
        if (_writing || _unconfirmed) throw new InvalidOperationException("请先核对上次字幕写入结果");
        _candidates = [];
        var searchRevision = Interlocked.Increment(ref _searchRevision);
        var revision = _scope.PermissionRevision;
        var result = await _scope.ExecuteAsync((client, token) =>
            client.FnNas.SearchSubtitlesAsync(Scoped(client, revision), _mediaGuid, language, token), cancellationToken).ConfigureAwait(false);
        RequireRevision(revision);
        if (searchRevision != Interlocked.Read(ref _searchRevision) || _writing)
            throw new OperationCanceledException("字幕搜索条件或操作已改变");
        _candidates = result;
        return result;
    }

    public Task DownloadAsync(FnNasSubtitleCandidate candidate, CancellationToken cancellationToken = default)
    {
        RequireCurrent();
        if (!CanDownload(candidate))
            throw new InvalidOperationException("字幕候选已变化、已添加或格式不支持，请重新搜索");
        return WriteAsync((client, native, confirmed, dispatched, token) => client.FnNas.DownloadSubtitleAsync(native,
            _mediaGuid, candidate.TrimId, confirmed, token, dispatched), cancellationToken, afterConfirmed: () => _downloaded.Add(candidate.TrimId));
    }

    public Task UploadAsync(FnNasSubtitleUpload upload, CancellationToken cancellationToken = default)
    {
        FnNasSubtitleInput.ValidateUpload(upload);
        // The WinRT read buffer belongs to the picker. Freeze exactly the bytes this click submits.
        var submitted = upload with { Bytes = upload.Bytes.ToArray() };
        return WriteAsync((client, native, confirmed, dispatched, token) => client.FnNas.UploadSubtitleAsync(native,
            _mediaGuid, submitted, confirmed, token, dispatched), cancellationToken);
    }

    public Task MarkServerFileAsync(string path, CancellationToken cancellationToken = default)
    {
        FnNasSubtitleInput.ValidateServerPath(path);
        RequireAdministrator();
        return WriteAsync((client, native, confirmed, dispatched, token) => client.FnNas.MarkSubtitleFilesAsync(native,
            _mediaGuid, new[] { path }, confirmed, token, dispatched), cancellationToken, administrator: true);
    }

    /// <summary>Called only after an explicit inline confirmation. Reject stale and ambiguous targets before DELETE.</summary>
    public Task DeleteAsync(FnNasSubtitleTrack track, bool confirmedByUser, CancellationToken cancellationToken = default)
    {
        if (!confirmedByUser) return Task.CompletedTask;
        RequireCurrent();
        if (_snapshot is null || !_snapshot.Tracks.Any(known => ReferenceEquals(known, track)) || !track.CanDelete
            || track.MediaGuid != _mediaGuid || _snapshot.Tracks.Count(known => known.Guid == track.Guid) != 1
            || _snapshot.ExternalTracks.Count(known => known.Label == track.Label) != 1)
            throw new InvalidOperationException("字幕列表已变化或无法区分同名文件，请刷新后重新确认");
        return WriteAsync((client, native, confirmed, dispatched, token) => client.FnNas.DeleteSubtitleAsync(native,
            track.Guid, confirmed, token, dispatched), cancellationToken);
    }

    private async Task WriteAsync(Func<EmbyClient, FnNasConnection, Action, Action, CancellationToken, Task> write,
        CancellationToken cancellationToken, bool administrator = false, Action? afterConfirmed = null)
    {
        RequireCurrent();
        if (!CanWrite) throw new InvalidOperationException("请先刷新字幕列表，核对文件与上次操作结果");
        if (administrator) RequireAdministrator();
        var revision = _scope.PermissionRevision;
        _writing = true;
        Interlocked.Increment(ref _searchRevision);
        var dispatched = false;
        var acknowledged = false;
        void Confirm()
        {
            acknowledged = true;
            Touched = true;
            NeedsRefresh = true;
            _requiresRead = true;
            afterConfirmed?.Invoke();
        }
        try
        {
            var current = await ReadAsync(cancellationToken).ConfigureAwait(false);
            RequireRevision(revision);
            if (!_snapshot!.Tracks.SequenceEqual(current.Tracks))
                throw new InvalidOperationException("服务器字幕列表已变化，未执行写入；请刷新后重新确认");
            await _scope.ExecuteAsync(async (client, token) =>
            {
                // A rejected expired-token attempt may be retried by the scope. The next attempt's
                // read/validation is not a mutation until its own transport begins sending.
                dispatched = false;
                RequireRevision(revision);
                if (administrator) RequireAdministrator();
                token.ThrowIfCancellationRequested();
                var native = Scoped(client, revision, administrator);
                try
                {
                    await write(client, native, Confirm, () =>
                    {
                        RequireRevision(revision);
                        if (administrator) RequireAdministrator();
                        token.ThrowIfCancellationRequested();
                        dispatched = true;
                    }, token).ConfigureAwait(false);
                }
                catch (FnNasApiException error) when (IsExplicitRejection(error))
                {
                    // EmbySession translates native business codes. Keep the rejected-attempt fact
                    // here so a lost code or a failed reauthentication cannot become an unknown write.
                    dispatched = false;
                    throw;
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (acknowledged)
        {
            // Scope cancellation after parsing code=0 cannot turn an acknowledged mutation into a retry.
        }
        catch (Exception error) when (dispatched && !IsExplicitRejection(error))
        {
            NeedsRefresh = true;
            _requiresRead = true;
            _unconfirmed = true;
            throw new FnNasSubtitleWriteUnconfirmedException(error);
        }
        finally { _writing = false; }
    }

    private async Task<FnNasSubtitleSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        RequireCurrent();
        var revision = _scope.PermissionRevision;
        var streams = await _scope.ExecuteAsync((client, token) =>
            client.FnNas.GetStreamsAsync(Scoped(client, revision), _itemId, token), cancellationToken).ConfigureAwait(false);
        RequireRevision(revision);
        return FnNasSubtitleSnapshot.From(streams, _mediaGuid, _path);
    }

    private FnNasConnection Scoped(EmbyClient client, long revision, bool administrator = false) =>
        client.FnNasConnection with { IsCurrent = () => CanManage && _scope.PermissionRevision == revision
            && (!administrator || CanMarkServerFile) };

    private void RequireCurrent()
    {
        if (!IsCurrent) throw new OperationCanceledException("任务所属的页面、登录身份或媒体版本已改变");
        if (!CanManage) throw new InvalidOperationException("当前飞牛影视账号没有字幕操作权限");
    }

    private void RequireRevision(long revision)
    {
        if (_scope.PermissionRevision != revision) throw new OperationCanceledException("字幕操作所属的权限快照已改变，请刷新后重试");
        RequireCurrent();
    }

    private void RequireAdministrator()
    {
        RequireCurrent();
        if (!CanMarkServerFile) throw new InvalidOperationException("指定服务器字幕文件需要当前账号的管理员权限");
    }

    private static bool IsExplicitRejection(Exception error)
    {
        if (error is EmbyApiException { InnerException: FnNasApiException native }) error = native;
        return error is FnNasApiException { Code: not null }
            || error is EmbyApiException { StatusCode: >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError }
            || error is FnNasApiException { StatusCode: >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError };
    }
}
