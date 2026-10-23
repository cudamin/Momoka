using Momoka.Emby;
using Momoka.Playback;

namespace Momoka.NaturalContinuationCallerTests;

internal static class CallerCases
{
    internal static void Register(CaseRunner runner, string output)
    {
        foreach (var mode in new[] { "last", "empty", "missing-current", "other-season", "auto-off", "auto-off-late", "movie", "user-quit", "stopped", "error" })
            runner.Test("terminal-" + mode, () => Terminal(output, mode));
        foreach (var mode in new[] { "final-notice", "refresh", "next-notice", "following-capture", "release", "leave" })
            runner.Test("exception-" + mode, () => CallbackFailure(output, mode));
        foreach (var mode in new[] { "validate", "not-playable", "no-sources", "metadata", "no-episode" })
            runner.Test("successor-refused-" + mode, () => Refused(output, mode));
        foreach (var mode in new[] { "scope-before-capture", "lease-at-refresh", "lease-at-next-notice", "preparation-in-cover", "intent-at-cover", "stop-at-next-notice", "lifetime-at-refresh", "lease-during-metadata", "scope-during-metadata", "stop-during-metadata", "preparation-during-metadata" })
            runner.Test("cancel-" + mode, () => Cancel(output, mode));
        runner.Test("expected-context-closed-without-cancel-callback", () => ContextClosed(output));
        runner.Test("consumed-old-dispose-finishes-while-B-pending", () => Consumed(output));
        runner.Test("three-recursive-finally-inner-to-outer", () => Recursive(output));
        foreach (var mode in new[] { "refresh-B", "notice-B", "release-B" })
            runner.Test("recursive-exception-" + mode, () => RecursiveFailure(output, mode));
        runner.Test("source-capture-and-same-id-cross-source", () => SourceCapture(output));
        runner.Test("following-and-siblings-captured-before-refresh", () => CapturedDestination(output));
        runner.Test("old-start-result-does-not-leave-live-B", () => OldResult(output));
        runner.Test("old-intent-retains-original-feedback-no-successor", () => OldIntent(output));
        runner.Test("same-id-copy-does-not-adopt-pending-reference", () => SameIdCopy(output));
        runner.Test("same-id-list-copy-is-navigation-identity", () => SameIdList(output));
        runner.Test("recursive-series-resolution-no-window", () => SeriesResolution(output));
        runner.Test("null-continuation-preserves-auto-next", () => NullLease(output));
        runner.Test("public-Play-catches-successor-failure-after-dispose", () => PublicCatch(output));
        runner.Test("lease-dispose-idempotent-and-release-awaited", () => AwaitedRelease(output));
        foreach (var clone in new[] { false, true })
            runner.Test("failed-replace-original-reference-" + clone, () => FailedReplace(output, clone));
    }

    private static async Task Terminal(string output, string mode)
    {
        await using var f = new Fixture(output, "terminal-" + mode);
        var a = f.Episode("A");
        var b = f.Episode("B", season: mode == "other-season" ? "other" : "season");
        var lease = f.Lease("A");
        IReadOnlyList<EmbyItem> rows = mode switch { "empty" => [], "missing-current" => [b], "other-season" => [a, b], _ => [a] };
        if (mode == "auto-off") { f.Settings.Playback.AutoPlayNextEpisode = false; rows = [a, b]; }
        if (mode == "empty") f.Transport.Episodes(f.A, "series", []);
        if (mode == "movie") a.Type = EmbyItemType.Movie;
        var reason = mode switch { "user-quit" => PlaybackEndReason.UserQuit, "stopped" => PlaybackEndReason.Stopped, "error" => PlaybackEndReason.Error, _ => PlaybackEndReason.EndOfFile };
        var plan = f.Plan(new PlaybackResult(new PlaybackExit(reason, 1, reason == PlaybackEndReason.Error ? 1 : 0, "anonymous"), 1, false, false)
        { Continuation = lease.Lease, HasStarted = true });
        if (mode == "auto-off-late")
        {
            rows = [a, b];
            plan.BeforeReturn = _ => f.Settings.Playback.AutoPlayNextEpisode = false;
        }
        await Signals.Done(f.Start(a, rows)).ConfigureAwait(true);
        f.Settled(lease);
        Check.Equal(1, f.Notices.Count, "one terminal feedback");
        Check.Equal(1, f.Refreshes, "one terminal refresh");
        Check.Equal(mode != "movie", f.Playback.Calls[0].Ticket.RetainForAutomaticContinuation, "real ticket retain is episode-only");
    }

