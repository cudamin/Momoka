using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Mpv;
using Momoka.Playback;

namespace Momoka.NaturalContinuationCallerTests;

internal static class R10CallerCases
{
    internal static void Register(CaseRunner runner, string output)
    {
        foreach (var manual in new[] { false, true })
            runner.Test("R10-001-cross-series-parent-" + manual, async () =>
            {
                await using var f = new Fixture(output, "R10-parent-" + manual);
                var a = f.Episode("a");
                var b = f.Episode("b"); b.SeriesId = "other";
                var parent = f.Series(); parent.Genres = ["Animation"];
                var other = f.Series(); other.Id = "other"; other.Genres = ["Drama"];
                f.Transport.Item(f.A, other);
                var first = f.Plan();
                var run = f.Start(a, new UserPlaybackQueue([a, b]), parent);
                await Signals.Done(first.Accepted.Task);
                var next = f.Plan();
                if (manual) await Signals.Done(f.Vm.StepForFixture(1));
                else first.Result.TrySetResult(Fixture.Eof());
                var accepted = await Signals.Done(next.Accepted.Task);
                Check.Equal("other", accepted.Ticket.Parent?.Id, "cross-series must resolve the new parent");
                first.Result.TrySetResult(Fixture.Stopped());
                next.Result.TrySetResult(Fixture.Stopped());
                await Signals.Done(run);
                await Signals.Done(f.Vm.Idle);
            });
        foreach (var crossSource in new[] { false, true })
            runner.Test("R10-002-version-user-queue-" + crossSource, async () =>
            {
                await using var f = new Fixture(output, "R10-queue-" + crossSource);
                var a = f.Episode("a"); var c = f.Episode("c");
                var a2 = f.Episode("a2", crossSource ? f.B : f.A);
                var first = f.Plan();
                var run = f.Start(a, new UserPlaybackQueue([a, c]));
                await Signals.Done(first.Accepted.Task);
                var version = f.Plan();
                var switching = f.Vm.VersionForFixture(new(a2, a2.MediaSources[0], f.ParentFor(a2.Source)));
                await Signals.Done(version.Accepted.Task);
                Check.True(f.Vm.Episodes is UserPlaybackQueue, "switching a version must preserve explicit queue intent");
                Check.Equal(c.IdentityKey, EpisodeNavigation.Step(f.Vm.Episodes, a2.IdentityKey, 1)?.Episode.IdentityKey,
                    "new playback identity must keep the original slot");
                first.Result.TrySetResult(Fixture.Stopped());
                version.Result.TrySetResult(Fixture.Stopped());
                await Signals.Done(switching); await Signals.Done(run);
            });
        foreach (var fromStart in new[] { true, false })
            runner.Test("R10-003-unknown-position-start-" + fromStart, async () =>
            {
                await using var f = new Fixture(output, "R10-start-" + fromStart);
                f.Settings.Playback.ResumeRewindSeconds = 5;
                var a = f.Episode("a"); a.UserData = new() { PlaybackPositionTicks = 120 * TimeSpan.TicksPerSecond };
                var first = f.Plan();
                var choice = fromStart ? new PlaybackChoice(a.MediaSources[0], null, null, false, 0) : null;
                var run = f.Start(a, new UserPlaybackQueue([a]), choice: choice);
                await Signals.Done(first.Accepted.Task);
                var version = f.Plan();
                var switching = f.Vm.VersionForFixture(new(a, new MediaSource { Id = "other" }, f.ParentFor()));
                var accepted = await Signals.Done(version.Accepted.Task);
                Check.Equal(fromStart ? 0L : 115 * TimeSpan.TicksPerSecond, accepted.Ticket.StartTicks, "unknown position preserves accepted start with rewind");
                first.Result.TrySetResult(Fixture.Stopped()); version.Result.TrySetResult(Fixture.Stopped());
                await Signals.Done(switching); await Signals.Done(run);
            });
        runner.Test("R10-004-failed-replace-keeps-eof-continuation", async () =>
        {
            await using var f = new Fixture(output, "R10-failed-eof");
            var a = f.Episode("a"); var c = f.Episode("c"); var b = f.Episode("b", media: false);
            f.Transport.Item(f.A, b);
            var first = f.Plan(); var run = f.Start(a, new UserPlaybackQueue([a, c]));
            await Signals.Done(first.Accepted.Task);
            await Signals.Done(f.Start(b, replace: true));
            var next = f.Plan(); first.Result.TrySetResult(Fixture.Eof());
            await Signals.Done(next.Accepted.Task);
            Check.Equal(c.IdentityKey, f.Vm.NowPlaying!.IdentityKey, "failed preparation must keep the running viewing eligible for EOF");
            next.Result.TrySetResult(Fixture.Stopped()); await Signals.Done(run);
            Check.True(f.Refreshes > 0, "the actual viewing still refreshes on final end");
        });
        runner.Test("R10-005-failed-replace-restores-presentation", async () =>
        {
            await using var f = new Fixture(output, "R10-failed-presentation");
            f.Settings.Mpv.Pipeline = VideoPipelineKind.Standalone;
            var a = f.Episode("a"); var b = f.Episode("b", media: false); f.Transport.Item(f.A, b);
            var first = f.Plan(); var run = f.Start(a, new UserPlaybackQueue([a]));
            await Signals.Done(first.Accepted.Task);
            var presentations = new List<bool>(); f.Vm.PreparePresentation += presentations.Add;
            f.Settings.Mpv.Pipeline = VideoPipelineKind.Integrated;
            await Signals.Done(f.Start(b, replace: true));
            Check.True(f.Vm.PresentedHeadless && f.Vm.PreparingInHost != true, "failed integrated candidate cannot own the standalone viewing");
            Check.Equal(true, presentations.Last(), "presentation owner must be restored through the same callback");
            first.Result.TrySetResult(Fixture.Stopped()); await Signals.Done(run);
        });
        runner.Test("R10-002-version-already-later-keeps-original-slot", async () =>
        {
            await using var f = new Fixture(output, "R10-queue-duplicate-version");
            var a = f.Episode("a"); var c = f.Episode("c"); var a2 = f.Episode("a2");
            var first = f.Plan(); var run = f.Start(a, new UserPlaybackQueue([a, c, a2]));
            await Signals.Done(first.Accepted.Task);
            var version = f.Plan(); var switching = f.Vm.VersionForFixture(new(a2, a2.MediaSources[0], f.ParentFor()));
            await Signals.Done(version.Accepted.Task);
            var next = f.Plan(); await Signals.Done(f.Vm.StepForFixture(1)); await Signals.Done(next.Accepted.Task);
            Check.Equal(c.IdentityKey, f.Vm.NowPlaying!.IdentityKey, "the replacement remains anchored to the original slot");
            var later = f.Plan(); await Signals.Done(f.Vm.StepForFixture(1)); await Signals.Done(later.Accepted.Task);
            Check.Equal(a2.IdentityKey, f.Vm.NowPlaying!.IdentityKey, "later duplicate slot remains reachable");
            await Signals.Done(f.Vm.StepForFixture(1));
            Check.Equal(4, f.Playback.Starts.Count, "last duplicate slot cannot loop back through the first identity");
            first.Result.TrySetResult(Fixture.Stopped()); version.Result.TrySetResult(Fixture.Stopped());
            next.Result.TrySetResult(Fixture.Stopped()); later.Result.TrySetResult(Fixture.Stopped());
            await Signals.Done(switching); await Signals.Done(run); await Signals.Done(f.Vm.Idle);
        });
        runner.Test("R10-004-failed-replace-before-initial-announcement", async () =>
        {
            await using var f = new Fixture(output, "R10-before-announcement");
            var a = f.Episode("a"); var c = f.Episode("c"); var b = f.Episode("b", media: false); f.Transport.Item(f.A, b);
            var entered = Signals.New(); var release = f.Gate();
            var first = f.Plan(); first.BeforeAnnounce = () => { entered.TrySetResult(); return release.Task; };
            var run = f.Start(a, new UserPlaybackQueue([a, c])); await Signals.Done(entered.Task);
            await Signals.Done(f.Start(b, replace: true));
            release.TrySetResult(); await Signals.Done(first.Accepted.Task);
            var next = f.Plan(); first.Result.TrySetResult(Fixture.Eof()); await Signals.Done(next.Accepted.Task);
            Check.Equal(c.IdentityKey, f.Vm.NowPlaying!.IdentityKey, "failure while initial tracks are pending retains the established viewing");
            next.Result.TrySetResult(Fixture.Stopped()); await Signals.Done(run);
        });
    }
}
