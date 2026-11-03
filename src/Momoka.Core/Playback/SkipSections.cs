using System.Text.RegularExpressions;
using Momoka.Emby;
using Momoka.Infrastructure;

namespace Momoka.Playback;

/// <summary>Which end of the file a recognised section belongs to.</summary>
public enum SkipSectionKind
{
    /// <summary>Everything before the episode proper: a recap, then the titles.</summary>
    Opening,

    /// <summary>Everything after it: the credit roll, then the next-episode trailer.</summary>
    Ending
}

/// <summary>What the client does when playback walks into a recognised section.</summary>
public enum SkipSectionMode
{
    /// <summary>Offer the 跳过 button and wait to be asked.</summary>
    Ask,

    /// <summary>Jump over it without asking.</summary>
    Auto,

    /// <summary>Recognise nothing and offer nothing.</summary>
    Off
}

/// <summary>One chapter mark, from Emby's item or from mpv's own <c>chapter-list</c>.</summary>
/// <param name="Start">Where the chapter begins, in seconds.</param>
/// <param name="Title">The chapter's name; null or empty for the majority that have none.</param>
public readonly record struct SkipChapter(double Start, string? Title);

/// <summary>Who recognised a section: the server's own scan, or the player's chapter rules.</summary>
public enum SkipSectionOrigin
{
    /// <summary>Emby's <c>IntroStart</c>/<c>IntroEnd</c>/<c>CreditsStart</c> markers.</summary>
    Server,

    /// <summary>The file's own chapter titles and positions, read through mpv.</summary>
    Player
}

/// <summary>
/// A stretch of the file worth jumping over: <see cref="Start"/>..<see cref="End"/>, with the label the
/// button carries.
/// </summary>
public readonly record struct SkipSection(
    SkipSectionKind Kind,
    double Start,
    double End,
    string Label,
    SkipSectionOrigin Origin = SkipSectionOrigin.Server)
{
    /// <summary>What the button says: 跳过片头, 跳过片尾 …</summary>
    public string Caption => "跳过" + Label;

    /// <summary>Whether the section is what is playing at <paramref name="position"/> seconds.</summary>
    public bool Covers(double position) =>
        End > 0 && position >= Start - 0.5 && position < End - 0.5;
}

/// <summary>
/// Turns chapter marks into the two sections the 跳过 offer knows about: the server's own detection
/// first, then — only for the kinds the server did not mark — the player's chapter list.
/// <para>
/// 「The server's own detection」 has three shapes behind one entry point. Emby tags entries in the item's
/// <c>Chapters</c> field with <see cref="ChapterInfo.MarkerType"/>: an opening is <c>IntroStart</c> to
/// the next <c>IntroEnd</c>, and the credits run from <c>CreditsStart</c> to the end of the file.
/// Jellyfin publishes media segments and Plex publishes markers; both carry a start and an end, and both
/// arrive in <see cref="EmbyItem.SkipMarkers"/>. Chapter titles and lengths are not consulted for a server
/// section — 「OP」 written in a chapter name is a release group's label, not proof that the server found
/// titles there.
/// </para>
/// <para>
/// The player half follows chapterskip.lua from dyphire/mpv-config: a chapter title announces the
/// opening or ending, and a chapter of 80–100 seconds sitting at the very start or end is inferred
/// when no title does. It is only consulted for a kind the server did not provide; see <see cref="Merge"/>.
/// </para>
/// </summary>
public static partial class SkipSectionPlanner
{
    /// <summary>Emby's marker kinds, spelled as the server sends them.</summary>
    public const string IntroStartMarker = "IntroStart";
    public const string IntroEndMarker = "IntroEnd";
    public const string CreditsStartMarker = "CreditsStart";

    /// <summary>How far into the file chapterskip.lua lets an opening begin.</summary>
    public const double PlayerIntroWindow = 200;

    /// <summary>How close to the run time chapterskip.lua lets an ending begin.</summary>
    public const double PlayerOutroWindow = 300;

    /// <summary>The chapter length chapterskip.lua treats as a likely opening or ending.</summary>
    public const double PlayerChapterMinimum = 80;
    public const double PlayerChapterMaximum = 100;

    /// <summary>
    /// Every section the server marked on <paramref name="item"/>.
    /// <para>
    /// Two server shapes exist and both are read here: Jellyfin's media segments and Plex's markers
    /// arrive in <see cref="EmbyItem.SkipMarkers"/> with both ends known, while Emby's own detection is
    /// written into its chapter list as <c>IntroStart</c>/<c>IntroEnd</c>/<c>CreditsStart</c> entries.
    /// The two never occur on the same item, and an item carrying neither yields nothing here — that is
    /// the case the player's own chapter rules fill in, one kind at a time (see <see cref="Merge"/>).
    /// </para>
    /// </summary>
    public static IReadOnlyList<SkipSection> Resolve(EmbyItem? item)
    {
        if (item is null) return [];
        var duration = TimeFormat.ToSeconds(item.RunTimeTicks);
        return item.SkipMarkers.Count > 0 ? Resolve(item.SkipMarkers, duration) : Resolve(item.Chapters, duration);
    }