    private static async Task CallbackFailure(string output, string mode)
    {
        await using var f = new Fixture(output, "exception-" + mode);
        var a = f.Episode("A");
        var b = f.Episode("B", mode == "following-capture" ? f.B : f.A);
        var lease = f.Lease("A");
        var next = mode is "next-notice" or "following-capture";
        if (mode == "final-notice") f.NoticeInput = _ => throw new EndpointFailure(mode);
        if (mode == "refresh") f.RefreshInput = () => throw new EndpointFailure(mode);
        if (mode == "next-notice") f.NoticeInput = text => { if (text.StartsWith("自动播放", StringComparison.Ordinal)) throw new EndpointFailure(mode); };
        if (mode == "following-capture") f.RefreshInput = () => Fixture.Revoke(f.B);
        if (mode == "release") lease.ReleaseFailure = new EndpointFailure(mode);
        if (mode == "leave") f.LeaveInput = () => throw new EndpointFailure(mode);
        f.Plan(Fixture.Eof(lease));
        var run = f.Start(a, next ? [a, b] : [a]);
        if (mode == "following-capture") await Check.Throws<OperationCanceledException>(() => Signals.Done(run)).ConfigureAwait(true);
        else await Check.Throws<EndpointFailure>(() => Signals.Done(run)).ConfigureAwait(true);
        Check.Equal(1, lease.ReleaseCalls, "every callback exception releases actual result lease once");
        Check.Equal(0, f.Vm.Hold, "callback exceptions still drain player hold");
        Check.True(f.Vm.Idle.IsCompletedSuccessfully, "nested finally sets idle even if Leave callback throws");
        Check.Equal(1, f.Playback.Starts.Count, "callback exception does not start B");
        if (mode != "release") f.Settled(lease);
    }

    private static async Task Refused(string output, string mode)
    {
        await using var f = new Fixture(output, "successor-refused-" + mode);
        var a = f.Episode("A");
        var b = f.Episode("B", media: mode != "no-sources" && mode != "metadata");
        if (mode == "not-playable") b.Type = EmbyItemType.Folder;
        if (mode == "no-episode") b.Type = EmbyItemType.Series;
        var lease = f.Lease("A");
        f.Plan(Fixture.Eof(lease));
        if (mode == "validate") f.Playback.ValidateInput = count => count == 2 ? "anonymous validation refusal" : null;
        if (mode == "no-sources") f.Transport.Item(f.A, b);
        if (mode == "metadata") f.Transport.Item(f.A, b.Id, _ => throw new EndpointFailure("metadata"));
        if (mode == "no-episode")
        {
            f.Transport.Seasons(f.A, b.Id, []);
            f.Transport.Episodes(f.A, b.Id, []);
        }
        var run = f.Start(a, [a, b]);
        if (mode == "metadata") await Check.Throws<EndpointFailure>(() => Signals.Done(run)).ConfigureAwait(true);
        else await Signals.Done(run).ConfigureAwait(true);
        f.Settled(lease);
        Check.Equal(1, f.Playback.Calls.Count, "failure before PlayAsync never calls service for B");
    }

