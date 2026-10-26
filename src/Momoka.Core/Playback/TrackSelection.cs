using Momoka.Configuration;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Mpv;

namespace Momoka.Playback;

/// <summary>When subtitles are turned on by themselves.</summary>
public enum SubtitleMode
{
    /// <summary>Always show the best match from the language priority list.</summary>
    Always,

    /// <summary>Only a track marked forced — signs and songs over foreign-language audio.</summary>
    ForcedOnly,

    /// <summary>Only when the audio is not already in one of the preferred subtitle languages.</summary>
    ForeignAudioOnly,

    /// <summary>Never; start with subtitles off.</summary>
    Off
}

/// <summary>
/// The subtitle decision: a stream to select, or nothing. <see cref="Disabled"/> separates 「本片没有
/// 合适的字幕，交给 mpv」 from 「明确不要字幕」 — the first leaves mpv's own <c>slang</c> matching in
/// charge, the second passes <c>--sid=no</c>.
/// </summary>
public readonly record struct SubtitleChoice(MediaStream? Stream, bool Disabled)
{
    public static SubtitleChoice None => new(null, false);

    public static SubtitleChoice Off => new(null, true);
}

/// <summary>What the client would start this file with if the user picked nothing.</summary>
public readonly record struct AutoTracks(MediaStream? Audio, SubtitleChoice Subtitle);

/// <summary>
/// Picks the starting audio and subtitle track from the user's preferences.
/// <para>
/// This runs in the client rather than being left to mpv's own <c>alang</c>/<c>slang</c> matching for
/// two reasons: mpv cannot tell 简体 from 繁体 when both tracks are labelled <c>chi</c> and only their
/// titles differ, and the index the client reports back to Emby has to be the track that is actually
/// playing. mpv still receives the language lists as a second line of defence.
/// </para>
/// <para>
/// 反过来也成立：客户端这边**分不开**两条时（Emby 既没给标题、又把两条都只标成 <c>chi</c>），决定交回 mpv
/// —— 容器自己的语言标记（<c>zh-Hans</c>／<c>zh-Hant</c>）和 default 标记只有它看得见，按库序猜一条只会
/// 猜中排在前面那条（2026-10-22）。
/// </para>
/// </summary>
public static class TrackSelection
{
    private const string Category = "playback";

    /// <summary>The tracks <paramref name="source"/> should start with under <paramref name="settings"/>.</summary>
    public static AutoTracks Resolve(PlaybackSettings settings, MediaSource source)
    {
        var audio = ChooseAudio(settings, source);
        return new AutoTracks(audio, ChooseSubtitle(settings, source, audio));
    }

    /// <summary>
    /// The first language in <see cref="PlaybackSettings.AudioLanguages"/> that the file actually has, or
    /// whatever is marked default. A file with none of the listed languages falls back to the default rather
    /// than to silence.
    /// <para>
    /// 「音轨不需要优先级，选默认或者单选特定语言就好」 was the earlier answer here, and it was one language for
    /// that reason. It has since become a list for the same reason the subtitle side is one: 「日语 &gt; 粤语 &gt;
    /// 英语」 is a real preference and a single slot cannot hold it. A one-language list is that list's
    /// degenerate case, so nothing about the single-language behaviour changed.
    /// </para>
    /// </summary>
    public static MediaStream? ChooseAudio(PlaybackSettings settings, MediaSource source)
    {
        var map = MpvTrackMap.Build(source);
        var streams = source.AudioStreams.Where(stream => map.CanSelect(stream.Index)).ToList();
        if (streams.Count == 0) return null;

        var formatRules = settings.AudioFormatRules;

        foreach (var language in settings.AudioLanguages)
        {
            var matches = streams.Where(stream => TrackPreference.LanguageMatches(stream, language)).ToList();
            // 格式这一关只在同一语言内部作用：语言优先级仍是第一位的，选中的这批日语轨里再按编码／声道挑，
            // 而不是让一条英语 Atmos 越过日语。语言全没匹配时才落到下面的兜底。
            if (matches.Count > 0) return BestByFormat(matches, formatRules, source.DefaultAudioStreamIndex);
        }

        // 语言全对不上（或压根没设语言）：格式偏好照样作用于全部音轨那一批。
        return BestByFormat(streams, formatRules, source.DefaultAudioStreamIndex);
    }

