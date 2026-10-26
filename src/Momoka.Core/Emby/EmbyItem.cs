using System.Text.Json.Serialization;
using Momoka.Infrastructure;

namespace Momoka.Emby;

public static class EmbyItemType
{
    public const string Movie = "Movie";
    public const string Series = "Series";
    public const string Season = "Season";
    public const string Episode = "Episode";
    public const string Video = "Video";
    public const string MusicVideo = "MusicVideo";
    public const string Trailer = "Trailer";
    public const string Folder = "Folder";
    public const string CollectionFolder = "CollectionFolder";
    public const string BoxSet = "BoxSet";
    public const string Playlist = "Playlist";
    public const string Person = "Person";
    public const string Audio = "Audio";
    public const string MusicAlbum = "MusicAlbum";
    public const string MusicArtist = "MusicArtist";
    public const string Genre = "Genre";
    public const string Studio = "Studio";

    private static readonly HashSet<string> Playable =
        new(StringComparer.OrdinalIgnoreCase) { Movie, Episode, Video, MusicVideo, Trailer };

    /// <summary>
    /// The types this app deliberately does not show. It plays video through mpv and has no music face at
    /// all — no album grid, no track list, no queue — so a music library reached through it is a wall of
    /// folders that answer a click with nothing worth looking at. Hidden rather than half-built.
    /// </summary>
    private static readonly HashSet<string> Music =
        new(StringComparer.OrdinalIgnoreCase) { Audio, MusicAlbum, MusicArtist, MusicVideo };

    /// <summary>The <c>CollectionType</c> values Emby gives a music library.</summary>
    private static readonly HashSet<string> MusicCollections =
        new(StringComparer.OrdinalIgnoreCase) { "music", "musicvideos" };

    /// <summary>
    /// The types 已观看 and 收藏 are not a thing for. A 媒体库 is the shelf the items live on, not one of
    /// them, and a 演职人员 is a name — neither has a watched state to toggle, and the server's copy of
    /// them carries no user data to toggle it on.
    /// <para>
    /// 类型与播出平台同理（2026-10-18）：一个类型名、一家电视台都不是能看的条目。服务器那边也只是把一份
    /// 空 <c>UserData</c> 挂上去，按下去没有任何东西会改。索引卡上那两个按钮从前一直在，是从没人点开过。
    /// </para>
    /// </summary>
    private static readonly HashSet<string> Stateless =
        new(StringComparer.OrdinalIgnoreCase) { CollectionFolder, Person, Genre, Studio };

    public static bool IsPlayable(string? type) => type is not null && Playable.Contains(type);

    /// <summary>
    /// Whether 已观看 and 收藏 apply to this type. Unknown types say yes: a server sending something we
    /// have no name for is still sending an item, and hiding its controls would be a guess.
    /// </summary>
    public static bool TracksUserState(string? type) => type is null || !Stateless.Contains(type);

    /// <summary>Whether this item's type is one of the music ones the shell hides.</summary>
    public static bool IsMusic(string? type) => type is not null && Music.Contains(type);

    /// <summary>Whether a library's <c>CollectionType</c> makes it a music library.</summary>
    public static bool IsMusicLibrary(string? collectionType) =>
        collectionType is not null && MusicCollections.Contains(collectionType);

    public static string ToChinese(string? type) => type switch
    {
        Movie => "电影",
        Series => "剧集",
        Season => "季",
        Episode => "单集",
        Video => "视频",
        MusicVideo => "音乐视频",
        Trailer => "预告片",
        Folder => "文件夹",
        CollectionFolder => "媒体库",
        BoxSet => "合集",
        Playlist => "播放列表",
        Person => "演职人员",
        Audio => "音乐",
        MusicAlbum => "专辑",
        MusicArtist => "艺术家",
        Genre => "类型",
        Studio => "播出平台",
        null or "" => "",
        _ => type
    };
}

public sealed class EmbyUserData
{
    public long PlaybackPositionTicks { get; set; }

    public int PlayCount { get; set; }

    public bool Played { get; set; }

    public bool IsFavorite { get; set; }

    public double? PlayedPercentage { get; set; }

    public int? UnplayedItemCount { get; set; }

    public DateTimeOffset? LastPlayedDate { get; set; }
}

/// <summary>
/// One chapter marker, as sent with <c>Fields=Chapters</c>. The ones that carry an
/// <see cref="ImageTag"/> also have a still on the server, which is what the seek bar shows
/// when the pointer hovers over that part of the timeline.
/// </summary>
public sealed class ChapterInfo
{
    public long StartPositionTicks { get; set; }

    public string? Name { get; set; }

    /// <summary>Image cache tag; null when the server extracted no still for this chapter.</summary>
    public string? ImageTag { get; set; }

    /// <summary>
    /// Emby's own marker kind: <c>Chapter</c>, <c>IntroStart</c>, <c>IntroEnd</c> or
    /// <c>CreditsStart</c>. Plain chapters often omit it; the three non-chapter kinds are the intro and
    /// credit segments Emby detected, and they are what the 跳过 offer is built from.
    /// </summary>
    public string? MarkerType { get; set; }