    private static async Task Cancel(string output, string mode)
    {
        await using var f = new Fixture(output, "cancel-" + mode);
        var a = f.Episode("A");
        var pendingMetadata = mode.Contains("during-metadata", StringComparison.Ordinal);
        var b = f.Episode("B", media: !pendingMetadata);
        var lease = f.Lease("A");
        f.Plan(Fixture.Eof(lease));
        Task cancel = Task.CompletedTask;
        if (mode == "scope-before-capture") f.RefreshInput = () => Fixture.Revoke(f.A);
        if (mode == "lease-at-refresh") f.RefreshInput = () => cancel = lease.Cancel();
        if (mode == "lifetime-at-refresh") f.RefreshInput = f.Vm.CancelLifetimeForFixture;
        if (mode is "lease-at-next-notice" or "stop-at-next-notice")
            f.NoticeInput = text =>
            {
                if (!text.StartsWith("自动播放", StringComparison.Ordinal)) return;
                cancel = mode == "lease-at-next-notice" ? lease.Cancel() : f.Vm.StopAsync();
            };
        if (mode is "preparation-in-cover" or "intent-at-cover")
            f.Vm.CoverWait = item =>
            {
                if (ReferenceEquals(item, b))
                {
                    if (mode == "preparation-in-cover") f.Vm.CancelPreparationForFixture();
                    else f.Vm.NewIntentForFixture();
                }
                return Task.CompletedTask;
            };
        var metadata = Signals.New<object>();
        var entered = Signals.New<AnonymousTransport.Read>();
        if (pendingMetadata) f.Transport.Item(f.A, b.Id, async read =>
        {
            entered.TrySetResult(read);
            return await metadata.Task.WaitAsync(read.Token).ConfigureAwait(false);
        });
        var run = f.Start(a, [a, b]);
        if (pendingMetadata)
        {
            var read = await Signals.Done(entered.Task).ConfigureAwait(true);
            Check.Equal(2, f.Vm.Hold, "parent lease remains held while child metadata suspended");
            Check.False(f.Vm.Idle.IsCompleted, "no idle during successor metadata");
            Check.Equal(0, lease.ReleaseCalls, "caller still owns original lease while preparing B");
            cancel = mode switch
            {
                "lease-during-metadata" => lease.Cancel(),
                "stop-during-metadata" => f.Vm.StopAsync(),
                _ => Task.CompletedTask
            };
            if (mode == "scope-during-metadata") Fixture.Revoke(f.A);
            if (mode == "preparation-during-metadata") f.Vm.CancelPreparationForFixture();
            await Signals.Done(cancel).ConfigureAwait(true);
            Check.True(read.Token.IsCancellationRequested, "actual captured metadata token canceled");
        }
        if (mode is "intent-at-cover" or "stop-at-next-notice")
            await Signals.Done(run).ConfigureAwait(true);
        else await Check.Throws<OperationCanceledException>(() => Signals.Done(run)).ConfigureAwait(true);
        await Signals.Done(cancel).ConfigureAwait(true);
        f.Settled(lease, leaves: mode == "lifetime-at-refresh" ? 0 : 1);
        Check.Equal(1, f.Playback.Calls.Count, "canceled successor never calls service");
        metadata.TrySetResult(b);
    }

    private static async Task ContextClosed(string output)
    {
        await using var f = new Fixture(output, "expected-context-closed-without-cancel-callback");
        var a = f.Episode("A");
        var b = f.Episode("B");
        var lease = f.Lease("A");
        f.Plan(Fixture.Eof(lease));
        var child = f.Plan(Fixture.Stopped());
        f.RefreshInput = () => lease.Closed.TrySetResult();
        child.Admission = call =>
        {
            Check.Same(lease.Lease, call.ExpectedContinuation, "actual Start passes the ended lease as sixth admission argument");
            Check.Equal(lease.Lease.Lifetime, call.ContinuationCancellation, "independent continuation token forwarded");
            Check.True(LeaseProbe.ContextClosed(call.ExpectedContinuation!).IsCompleted, "Q context completion visible before async callbacks");
            Check.False(call.ContinuationCancellation.IsCancellationRequested, "this case intentionally delays cancel callbacks");
            throw new OperationCanceledException("controlled fake Core admission refuses already-closed context");
        };
        await Check.Throws<OperationCanceledException>(() => Signals.Done(f.Start(a, [a, b]))).ConfigureAwait(true);
        f.Settled(lease);
        Check.Equal(2, f.Playback.Calls.Count, "B reaches fake admission fence, not a fake start");
    }

