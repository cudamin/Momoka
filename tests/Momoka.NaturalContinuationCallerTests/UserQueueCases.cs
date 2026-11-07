using Momoka.Playback;

namespace Momoka.NaturalContinuationCallerTests;

internal static class UserQueueCases
{
    internal static async Task Run(string output, string mode)
    {
        await using var f = new Fixture(output, "R21-" + mode);
        var a = f.Episode("a");
        var b = f.Episode("b");
        var c = f.Episode("c");
        a.IndexNumber = 1; b.IndexNumber = 2; c.IndexNumber = 3;
        if (mode == "cross-series")
        {
            c.SeriesId = "other-series";
            var other = f.Series(); other.Id = "other-series";
            f.Transport.Item(f.A, other);
            f.Transport.Item(f.A, f.Series());
        }
        var reads = 0;
        f.Transport.Episodes(f.A, "series", _ =>
        {
            reads++;
            return Task.FromResult<object>(new { Items = new[] { a, b, c } });
        });
        IReadOnlyList<Momoka.Emby.EmbyItem> queue = mode == "ordinary" ? new[] { a }
            : new UserPlaybackQueue(new[] { a, c, b });
        var first = f.Plan();
        var run = f.Start(a, queue);
        await Signals.Done(first.Accepted.Task);
        // Allow the real asynchronous episode fill to publish its result when it was admitted.
        await Task.Delay(100);
        Check.Equal(mode == "ordinary" ? 1 : 0, reads, "explicit queues must not request a replacement series list");
        Check.Equal(mode == "ordinary" ? b.IdentityKey : c.IdentityKey,
            f.Vm.Episodes[1].IdentityKey, "current queue order survives series fill");
        var next = f.Plan();
        first.Result.TrySetResult(Fixture.Eof());
        await Signals.Done(next.Accepted.Task);
        await Task.Delay(100);
        Check.Equal(mode == "ordinary" ? b.IdentityKey : c.IdentityKey, f.Vm.NowPlaying!.IdentityKey,
            "automatic continuation chooses the original next item");
        if (mode != "ordinary")
        {
            Check.True(f.Vm.Episodes is UserPlaybackQueue, "intent survives binding and automatic continuation");
            Check.Equal(0, reads, "successor must not refill user queue");
            var manual = f.Plan();
            await Signals.Done(f.Vm.StepForFixture(1));
            await Signals.Done(manual.Accepted.Task);
            Check.Equal(b.IdentityKey, f.Vm.NowPlaying!.IdentityKey, "manual step preserves original shuffled order");
            Check.True(f.Vm.Episodes is UserPlaybackQueue, "manual step retains queue intent");
            next.Result.TrySetResult(Fixture.Stopped());
            await Signals.Done(run);
            await Signals.Done(f.Vm.StepForFixture(1));
            Check.Equal(0, reads, "queue end must not query or append server series");
            manual.Result.TrySetResult(Fixture.Stopped());
            await Signals.Done(f.Vm.Idle);
        }
        else
        {
            next.Result.TrySetResult(Fixture.Stopped());
            await Signals.Done(run);
        }
    }
}