    [JsonIgnore]
    public bool HasImage => !string.IsNullOrEmpty(ImageTag);

    [JsonIgnore]
    public double StartSeconds => TimeFormat.ToSeconds(StartPositionTicks);
}

public sealed class MediaStream
{
    [JsonIgnore]
    public string? ProviderId { get; set; }

    public int Index { get; set; }

    /// <summary>"Video", "Audio", "Subtitle", "EmbeddedImage".</summary>
    public string Type { get; set; } = "";

    public string? Codec { get; set; }

    /// <summary>
    /// The encoder profile as the server spells it — "Main 10", "High", "dvhe.05.06". Shown in 媒体信息
    /// next to the codec, because "HEVC" alone does not say whether a file is 8-bit or 10-bit.
    /// <para>
    /// The sibling <c>Level</c> is deliberately not mapped: Emby sends it as an integer whose scale
    /// differs per codec (41 for H.264 L4.1, 150 for HEVC L5), so printing it would be guesswork.
    /// </para>
    /// </summary>
    public string? Profile { get; set; }

    public string? Language { get; set; }

    public string? Title { get; set; }

    public string? DisplayTitle { get; set; }

    public string? DisplayLanguage { get; set; }

    public string? ChannelLayout { get; set; }

    public int? Channels { get; set; }

    public int? Height { get; set; }

    public int? Width { get; set; }

    public int? BitRate { get; set; }

    /// <summary>
    /// 飞牛影视 <c>resolution_type</c> 那个档位字面量（<c>1080p</c> / <c>4k</c> …）。只有飞牛适配器填它
    /// （见 <c>FnNasMediaAdapter.AddStreams</c>）；Emby / Jellyfin / Plex 的流里没有这一项，保持 null。
    /// 播放进度上报的 <c>resolution</c> 就取这个值 —— 那是服务端自己认的档位名，比自己从 Height 推一个更准。
    /// </summary>
    [JsonIgnore]
    public string? ResolutionType { get; set; }

    /// <summary>Bits per colour component: 8 for an ordinary encode, 10 for HDR and most anime releases.</summary>
    public int? BitDepth { get; set; }

    /// <summary>Emby sends these as decimals such as 23.976023; either may be missing.</summary>
    public double? AverageFrameRate { get; set; }

    public double? RealFrameRate { get; set; }

    /// <summary>"SDR", "HDR", "DOVI", "HDR10", "HLG"… — Emby's own spelling varies by version.</summary>
    public string? VideoRange { get; set; }

    /// <summary>
    /// 细分档位（Jellyfin 与 Emby 4.8+）：<c>SDR</c> / <c>HDR10</c> / <c>HDR10Plus</c> / <c>HLG</c> /
    /// <c>DOVI</c> / <c>DOVIWithHDR10</c> / <c>DOVIWithHLG</c> / <c>DOVIWithSDR</c>。老服务器、Plex 与飞牛
    /// 不写这一项，由各适配器把自家的写法折进来（Plex 的 <c>videoDynamicRangeType</c>、飞牛的 <c>dv_profile</c>）。
    /// <para>
    /// 与 <see cref="VideoRange"/> 一起判「HDR / DV」两个角标：<c>DOVIWithHDR10</c> 这种既带 DV 又带
    /// HDR10 的片源只有细分档位说得清 —— 粗档位那边只写一个 <c>DOVI</c>。
    /// </para>
    /// </summary>
    public string? VideoRangeType { get; set; }

    /// <summary>Display aspect ratio as text, e.g. "2.40:1" or "16:9"; often absent.</summary>
    public string? AspectRatio { get; set; }

    public bool IsDefault { get; set; }

    public bool IsForced { get; set; }

    public bool IsExternal { get; set; }

    public bool SupportsExternalStream { get; set; }

    public string? DeliveryUrl { get; set; }