    private static async Task Consumed(string output)
    {
        await using var f = new Fixture(output, "consumed-old-dispose-finishes-while-B-pending");
        var a = f.Episode("A");
        var b = f.Episode("B");
        var old = f.Lease("A");
        var last = f.Lease("B");
        f.Plan(Fixture.Eof(old));
        var child = f.Plan();
        child.OnAccepted = _ => old.Consumed = true;
        var run = f.Start(a, [a, b]);
        var call = await Signals.Done(child.Accepted.Task).ConfigureAwait(true);
        Check.Same(old.Lease, call.ExpectedContinuation, "exact expected lease reaches successor");
        Check.Equal(old.Lease.Lifetime, call.ContinuationCancellation, "exact continuation token reaches successor");
        Check.Equal(f.Vm.Lifetime, call.Lifetime, "running lifetime is VM lifetime, not parent's preparation");
        Check.False(call.Preparation.IsCancellationRequested, "successor has live linked preparation");
        await Signals.Done(old.Lease.DisposeAsync().AsTask()).ConfigureAwait(true);
        Check.Equal(1, old.ReleaseCalls, "old explicit dispose invokes real Lazy release once");
        Check.Equal(0, old.CleanupCalls, "consumed fake input does not clean a successor");
        Check.False(run.IsCompleted, "actual caller still awaits suspended B");
        Check.True(f.Playback.IsPlaying, "explicit old Dispose did not terminate B");
        Check.False(call.Lifetime.IsCancellationRequested, "B lifetime remains live");
        Check.False(call.ContinuationCancellation.IsCancellationRequested, "old release does not mark B continuation token");
        Check.Equal(0, f.Leaves, "no LeavePlayer while B plays");
        child.Result.TrySetResult(Fixture.Eof(last));
        await Signals.Done(run).ConfigureAwait(true);
        f.Settled(old, 2);
        Check.Equal(1, last.ReleaseCalls, "B terminal lease released exactly once");
        Check.Equal(2, f.Tape.One("lease-release-start:B").Hold, "B release precedes B's own hold decrement");
    }

    private static async Task Recursive(string output)
    {
        await using var f = new Fixture(output, "three-recursive-finally-inner-to-outer");
        var rows = new[] { f.Episode("A"), f.Episode("B"), f.Episode("C") };
        var leases = new[] { f.Lease("A"), f.Lease("B"), f.Lease("C") };
        for (var i = 0; i < rows.Length; i++)
        {
            var index = i;
            var plan = f.Plan(Fixture.Eof(leases[i]));
            if (i > 0) plan.OnAccepted = _ => leases[index - 1].Consumed = true;
        }
        await Signals.Done(f.Start(rows[0], rows)).ConfigureAwait(true);
        f.Settled(leases[0], 3);
        var release = f.Tape.Snapshot().Where(e => e.Event.StartsWith("lease-release-start:", StringComparison.Ordinal)).ToArray();
        Check.Equal("lease-release-start:C", release[0].Event, "inner-most lease first");
        Check.Equal(3, release[0].Hold, "C is disposed before any recursive holds drain");
        Check.Equal("lease-release-start:B", release[1].Event, "B next");
        Check.Equal(2, release[1].Hold, "B own hold is still present during release");
        Check.Equal("lease-release-start:A", release[2].Event, "A last");
        Check.Equal(1, release[2].Hold, "A hold protects final release");
        Check.Equal(3, f.Refreshes, "original one-refresh-per-newest-recursion behavior");
        for (var i = 1; i < rows.Length; i++)
            Check.Same(leases[i - 1].Lease, f.Playback.Calls[i].ExpectedContinuation, "recursive fence preserved");
    }