    /// <summary>
    /// Every section a server published as a segment with both ends — Jellyfin media segments and Plex
    /// markers — in playing order.
    /// <para>
    /// An ending that arrived without a usable end runs to the item's run time, which is what
    /// <c>CreditsStart</c> has always meant. An opening without one is dropped: there is no rule to
    /// invent where the titles stop, and a button that jumps into the episode is worse than none.
    /// </para>
    /// </summary>
    public static IReadOnlyList<SkipSection> Resolve(IReadOnlyList<ServerSkipMarker> markers, double duration)
    {
        if (markers.Count == 0) return [];

        var sections = new List<SkipSection>(markers.Count);
        foreach (var marker in markers)
        {
            var start = marker.StartSeconds;
            if (!double.IsFinite(start) || start < 0) continue;

            var end = marker.EndSeconds;
            if (!double.IsFinite(end) || end <= start + 0.5)
            {
                if (marker.Kind == ServerSkipMarkerKind.Opening || !(duration > start + 0.5)) continue;
                end = duration;
            }
            else if (marker.Kind == ServerSkipMarkerKind.Ending && duration > start + 0.5)
            {
                end = Math.Min(end, duration);
            }

            if (end <= start + 0.5) continue;
            sections.Add(new SkipSection(
                marker.Kind == ServerSkipMarkerKind.Opening ? SkipSectionKind.Opening : SkipSectionKind.Ending,
                start,
                end,
                marker.Kind == ServerSkipMarkerKind.Opening ? "片头" : "片尾"));
        }

        sections.Sort((left, right) => left.Start.CompareTo(right.Start));
        return sections;
    }

    /// <summary>
    /// Every section in a file of <paramref name="duration"/> seconds whose chapters carry these Emby
    /// markers, in playing order.
    /// <para>
    /// Markers are paired by position rather than by list index: a marker list that arrived out of order
    /// still gives the same answer, and a stray <c>IntroEnd</c> with no opening before it is ignored
    /// instead of becoming a section with nowhere to jump from. Multiple detected openings are each
    /// emitted; the button follows whichever one playback is inside.
    /// </para>
    /// </summary>
    public static IReadOnlyList<SkipSection> Resolve(IReadOnlyList<ChapterInfo> chapters, double duration)
    {
        var sections = new List<SkipSection>();
        if (chapters.Count == 0) return sections;

        var markers = chapters
            .Where(chapter => chapter.StartSeconds >= 0 && !string.IsNullOrWhiteSpace(chapter.MarkerType))
            .OrderBy(chapter => chapter.StartSeconds)
            .ToList();

        double? introStart = null;
        foreach (var marker in markers)
        {
            if (Same(marker.MarkerType, IntroStartMarker))
            {
                introStart ??= marker.StartSeconds;
            }
            else if (Same(marker.MarkerType, IntroEndMarker))
            {
                if (introStart is { } start && marker.StartSeconds > start)
                    sections.Add(new SkipSection(SkipSectionKind.Opening, start, marker.StartSeconds, "片头"));

                introStart = null;
            }
            else if (Same(marker.MarkerType, CreditsStartMarker) && duration > marker.StartSeconds)
            {
                sections.Add(new SkipSection(SkipSectionKind.Ending, marker.StartSeconds, duration, "片尾"));
            }
        }

        return sections;
    }

