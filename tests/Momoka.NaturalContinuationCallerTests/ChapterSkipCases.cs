using Momoka.Playback;

namespace Momoka.NaturalContinuationCallerTests;

internal static class ChapterSkipCases
{
    internal static void Register(CaseRunner runner, string output)
    {
        runner.Test("chapter-skip-distinct-opening-with-both-server-kinds", () => Distinct(output, ending: false, duration: 1440));
        runner.Test("chapter-skip-distinct-ending-with-both-server-kinds", () => Distinct(output, ending: true, duration: 1440));
        runner.Test("chapter-skip-named-bounded-opening-with-unknown-duration", () => Distinct(output, ending: false, duration: 0));
        runner.Test("chapter-skip-overlap-retains-server-sections", () => Overlap(output));
        runner.Test("chapter-skip-empty-repeat-and-late-update-retain-decisions", () => Repeat(output));
    }

    private static readonly SkipSection[] ServerSections =
    [
        new(SkipSectionKind.Opening, 535.6, 555, "片头"),
        new(SkipSectionKind.Ending, 1300, 1440, "片尾")
    ];
    private static readonly SkipChapter[] OpeningChapters = [new(0, "Opening"), new(80, "Episode")];

    private static async Task Distinct(string output, bool ending, double duration)
    {
        await using var f = new Fixture(output, $"chapter-skip-distinct-{ending}-{duration}");
        f.Vm.SeedSkipForFixture(ServerSections, duration);
        SkipChapter[] chapters = ending
            ? [new(0, "Episode"), new(1150, "Ending"), new(1200, "Preview")]
            : OpeningChapters;
        Check.True(f.Vm.ConsiderSkipForFixture(chapters), "both server kinds must still allow a distinct chapter section");
        var sections = f.Vm.SkipPlanForFixture.Sections;
        Check.Equal(3, sections.Count, "one distinct player section is added");
        var player = sections.Single(section => section.Origin == SkipSectionOrigin.Player);
        Check.Equal(ending ? SkipSectionKind.Ending : SkipSectionKind.Opening, player.Kind, "recognized kind");
        Check.Equal(ending ? 1150d : 0d, player.Start, "chapter start remains distinct");
        Check.Equal(ending ? 1200d : 80d, player.End, "next chapter is the bounded end");
        Check.True(ServerSections.All(sections.Contains), "original server intervals remain unchanged");
    }

    private static async Task Overlap(string output)
    {
        await using var f = new Fixture(output, "chapter-skip-overlap");
        SkipSection[] server =
        [
            new(SkipSectionKind.Opening, 0, 90, "片头"),
            new(SkipSectionKind.Ending, 1300, 1440, "片尾")
        ];
        f.Vm.SeedSkipForFixture(server, 1440);
        Check.False(f.Vm.ConsiderSkipForFixture([new(0, "Opening"), new(80, "Episode"), new(1300, "Ending"), new(1380, "Preview")]),
            "overlapping player intervals must not replace server intervals");
        Check.True(server.SequenceEqual(f.Vm.SkipPlanForFixture.Sections), "server endpoints and origin survive");
    }

    private static async Task Repeat(string output)
    {
        await using var f = new Fixture(output, "chapter-skip-repeat");
        f.Vm.SeedSkipForFixture(ServerSections, 1440);
        var plan = f.Vm.SkipPlanForFixture;
        Check.False(f.Vm.ConsiderSkipForFixture([]), "empty chapters do not change the current plan");
        Check.Equal(2, plan.Sections.Count, "empty read preserves both server sections");
        plan.Advance(535.6, playing: true);
        Check.True(plan.Prompt.Visible, "server offer is armed before chapter completion");
        Check.True(plan.Accept() is not null, "user accepts the server offer");
        Check.True(f.Vm.ConsiderSkipForFixture(OpeningChapters), "late chapter adds the missing interval");
        Check.False(f.Vm.ConsiderSkipForFixture(OpeningChapters), "same chapter list is idempotent");
        Check.Equal(3, plan.Sections.Count, "repeat does not duplicate sections");
        plan.Advance(600, playing: true);
        plan.Advance(535.6, playing: true);
        Check.False(plan.Prompt.Visible, "the already accepted server jump is still remembered");
        Check.True(plan.Accept() is null, "late chapters must not re-arm a completed jump");
    }
}