    private static async Task RecursiveFailure(string output, string mode)
    {
        await using var f = new Fixture(output, "recursive-exception-" + mode);
        var a = f.Episode("A");
        var b = f.Episode("B");
        var old = f.Lease("A");
        var last = f.Lease("B");
        f.Plan(Fixture.Eof(old));
        var child = f.Plan(Fixture.Eof(last));
        child.OnAccepted = _ => old.Consumed = true;
        if (mode == "refresh-B") f.RefreshInput = () => { if (f.Refreshes == 2) throw new EndpointFailure(mode); };
        if (mode == "notice-B") f.NoticeInput = text => { if (text.StartsWith("播放完毕", StringComparison.Ordinal)) throw new EndpointFailure(mode); };
        if (mode == "release-B") last.ReleaseFailure = new EndpointFailure(mode);
        await Check.Throws<EndpointFailure>(() => Signals.Done(f.Start(a, [a, b]))).ConfigureAwait(true);
        f.Settled(old, 2);
        Check.Equal(1, last.ReleaseCalls, "B exception still releases B lease exactly once");
        Check.Equal(2, f.Tape.One("lease-release-start:B").Hold, "B cleanup precedes its finally");
        Check.Equal(1, f.Tape.One("lease-release-start:A").Hold, "A lease disposes after B exception unwinds child hold");
    }
    private static async Task SourceCapture(string output)
    {
        await using var f = new Fixture(output, "source-capture-and-same-id-cross-source");
        var a = f.Episode("same", f.A);
        var b = f.Episode("same", f.B);
        b.SeriesId = null;
        var lease = f.Lease("A");
        f.Plan(Fixture.Eof(lease));
        var next = f.Plan(Fixture.Stopped());
        next.OnAccepted = _ => lease.Consumed = true;
        f.RefreshInput = () => f.Default(f.C);
        await Signals.Done(f.Start(a, [a, b])).ConfigureAwait(true);
        f.Settled(lease, 2);
        var call = f.Playback.Calls[1];
        Check.Same(f.B, call.Scope, "following Capture uses bound source, not new default C");
        Check.Same(b, call.Ticket.Item, "same IDs on different source do not alias");
        Check.True(call.Ticket.Parent is null, "A parent not borrowed across sources");
        Check.False(f.A.Connection.IsSameIdentityAs(f.B.Connection), "anonymous sources really differ");
    }

    private static async Task CapturedDestination(string output)
    {
        await using var f = new Fixture(output, "following-and-siblings-captured-before-refresh");
        var a = f.Episode("A");
        var b = f.Episode("B");
        var lease = f.Lease("A");
        f.Plan(Fixture.Eof(lease));
        var child = f.Plan();
        child.OnAccepted = _ => lease.Consumed = true;
        f.RefreshInput = () => f.Vm.ChangeEpisodesForFixture([f.Episode("unrelated")]);
        var run = f.Start(a, [a, b]);
        await Signals.Done(child.Accepted.Task).ConfigureAwait(true);
        Check.Same(b, f.Vm.NowPlaying, "local following snapshot survives refresh mutation");
        Check.Equal(2, f.Vm.Episodes.Count, "captured siblings, not mutated live collection, adopted");
        Check.Same(a, f.Vm.Episodes[0], "captured sibling A reference preserved");
        Check.Same(b, f.Vm.Episodes[1], "captured sibling B reference preserved");
        child.Result.TrySetResult(Fixture.Stopped());
        await Signals.Done(run).ConfigureAwait(true);
        f.Settled(lease, 2);
    }

    private static async Task OldResult(string output)
    {
        await using var f = new Fixture(output, "old-start-result-does-not-leave-live-B");
        var a = f.Episode("A");
        var b = f.Episode("B");
        var old = f.Lease("A");
        var last = f.Lease("B");
        var first = f.Plan();
        var child = f.Plan();
        child.OnAccepted = _ => old.Consumed = true;
        var runA = f.Start(a, [a, b]);
        await Signals.Done(first.Accepted.Task).ConfigureAwait(true);
        var runB = f.Start(b, [a, b], replace: true);
        await Signals.Done(child.Accepted.Task).ConfigureAwait(true);
        first.Result.TrySetResult(Fixture.Eof(old));
        await Signals.Done(runA).ConfigureAwait(true);
        Check.Equal(1, old.ReleaseCalls, "stale caller still disposes its result");
        Check.Equal(0, f.Notices.Count, "old attempt does not show misleading stop/auto toast");
        Check.Equal(0, f.Refreshes, "old attempt does not refresh B's browsing page");
        Check.Equal(0, f.Leaves, "old attempt never leaves B");
        Check.Equal(1, f.Vm.Hold, "B owns remaining hold");
        Check.False(f.Vm.Idle.IsCompleted, "idle stays pending until B finishes");
        Check.True(f.Vm.HasPreparation, "old finally cannot clear the newer preparation owner");
        Check.False(f.Playback.Calls[1].Preparation.IsCancellationRequested, "old finally does not cancel B preparation");
        Check.False(f.Playback.Calls[1].Lifetime.IsCancellationRequested, "old finally does not cancel B lifetime");
        Check.Same(b, f.Vm.NowPlaying, "stale null NowPlaying update is rejected");
        child.Result.TrySetResult(Fixture.Stopped(last));
        await Signals.Done(runB).ConfigureAwait(true);
        f.Settled(old, 2);
    }

