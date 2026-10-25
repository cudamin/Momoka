using Momoka.Emby;

namespace Momoka.FnNas;

/// <summary>Opaque track GUIDs needed by play/record; contains no login credentials.</summary>
public sealed record FnNasPlaybackSelection(string VideoGuid, IReadOnlyDictionary<int, string> AudioGuids,
    IReadOnlyDictionary<int, string> SubtitleGuids, long DurationTicks)
{
    /// <summary>
    /// 官方前端关字幕 / 本来就没有字幕时发的字面量，<b>不是空串</b>（bundle 里
    /// <c>hiddenSubtitleStream = {guid: "_no_display_", hidden: true}</c>）。<c>subtitle_guid</c> 空着
    /// 在服务端只是被存进去，读回来分不清「没字幕」和「没报过」。
    /// </summary>
    public const string NoSubtitleGuid = "_no_display_";

    /// <summary>
    /// 默认音轨的 GUID：所选索引缺失、或该索引在映射里找不到时用它兜底，别让 <c>audio_guid</c> 空着
    /// （官方前端那一栏永远有值）。一条带 GUID 的音轨都没有时才是空串 —— 那时播放器也没有音轨可报。
    /// </summary>
    public string DefaultAudioGuid { get; init; } = "";

    /// <summary>实际播放的视频流的档位（飞牛 <c>resolution_type</c>，如 <c>1080p</c>）；取不到是空串。</summary>
    public string Resolution { get; init; } = "";

    /// <summary>实际播放的视频流的码率（飞牛 <c>bps</c>）；取不到是 0（官方原画档语义）。</summary>
    public int Bitrate { get; init; }

    public string AudioGuid(int? index) =>
        index is { } key && AudioGuids.TryGetValue(key, out var value) ? value : DefaultAudioGuid;

    public string SubtitleGuid(int? index) =>
        index is { } key && SubtitleGuids.TryGetValue(key, out var value) && value.Length > 0
            ? value
            : NoSubtitleGuid;

    public static FnNasPlaybackSelection FromSource(MediaSource source, long duration)
    {
        var video = source.PrimaryVideoStream;
        return new(
            video?.ProviderId ?? "",
            source.AudioStreams.Where(stream => !string.IsNullOrEmpty(stream.ProviderId))
                .ToDictionary(stream => stream.Index, stream => stream.ProviderId!),
            source.SubtitleStreams.Where(stream => !string.IsNullOrEmpty(stream.ProviderId))
                .ToDictionary(stream => stream.Index, stream => stream.ProviderId!), duration)
        {
            DefaultAudioGuid = DefaultAudio(source),
            Resolution = video?.ResolutionType ?? "",
            Bitrate = video?.BitRate ?? 0
        };
    }

    /// <summary>
    /// 服务器点名的默认音轨优先，其次第一条带 GUID 的音轨。逐条问 <see cref="MediaSource.AudioStreams"/>
    /// 而不是翻 <see cref="AudioGuids"/> 的值：后者的顺序是字典自己的，不该拿来决定「第一条」是谁。
    /// </summary>
    private static string DefaultAudio(MediaSource source) =>
        (source.DefaultAudioStreamIndex is { } index
            ? source.AudioStreams.FirstOrDefault(stream => stream.Index == index && !string.IsNullOrEmpty(stream.ProviderId))
            : null)?.ProviderId
        ?? source.AudioStreams.FirstOrDefault(stream => !string.IsNullOrEmpty(stream.ProviderId))?.ProviderId
        ?? "";

    public static Uri MediaUrl(EmbyConnection connection, MediaSource? source)
    {
        if (source?.DirectStreamUrl is not { } address || !FnNasEndpoint.SameOrigin(connection.ApiBase, address)
            || !string.IsNullOrEmpty(address.UserInfo) || !string.IsNullOrEmpty(address.Query) || !string.IsNullOrEmpty(address.Fragment))
            throw new InvalidOperationException("飞牛影视未提供可安全播放的原始媒体地址");
        return address;
    }
}
