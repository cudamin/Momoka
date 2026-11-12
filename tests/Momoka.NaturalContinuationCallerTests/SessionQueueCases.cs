using Momoka.Emby;
using Momoka.Playback;

namespace Momoka.NaturalContinuationCallerTests;

internal static class SessionQueueCases
{
    internal static void Register(CaseRunner runner, string output)
    {
        runner.Test("batch5-queue-next-before-append-and-auto-off", () => Order(output));
        runner.Test("batch5-queue-default-source-change-keeps-captured-owner", () => Source(output, false));
        runner.Test("batch5-queue-revoked-source-never-rebound", () => Source(output, true));
        runner.Test("batch5-queue-failed-head-survives-explicit-retry", () => Failure(output));
        runner.Test("batch5-queue-confirmation-before-consumption", () => Confirm(output));
        runner.Test("batch5-queue-stop-clears-and-old-finally-cannot-consume-new", () => Stop(output));
        runner.Test("batch5-queue-new-manual-viewing-clears", () => NewViewing(output));
        runner.Test("batch5-queue-existing-play-all-order-precedes-new-tail", () => ExistingOrder(output));
        runner.Test("batch5-queue-non-video-types-refused", () => Unsupported(output));
        runner.Test("batch5-queue-clear-play-all-does-not-replay-cleared-tail", () => ClearExisting(output));
        runner.Test("batch5-integrated-arrangement-search-reads-original-source-and-next-order", () => Arrange(output));
        runner.Test("batch5-integrated-stale-arrangement-refuses-new-viewing", () => StaleArrange(output));
    }

    private static EmbyItem Movie(Fixture f, string id, EmbySessionScope? scope = null)
    {
        var item = f.Episode(id, scope);
        item.Type = EmbyItemType.Movie;
        item.SeriesId = null;
        return item;
    }

    private static async Task Order(string output)
    {
        await using var f = new Fixture(output, "batch5-order");
        f.Settings.Playback.AutoPlayNextEpisode = false;
        var a = Movie(f, "A"); var b = Movie(f, "B"); var c = Movie(f, "C");
        var first = f.Plan();
        var run = f.Start(a, []);
        await Signals.Done(first.Accepted.Task);
        Check.True(f.Vm.Queue(c, next: false), "append while playing");
        Check.True(f.Vm.Queue(b, next: true), "insert next while playing");
        var second = f.Plan();
        first.Result.TrySetResult(Fixture.Eof());
        await Signals.Done(second.Accepted.Task);
        Check.Equal(b.IdentityKey, f.Vm.NowPlaying!.IdentityKey, "next has priority with auto-next disabled");
        Check.Equal(1, f.Vm.PendingQueue.Count, "only acknowledged B consumed");
        var third = f.Plan();
        second.Result.TrySetResult(Fixture.Eof());
        await Signals.Done(third.Accepted.Task);
        Check.Equal(c.IdentityKey, f.Vm.NowPlaying!.IdentityKey, "append follows next");
        Check.Equal(0, f.Vm.PendingQueue.Count, "all confirmed slots consumed");
        third.Result.TrySetResult(Fixture.Eof());
        await Signals.Done(run);
        Check.False(f.Vm.HasPendingQueue, "completed viewing clears queue");
    }

    private static async Task Source(string output, bool revoke)
    {
        await using var f = new Fixture(output, "batch5-source-" + revoke);
        var first = f.Plan();
        var run = f.Start(Movie(f, "same", f.A), []);
        await Signals.Done(first.Accepted.Task);
        Check.True(f.Vm.Queue(Movie(f, "same", f.B), next: false), "cross-source same id accepted");
        f.Default(f.C);
        if (revoke)
        {
            Fixture.Revoke(f.B);
            first.Result.TrySetResult(Fixture.Eof());
            await Signals.Done(run);
            Check.Equal(1, f.Playback.Starts.Count, "revoked queue must not start with C");
        }
        else
        {
            var second = f.Plan();
            first.Result.TrySetResult(Fixture.Eof());
            await Signals.Done(second.Accepted.Task);
            Check.Same(f.B.UserStateOwner, f.Playback.Calls[1].Scope.UserStateOwner, "captured source remains B");
            second.Result.TrySetResult(Fixture.Stopped());
            await Signals.Done(run);
        }
    }