    private static async Task OldIntent(string output)
    {
        await using var f = new Fixture(output, "old-intent-retains-original-feedback-no-successor");
        var a = f.Episode("A");
        var b = f.Episode("B");
        var lease = f.Lease("A");
        var first = f.Plan(Fixture.Eof(lease));
        first.BeforeReturn = _ => f.Vm.InvalidateIntentForFixture();
        await Signals.Done(f.Start(a, [a, b])).ConfigureAwait(true);
        f.Settled(lease);
        Check.Equal(1, f.Refreshes, "newest attempt original refresh stays even with old intent");
        Check.Equal(1, f.Notices.Count, "newest old-intent terminal feedback remains original");
    }

    private static async Task SameIdCopy(string output)
    {
        await using var f = new Fixture(output, "same-id-copy-does-not-adopt-pending-reference");
        var a = f.Episode("same");
        var copy = f.Episode("same");
        copy.SeriesId = null;
        var b = f.Episode("B");
        var lease = f.Lease("A");
        var plan = f.Plan();
        plan.AnnouncedItem = copy;
        var run = f.Start(a, [a, b]);
        await Signals.Done(plan.Accepted.Task).ConfigureAwait(true);
        Check.Same(copy, f.Vm.NowPlaying, "actual event uses supplied DTO");
        Check.Equal(0, f.Vm.Episodes.Count, "same ID but wrong reference cannot adopt pending siblings");
        Check.Equal(0L, f.Vm.ActiveIntent, "same-ID copy cannot adopt pending start intent");
        plan.Result.TrySetResult(Fixture.Eof(lease));
        await Signals.Done(run).ConfigureAwait(true);
        f.Settled(lease);
    }

    private static async Task SameIdList(string output)
    {
        await using var f = new Fixture(output, "same-id-list-copy-is-navigation-identity");
        var a = f.Episode("A");
        var copy = f.Episode("A");
        var b = f.Episode("B");
        var lease = f.Lease("A");
        f.Plan(Fixture.Eof(lease));
        var plan = f.Plan(Fixture.Stopped());
        plan.OnAccepted = _ => lease.Consumed = true;
        await Signals.Done(f.Start(a, [copy, b])).ConfigureAwait(true);
        f.Settled(lease, 2);
        Check.Same(b, f.Playback.Calls[1].Ticket.Item, "next navigation matches identity not DTO reference");
    }

    private static async Task SeriesResolution(string output)
    {
        await using var f = new Fixture(output, "recursive-series-resolution-no-window");
        var series = f.Series();
        var a = f.Episode("A");
        var lease = f.Lease("A");
        f.Transport.Seasons(f.A, series.Id, []);
        f.Transport.Episodes(f.A, series.Id, [a]);
        f.Plan(Fixture.Eof(lease));
        await Signals.Done(f.Start(series)).ConfigureAwait(true);
        f.Settled(lease);
        Check.Equal(2, f.Tape.One("lease-release-start:A").Hold, "container recursion holds both entries until lease releases");
        Check.True(f.Playback.Calls[0].Ticket.RetainForAutomaticContinuation, "resolved episode, not series, determines retain ticket");
    }

    private static async Task NullLease(string output)
    {
        await using var f = new Fixture(output, "null-continuation-preserves-auto-next");
        var a = f.Episode("A");
        var b = f.Episode("B");
        f.Plan(Fixture.Eof());
        f.Plan(Fixture.Stopped());
        await Signals.Done(f.Start(a, [a, b])).ConfigureAwait(true);
        Check.Equal(2, f.Playback.Starts.Count, "non-retaining service result still auto plays");
        Check.False(f.Playback.Calls[1].ContinuationCancellation.CanBeCanceled, "null lease sends default cancellation");
        Check.True(f.Playback.Calls[1].ExpectedContinuation is null, "null lease sends null fence");
        Check.Equal(0, f.Vm.Hold, "both normal recursive finally paths complete");
        Check.Equal(1, f.Leaves, "null lease leaves only once");
    }