    /// <summary>
    /// 格式这一关：从同一语言（或兜底）选出的那批音轨里，按格式规则的次序留下最合意的那些，再交给
    /// <see cref="TrackPreference.Best"/> 按默认轨定夺。规则从上往下比，第一条能区分两条轨的规则决定先后；
    /// 比较的文本是轨的编码／声道信息拼起来的（TrueHD、DTS-HD、Atmos、7.1、5.1 这些词落在那里），
    /// 和字幕的 <see cref="BestByTitle"/> 同一套 <see cref="KeywordFilter"/>，只是取的字段不同。
    /// </summary>
    private static MediaStream? BestByFormat(IReadOnlyList<MediaStream> pool, IReadOnlyList<KeywordRule>? rules, int? defaultIndex)
    {
        var ranked = KeywordFilter.Rank(pool, rules, FormatText);
        return TrackPreference.Best(ranked.ToList(), defaultIndex);
    }

    /// <summary>
    /// 一条音轨用来匹配格式关键词的文本：编码、profile、声道布局和标题拼在一起。声道数额外补成
    /// 「8ch」「6ch」，因为服务器有时只给声道数不给「7.1」这样的布局名，而用户按 7.1／5.1 的说法设词。
    /// 大小写、子串匹配由 <see cref="KeywordFilter"/> 负责，这里只管把该看的字段凑齐。
    /// </summary>
    private static string FormatText(MediaStream stream)
    {
        var parts = new List<string>(5);
        if (!string.IsNullOrWhiteSpace(stream.Codec)) parts.Add(stream.Codec!);
        if (!string.IsNullOrWhiteSpace(stream.Profile)) parts.Add(stream.Profile!);
        if (!string.IsNullOrWhiteSpace(stream.ChannelLayout)) parts.Add(stream.ChannelLayout!);
        if (stream.Channels is { } channels && channels > 0) parts.Add($"{channels}ch");
        if (!string.IsNullOrWhiteSpace(stream.Title)) parts.Add(stream.Title!);
        else if (!string.IsNullOrWhiteSpace(stream.DisplayTitle)) parts.Add(stream.DisplayTitle!);
        return string.Join(" ", parts);
    }

