using Momoka.Emby;

namespace Momoka.NaturalContinuationCallerTests;

internal static class EpisodeLookupCases
{
    internal static async Task Run(string output, string mode)
    {
        await using var f = new Fixture(output, "episode-lookup-owner-" + mode);
        f.Settings.Playback.AutoPlayNextEpisode = false;
        var a = f.Episode("A");
        var b = f.Episode("B", f.B);
        var c = f.Episode("C", f.B);
        var oldResponse = f.Gate();
        var newResponse = f.Gate();
        var enteredA = Signals.New<AnonymousTransport.Read>();
        var enteredB = Signals.New<AnonymousTransport.Read>();
        // 2026-11-03 起每一次单集开播都会先读一次「全剧列表」（PlayerViewModel.LoadSeriesEpisodesAsync）：
        // 那是**另一次**请求，落在同一条路由上。它先被放行 —— 回一份不含当前集的空表，产品侧原样不动列表 ——
        // 之后这条路由才归下面那两段跨季查询握着不放。按次数分身，不靠先后猜。
        var filledA = Signals.New<object>();
        var filledB = Signals.New<object>();
        var callsA = 0;
        var callsB = 0;

        f.Transport.Episodes(f.A, "series", async read =>
        {
            if (Interlocked.Exchange(ref callsA, 1) == 0)
            {
                filledA.TrySetResult(new object());
                return new { Items = Array.Empty<EmbyItem>() };
            }

            enteredA.TrySetResult(read);
            // Deliberately finish after cancellation to exercise the real caller's late finally.
            await oldResponse.Task.ConfigureAwait(false);
            if (mode == "stale-error") throw new EndpointFailure("old A lookup failed");
            return new { Items = new[] { a } };
        });
        f.Transport.Episodes(f.B, "series", async read =>
        {
            if (Interlocked.Exchange(ref callsB, 1) == 0)
            {
                filledB.TrySetResult(new object());
                return new { Items = Array.Empty<EmbyItem>() };
            }

            enteredB.TrySetResult(read);
            await newResponse.Task.ConfigureAwait(false);
            return new { Items = new[] { b } };
        });
        var planA = f.Plan();
        var playingA = f.Start(a, [a]);
        await Signals.Done(planA.Accepted.Task);
        await Signals.Done(filledA.Task);
        var lookupA = f.Vm.StepForFixture(1);
        var readA = await Signals.Done(enteredA.Task);
        Check.True(f.Vm.HasEpisodeLookup, "A owns pending cross-season lookup");

        if (mode == "stop")
        {
            f.Playback.StopInput = () => { planA.Result.TrySetResult(Fixture.Stopped()); return Task.CompletedTask; };
            await Signals.Done(f.Vm.StopAsync());
            Check.True(readA.Token.IsCancellationRequested, "Stop cancels the actual metadata request");
            Check.False(f.Vm.HasEpisodeLookup, "Stop releases the lookup slot immediately");
            oldResponse.TrySetResult();
            await Signals.Done(lookupA);
            await Signals.Done(playingA);
            return;
        }

        var planB = f.Plan();
        var playingB = f.Start(b, mode == "local-step" ? [b, c] : [b], replace: true);
        await Signals.Done(planB.Accepted.Task);
        await Signals.Done(filledB.Task);
        Check.True(readA.Token.IsCancellationRequested, "replacing A with B cancels A's HTTP request");
        Check.False(f.Vm.HasEpisodeLookup, "B does not inherit A's busy flag");
        planA.Result.TrySetResult(Fixture.Stopped());
        await Signals.Done(playingA);

        if (mode == "local-step")
        {
            var planC = f.Plan();
            await Signals.Done(f.Vm.StepForFixture(1));
            await Signals.Done(planC.Accepted.Task);
            Check.Same(c, f.Vm.NowPlaying, "B's local next episode starts before old query returns");
            oldResponse.TrySetResult();
            await Signals.Done(lookupA);
            planB.Result.TrySetResult(Fixture.Stopped());
            await Signals.Done(playingB);
            planC.Result.TrySetResult(Fixture.Stopped());
            await Signals.Done(f.Vm.Idle);
            return;
        }

        var lookupB = f.Vm.StepForFixture(1);
        var readB = await Signals.Done(enteredB.Task);
        var notices = f.Notices.Count;
        oldResponse.TrySetResult();
        await Signals.Done(lookupA);
        Check.True(f.Vm.HasEpisodeLookup, "A's late finally cannot clear B's lookup owner");
        Check.False(readB.Token.IsCancellationRequested, "A cleanup cannot cancel B");
        Check.Equal(notices, f.Notices.Count, "A's stale success/error cannot announce against B");
        var reads = f.Transport.Reads.Length;
        await Signals.Done(f.Vm.StepForFixture(1));
        Check.Equal(reads, f.Transport.Reads.Length, "B's own pending lookup still suppresses duplicate requests");
        newResponse.TrySetResult();
        await Signals.Done(lookupB);
        Check.False(f.Vm.HasEpisodeLookup, "B releases its own completed lookup");
        Check.Equal(notices + 1, f.Notices.Count, "current terminal lookup retains its feedback");
        planB.Result.TrySetResult(Fixture.Stopped());
        await Signals.Done(playingB);
    }
}