    private static async Task PublicCatch(string output)
    {
        await using var f = new Fixture(output, "public-Play-catches-successor-failure-after-dispose");
        var a = f.Episode("A");
        var b = f.Episode("B", media: false);
        var lease = f.Lease("A");
        f.Plan(Fixture.Eof(lease));
        f.Transport.Item(f.A, b.Id, _ => throw new EndpointFailure("anonymous metadata"));
        await Signals.Done(f.ViaPublicPlay(a, [a, b])).ConfigureAwait(true);
        Check.Equal(1, lease.ReleaseCalls, "public catch cannot bypass inner lease disposal");
        Check.True(f.Notices.Last().StartsWith("播放失败", StringComparison.Ordinal), "original public wrapper reports caught failure");
        // The existing public catch also calls LeavePlayer again when hold==0. Record, do not repair.
        Check.Equal(2, f.Leaves, "preserve original second wrapper Leave, not a copied fixed state machine");
        Check.Equal(0, f.Vm.Hold, "public catch sees drained holds");
        Check.True(f.Vm.Idle.IsCompletedSuccessfully, "public catch sees completed actual idle");
    }

    private static async Task AwaitedRelease(string output)
    {
        await using var f = new Fixture(output, "lease-dispose-idempotent-and-release-awaited");
        var a = f.Episode("A");
        var lease = f.Lease("A");
        lease.ReleaseGate = f.Gate();
        f.Plan(Fixture.Eof(lease));
        var run = f.Start(a, [a]);
        await Signals.Done(lease.ReleaseEntered.Task).ConfigureAwait(true);
        var again = lease.Lease.DisposeAsync().AsTask();
        Check.Equal(1, lease.ReleaseCalls, "actual Lazy release factory idempotent while suspended");
        Check.False(run.IsCompleted, "StartPlayback awaits actual Dispose before final hold");
        Check.False(again.IsCompleted, "second Dispose awaits the same release receipt");
        Check.Equal(1, f.Vm.Hold, "hold retained while release pending");
        Check.False(f.Vm.Idle.IsCompleted, "idle not early");
        Check.Equal(0, f.Leaves, "no Leave before cleanup receipt");
        lease.ReleaseGate.TrySetResult();
        await Signals.Done(run).ConfigureAwait(true);
        await Signals.Done(again).ConfigureAwait(true);
        f.Settled(lease);
    }

    private static async Task FailedReplace(string output, bool clone)
    {
        await using var f = new Fixture(output, "failed-replace-original-reference-" + clone);
        var a = f.Episode("same");
        var b = f.Episode("new", media: false);
        var lease = f.Lease("A");
        var plan = f.Plan();
        var run = f.Start(a, [a]);
        await Signals.Done(plan.Accepted.Task).ConfigureAwait(true);
        var oldIntent = f.Vm.ActiveIntent;
        f.Transport.Item(f.A, b.Id, _ => throw new EndpointFailure("failed replacement"));
        if (clone) f.Vm.CoverWait = item =>
        {
            if (ReferenceEquals(item, b))
            {
                var copy = f.Episode("same");
                copy.SeriesId = null;
                f.Vm.NotifyForFixture(new PlaybackUpdate<EmbyItem?>(f.Playback.Generation, copy));
            }
            return Task.CompletedTask;
        };
        await Check.Throws<EndpointFailure>(() => Signals.Done(f.Start(b, replace: true))).ConfigureAwait(true);
        Check.Equal(1, f.Vm.Hold, "failed replacement only releases its own hold");
        Check.Equal(0, f.Leaves, "original live file stays presented");
        Check.True(f.Playback.IsPlaying, "original active fake file remains");
        if (clone) Check.Equal(oldIntent, f.Vm.ActiveIntent, "same ID other reference is not restored as previousItem");
        else
        {
            Check.True(f.Vm.ActiveIntent > oldIntent, "same original reference receives current intent after pre-stop failure");
            Check.False(f.Vm.CoverVisible, "original reference failure restores cover");
        }
        plan.Result.TrySetResult(Fixture.Eof(lease));
        await Signals.Done(run).ConfigureAwait(true);
        f.Settled(lease);
    }
}
