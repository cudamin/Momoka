namespace Momoka.Tests;

/// <summary>No wall-clock waiting or real timer callbacks unless the test explicitly requests one.</summary>
internal sealed class BudgetClock : TimeProvider
{
    private readonly List<BudgetTimer> _timers = [];
    internal DateTimeOffset Now { get; private set; } = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    internal int TimerCreations => _timers.Count;
    internal int ActiveTimers => _timers.Count(timer => !timer.Disposed);
    internal TimeSpan LastPeriod => _timers[^1].Period;
    public override DateTimeOffset GetUtcNow() => Now;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new BudgetTimer(this, callback, state, dueTime, period);
        _timers.Add(timer);
        return timer;
    }

    internal void Advance(TimeSpan amount, bool fire = true)
    {
        Now += amount;
        if (fire) Fire();
    }

    internal void Fire()
    {
        foreach (var timer in _timers.ToArray()) timer.FireIfDue();
    }

    // Models a callback already queued before Dispose, which timer disposal cannot retract.
    internal Action CaptureCallback() => _timers[^1].Invoke;

    private sealed class BudgetTimer(BudgetClock owner, TimerCallback callback, object? state,
        TimeSpan dueTime, TimeSpan period) : ITimer
    {
        private DateTimeOffset _next = owner.Now + dueTime;
        internal bool Disposed;
        internal TimeSpan Period = period;
        internal void Invoke() => callback(state);
        internal void FireIfDue()
        {
            if (Disposed || owner.Now < _next) return;
            _next = owner.Now + Period;
            Invoke();
        }
        public bool Change(TimeSpan due, TimeSpan repeat)
        {
            if (Disposed) return false;
            _next = owner.Now + due;
            Period = repeat;
            return true;
        }
        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
