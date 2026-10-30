using System.Collections.Concurrent;

namespace Momoka.Infrastructure;

/// <summary>
/// One running task per key, shared by everyone who asks for the same thing while it is in flight, and
/// owned by none of them.
/// <para>
/// The poster store collapses duplicate downloads this way, and the reason it is a class of its own is
/// the bug that made it worth stating once: the shared task used to be started with the *first* caller's
/// cancellation token. A card that scrolled out of view therefore cancelled the download the card beside
/// it was still waiting on; that one got an empty answer back, read it as 「服务器没有这张图」 and
/// remembered it — a permanently blank cover until the page was left and reopened.
/// </para>
/// <para>
/// Work belongs to no individual caller. The original parameterless mode still finishes abandoned
/// non-cancellable factories; the explicit-budget mode tracks aggregate demand and cancels only when
/// its last waiter leaves. The image store opts into this bounded, demand-owned mode.
/// </para>
/// </summary>
public sealed class SharedWork<TResult>
{
    private readonly ConcurrentDictionary<string, Lazy<Task<TResult>>> _running = new(StringComparer.Ordinal);
    private readonly SharedWorkBudget<TResult>? _budget;

    /// <summary>Legacy completion-owned work; existing non-cancellable factories keep their contract.</summary>
    public SharedWork() { }

    /// <summary>
    /// Demand-owned work: execution slots, queued keys and individual waiters are all bounded.
    /// A new demand replaces the oldest queued key on overflow; it never waits outside the budget.
    /// </summary>
    public SharedWork(int maxExecuting, int maxQueued, int maxDemands) =>
        _budget = new SharedWorkBudget<TResult>(maxExecuting, maxQueued, maxDemands);

    /// <summary>How many keys are in flight right now. Diagnostics, and what the tests count.</summary>
    public int Running => _budget?.Running ?? _running.Count;

    /// <summary>Factories actually executing, including cancelled work that has not returned yet.</summary>
    public int Executing => _budget?.Executing ?? _running.Count;

    /// <summary>Admitted keys not started yet. No factory/task per queued key is started.</summary>
    public int Queued => _budget?.Queued ?? 0;

    /// <summary>Retained individual waiters in demand-owned mode.</summary>
    public int Demands => _budget?.Demands ?? 0;

    /// <summary>
    /// Joins a bounded demand. The token passed to the factory belongs to all its current waiters,
    /// never to the first one; only zero demand cancels it. Cancellation/overflow never returns null.
    /// </summary>
    public Task<TResult> RunAsync(string key, Func<CancellationToken, Task<TResult>> work, CancellationToken waiter) =>
        (_budget ?? throw new InvalidOperationException("Demand-owned work requires explicit budgets"))
        .RunAsync(key, work, waiter);

    /// <summary>
    /// Runs <paramref name="work"/> for <paramref name="key"/>, or joins the run already under way.
    /// Throws <see cref="OperationCanceledException"/> if <paramref name="waiter"/> is cancelled first.
    /// </summary>
    public Task<TResult> RunAsync(string key, Func<Task<TResult>> work, CancellationToken waiter)
    {
        if (_budget is not null) return _budget.RunAsync(key, _ => work(), waiter);

        // Lazy rather than GetOrAdd's factory overload: that overload is allowed to run a losing
        // factory as well, which here would start a second download that nothing ever waits on and
        // nothing ever removes from the table.
        var mine = new Lazy<Task<TResult>>(work, LazyThreadSafetyMode.ExecutionAndPublication);
        var entry = _running.GetOrAdd(key, mine);

        Task<TResult> shared;
        try
        {
            shared = entry.Value;
        }
        catch
        {
            // A synchronous throw is cached by the Lazy, so the entry has to go: otherwise every later
            // caller for this key is handed the same failure for the lifetime of the process.
            Forget(key, entry);
            throw;
        }

        // Only the caller whose entry won arranges the removal, and only now that the table holds it: a
        // task that finished before the insert would otherwise remove nothing and stay there forever,
        // answering every later caller with the bytes of a poster that has since changed.
        if (ReferenceEquals(entry, mine))
        {
            _ = shared.ContinueWith(
                _ => Forget(key, entry),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        // WaitAsync, not a plain await: the caller's token releases the caller, not the work.
        return shared.WaitAsync(waiter);
    }

    /// <summary>
    /// Removes the entry only if it is still this one. The pair overload is the atomic form — a plain
    /// <c>TryRemove(key)</c> could drop a newer run that took the same key in the meantime.
    /// </summary>
    private void Forget(string key, Lazy<Task<TResult>> entry) =>
        _running.TryRemove(new KeyValuePair<string, Lazy<Task<TResult>>>(key, entry));
}
