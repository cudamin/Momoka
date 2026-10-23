using Momoka.Configuration;
using Momoka.Diagnostics;
using Momoka.Emby;

namespace Momoka.Playback;

public sealed partial class PlaybackService
{
    private sealed class RetainedPlayback
    {
        internal IPlaybackHandle Handle { get; }
        internal long Generation { get; }
        internal PlaybackContinuation Lease { get; }
        internal TaskCompletionSource Claimed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationTokenRegistration Revocation;
        internal RetainedPlayback(IPlaybackHandle handle, long generation, Func<RetainedPlayback, Task> release)
        {
            Handle = handle;
            Generation = generation;
            Lease = new PlaybackContinuation(() => release(this), handle.ContinuationClosed);
        }
    }

    // Accessed under the request lock; native handoff and disposal remain serialized by _gate.
    private RetainedPlayback? _continuation;

    private PlaybackContinuation? RetainAfterEndOfFile(EmbySessionScope scope, PlaybackTicket ticket,
        PlayOperation operation, PlayingAttempt attempt, PlaybackResult result, MpvBackendKind backend)
    {
        if (!ticket.RetainForAutomaticContinuation || !settings.Playback.AutoPlayNextEpisode
            || backend != MpvBackendKind.BuiltInLibMpv || result.Exit.Reason != PlaybackEndReason.EndOfFile
            || !scope.IsAvailable) return null;
        lock (_requestGate)
        {
            if (!ReferenceEquals(_latestRequest, operation) || operation.Lifetime.IsCancellationRequested
                || operation.Startup.IsCancellationRequested) return null;
        }
        if (!attempt.Handle.TryRetainAfterEndOfFile())
        {
            if (!attempt.Handle.ContinuationCanceled) return null;
            // EOF remains the report outcome, but a subsequent user close must still veto automatic
            // restart when it arrived before we could create a retained holder. No handle is retained.
            var canceled = new PlaybackContinuation(() => Task.CompletedTask, attempt.Handle.ContinuationClosed);
            _ = ObserveCancellationAsync(canceled.CancelAsync());
            return canceled;
        }
        RetainedPlayback retained;
        lock (_requestGate)
        {
            if (!ReferenceEquals(_latestRequest, operation) || operation.Lifetime.IsCancellationRequested
                || operation.Startup.IsCancellationRequested) return null;
            retained = new RetainedPlayback(attempt.Handle, attempt.Generation, ReleaseContinuationAsync);
            _continuation = retained;
        }
        // If revocation already happened, registration calls this while _gate is still held. It only
        // requests asynchronous release; disposal never waits inside the cancellation notification.
        retained.Revocation = scope.Lifetime.Register(() => _ = AbortContinuationAsync(retained));
        _ = MonitorContinuationClosedAsync(retained, attempt.Handle.ContinuationClosed);
        return retained.Lease;
    }

    private async Task MonitorContinuationClosedAsync(RetainedPlayback retained, Task closed)
    {
        if (await Task.WhenAny(closed, retained.Claimed.Task).ConfigureAwait(false) == closed)
            await AbortContinuationAsync(retained).ConfigureAwait(false);
    }

    private async Task AbortContinuationAsync(RetainedPlayback retained)
    {
        Task cancellation;
        lock (_requestGate)
        {
            if (!ReferenceEquals(_continuation, retained)) return;
            // Invalidate before a successor can consume the holder, with callbacks outside this lock.
            cancellation = retained.Lease.CancelAsync();
        }
        _ = ObserveCancellationAsync(cancellation);
        try { await retained.Lease.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { Log.Warn(Category, "释放已结束的接续窗口失败", error); }
    }

    // Called only after the successor has acquired _gate. Even a cancelled/incompatible successor
    // owns cleanup from here; the old caller's later Dispose must no longer act on this handle.
    private void ConsumeContinuation(IPlaybackHandle? handle, long generation)
    {
        RetainedPlayback? retained;
        lock (_requestGate)
        {
            retained = _continuation;
            if (retained is null || !ReferenceEquals(retained.Handle, handle) || retained.Generation != generation) return;
            _continuation = null;
        }
        if (retained is not null)
        {
            retained.Claimed.TrySetResult();
            retained.Revocation.Unregister();
        }
    }

    private async Task ReleaseContinuationAsync(RetainedPlayback retained)
    {
        lock (_requestGate)
        {
            if (!ReferenceEquals(_continuation, retained)) return;
        }
        // A successor can acquire the player gate before this release. When it claims the lease,
        // cancel our private semaphore wait instead of waiting for the whole next film to end.
        using var waiting = new CancellationTokenSource();
        var entered = _gate.WaitAsync(waiting.Token);
        if (await Task.WhenAny(entered, retained.Claimed.Task).ConfigureAwait(false) != entered)
            waiting.Cancel();
        try { await entered.ConfigureAwait(false); }
        catch (OperationCanceledException) when (retained.Claimed.Task.IsCompleted) { return; }
        try
        {
            lock (_requestGate)
            {
                if (!ReferenceEquals(_continuation, retained)) return;
                _continuation = null;
            }
            retained.Claimed.TrySetResult();
            retained.Revocation.Unregister();
            _ = ObserveCancellationAsync(retained.Lease.CancelAsync());
            if (!ReferenceEquals(_current, retained.Handle) || _active is not null || Generation != retained.Generation) return;
            await StopHandleAsync(retained.Handle).ConfigureAwait(false);
            await ReleaseAsync(retained.Handle).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
}
