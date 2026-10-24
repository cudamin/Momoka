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

/// <summary>
/// A stretch of the file worth jumping over: <see cref="Start"/>..<see cref="End"/>, with the label the
/// button carries.
/// </summary>
public readonly record struct SkipSection(
    SkipSectionKind Kind,
    double Start,
    double End,
    string Label)
{
    /// <summary>What the button says: 跳过片头, 跳过片尾 …</summary>
    public string Caption => "跳过" + Label;

    /// <summary>Whether the section is what is playing at <paramref name="position"/> seconds.</summary>
    public bool Covers(double position) =>
        End > 0 && position >= Start - 0.5 && position < End - 0.5;
}

/// <summary>
/// Turns Emby's own intro and credit markers into the two sections the 跳过 offer knows about.
/// <para>
/// This reads marker data and nothing else. Emby tags the entries it detected in the item's
/// <c>Chapters</c> field with <see cref="ChapterInfo.MarkerType"/>: an opening is <c>IntroStart</c> to
/// the next <c>IntroEnd</c>, and the credits run from <c>CreditsStart</c> to the end of the file.
/// Chapter titles and lengths are not consulted — 「OP」 written in a chapter name is a release group's
/// label, not proof that the server found titles there, and guessing from an unnamed 90-second chapter
/// put a 跳过 button on episodes with nothing to skip. When Emby has no markers the answer is no
/// sections, which is also what the setting means by 关闭 on that file.
/// </para>
/// </summary>
public static class SkipSectionPlanner
{
    /// <summary>Emby's marker kinds, spelled as the server sends them.</summary>
    public const string IntroStartMarker = "IntroStart";
    public const string IntroEndMarker = "IntroEnd";
    public const string CreditsStartMarker = "CreditsStart";

    /// <summary>Every section Emby marked on <paramref name="item"/>.</summary>
    public static IReadOnlyList<SkipSection> Resolve(EmbyItem? item) =>
        item is null ? [] : Resolve(item.Chapters, TimeFormat.ToSeconds(item.RunTimeTicks));

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
    /// Emby's chapter records as the planner's own shape. The list is kept whole — marker entries
    /// included — because the hover preview indexes chapter stills against exactly this array.
    /// </summary>
    public static IReadOnlyList<SkipChapter> FromEmby(IReadOnlyList<ChapterInfo> chapters)
    {
        var converted = new List<SkipChapter>(chapters.Count);
        foreach (var chapter in chapters) converted.Add(new SkipChapter(chapter.StartSeconds, chapter.Name));
        return converted;
    }

    private static bool Same(string? markerType, string expected) =>
        string.Equals(markerType, expected, StringComparison.OrdinalIgnoreCase);
}