    /// <summary>
    /// The first language in <see cref="PlaybackSettings.SubtitleLanguages"/> that the file actually
    /// has, subject to <see cref="PlaybackSettings.SubtitleMode"/>.
    /// </summary>
    public static SubtitleChoice ChooseSubtitle(PlaybackSettings settings, MediaSource source, MediaStream? audio)
    {
        if (settings.SubtitleMode == SubtitleMode.Off) return SubtitleChoice.Off;

        var languages = settings.SubtitleLanguages;

        // 仅在音频为外语时显示：听得懂的语言不需要字幕。
        if (settings.SubtitleMode == SubtitleMode.ForeignAudioOnly
            && audio is not null
            && languages.Any(language => language != TrackLanguagePriority.Any
                && TrackPreference.LanguageMatches(audio, language)))
        {
            return SubtitleChoice.Off;
        }

        var forcedOnly = settings.SubtitleMode == SubtitleMode.ForcedOnly;
        var map = MpvTrackMap.Build(source);
        var streams = source.SubtitleStreams.Where(stream => map.CanSelect(stream.Index)).ToList();
        if (streams.Count == 0)
        {
            // 未提供字幕元数据不等于容器没有字幕；普通 auto 不能携带“只强制”约束。
            // 无法确认可加载的强制轨时保守关闭，显式手选由规划器在此规则之前处理。
            return forcedOnly || source.SubtitleStreams.Any() ? SubtitleChoice.Off : SubtitleChoice.None;
        }

        var pool = forcedOnly ? streams.Where(stream => stream.IsForced).ToList() : streams;

        // 只要强制字幕，而这个文件一条都没有：什么都不选，而不是退回整轨字幕。
        if (pool.Count == 0) return SubtitleChoice.Off;

        // 用户设的标题规则，外加隐式的那条：优先级里有简体中文时，自动把标题带「繁/繁体」的压成候补。这条策略
        // 留在这儿（字幕域），通用的合并动作交给 KeywordFilter.WithExclusions。
        var namesSimplified = languages.Any(name => TrackLanguagePriority.Canonical(name) == "简体中文");
        var rules = settings.SubtitleTitleRules;
        if (namesSimplified)
        {
            rules = [.. KeywordFilter.WithExclusions(rules, "繁", "繁体")];
        }

        // 优先级里点了简/繁中的一种（「我要那一种写法」）。
        var namesVariant = languages.Any(name => TrackLanguagePriority.Canonical(name) is "简体中文" or "繁体中文");

        Dump(source);

        foreach (var language in languages)
        {
            // 语言这一关只看语言字段，不看标题（includeTitle: false）——「先确定哪几条字幕是中文」。选中的这批再
            // 交给标题这一关，那才是「决定不要双语和特效」的地方。
            var matches = pool.Where(stream => TrackPreference.LanguageMatches(stream, language, includeTitle: false)).ToList();
            if (matches.Count == 0) continue;

            // 点了简体中文时，标题写着「繁」的那几条在标题规则之前就让位 —— 用户的关键词是按一种写法敲进去的
            // （「双语」匹配不到「雙語」），把它当成一条排在最后的候补规则不够用：「双语→候补」只压住了
            // 简日双语，繁日雙語躲过这条反而排到前头（2026-10-22 用户报的那一条）。排除仍是软的：全都写着
            // 繁体时照旧留用。
            var candidates = matches;
            if (namesSimplified)
            {
                var withoutTraditional = matches.Where(stream => !SaysTraditional(TitleText(stream))).ToList();
                if (withoutTraditional.Count > 0) candidates = withoutTraditional;
            }

            var ranked = KeywordFilter.Rank(candidates, rules, TitleText).ToList();

            // 分不开时把决定交给 mpv（不传 sid，只给 slang）：客户端手里只有 Emby 那份元数据，而容器里的
            // 语言标记（zh-Hans／zh-Hant）与 default 标记只有 mpv 看得见。只对「点名了简/繁」的场合这么做：
            // 没点名时按默认轨/库序取一条仍是用户要的语义，而「其他字幕」本来就是「随便哪条都行」。
            // 强制字幕模式也除外：那里必须落在一条 forced 轨上。
            if (namesVariant && !forcedOnly && ranked.Count > 1
                && TrackLanguagePriority.Canonical(language) != TrackLanguagePriority.Any) return SubtitleChoice.None;

            return new SubtitleChoice(TrackPreference.Best(ranked, source.DefaultSubtitleStreamIndex, forcedOnly), false);
        }

        if (languages.Count > 0 && !settings.SubtitleFallbackToDefault) return SubtitleChoice.Off;

        var fallback = pool.FirstOrDefault(stream => stream.Index == source.DefaultSubtitleStreamIndex)
            ?? pool.FirstOrDefault(stream => stream.IsDefault);
        return fallback is null ? SubtitleChoice.Off : new SubtitleChoice(fallback, false);
    }

    /// <summary>
    /// 标题这一关用的文本：轨的 <c>Title</c>，没有就退到 <c>DisplayTitle</c> —— 和语言那一关分家
    /// （见 <see cref="KeywordFilter"/>）。两条都是空的时候，这一关对这条轨什么也说不出来。
    /// </summary>
    private static string? TitleText(MediaStream stream) => stream.Title ?? stream.DisplayTitle;

    /// <summary>标题里有没有「繁」这个字：繁体、繁體、繁中、繁日雙語都算。</summary>
    private static bool SaysTraditional(string? text) =>
        !string.IsNullOrEmpty(text) && text.Contains('繁');

    /// <summary>
    /// 把这一版文件的字幕流原样写进日志。Emby 那份元数据是选轨的唯一依据，而它丢标题、把两条中文都写成同一个
    /// 粗粒度码是常有的事 —— 选错的时候，日志里得有原始读数可查，否则只能对着屏幕猜（2026-10-22）。
    /// </summary>
    private static void Dump(MediaSource source)
    {
        var subtitles = source.SubtitleStreams.ToList();
        if (subtitles.Count == 0) return;

        var lines = subtitles.Select(stream =>
            $"[{stream.Index}] lang={stream.Language} display={stream.DisplayLanguage} title={stream.Title} " +
            $"displayTitle={stream.DisplayTitle} default={stream.IsDefault} forced={stream.IsForced} external={stream.IsExternal}");
        Log.Debug(Category, $"字幕候选（{subtitles.Count} 条）：{string.Join("；", lines)}");
    }
}
