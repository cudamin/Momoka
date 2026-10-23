using Momoka.Emby;
using Momoka.Playback;

namespace Momoka.NaturalContinuationCallerTests;

// A narrow controllable endpoint, NOT PlaybackService/PlayOperation/native ownership emulation.
// Admission outcomes are explicit test inputs. No planner, handle, native gate, HTTP or media.
internal sealed class FakePlayback(Tape tape)
{
    internal sealed record Call(PlaybackTicket Ticket, EmbySessionScope Scope,
        CancellationToken Lifetime, CancellationToken Preparation, CancellationToken ContinuationCancellation,
        PlaybackContinuation? ExpectedContinuation, Plan Plan)
    {
        internal long Generation { get; set; }
        internal bool Started { get; set; }
    }

    internal sealed class Plan
    {
        internal TaskCompletionSource<PlaybackResult> Result { get; } = Signals.New<PlaybackResult>();
        internal TaskCompletionSource<Call> Entered { get; } = Signals.New<Call>();
        internal TaskCompletionSource<Call> Accepted { get; } = Signals.New<Call>();
        internal Func<Call, Task>? Admission { get; set; }
        internal Action<Call>? OnAccepted { get; set; }
        internal Action<Call>? BeforeReturn { get; set; }
        internal bool PublishStarted { get; set; } = true;
        internal EmbyItem? AnnouncedItem { get; set; }
        internal static Plan Complete(PlaybackResult result)
        {
            var plan = new Plan();
            plan.Result.TrySetResult(result);
            return plan;
        }
    }

    private Call? _active;
    internal Queue<Plan> Plans { get; } = new();
    internal List<Call> Calls { get; } = [];
    internal List<Call> Starts { get; } = [];
    internal Action<PlaybackUpdate<EmbyItem?>>? NowPlaying { get; set; }
    internal Func<int, string?>? ValidateInput { get; set; }
    internal Func<Task>? StopInput { get; set; }
    internal int Validations { get; private set; }
    internal int Stops { get; private set; }
    internal bool IsPlaying { get; private set; }
    internal long Generation { get; private set; }

    internal string? Validate()
    {
        Validations++;
        tape.Add("validate:" + Validations);
        return ValidateInput?.Invoke(Validations);
    }

    // Signature mirrors the current actual Core/Shell call. Captured tokens are asserted separately.
    internal async Task<PlaybackResult> PlayAsync(PlaybackTicket ticket, EmbySessionScope? identity,
        CancellationToken lifetime, CancellationToken startupCancellation = default,
        CancellationToken continuationCancellation = default, PlaybackContinuation? expectedContinuation = null)
    {
        if (identity is null) throw new TestFailure("caller did not supply captured scope");
        if (Plans.Count == 0) throw new TestFailure("unexpected fake PlayAsync: " + ticket.Item.IdentityKey);
        var plan = Plans.Dequeue();
        var call = new Call(ticket, identity, lifetime, startupCancellation, continuationCancellation, expectedContinuation, plan);
        Calls.Add(call);
        tape.Add("play-call:" + ticket.Item.IdentityKey);
        plan.Entered.TrySetResult(call);
        // Cancellation checks are the fake endpoint's refusal, not a proof of Core admission behavior.
        lifetime.ThrowIfCancellationRequested();
        startupCancellation.ThrowIfCancellationRequested();
        continuationCancellation.ThrowIfCancellationRequested();
        if (plan.Admission is { } admission) await admission(call).ConfigureAwait(true);
        if (plan.PublishStarted)
        {
            call.Generation = ++Generation;
            call.Started = true;
            Starts.Add(call);
            _active = call;
            IsPlaying = true;
            plan.OnAccepted?.Invoke(call);
            tape.Add("play-start:" + ticket.Item.IdentityKey);
            NowPlaying?.Invoke(new PlaybackUpdate<EmbyItem?>(call.Generation, plan.AnnouncedItem ?? ticket.Item));
        }
        plan.Accepted.TrySetResult(call);
        var result = await plan.Result.Task.ConfigureAwait(true);
        plan.BeforeReturn?.Invoke(call);
        if (ReferenceEquals(_active, call))
        {
            _active = null;
            IsPlaying = false;
        }
        if (plan.PublishStarted) NowPlaying?.Invoke(new PlaybackUpdate<EmbyItem?>(call.Generation, null));
        tape.Add("play-return:" + ticket.Item.IdentityKey);
        return result;
    }

    internal Task StopAsync()
    {
        Stops++;
        tape.Add("fake-stop");
        return StopInput?.Invoke() ?? Task.CompletedTask;
    }
}
