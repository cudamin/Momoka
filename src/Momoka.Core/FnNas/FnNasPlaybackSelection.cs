using Momoka.Emby;

namespace Momoka.FnNas;

/// <summary>Opaque track GUIDs needed by play/record; contains no login credentials.</summary>
public sealed record FnNasPlaybackSelection(string VideoGuid, IReadOnlyDictionary<int, string> AudioGuids,
    IReadOnlyDictionary<int, string> SubtitleGuids, long DurationTicks)
{
    public string AudioGuid(int? index) => index is { } key && AudioGuids.TryGetValue(key, out var value) ? value : "";
    public string SubtitleGuid(int? index) => index is { } key && SubtitleGuids.TryGetValue(key, out var value) ? value : "";

    public static FnNasPlaybackSelection FromSource(MediaSource source, long duration) => new(
        source.PrimaryVideoStream?.ProviderId ?? "",
        source.AudioStreams.Where(stream => !string.IsNullOrEmpty(stream.ProviderId))
            .ToDictionary(stream => stream.Index, stream => stream.ProviderId!),
        source.SubtitleStreams.Where(stream => !string.IsNullOrEmpty(stream.ProviderId))
            .ToDictionary(stream => stream.Index, stream => stream.ProviderId!), duration);

    public static Uri MediaUrl(EmbyConnection connection, MediaSource? source)
    {
        if (source?.DirectStreamUrl is not { } address || !FnNasEndpoint.SameOrigin(connection.ApiBase, address)
            || !string.IsNullOrEmpty(address.UserInfo) || !string.IsNullOrEmpty(address.Query) || !string.IsNullOrEmpty(address.Fragment))
            throw new InvalidOperationException("飞牛影视未提供可安全播放的原始媒体地址");
        return address;
    }
}
