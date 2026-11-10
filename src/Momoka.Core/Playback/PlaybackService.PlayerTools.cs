namespace Momoka.Playback;

public sealed partial class PlaybackService
{
    public bool CanUsePlayerTools => _active is { } attempt && IsCurrent(attempt)
        && attempt.Scope.IsAvailable && CanControl && attempt.Request.MediaParts.Count <= 1;
    /// <summary>工具任务只持有起点文件与认证，不能重新读取替换后的播放请求。</summary>
    public PlayerToolSession? CapturePlayerToolSession()
    {
        var attempt = _active;
        if (attempt is null || !CanUsePlayerTools) return null;
        var scoped = attempt.Handle as IFileScopedPlayerControl;
        var fileGeneration = scoped?.FileGeneration;
        return new PlayerToolSession(attempt.Request, attempt.Scope.Lifetime,
            () => IsCurrent(attempt) && attempt.Scope.IsAvailable && IsCurrent(attempt.Handle, attempt.Generation)
                && (scoped is null || scoped.FileGeneration == fileGeneration));
    }

    public sealed class PlayerToolSession
    {
        private readonly PlaybackRequest _request;
        private readonly Func<bool> _isCurrent;
        public CancellationToken Lifetime { get; }
        public bool IsCurrent => _isCurrent();

        internal PlayerToolSession(PlaybackRequest request, CancellationToken lifetime, Func<bool> isCurrent)
        {
            _request = request;
            Lifetime = lifetime;
            _isCurrent = isCurrent;
        }

        public async Task<string> PrepareMediaAsync(string destination,
            IProgress<(long Bytes, long? Total)>? progress, CancellationToken cancellationToken)
        {
            if (!IsCurrent) throw new OperationCanceledException(cancellationToken);
            if (_request.MediaUrl.IsFile) return _request.MediaUrl.LocalPath;
            using var client = PlayerToolDownload.CreateClient();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Lifetime);
            await PlayerToolDownload.CopyAsync(client, _request.MediaUrl, _request.HttpHeaders,
                destination, progress, linked.Token).ConfigureAwait(false);
            if (!IsCurrent) throw new OperationCanceledException(cancellationToken);
            return destination;
        }

        public async Task<string> PrepareSubtitleAsync(string externalFilename, string destination, CancellationToken cancellationToken)
        {
            if (!IsCurrent) throw new OperationCanceledException(cancellationToken);
            if (Path.IsPathFullyQualified(externalFilename) && File.Exists(externalFilename)) return externalFilename;
            // Only subtitles already authorized in the frozen playback request can receive its headers.
            if (!Uri.TryCreate(externalFilename, UriKind.Absolute, out var url)
                || !_request.ExternalSubtitles.Contains(url))
                throw new PlayerToolDownloadException("无法确认当前外挂字幕的来源。");
            using var client = PlayerToolDownload.CreateClient();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Lifetime);
            var headers = Momoka.Emby.EmbyHttpRedirect.SameOrigin(_request.MediaUrl, url) ? _request.HttpHeaders : [];
            await PlayerToolDownload.CopyAsync(client, url, headers, destination, null, linked.Token).ConfigureAwait(false);
            if (!IsCurrent) throw new OperationCanceledException(cancellationToken);
            return destination;
        }
    }
}