    /// <summary>
    /// Every section the player can recognise from <paramref name="chapters"/> — mpv's own
    /// <c>chapter-list</c> — in a file of <paramref name="duration"/> seconds.
    /// <para>
    /// This is chapterskip.lua's recognition: titles first (OP/Opening/Intro Start/片头 for an
    /// opening; ED/Ending/Credits/片尾 for an ending), then the position heuristic for an unlabelled
    /// chapter of 80–100 seconds in the first or last two of the list. An opening runs to the next
    /// chapter; an ending runs to the next chapter as well, so a preview chapter after the credits
    /// stays where it is. A duration of zero still permits title matches, but not the heuristic.
    /// </para>
    /// </summary>
    public static IReadOnlyList<SkipSection> ResolveFromPlayer(
        IReadOnlyList<SkipChapter> chapters,
        double duration)
    {
        if (chapters.Count < 2) return [];

        var ordered = chapters
            .Where(chapter => double.IsFinite(chapter.Start) && chapter.Start >= 0)
            .OrderBy(chapter => chapter.Start)
            .ToList();
        if (ordered.Count < 2) return [];

        // The last chapter has no following mark to end it; without a run time there is nothing to
        // recognise there, and the position heuristic cannot judge 「near the end」 either.
        var total = double.IsFinite(duration) && duration > ordered[^1].Start ? duration : 0;
        var candidates = new PlayerCandidate[ordered.Count];
        for (var index = 0; index < ordered.Count; index++)
        {
            var start = ordered[index].Start;
            var end = index + 1 < ordered.Count ? ordered[index + 1].Start : total;
            if (!double.IsFinite(end) || end <= start + 0.5) continue;

            var kind = KindFromTitle(ordered[index].Title)
                ?? KindFromPosition(index, ordered.Count, start, end - start, total);
            if (kind is { } found) candidates[index] = new PlayerCandidate(found, start, end);
        }

        // chapterskip.lua keeps the second opening candidate inside the intro window: a recap and
        // the titles both look like openings, and the titles are the one after the recap.
        var openings = new List<int>();
        for (var index = 0; index < candidates.Length; index++)
            if (candidates[index] is { Kind: SkipSectionKind.Opening, Start: < PlayerIntroWindow })
                openings.Add(index);
        if (openings.Count >= 2)
        {
            var chosen = openings[1];
            foreach (var index in openings)
                if (index != chosen) candidates[index] = default;
        }

        var sections = new List<SkipSection>();
        foreach (var candidate in candidates)
        {
            if (candidate.Kind is not { } kind) continue;
            sections.Add(new SkipSection(
                kind,
                candidate.Start,
                candidate.End,
                kind == SkipSectionKind.Opening ? "片头" : "片尾",
                SkipSectionOrigin.Player));
        }

        return sections;
    }

    /// <summary>
    /// Combines Emby's sections with the player's. A player section is dropped only when it overlaps
    /// an Emby section of the same kind — Emby wins where the two describe the same stretch — while a
    /// distinct chapter is kept. That is what lets a chapter named Opening at 0–80 survive a
    /// misdetected server IntroStart at 8:55 instead of being suppressed by it. The result stays in
    /// playing order.
    /// </summary>
    public static IReadOnlyList<SkipSection> Merge(
        IReadOnlyList<SkipSection> server,
        IReadOnlyList<SkipSection> player)
    {
        if (server.Count == 0) return player;
        if (player.Count == 0) return server;

        var merged = new List<SkipSection>(server);
        foreach (var section in player)
        {
            var overlapped = server.Any(existing =>
                existing.Kind == section.Kind && Overlaps(existing, section));
            if (!overlapped) merged.Add(section);
        }

        merged.Sort((left, right) => left.Start.CompareTo(right.Start));
        return merged;
    }

    private static bool Overlaps(SkipSection left, SkipSection right) =>
        left.Start < right.End && right.Start < left.End;

    /// <summary>
    /// Emby's chapter records as the planner's own shape. The list is kept whole — marker entries
    /// included — because the hover preview indexes chapter stills against exactly this array.
    /// </summary>
    public static IReadOnlyList<SkipChapter> FromEmby(IReadOnlyList<ChapterInfo> chapters)
    {
        var converted = new List<SkipChapter>(chapters.Count);
        foreach (var chapter in chapters) converted.Add(new SkipChapter(chapter.StartSeconds, chapter.Name));
        return converted;
    }

    private static SkipSectionKind? KindFromTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var name = title.Trim();
        if (PlayerOpeningTitle().IsMatch(name)) return SkipSectionKind.Opening;
        if (PlayerEndingTitle().IsMatch(name)) return SkipSectionKind.Ending;
        return null;
    }

    private static SkipSectionKind? KindFromPosition(
        int index, int count, double start, double length, double duration)
    {
        // chapterskip.lua's window is meaningless on a file shorter than an opening.
        if (duration <= PlayerIntroWindow) return null;
        if (length < PlayerChapterMinimum || length > PlayerChapterMaximum) return null;

        // Only the first two and last two chapters are candidates.
        if (index > 1 && index < count - 2) return null;

        if (index < (count + 1) / 2)
            return start < PlayerIntroWindow ? SkipSectionKind.Opening : null;

        return start >= duration - PlayerOutroWindow ? SkipSectionKind.Ending : null;
    }

    private readonly record struct PlayerCandidate(
        SkipSectionKind? Kind,
        double Start,
        double End);

    [GeneratedRegex(
        @"^(?:op|opening)(?:\d+)?(?=[\s/._-]|$)|\b(?:op|opening)$|^intro\s*start\b|^intro$|オープニング$|^片头$|^片头开始$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlayerOpeningTitle();

    [GeneratedRegex(
        @"^(?:ed|ending)(?:\d+)?(?=[\s/._-]|$)|\b(?:ed|ending)$|^credits\b|\bcredits$|エンディング$|^片尾$|^片尾开始$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlayerEndingTitle();

    private static bool Same(string? markerType, string expected) =>
        string.Equals(markerType, expected, StringComparison.OrdinalIgnoreCase);
}