    private static async Task Failure(string output)
    {
        await using var f = new Fixture(output, "batch5-failure");
        var first = f.Plan();
        var run = f.Vm.PlayAsync(Movie(f, "A"));
        await Signals.Done(first.Accepted.Task);
        var b = Movie(f, "B");
        Check.True(f.Vm.Queue(b, next: true), "queue B");
        var rejected = f.Plan();
        rejected.Admission = _ => throw new EndpointFailure("inert admission rejection");
        first.Result.TrySetResult(Fixture.Eof());
        await Signals.Done(run);
        Check.True(f.Vm.CanResumeQueue, "failed unconfirmed head remains retryable");
        Check.Equal(b.IdentityKey, f.Vm.PendingQueue[0].Item.IdentityKey, "B not consumed by failed start");
        var retry = f.Plan();
        var retryRun = f.Vm.ResumeQueueAsync();
        await Signals.Done(retry.Accepted.Task);
        Check.Equal(0, f.Vm.PendingQueue.Count, "only successful retry consumes B");
        retry.Result.TrySetResult(Fixture.Stopped());
        await Signals.Done(retryRun);
        Check.Equal(2, f.Playback.Starts.Count, "A and one successful B, no duplicate");
    }

    private static async Task Confirm(string output)
    {
        await using var f = new Fixture(output, "batch5-confirm");
        var first = f.Plan(); var run = f.Start(Movie(f, "A"), []);
        await Signals.Done(first.Accepted.Task);
        f.Vm.Queue(Movie(f, "B"), next: true);
        var gate = Signals.New();
        var second = f.Plan(); second.BeforeAnnounce = () => gate.Task;
        first.Result.TrySetResult(Fixture.Eof());
        await Signals.Done(second.Entered.Task);
        Check.Equal(1, f.Vm.PendingQueue.Count, "before actual NowPlaying slot stays pending");
        gate.TrySetResult();
        await Signals.Done(second.Accepted.Task);
        Check.Equal(0, f.Vm.PendingQueue.Count, "actual NowPlaying consumes exact slot");
        second.Result.TrySetResult(Fixture.Stopped()); await Signals.Done(run);
    }

    private static async Task Stop(string output)
    {
        await using var f = new Fixture(output, "batch5-stop");
        var first = f.Plan(); var run = f.Start(Movie(f, "A"), []);
        await Signals.Done(first.Accepted.Task);
        f.Vm.Queue(Movie(f, "B"), next: false);
        await Signals.Done(f.Vm.StopAsync());
        Check.Equal(0, f.Vm.PendingQueue.Count, "Stop clears pending immediately");
        first.Result.TrySetResult(Fixture.Eof()); await Signals.Done(run);
        Check.Equal(1, f.Playback.Starts.Count, "late EOF after Stop does not start B");
    }

    private static async Task NewViewing(string output)
    {
        await using var f = new Fixture(output, "batch5-new-viewing");
        var first = f.Plan(); var run = f.Start(Movie(f, "A"), []);
        await Signals.Done(first.Accepted.Task);
        f.Vm.Queue(Movie(f, "B"), next: false);
        var replacement = f.Plan();
        var replaced = f.Vm.PlayReplacingAsync(Movie(f, "C"));
        await Signals.Done(replacement.Accepted.Task);
        Check.Equal(0, f.Vm.PendingQueue.Count, "new manual C replaces old pending B");
        first.Result.TrySetResult(Fixture.Stopped()); await Signals.Done(run);
        replacement.Result.TrySetResult(Fixture.Eof()); await Signals.Done(replaced);
        Check.Equal(2, f.Playback.Starts.Count, "A and C only");
    }

    private static async Task ExistingOrder(string output)
    {
        await using var f = new Fixture(output, "batch5-existing-order");
        var a = Movie(f, "A"); var b = Movie(f, "B"); var c = Movie(f, "C");
        var first = f.Plan(); var run = f.Start(a, new UserPlaybackQueue([a, b, b]));
        await Signals.Done(first.Accepted.Task);
        Check.Equal(2, f.Vm.PendingQueue.Count, "duplicate play-all slots preserved");
        f.Vm.Queue(c, next: false);
        Check.Equal(b.IdentityKey, f.Vm.PendingQueue[0].Item.IdentityKey, "new tail follows existing queue");
        var second = f.Plan(); first.Result.TrySetResult(Fixture.Eof());
        await Signals.Done(second.Accepted.Task);
        var third = f.Plan(); second.Result.TrySetResult(Fixture.Eof());
        await Signals.Done(third.Accepted.Task);
        Check.Equal(b.IdentityKey, f.Vm.NowPlaying!.IdentityKey, "second duplicate slot plays once");
        var fourth = f.Plan(); third.Result.TrySetResult(Fixture.Eof());
        await Signals.Done(fourth.Accepted.Task);
        Check.Equal(c.IdentityKey, f.Vm.NowPlaying!.IdentityKey, "new tail follows duplicate slots");
        fourth.Result.TrySetResult(Fixture.Stopped()); await Signals.Done(run);
        Check.Equal(4, f.Playback.Starts.Count, "A B B C confirmed individually");
    }

