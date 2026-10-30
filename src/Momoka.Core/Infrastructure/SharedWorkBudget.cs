namespace Momoka.Infrastructure;

/// <summary>
/// The opt-in demand scheduler behind SharedWork. It retains at most maxQueued deferred factories,
/// starts at most maxExecuting workers, and never retains an admission wait outside maxDemands.
/// New/renewed queued demand goes first; overflow replaces the oldest unstarted demand, not a download
/// another caller still needs. Cancellation retires a key immediately, but an uncooperative executing
/// factory keeps its slot until it really exits (otherwise cancellation could exceed the CPU/HTTP cap).
/// </summary>
internal sealed class SharedWorkBudget<TResult>
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _queue = new();
    private readonly int _maxExecuting;
    private readonly int _maxQueued;
    private readonly int _maxDemands;
    private int _executing;
    private int _demands;

    internal SharedWorkBudget(int maxExecuting, int maxQueued, int maxDemands)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxExecuting, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxQueued, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDemands, maxExecuting);
        _maxExecuting = maxExecuting;
        _maxQueued = maxQueued;
        _maxDemands = maxDemands;
    }

    internal int Running { get { lock (_gate) return _executing + _queue.Count; } }
    internal int Executing { get { lock (_gate) return _executing; } }
    internal int Queued { get { lock (_gate) return _queue.Count; } }
    internal int Demands { get { lock (_gate) return _demands; } }

    internal Task<TResult> RunAsync(string key, Func<CancellationToken, Task<TResult>> work, CancellationToken waiter)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(work);
        if (waiter.IsCancellationRequested) return Task.FromCanceled<TResult>(waiter);

        Demand demand;
        List<Entry> start;
        lock (_gate)
        {
            _entries.TryGetValue(key, out var entry);
            if (_demands == _maxDemands)
            {
                // A hot key must not retain unlimited individual waiters. Never sacrifice an executing
                // shared download: if all demand belongs to executing keys, reject without an admission wait.
                var victim = _queue.First;
                if (victim is not null && ReferenceEquals(victim.Value, entry)) victim = victim.Next;
                if (victim is null) return Task.FromCanceled<TResult>(new CancellationToken(canceled: true));
                RetireLocked(victim.Value, cancelled: true);
                victim.Value.Stop.Dispose(); // Never started, therefore no cancellation callbacks.
            }

            if (entry is null)
            {
                if (_queue.Count == _maxQueued)
                {
                    var victim = _queue.First!.Value;
                    RetireLocked(victim, cancelled: true);
                    victim.Stop.Dispose();
                }
                entry = new Entry(key, work);
                _entries.Add(key, entry);
                entry.Node = _queue.AddLast(entry);
            }
            else if (entry.Node is { } node)
            {
                _queue.Remove(node);
                entry.Node = _queue.AddLast(entry); // Renewed visible demand gets the same priority as new.
            }
            demand = new Demand(this, entry, waiter);
            demand.Node = entry.Waiters.AddLast(demand);
            _demands++;
            start = StartableLocked();
        }

        // Register before releasing our starts. A concurrent completion/eviction can finish the demand
        // first; Register handles that race without keeping an obsolete token registration alive.
        demand.Register();
        Start(start);
        return demand.Completion.Task;
    }

    private void Release(Demand demand)
    {
        List<Entry> start;
        lock (_gate)
        {
            if (demand.Node is not { } node) return; // Completion/eviction already owns the answer.
            var entry = demand.Entry;
            entry.Waiters.Remove(node);
            demand.Node = null;
            _demands--;
            if (entry.Waiters.Count == 0)
            {
                RetireLocked(entry);
                if (entry.Executing)
                {
                    // .NET CancelAsync sets the token synchronously but invokes callbacks on the pool.
                    // Keep the execution slot until BOTH the factory and those callbacks actually exit.
                    entry.Cancellation = entry.Stop.CancelAsync();
                }
                else entry.Stop.Dispose();
            }
            demand.Finish(cancelled: true); // Unregister before a replacement can consume its released budget.
            start = StartableLocked();
        }
        Start(start);
    }

    private void RetireLocked(Entry entry, bool cancelled = true, Exception? failure = null, TResult result = default!)
    {
        if (!entry.Retained) return;
        entry.Retained = false;
        if (_entries.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry))
            _entries.Remove(entry.Key);
        if (entry.Node is { } node) _queue.Remove(node);
        entry.Node = null;
        foreach (var demand in entry.Waiters)
        {
            demand.Node = null;
            demand.Finish(cancelled, failure, result);
        }
        _demands -= entry.Waiters.Count;
        entry.Waiters.Clear();
    }

    private List<Entry> StartableLocked()
    {
        List<Entry> start = [];
        while (_executing < _maxExecuting && _queue.Last is { } node)
        {
            var entry = node.Value;
            _queue.Remove(node);
            entry.Node = null;
            entry.Executing = true;
            _executing++;
            start.Add(entry);
        }
        return start;
    }

    private void Start(List<Entry> entries)
    {
        foreach (var entry in entries)
            _ = Task.Run(() => ExecuteAsync(entry));
    }

    private async Task ExecuteAsync(Entry entry)
    {
        TResult result = default!;
        Exception? failure = null;
        var cancelled = false;
        try
        {
            entry.Stop.Token.ThrowIfCancellationRequested();
            result = await entry.Work(entry.Stop.Token).ConfigureAwait(false);
            entry.Stop.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) { cancelled = true; }
        catch (Exception error) { failure = error; }

        Task? cancellation;
        lock (_gate)
        {
            RetireLocked(entry, cancelled, failure, result); // An old completion cannot remove a newer same-key demand.
            cancellation = entry.Cancellation;
        }
        if (cancellation is not null)
        {
            try { await cancellation.ConfigureAwait(false); }
            catch (AggregateException) { } // Callback faults have no remaining demand to fail.
        }
        entry.Stop.Dispose();
        List<Entry> start;
        lock (_gate)
        {
            _executing--;
            start = StartableLocked();
        }
        Start(start);
    }

    private sealed class Entry(string key, Func<CancellationToken, Task<TResult>> work)
    {
        internal string Key { get; } = key;
        internal Func<CancellationToken, Task<TResult>> Work { get; } = work;
        internal LinkedList<Demand> Waiters { get; } = new();
        internal Task? Cancellation { get; set; }
        internal CancellationTokenSource Stop { get; } = new();
        internal LinkedListNode<Entry>? Node { get; set; }
        internal bool Retained { get; set; } = true;
        internal bool Executing { get; set; }
    }

    private sealed class Demand(SharedWorkBudget<TResult> owner, Entry entry, CancellationToken token)
    {
        private readonly object _registrationGate = new();
        private CancellationTokenRegistration _registration;
        private bool _finished;

        internal Entry Entry { get; } = entry;
        internal LinkedListNode<Demand>? Node { get; set; }
        internal TaskCompletionSource<TResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void Register()
        {
            var registration = token.UnsafeRegister(static state =>
            {
                var demand = (Demand)state!;
                demand.Owner.Release(demand);
            }, this);
            lock (_registrationGate)
            {
                if (_finished) registration.Unregister();
                else _registration = registration;
            }
        }

        private SharedWorkBudget<TResult> Owner { get; } = owner;

        internal void Finish(bool cancelled, Exception? failure = null, TResult result = default!)
        {
            lock (_registrationGate)
            {
                _finished = true;
                // Unregister, not Dispose: never block a UI cancellation waiting for an active callback.
                // Once terminal, the scheduler retains neither the task nor its cancellation registration.
                _registration.Unregister();
            }
            if (cancelled) Completion.TrySetCanceled(token.IsCancellationRequested ? token : new CancellationToken(canceled: true));
            else if (failure is not null) Completion.TrySetException(failure);
            else Completion.TrySetResult(result);
        }
    }
}
