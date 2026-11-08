namespace Momoka.Emby;

/// <summary>
/// 编解码器在屏上的写法：把 ffmpeg／mpv 的机器名（<c>subrip</c>、<c>hdmv_pgs_subtitle</c>、
/// <c>eac3</c>）换成播放器里通常那么写的短名（SRT、PGS、E-AC3）。
/// <para>
/// 两处共用这一份：详情页的轨道下拉与「媒体信息」表（<see cref="MediaStream.ToDisplayLabel"/>），
/// 以及播放中那几行读数（<c>MpvTrack.DisplayDetail</c>）。同一份文件在两处读起来必须是同一个名字 ——
/// 从前详情页写 <c>SUBRIP</c>、播放里写 <c>SRT</c>，同一个东西两个说法。
/// </para>
/// <para>
/// 认不出来的编码照旧大写原样给出：没见过的名字仍是信息，吞掉它只会让人少一条线索。PCM 例外 ——
/// 它有一打拼法（<c>pcm_s16le</c>、<c>pcm_f32be</c>…），尾巴对读的人没有意义，统一写 PCM。
/// </para>
/// </summary>
public static class MediaCodec
{
    /// <summary>「SRT」for <c>subrip</c>, 「E-AC3」for <c>eac3</c>, the upper-cased name for anything unknown, and "" for nothing.</summary>
    public static string Describe(string? codec)
    {
        var trimmed = (codec ?? "").Trim();
        if (trimmed.Length == 0) return "";

        var known = trimmed.ToLowerInvariant() switch
        {
            "eac3" or "e-ac-3" => "E-AC3",
            "ac3" => "AC3",
            "truehd" => "TrueHD",
            "dts" => "DTS",
            "dtshd" or "dts-hd" => "DTS-HD",
            "aac" => "AAC",
            "opus" => "Opus",
            "vorbis" => "Vorbis",
            "flac" => "FLAC",
            "alac" => "ALAC",
            "mp3" => "MP3",
            "mp2" => "MP2",
            "subrip" or "srt" => "SRT",
            "ass" => "ASS",
            "ssa" => "SSA",
            "webvtt" or "vtt" => "WebVTT",
            "mov_text" => "MOV 文本",
            "hdmv_pgs_subtitle" or "pgs" => "PGS",
            "dvd_subtitle" or "vobsub" => "VobSub",
            "dvb_subtitle" => "DVB",
            "dvb_teletext" => "图文电视",
            "eia_608" or "cc" => "CC",
            "text" => "文本",
            _ => ""
        };

        if (known.Length == 0 && trimmed.StartsWith("pcm", StringComparison.OrdinalIgnoreCase)) known = "PCM";

        return known.Length > 0 ? known : trimmed.ToUpperInvariant();
    }
}