    private static async Task Unsupported(string output)
    {
        await using var f = new Fixture(output, "batch5-type-guard");
        var first = f.Plan(); var run = f.Start(Movie(f, "A"), []);
        await Signals.Done(first.Accepted.Task);
        foreach (var type in new[] { EmbyItemType.Audio, EmbyItemType.MusicVideo, EmbyItemType.Series, EmbyItemType.Season, EmbyItemType.Playlist })
        {
            var item = Movie(f, type); item.Type = type;
            Check.False(f.Vm.CanQueue(item), "hide unavailable queue action " + type);
            Check.False(f.Vm.Queue(item, next: false), "command refuses " + type);
        }
        first.Result.TrySetResult(Fixture.Stopped()); await Signals.Done(run);
    }

    private static async Task ClearExisting(string output)
    {
        await using var f = new Fixture(output, "batch5-clear-existing");
        var a = Movie(f, "A"); var b = Movie(f, "B");
        var first = f.Plan(); var run = f.Start(a, new UserPlaybackQueue([a, b]));
        await Signals.Done(first.Accepted.Task);
        Check.Equal(1, f.Vm.PendingQueue.Count, "initial pending B");
        f.Vm.ClearQueue();
        Check.Equal(0, f.Vm.PendingQueue.Count, "pending B cleared");
        Check.Equal(1, f.Vm.Episodes.Count, "navigation fallback cannot resurrect cleared B");
        first.Result.TrySetResult(Fixture.Eof());
        await Signals.Done(run);
        Check.Equal(1, f.Playback.Starts.Count, "only A, despite automatic continuation enabled");
    }

    private static async Task Arrange(string output)
    {
        await using var f = new Fixture(output, "batch5-arrange");
        var first = f.Plan(); var run = f.Start(Movie(f, "A"), []);
        await Signals.Done(first.Accepted.Task);
        var arrangement = f.Vm.CaptureQueueArrangement();
        Check.True(arrangement is not null, "playing integrated/standalone shares arrangement capability");
        var requested = false;
        f.Vm.QueueRequested += next => requested = next;
        f.Vm.RequestQueue(true);
        Check.True(requested, "native player menu dispatch can request original scope dialog");
        var b = Movie(f, "B"); var c = Movie(f, "C");
        f.Transport.Search(f.A, [b, c]);
        f.Default(f.B);
        var page = await f.Vm.SearchQueueAsync(arrangement!, "fixture", 0, CancellationToken.None);
        Check.Equal(2, page.Items.Count, "read-only picker result");
        Check.True(page.Items.All(item => ReferenceEquals(item.Source?.UserStateOwner, f.A.UserStateOwner)), "default B cannot own A results");
        Check.True(f.Vm.ArrangeQueue(arrangement!, page.Items, true), "confirm multi-selected next rows");
        Check.Equal(b.IdentityKey, f.Vm.PendingQueue[0].Item.IdentityKey, "reverse insert preserves chosen order B then C");
        Check.Equal(c.IdentityKey, f.Vm.PendingQueue[1].Item.IdentityKey, "C remains second");
        f.Vm.ClearQueue(); first.Result.TrySetResult(Fixture.Stopped()); await Signals.Done(run);
    }

    private static async Task StaleArrange(string output)
    {
        await using var f = new Fixture(output, "batch5-arrange-stale");
        var first = f.Plan(); var run = f.Start(Movie(f, "A"), []);
        await Signals.Done(first.Accepted.Task);
        var old = f.Vm.CaptureQueueArrangement()!;
        var second = f.Plan(); var replaced = f.Vm.PlayReplacingAsync(Movie(f, "C"));
        await Signals.Done(second.Accepted.Task);
        Check.False(f.Vm.IsCurrentQueueArrangement(old), "same source new file invalidates old picker");
        Check.False(f.Vm.ArrangeQueue(old, [Movie(f, "B")], true), "old picker cannot queue in C viewing");
        await Check.Throws<OperationCanceledException>(() => f.Vm.SearchQueueAsync(old, "fixture", 0, CancellationToken.None));
        Check.Equal(0, f.Transport.Reads.Length, "stale picker does not request metadata");
        first.Result.TrySetResult(Fixture.Stopped()); await Signals.Done(run);
        second.Result.TrySetResult(Fixture.Stopped()); await Signals.Done(replaced);
    }
}