    [JsonIgnore]
    public bool IsAudio => string.Equals(Type, "Audio", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsSubtitle => string.Equals(Type, "Subtitle", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsVideo => string.Equals(Type, "Video", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The frame rate to reason about, or 0 when the server reported none. Prefers the average, which
    /// is the honest number for a VFR file; the real rate on one of those is the peak, and treating a
    /// 24fps film as 60fps because a few frames were duplicated would switch display sync off.
    /// </summary>
    [JsonIgnore]
    public double FrameRate => AverageFrameRate ?? RealFrameRate ?? 0;

    /// <summary>
    /// True for any high-dynamic-range flavour. Matched as a substring because the field holds
    /// "HDR", "HDR10", "HDR10Plus" or "DOVI" depending on the Emby version and the file, and
    /// everything except a bare "SDR" wants tone mapping.
    /// </summary>
    [JsonIgnore]
    public bool IsHdr => VideoRange is { } range &&
        (range.Contains("HDR", StringComparison.OrdinalIgnoreCase) ||
         range.Contains("DOVI", StringComparison.OrdinalIgnoreCase) ||
         range.Contains("HLG", StringComparison.OrdinalIgnoreCase) ||
         range.Contains("Dolby Vision", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 片源自带的动态范围角标：<c>HDR</c>、<c>DV</c>、<c>HDR/DV</c>；SDR 与未知一律空串（用户令 2026-10-25
    /// 「片源带 HDR/DV 时标题也要带上，放在视频编码后面，HDR/DV 都有的时候 HDR 在前 DV 在后」）。
    /// <para>
    /// HDR 这一格涵盖 <b>HDR10、HDR10+（HDR10Plus）、HLG</b> 三种（用户令 2026-10-25「包括 HDR10+、HDR10、
    /// HLG」）—— 它们都是 HDR 片源，屏上不必再把服务器那套枚举名逐字抄一遍。
    /// </para>
    /// <para>
    /// 与 <see cref="IsHdr"/> 不是一回事：那个是「要不要色调映射」的开关，DV 也算在里面；这一条只回答
    /// 「屏上写哪几个字」，所以 DV 单飞时只写 DV、不写 HDR（profile 5 那种 DV 底子是 SDR）。判据合三个
    /// 字段看 —— 粗档位、细分档位，以及编码档位里的 <c>dvhe.05.06</c> 这类 DV 专用前缀（见 <see cref="Profile"/>）。
    /// </para>
    /// </summary>
    [JsonIgnore]
    public string DynamicRangeLabel
    {
        get
        {
            var range = RangeText;
            var hdr = Contains(range, "HDR") || Contains(range, "HLG");
            var dolbyVision = Contains(range, "DOVI") || Contains(range, "Dolby") ||
                Mentions(range, "DV") || IsDolbyVisionProfile(Profile);

            return (hdr, dolbyVision) switch
            {
                (true, true) => "HDR/DV",
                (true, false) => "HDR",
                (false, true) => "DV",
                _ => ""
            };
        }
    }

    /// <summary>粗档位与细分档位拼成一串，给上面那条判据读；两项都缺就是空串。</summary>
    private string RangeText => string.Join(' ',
        new[] { VideoRange, VideoRangeType }.Where(value => !string.IsNullOrWhiteSpace(value)));

    /// <summary>DV 的编码档位前缀：<c>dvhe.05.06</c>（HEVC）、<c>dvh1.08.07</c>、<c>dav1.10.08</c>（AV1）。</summary>
    private static bool IsDolbyVisionProfile(string? profile) => profile is { Length: >= 4 } &&
        (profile.StartsWith("dvhe", StringComparison.OrdinalIgnoreCase) ||
         profile.StartsWith("dvh1", StringComparison.OrdinalIgnoreCase) ||
         profile.StartsWith("dav1", StringComparison.OrdinalIgnoreCase));

    private static bool Contains(string? text, string token) =>
        text is { Length: > 0 } && text.Contains(token, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 记号要独立成词：Plex 写的 <c>DV HDR10</c> 里那个 DV 是，<c>DVDRip</c> 里那个不是。
    /// </summary>
    private static bool Mentions(string? text, string token)
    {
        if (string.IsNullOrEmpty(text)) return false;

        for (var at = text.IndexOf(token, StringComparison.OrdinalIgnoreCase);
             at >= 0;
             at = text.IndexOf(token, at + token.Length, StringComparison.OrdinalIgnoreCase))
        {
            var before = at == 0 ? '\0' : text[at - 1];
            var after = at + token.Length == text.Length ? '\0' : text[at + token.Length];
            if (!char.IsLetterOrDigit(before) && !char.IsLetterOrDigit(after)) return true;
        }

        return false;
    }

    /// <summary>
    /// True for subtitle codecs Emby can hand over as a standalone text file. Image-based
    /// subtitles (PGS, VobSub) can only be selected inside the container.
    /// </summary>
    [JsonIgnore]
    public bool IsTextSubtitle => IsSubtitle && Codec is not null && Codec.ToLowerInvariant() is
        "srt" or "subrip" or "ass" or "ssa" or "webvtt" or "vtt" or "mov_text" or "text" or "microdvd" or "sami" or "smi";

    /// <summary>A label good enough for a track picker even when the server sends nothing useful.</summary>
    public string ToDisplayLabel()
    {
        if (!string.IsNullOrWhiteSpace(DisplayTitle)) return Decorate(DisplayTitle!);

        var parts = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(DisplayLanguage)) parts.Add(DisplayLanguage!);
        else if (!string.IsNullOrWhiteSpace(Language)) parts.Add(Language!);
        if (!string.IsNullOrWhiteSpace(Title)) parts.Add(Title!);
        if (!string.IsNullOrWhiteSpace(Codec)) parts.Add(Codec!.ToUpperInvariant());
        if (IsAudio && !string.IsNullOrWhiteSpace(ChannelLayout)) parts.Add(ChannelLayout!);
        if (parts.Count == 0) parts.Add($"轨道 {Index}");
        return Decorate(string.Join(" · ", parts));
    }

    private string Decorate(string label)
    {
        if (IsForced) label += "（强制）";
        if (IsExternal) label += "（外挂）";
        return label;
    }
}

public sealed class MediaSource
{
    /// <summary>Provider-resolved original media URL. Authentication remains on the captured connection.</summary>
    [JsonIgnore]
    public Uri? DirectStreamUrl { get; set; }

    public string Id { get; set; } = "";

    /// <summary>
    /// 这一版在服务器上<b>自己的条目号</b>。多版本电影在 Emby 里每一版都是独立条目，合并进主条目的
    /// 媒体源表时把它捎上 —— 版本的入库时间按它问（见 <see cref="ItemDetail.OrderVersionsNewestFirst"/>；
    /// 备选版本条目被服务器藏起来、批量列表问不到，只有带着这个号一条请求才认）。空表示服务器没给，
    /// 那一版不参与按时间排序。
    /// </summary>
    public string ItemId { get; set; } = "";

    public string? Name { get; set; }

    public string? Path { get; set; }

    public string? Container { get; set; }

    public long? Size { get; set; }

    public long? RunTimeTicks { get; set; }

    public int? Bitrate { get; set; }

    public bool SupportsDirectPlay { get; set; }

    public bool SupportsDirectStream { get; set; }

    public int? DefaultAudioStreamIndex { get; set; }

    public int? DefaultSubtitleStreamIndex { get; set; }

    public List<MediaStream> MediaStreams { get; set; } = [];

    [JsonIgnore]
    public IEnumerable<MediaStream> AudioStreams => MediaStreams.Where(stream => stream.IsAudio);

    [JsonIgnore]
    public IEnumerable<MediaStream> SubtitleStreams => MediaStreams.Where(stream => stream.IsSubtitle);

    [JsonIgnore]
    public MediaStream? PrimaryVideoStream => MediaStreams.FirstOrDefault(stream => stream.IsVideo);

    /// <summary>"1080p · HEVC · MKV · 8.4 GB" for the progress bar, the version menu and the 播放信息 overlay.</summary>
    /// <param name="includeSize">False when the media-source label already puts the file size before its name.</param>
    public string ToQualityLabel(bool includeSize = true)
    {
        var parts = new List<string>(4);
        var video = PrimaryVideoStream;
        if (video?.Height is > 0) parts.Add(DescribeHeight(video.Height.Value));
        if (!string.IsNullOrWhiteSpace(video?.Codec)) parts.Add(video!.Codec!.ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(Container)) parts.Add(Container!.ToUpperInvariant());
        if (includeSize && TimeFormat.FileSize(Size) is { Length: > 0 } size) parts.Add(size);
        return string.Join("  ·  ", parts);
    }

    /// <summary>
    /// 「417.7 MB · 1080p · HEVC · HDR/DV · AAC · Studio GreenTea」—— 这一份文件的读数（文件大小、分辨率、
    /// 视频编码、动态范围、音频格式、组名），详情页那行「视频：…」与播放页左上角第二行共用同一条字符串（用户令
    /// 2026-10-09「这里显示的视频信息改为 文件大小 分辨率 视频编码 音频格式 组名，播放页面标题的也改成
    /// 一样的」；2026-10-25 起 HDR/DV 角标跟在视频编码后面，SDR 片源不多写一个字）。逐项为空即跳过，全空给
    /// 空串 —— 集成模式那一行空串＝副标题框收起。
    /// <para>
    /// 与 <see cref="ToQualityLabel"/> 分工不同，两条都要留着：那条是**画质**读数（分辨率·视频编码·容器·
    /// 大小），进度条中间、版本菜单右列、播放信息浮层问它；这条是**文件信息**读数，容器不写（详情页
    /// 「媒体信息」表里「格 式」那行已经说了），改带音频格式与制作组。
    /// </para>
    /// </summary>
    public string ToFileInfoLabel()
    {
        var parts = new List<string>(6);
        var video = PrimaryVideoStream;

        if (TimeFormat.FileSize(Size) is { Length: > 0 } size) parts.Add(size);
        if (video?.Height is > 0) parts.Add(DescribeHeight(video.Height.Value));
        if (video is not null && VideoCodecLabel(video) is { Length: > 0 } codec) parts.Add(codec);
        if (video?.DynamicRangeLabel is { Length: > 0 } range) parts.Add(range);
        if (PrimaryAudioCodec() is { Length: > 0 } audioCodec) parts.Add(audioCodec.ToUpperInvariant());
        if (ReleaseGroup.FromFileName(Path) is { Length: > 0 } group) parts.Add(group);

        return string.Join("  ·  ", parts);
    }

    /// <summary>
    /// 视频编码那一格：媒体源名字或文件名里写明 x264/x265 时用那个写法，认不出仍是服务器给的编码名。
    /// <para>
    /// MediaInfo 的 Writing library 服务器不会发（Emby、Jellyfin、Plex、飞牛影视的媒体流里都没有这一项），
    /// 客户端手上能核对的是媒体源名字与文件名。名字与编码对不上时以编码为准 —— 名字里的 x264 不该把一份
    /// HEVC 的片子写成 x264。
    /// </para>
    /// </summary>
    private string VideoCodecLabel(MediaStream video)
    {
        if (EncoderLibraryHint(video.Codec) is { } hint) return hint;

        var codec = video.Codec;
        return string.IsNullOrWhiteSpace(codec) ? "" : codec.ToUpperInvariant();
    }

    /// <summary>
    /// 名字里写明的编码库：x264 / x265（不认 H265 这种编码名，也不是 Writing library 的口径）。两个都写着
    /// 时按视频编码挑；编码缺失才由名字单独决定，别的编码（AV1 等）不拿名字去改它。
    /// </summary>
    private string? EncoderLibraryHint(string? codec)
    {
        var fileName = Path is { Length: > 0 } path ? System.IO.Path.GetFileName(path) : null;
        var x264 = Mentions(Name, "x264") || Mentions(fileName, "x264");
        var x265 = Mentions(Name, "x265") || Mentions(fileName, "x265");
        if (!x264 && !x265) return null;

        if (IsHevc(codec)) return x265 ? "x265" : null;
        if (IsH264(codec)) return x264 ? "x264" : null;
        if (!string.IsNullOrWhiteSpace(codec)) return null;
        return x264 ? "x264" : "x265";
    }

    private static bool IsH264(string? codec) =>
        string.Equals(codec, "h264", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(codec, "avc", StringComparison.OrdinalIgnoreCase);

    private static bool IsHevc(string? codec) =>
        string.Equals(codec, "hevc", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(codec, "h265", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 记号要独立成词：<c>x2650</c> 不是 <c>x265</c>，而 <c>.x265-</c>、<c>[x265]</c> 是。
    /// </summary>
    private static bool Mentions(string? text, string token)
    {
        if (string.IsNullOrEmpty(text)) return false;

        for (var at = text.IndexOf(token, StringComparison.OrdinalIgnoreCase);
             at >= 0;
             at = text.IndexOf(token, at + token.Length, StringComparison.OrdinalIgnoreCase))
        {
            var before = at == 0 ? '\0' : text[at - 1];
            var after = at + token.Length == text.Length ? '\0' : text[at + token.Length];
            if (!char.IsLetterOrDigit(before) && !char.IsLetterOrDigit(after)) return true;
        }

        return false;
    }

    /// <summary>
    /// 正在放这一版的主音轨编码：服务器点名的默认音轨优先（<see cref="DefaultAudioStreamIndex"/>），找不到就
    /// 退到第一条音轨。取编码而不是语言，是与「视频编码」对称、且不随「当前选了哪条轨」而变。
    /// </summary>
    private string? PrimaryAudioCodec()
    {
        var audio = DefaultAudioStreamIndex is { } index
            ? MediaStreams.FirstOrDefault(stream => stream.IsAudio && stream.Index == index)
            : null;
        audio ??= AudioStreams.FirstOrDefault();
        return audio?.Codec;
    }

    private static string DescribeHeight(int height) => height switch
    {
        >= 2000 => "4K",
        >= 1000 => "1080p",
        >= 700 => "720p",
        >= 500 => "576p",
        _ => $"{height}p"
    };
}

/// <summary>
/// One credited person, as Emby sends it alongside a single item. The server leaves People out of
/// list responses, so this is only populated on an item fetched by id.
/// </summary>
public sealed class EmbyPerson
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>The character an actor plays; empty for crew.</summary>
    public string? Role { get; set; }

    /// <summary>The kind of credit: "Actor", "GuestStar", "Director", "Writer", "Producer".</summary>
    public string? Type { get; set; }

    /// <summary>Image cache tag for the portrait; null when the server holds no photo.</summary>
    public string? PrimaryImageTag { get; set; }

    /// <summary>What a portrait card shows under the name: the character, else the credit.</summary>
    [JsonIgnore]
    public string Credit => Role is { Length: > 0 } role ? role.Trim() : DescribeCredit(Type);

    private static string DescribeCredit(string? type) => type switch
    {
        "Actor" => "演员",
        "GuestStar" => "客串",
        "Director" => "导演",
        "Writer" => "编剧",
        "Producer" => "制片",
        "Composer" => "配乐",
        null or "" => "",
        _ => type
    };
}

/// <summary>
/// A production company as Emby sends it. Only the name is kept: 「TBS」 is a segment of the detail
/// page's facts line, and this client has nowhere to browse a studio to. The server also sends an
/// <c>Id</c> here, but as a JSON <em>number</em> rather than the string it uses for item ids — reading
/// it into a <see cref="string"/> throws and takes the whole detail response down with it, so the
/// field is deliberately absent and left to be ignored.
/// </summary>
public sealed class EmbyStudio
{
    public string Name { get; set; } = "";
}

public sealed class EmbyItem
{
    /// <summary>运行期所属服务器账号；不进入服务器 JSON 或持久化设置。</summary>
    [JsonIgnore]
    public EmbySessionScope? Source { get; set; }

    /// <summary>区分不同服务器、不同账号上编号相同的条目。</summary>
    [JsonIgnore]
    public string IdentityKey => Source is { } source ? $"{source.Key}:{Id}" : Id;

    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>
    /// 这一份是个存根：只有 id、名字、类型（顶多再加一张海报的标签），别的字段不是「空的」而是「没问过」。
    /// <para>
    /// 两处会造这种东西：<c>ItemDetail.TitleTarget</c>（集页上那行剧名点过去）和 <c>ItemDetail.Cast</c>（演职人员
    /// 那一排卡片）。它们手上只有父条目或者演员的 id 和名字，落到详情页之后由那一页按 id 重新问一遍。
    /// </para>
    /// <para>
    /// 详情页拿这一位决定要不要「先用手上这份画一屏」（<c>DetailViewModel.Preview</c>）：列表接口回来的条目带着
    /// 简介、类型和全部图片标签，先画一屏是白赚的；而拿一个存根先画，屏上就是先写一遍「暂无简介」再换成真的、
    /// 头图的位置先空一下再长出来 —— 那不是快，是闪。显式一位而不是「看看有没有简介」那类猜法：一部真的没写简介
    /// 的电影和一个存根，猜法分不出来。
    /// </para>
    /// </summary>
    [JsonIgnore]
    public bool IsStub { get; set; }

    public string? OriginalTitle { get; set; }

    public string? SortName { get; set; }

    public string Type { get; set; } = "";

    public string? ServerId { get; set; }

    /// <summary>
    /// 服务器上这个条目自己的文件路径（Emby 只在请求带 <c>Fields=Path</c> 时才发）。MoviePilot 的整理/转移要拿它
    /// 当「这是磁盘上的哪个文件」。多版本条目每一版各自的路径在 <see cref="MediaSource.Path"/> 上，这一份是条目级的那个。
    /// </summary>
    public string? Path { get; set; }

    public string? LocationType { get; set; }

    public string? Overview { get; set; }

    public int? ProductionYear { get; set; }

    public DateTimeOffset? PremiereDate { get; set; }

    /// <summary>
    /// When a show finished, which is what turns its facts line into 「2016 – 2018」 rather than a bare
    /// premiere year. Sent only for a series that has ended, and only when asked for by name: this
    /// server omits it — along with the studios, the rating and the premiere date itself — from any
    /// response whose <c>Fields</c> did not list it (see <see cref="EmbyFields.Detail"/>).
    /// </summary>
    public DateTimeOffset? EndDate { get; set; }

    /// <summary>剧集的播出状态：Continuing 或 Ended；缺失时不推测仍在连载。</summary>
    public string? Status { get; set; }

    public DateTimeOffset? DateCreated { get; set; }

    public long? RunTimeTicks { get; set; }

    public int? IndexNumber { get; set; }

    public int? IndexNumberEnd { get; set; }

    public int? ParentIndexNumber { get; set; }

    public bool IsFolder { get; set; }

    public string? CollectionType { get; set; }

    public string? SeriesId { get; set; }

    public string? SeriesName { get; set; }

    public string? SeasonId { get; set; }

    public string? SeasonName { get; set; }

    public string? ParentId { get; set; }

    public int? ChildCount { get; set; }

    /// <summary>
    /// 这部剧在库里有几集 —— <b>本地集数</b>，不是元数据说的总集数。只有飞牛的列表会带
    /// （<c>local_number_of_episodes</c>），Emby / Jellyfin / Plex 一律 null。
    /// <para>
    /// 和 <see cref="ChildCount"/> 不是一回事：飞牛的「几季」走 <c>local_number_of_seasons</c>（→ ChildCount），
    /// 集数是另一个字段。2026-10-25 在用户服务器上核过两条真实数据：《牧神记 年番1》元数据 105 集、库里 14 集；
    /// 《JOJO的奇妙冒险》本地两季共 3 集 —— 所以这个数是**整部剧跨季的本地合计**，不是某一季的。
    /// </para>
    /// <para>
    /// 卡片右上角那颗角标写的就是它（<see cref="LibraryCardText.EpisodeCount"/>）。飞牛不给「未看几集」，
    /// 于是同一个位置改说这部剧库里有多少集。
    /// </para>
    /// </summary>
    public int? LocalEpisodeCount { get; set; }

    public float? CommunityRating { get; set; }

    /// <summary>
    /// 影评人评分，0–100。Emby 里叫「影评指数」，装了 OMDb 那类插件时它就是烂番茄的新鲜度 —— 也是这个客户端能
    /// 拿到的<b>唯一一个真正独立于 <see cref="CommunityRating"/> 的数</b>（见 <see cref="ItemScore"/>）。
    /// <para>
    /// 和 <see cref="CommunityRating"/> 一样是 Fields 点名才发的，所以 <see cref="EmbyFields.Detail"/> 里带着它。
    /// 「编辑元数据」那条路读的是另一个接口（不带 Fields 的单条目接口，回的是完整报文），所以那一头一直读得到它，
    /// 这里从前读不到 —— 两条路各走各的，不矛盾。
    /// </para>
    /// </summary>
    public float? CriticRating { get; set; }

    /// <summary>
    /// 这个条目在服务器上对上了哪几家刮削源的 id —— <c>Tmdb</c> / <c>Imdb</c> / <c>Tvdb</c> / <c>Douban</c>。
    /// <para>
    /// <b>只有 id，没有分数。</b>服务器上豆瓣、TMDB、IMDb 三家的大众分都写进同一个
    /// <see cref="CommunityRating"/>，客户端换不出来，能说的只有「这个条目属于哪一家」。比对的人不止一个
    /// （MoviePilot 的入库匹配、多版本发现），所以比较由调用方显式做、不吃字典自己的比较器：**键的大小写不要指望
    /// 这个字典自己** —— <c>System.Text.Json</c> 会新建一个默认比较器的字典把这里整个换掉。
    /// </para>
    /// <para>
    /// 形状照 <see cref="ImageTags"/> 来（服务器的 schema 说这个字典的值全是字符串）。
    /// </para>
    /// </summary>
    public Dictionary<string, string> ProviderIds { get; set; } = [];

    public string? OfficialRating { get; set; }

    public double? PrimaryImageAspectRatio { get; set; }

    public List<string> Genres { get; set; } = [];

    /// <summary>Free-form server tags; some libraries carry 动画/动漫 here rather than in Genres.</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Who made it, in the server's order. Emby lists every credited company, so a film can arrive with
    /// four of them; the facts line shows the first, which is the broadcaster or the lead studio.
    /// </summary>
    public List<EmbyStudio> Studios { get; set; } = [];

    public Dictionary<string, string> ImageTags { get; set; } = [];

    public List<string> BackdropImageTags { get; set; } = [];

    /// <summary>
    /// The show's logo, as the server hands it to an episode or a season. Emby fills these two on any item
    /// that has no logo of its own but whose parent does — which is the whole reason an episode page can
    /// show the show's name plate. Both or neither: the tag belongs to the other item's id.
    /// </summary>
    public string? ParentLogoItemId { get; set; }

    public string? ParentLogoImageTag { get; set; }

    /// <summary>
    /// The show's backdrop, as the server hands it to an episode or a season — the same inherited-image walk
    /// as <see cref="ParentLogoItemId"/>, for the one artwork an episode almost never has of its own. A
    /// backdrop is what the home page's banner stands on, and an episode without this one would drop to its
    /// own still: a frame out of the video where a picture made to be a background belongs.
    /// <para>
    /// A list rather than one tag, because that is how the server sends backdrops (see
    /// <see cref="BackdropImageTags"/>); the first is the one anything here asks for.
    /// </para>
    /// </summary>
    public string? ParentBackdropItemId { get; set; }

    public List<string> ParentBackdropImageTags { get; set; } = [];

    /// <summary>
    /// 这个剧的缩略图，服务器发给单集或季的那一份 —— 「集页面要用这个剧的背景图或缩略图」里的第二档，也是剧集
    /// 除了背景图之外唯一一张宽幅的画面。
    /// <para>
    /// 两套字段而不是一套，因为服务器就是发两套：<see cref="ParentThumbItemId"/> 是那趟继承图片的上溯 —— 它停在
    /// 手上有这张图的那一层，所以单集拿到的可能是季的；<see cref="SeriesThumbImageTag"/> 明说是这个剧的，和
    /// <see cref="SeriesId"/> 配成一对。先问后者（见 <see cref="ItemArtwork.Inherited"/>）：用户要的是「这个剧的」。
    /// </para>
    /// </summary>
    public string? ParentThumbItemId { get; set; }

    public string? ParentThumbImageTag { get; set; }

    public string? SeriesThumbImageTag { get; set; }

    public EmbyUserData? UserData { get; set; }

    public List<MediaSource> MediaSources { get; set; } = [];

    /// <summary>
    /// 列表响应自带的画质档位（飞牛 <c>media_stream.resolutions</c>，如 <c>1080p</c> / <c>4k</c>）。
    /// 别的后端不在列表里发这一项，所以为空；要完整流信息的地方仍读 <see cref="MediaSources"/>。
    /// </summary>
    public List<string> Resolutions { get; set; } = [];

    /// <summary>
    /// The tracks, flattened out of the media source. Browse and detail responses leave this empty —
    /// they carry <see cref="MediaSources"/> instead — but a session's NowPlayingItem is sent with
    /// the streams and no sources at all, which is how the 服务器 page names what is being decoded.
    /// </summary>
    public List<MediaStream> MediaStreams { get; set; } = [];

    /// <summary>Chapter markers, in timeline order; empty unless the request asked for them.</summary>
    public List<ChapterInfo> Chapters { get; set; } = [];

    /// <summary>
    /// Cast and crew in the server's own billing order. Emby returns this with a single-item
    /// request and omits it from list responses, so it is empty on anything the browse queries
    /// produced — the detail page is the only place with enough of the item to show it.
    /// </summary>
    public List<EmbyPerson> People { get; set; } = [];

    /// <summary>
    /// 宣传语，服务器只在 <c>Fields=Taglines</c> 时才给。列表接口默认不带它 —— 媒体库设置里勾了
    /// 「宣传语」之后，<see cref="LibraryDisplayCatalog.RequestFields"/> 才会把它写进请求。
    /// </summary>
    public List<string> Taglines { get; set; } = [];

    [JsonIgnore]
    public bool IsPlayable => EmbyItemType.IsPlayable(Type);

    /// <summary>Whether 已观看 and 收藏 mean anything here; see <see cref="EmbyItemType.TracksUserState"/>.</summary>
    [JsonIgnore]
    public bool TracksUserState => EmbyItemType.TracksUserState(Type);

    [JsonIgnore]
    public string DisplayTypeName => EmbyItemType.ToChinese(Type);

    [JsonIgnore]
    public long ResumeTicks => UserData?.PlaybackPositionTicks ?? 0;

    [JsonIgnore]
    public bool IsWatched => UserData?.Played ?? false;

    [JsonIgnore]
    public bool HasResumePosition => ResumeTicks > 0 && ProgressFraction is > 0.001 and < 0.995;

    /// <summary>0..1 watched fraction, from the server's own percentage when it sends one.</summary>
    [JsonIgnore]
    public double ProgressFraction
    {
        get
        {
            if (UserData?.PlayedPercentage is { } percentage and > 0) return Math.Clamp(percentage / 100d, 0, 1);
            if (RunTimeTicks is null or <= 0 || ResumeTicks <= 0) return 0;
            return Math.Clamp(ResumeTicks / (double)RunTimeTicks.Value, 0, 1);
        }
    }

    /// <summary>"S01E02" when both numbers are known.</summary>
    [JsonIgnore]
    public string EpisodeCode =>
        ParentIndexNumber is { } season && IndexNumber is { } episode ? $"S{season:00}E{episode:00}" : "";

    /// <summary>What a poster card shows under the title.</summary>
    [JsonIgnore]
    public string CardSubtitle
    {
        get
        {
            if (Type == EmbyItemType.Episode)
            {
                var code = EpisodeCode;
                return code.Length > 0 ? $"{code}  ·  {SeriesName}" : SeriesName ?? "";
            }

            if (Type == EmbyItemType.Series)
            {
                if (ProductionYear is not { } start || start <= 0) return "";
                return string.Equals(Status, "Continuing", StringComparison.OrdinalIgnoreCase)
                    ? $"{start} – 至今"
                    : ItemDetail.Years(this);
            }

            var parts = new List<string>(2);
            if (ProductionYear is { } year and > 0) parts.Add(year.ToString());
            if (DisplayTypeName.Length > 0 && Type != EmbyItemType.Movie) parts.Add(DisplayTypeName);
            return string.Join("  ·  ", parts);
        }
    }

    /// <summary>The episode/season aware title used in window titles and the now-playing bar.</summary>
    public string ToPlaybackTitle()
    {
        if (Type != EmbyItemType.Episode) return Name;
        var code = EpisodeCode;
        var prefix = string.IsNullOrWhiteSpace(SeriesName) ? "" : SeriesName + " ";
        return code.Length > 0 ? $"{prefix}{code} {Name}".Trim() : $"{prefix}{Name}".Trim();
    }

    /// <summary>
    /// 播放页左上角那一行主标题：单集＝「剧名 SxxExx 集名」（用现成的 <see cref="EpisodeCode"/>），其余留片名本身。
    /// <para>
    /// 2026-09-27 那版曾**不带剧名前缀**（当时的说法是「剧名让给第二行的文件信息」）；**2026-09-28 晚按用户令
    /// 「标题前面加上剧名」改回带前缀** —— 第二行如今是「文件大小 · 分辨率 · 视频编码 · 音频格式 · 组名」，剧名在那行里
    /// 本来就不出现，主标题于是把它接回来。写法直接复用 <see cref="ToPlaybackTitle"/>（同一条字符串），
    /// 两处不再各写一份、也不会再悄悄走偏；诊断 / 概览 / 下载命名用的仍是同一个方法。
    /// </para>
    /// <para>
    /// 两条管线共用它 —— 集成模式的标题条与独占模式的 <c>force-media-title</c>（<see cref="Playback.PlaybackPlanner"/>
    /// 与 <c>PlayerViewModel</c> 各喂一处）。
    /// </para>
    /// </summary>
    public string ToPlaybackHeadline() => ToPlaybackTitle();

    public string? PrimaryImageTag => ImageTags.TryGetValue("Primary", out var tag) ? tag : null;

    public string? ThumbImageTag => ImageTags.TryGetValue("Thumb", out var tag) ? tag : null;

    public MediaSource? DefaultMediaSource => MediaSources.FirstOrDefault();
}
