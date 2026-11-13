using Momoka.Emby;
using Momoka.Playback;

namespace Momoka.Shell.Media;

/// <summary>商店预告只读取独立预告原始流，不走正片队列或观看进度上报。</summary>
internal sealed class StoreTrailerPreview : IAsyncDisposable
{
    private readonly PlaybackHttpTransport _transport;
    private StoreTrailerPreview(PlaybackHttpTransport transport, Uri source) { _transport = transport; Source = source; }
    internal Uri Source { get; }
    internal void Cancel() => _transport.Cancel();

    internal static async Task<StoreTrailerPreview?> CreateAsync(EmbyItem subject, CancellationToken token)
    {
        if (subject.Source is not { IsAvailable: true } scope || !ItemTrailers.Supports(scope.Connection.ServerKind)
            || subject.LocalTrailerCount is not > 0) return null;
        var candidates = await scope.ExecuteAsync((client, ct) => client.GetLocalTrailersAsync(subject.Id, ct), token);
        foreach (var candidate in candidates)
        {
            token.ThrowIfCancellationRequested();
            if (!candidate.IsPlayable || candidate.Id == subject.Id || string.IsNullOrWhiteSpace(candidate.Id)) continue;
            var trailer = candidate.MediaSources.Count > 0 ? candidate
                : await scope.ExecuteAsync((client, ct) => client.GetItemAsync(candidate.Id, ct), token);
            if (!scope.IsAvailable || trailer.Id != candidate.Id || trailer.Source?.Key != scope.Key
                || string.IsNullOrWhiteSpace(trailer.Id) || trailer.Id == subject.Id || !trailer.IsPlayable
                || trailer.MediaSources.FirstOrDefault() is not { } source) continue;
            token.ThrowIfCancellationRequested();
            var connection = scope.CurrentConnection;
            var request = new PlaybackRequest
            {
                MediaUrl = EmbyUrl.Stream(connection.ApiBase, trailer.Id, source.Id, source.Container),
                Title = "预告片预览",
                HttpHeaders = MediaServerHeaders.Create(connection.Device, connection.AccessToken, connection.ServerKind),
                StartSeconds = 0,
                SubtitlesDisabled = true
            };
            var transport = new PlaybackHttpTransport(request);
            try { return new StoreTrailerPreview(transport, transport.Prepare(request).MediaUrl); }
            catch { await transport.DisposeAsync(); throw; }
        }
        return null;
    }

    public ValueTask DisposeAsync() => _transport.DisposeAsync();
}
