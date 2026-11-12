namespace Momoka.Emby;

/// <summary>一次字幕搜索的条件；开关关闭表示不筛选，不是只搜索未标记的字幕。</summary>
public sealed record ServerSubtitleSearch(string Language, bool ForcedOnly = false);

/// <summary>一次字幕管理对话固定的登录身份、媒体文件和轨道快照。</summary>
public sealed class ServerSubtitles
{
    private readonly EmbySessionScope _scope;
    private readonly string _itemId;
    private readonly string _sourceId;
    private readonly string? _sourcePath;
    private readonly CancellationToken _pageLifetime;
    private MediaSource _source;
    private TrackIdentity[] _snapshot;

    public ServerSubtitles(EmbySessionScope scope, EmbyItem item, MediaSource source)
        : this(scope, item, source, CancellationToken.None) { }

    public ServerSubtitles(EmbySessionScope scope, EmbyItem item, MediaSource source, CancellationToken pageLifetime)
    {
        if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(source.Id) || !item.MediaSources.Contains(source))
            throw new InvalidOperationException("字幕管理缺少有效的媒体文件");
        // Jellyfin's file-source Id is its version item's GUID. Its subtitle controllers only use
        // the route item, so validating B while writing the parent A would target a different file.
        if (scope.Connection.IsJellyfin && (!Guid.TryParse(source.Id, out var versionId) || versionId == Guid.Empty))
            throw new InvalidOperationException("字幕管理缺少有效的版本条目，请在服务器管理界面核对");
        _scope = scope;
        _pageLifetime = pageLifetime;
        _itemId = scope.Connection.IsJellyfin ? source.Id : item.Id;
        _sourceId = source.Id;
        _sourcePath = source.Path;
        _source = source;
        _snapshot = Snapshot(source);
    }

    public bool IsCurrent => _scope.IsAvailable;

    public bool SupportsUpload => _scope.Connection.IsJellyfin;
    public bool CanUpload => SupportsUpload && IsCurrent && !_pageLifetime.IsCancellationRequested && _scope.CanUploadSubtitles == true;

    /// <summary>当前身份的权限或可用来源变化；对话框关闭后必须退订。</summary>
    public event EventHandler? AvailabilityChanged;

    internal void StartObservingAvailability()
    {
        _scope.Session.PermissionsChanged += OnPermissionsChanged;
        _scope.Session.SourcesChanged += OnSourcesChanged;
    }

    internal void StopObservingAvailability()
    {
        _scope.Session.PermissionsChanged -= OnPermissionsChanged;
        _scope.Session.SourcesChanged -= OnSourcesChanged;
    }

    private void OnPermissionsChanged(object? sender, EmbySessionScope source)
    {
        if (ReferenceEquals(source.UserStateOwner, _scope.UserStateOwner)) AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSourcesChanged(object? sender, EventArgs args) => AvailabilityChanged?.Invoke(this, EventArgs.Empty);

    public bool SupportsForcedOnly => !_scope.Connection.IsJellyfin;

    /// <summary>Jellyfin returns 204 even after provider failure and queues successful indexing separately.</summary>
    public bool DownloadProvidesConfirmation => !_scope.Connection.IsJellyfin;

    public Task<List<RemoteSubtitleInfo>> SearchAsync(string language) => SearchAsync(new ServerSubtitleSearch(language));

    public async Task<List<RemoteSubtitleInfo>> SearchAsync(ServerSubtitleSearch search)
    {
        _scope.ThrowIfUnavailable();
        var result = await _scope.ExecuteAsync((client, token) =>
            client.SearchSubtitlesAsync(_itemId, _sourceId, search, token), CancellationToken.None).ConfigureAwait(false);
        _scope.ThrowIfUnavailable();
        return result;
    }

    public async Task DownloadAsync(RemoteSubtitleInfo subtitle)
    {
        _scope.ThrowIfUnavailable();
        if (string.IsNullOrWhiteSpace(subtitle.Id)) throw new InvalidOperationException("字幕候选缺少下载标识，请重新搜索");
        await _scope.ExecuteAsync((client, token) =>
            client.DownloadSubtitleAsync(_itemId, _sourceId, subtitle.Id, token), CancellationToken.None).ConfigureAwait(false);
        _scope.ThrowIfUnavailable();
    }

    public async Task UploadAsync(ServerSubtitleUpload upload)
    {
        ArgumentNullException.ThrowIfNull(upload);
        if (!SupportsUpload) throw new NotSupportedException("当前服务器不支持此原生字幕上传。");
        CheckUpload(_scope.CurrentConnection);
        var revision = _scope.PermissionRevision;
        await ReadSourceAsync(_pageLifetime).ConfigureAwait(false);
        var dispatched = false;
        var confirmed = false;
        try
        {
            await _scope.ExecuteAsync(async (client, token) =>
            {
                dispatched = false;
                token.ThrowIfCancellationRequested();
                CheckUpload(client.Connection);
                // A→B→A permission changes invalidate the prepared operation even when A is granted again.
                if (_scope.PermissionRevision != revision) throw new UnauthorizedAccessException("字幕上传权限已变化，请重新提交。");
                client.ValidateSubtitleUpload(_itemId, upload);
                dispatched = true;
                try { await client.UploadSubtitleAsync(_itemId, upload, token, () => confirmed = true).ConfigureAwait(false); }
                catch (EmbyApiException error) when (ExplicitRejection(error)) { dispatched = false; throw; }
            }, _pageLifetime).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (confirmed) { }
        catch (Exception error) when (dispatched && !ExplicitRejection(error))
        {
            throw new ServerSubtitleUploadUnconfirmedException(error);
        }
    }

    private static bool ExplicitRejection(Exception error) => error is EmbyApiException { StatusCode: { } status }
        && (int)status is >= 400 and < 500;

    private void CheckUpload(EmbyConnection connection)
    {
        _pageLifetime.ThrowIfCancellationRequested();
        _scope.ThrowIfUnavailable();
        if (!connection.IsSameIdentityAs(_scope.Connection)) throw new OperationCanceledException("字幕上传所属账号已改变。");
        if (connection.CanUploadSubtitles != true || _scope.CanUploadSubtitles != true)
            throw new UnauthorizedAccessException("当前账号没有字幕上传权限。");
    }

    public async Task DeleteAsync(MediaStream stream)
    {
        _scope.ThrowIfUnavailable();
        if (!_source.MediaStreams.Contains(stream) || !stream.IsSubtitle || !stream.IsExternal)
            throw new InvalidOperationException("字幕列表已变化，请刷新后重新确认");
        var expected = TrackIdentity.From(stream);
        if (!_snapshot.Contains(expected) || _snapshot.Count(track =>
                (track with { Index = -1 }) == (expected with { Index = -1 })) != 1)
            throw new InvalidOperationException("字幕信息已变化或无法区分同名文件，未执行删除；请在服务器管理界面核对");
        var current = await ReadSourceAsync().ConfigureAwait(false);
        var matches = current.SubtitleStreams.Where(track => track.Index == expected.Index).ToArray();
        if (!_snapshot.SequenceEqual(Snapshot(current)) || matches.Length != 1 || TrackIdentity.From(matches[0]) != expected)
            throw new InvalidOperationException("服务器字幕列表已变化，未执行删除；请刷新后重新确认");
        _scope.ThrowIfUnavailable();
        await _scope.ExecuteAsync((client, token) =>
            client.DeleteSubtitleAsync(_itemId, _sourceId, expected.Index, token), CancellationToken.None).ConfigureAwait(false);
        _scope.ThrowIfUnavailable();
    }

    public async Task<MediaSource> ReloadAsync()
    {
        var current = await ReadSourceAsync().ConfigureAwait(false);
        AcceptReload(current);
        return current;
    }

    /// <summary>The dialog accepts only its current read generation before advancing delete identity.</summary>
    internal Task<MediaSource> ReadReloadAsync() => ReadSourceAsync();

    internal void AcceptReload(MediaSource current)
    {
        if (current.Id != _sourceId || current.Path != _sourcePath)
            throw new InvalidOperationException("媒体文件已变化，请关闭字幕管理后重新打开");
        _source = current;
        _snapshot = Snapshot(current);
    }

    private async Task<MediaSource> ReadSourceAsync(CancellationToken cancellationToken = default)
    {
        _scope.ThrowIfUnavailable();
        var item = await _scope.ExecuteAsync((client, token) =>
            client.GetItemAsync(_itemId, token, EmbyFields.Files), cancellationToken).ConfigureAwait(false);
        _scope.ThrowIfUnavailable();
        var sources = item.MediaSources.Where(source => source.Id == _sourceId).ToArray();
        if (item.Id != _itemId || sources.Length != 1 || sources[0].Path != _sourcePath)
            throw new InvalidOperationException("媒体文件已变化，请关闭字幕管理后重新打开");
        return sources[0];
    }

    private static TrackIdentity[] Snapshot(MediaSource source) =>
        [.. source.SubtitleStreams.Select(TrackIdentity.From).OrderBy(track => track.Index)];

    private sealed record TrackIdentity(int Index, string Type, string? Codec, string? Language,
        string? Title, string? DisplayTitle, string? DisplayLanguage, bool External, bool Forced)
    {
        internal static TrackIdentity From(MediaStream stream) => new(stream.Index, stream.Type, stream.Codec,
            stream.Language, stream.Title, stream.DisplayTitle, stream.DisplayLanguage, stream.IsExternal, stream.IsForced);
    }
}
