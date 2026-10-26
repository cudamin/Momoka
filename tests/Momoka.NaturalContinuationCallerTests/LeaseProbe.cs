using System.Reflection;
using Momoka.Playback;

namespace Momoka.NaturalContinuationCallerTests;

// Real Core lease with counted managed callbacks. Consumed is an explicit endpoint input, not
// reconstructed service/native state. Its receipts cannot prove native handoff or gate correctness.
internal sealed class LeaseProbe
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Tape _tape;
    internal string Name { get; }
    internal PlaybackContinuation Lease { get; }
    internal TaskCompletionSource Closed { get; } = Signals.New();
    internal TaskCompletionSource ReleaseEntered { get; } = Signals.New();
    internal TaskCompletionSource? ReleaseGate { get; set; }
    internal Exception? ReleaseFailure { get; set; }
    internal int ReleaseCalls { get; private set; }
    internal int CleanupCalls { get; private set; }
    internal bool Consumed { get; set; }

    internal LeaseProbe(string name, Tape tape, bool closed = false)
    {
        Name = name;
        _tape = tape;
        if (closed) Closed.TrySetResult();
        var ctor = typeof(PlaybackContinuation).GetConstructor(Members, null, [typeof(Func<Task>), typeof(Task)], null)
            ?? throw new TestFailure("Current actual lease constructor was not found");
        Lease = (PlaybackContinuation)ctor.Invoke([new Func<Task>(Release), Closed.Task]);
        Check.Same(Closed.Task, ContextClosed(Lease), "actual lease retains supplied ContextClosed task");
    }

    internal Task Cancel()
    {
        var method = typeof(PlaybackContinuation).GetMethod("CancelAsync", Members)
            ?? throw new TestFailure("Current actual lease CancelAsync was not found");
        return (Task)(method.Invoke(Lease, null) ?? throw new TestFailure("actual lease returned no cancel task"));
    }

    internal static Task ContextClosed(PlaybackContinuation lease) =>
        (Task)(typeof(PlaybackContinuation).GetProperty("ContextClosed", Members)?.GetValue(lease)
            ?? throw new TestFailure("Current actual lease ContextClosed was not found"));

    private async Task Release()
    {
        ReleaseCalls++;
        _tape.Add("lease-release-start:" + Name);
        ReleaseEntered.TrySetResult();
        if (!Consumed)
        {
            CleanupCalls++;
            if (ReleaseGate is { } gate) await gate.Task.ConfigureAwait(true);
        }
        if (ReleaseFailure is { } failure) throw failure;
        _tape.Add("lease-release-end:" + Name);
    }
}
