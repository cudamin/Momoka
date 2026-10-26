using System.Collections.Concurrent;
using System.Text.Json;

namespace Momoka.NaturalContinuationCallerTests;

internal sealed class TestFailure(string message) : Exception(message);
internal sealed class EndpointFailure(string message) : Exception(message);

internal static class Check
{
    internal static void True(bool value, string message)
    {
        if (!value) throw new TestFailure(message);
    }
    internal static void False(bool value, string message) => True(!value, message);
    internal static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new TestFailure(message + $" expected={expected}, actual={actual}");
    }
    internal static void Same(object? expected, object? actual, string message) => True(ReferenceEquals(expected, actual), message);
    internal static async Task<T> Throws<T>(Func<Task> action) where T : Exception
    {
        try { await action().ConfigureAwait(true); }
        catch (T error) { return error; }
        throw new TestFailure("Expected " + typeof(T).Name);
    }
}

internal static class Signals
{
    internal static TaskCompletionSource New() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static TaskCompletionSource<T> New<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static Task Done(Task task) => task.WaitAsync(TimeSpan.FromSeconds(5));
    internal static Task<T> Done<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(5));
}

internal sealed record Observation(int Sequence, string Event, int Hold, bool Idle, bool Playing, string? Item);
internal sealed class Tape
{
    private readonly object _gate = new();
    private readonly List<Observation> _events = [];
    internal Func<(int Hold, bool Idle, bool Playing, string? Item)>? State { get; set; }
    internal void Add(string name)
    {
        lock (_gate)
        {
            var state = State?.Invoke() ?? (0, true, false, null);
            _events.Add(new(_events.Count, name, state.Item1, state.Item2, state.Item3, state.Item4));
        }
    }
    internal Observation[] Snapshot() { lock (_gate) return _events.ToArray(); }
    internal Observation One(string name) => Snapshot().Single(e => e.Event == name);
    internal int Count(string prefix) => Snapshot().Count(e => e.Event.StartsWith(prefix, StringComparison.Ordinal));
}

// In-memory managed synchronization queue models ConfigureAwait(true) deterministically.
// It neither loads WinUI nor creates windows, named mutexes or a desktop input surface.
internal sealed class ManagedQueue : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<Action> _actions = new();
    public override void Post(SendOrPostCallback callback, object? state) => _actions.Add(() => callback(state));
    internal Task<int> Run(Func<Task<int>> action)
    {
        var completed = Signals.New<int>();
        var exit = 2;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                SetSynchronizationContext(this);
                _ = Root();
                foreach (var item in _actions.GetConsumingEnumerable()) item();
                if (failure is null) completed.TrySetResult(exit);
                else completed.TrySetException(failure);
            }
            catch (Exception error) { completed.TrySetException(error); }
        })
        { IsBackground = true, Name = "R046 anonymous caller queue" };
        thread.Start();
        return completed.Task;
        async Task Root()
        {
            try { exit = await action().ConfigureAwait(true); }
            catch (Exception error) { failure = error; }
            finally { _actions.CompleteAdding(); }
        }
    }
    public void Dispose() => _actions.Dispose();
}

internal sealed class CaseRunner(string output)
{
    private readonly List<(string Name, Func<Task> Action)> _cases = [];
    private readonly List<object> _results = [];
    private int _passed;
    private int _failed;
    internal void Test(string name, Func<Task> action) => _cases.Add((name, action));
    internal async Task<int> Run()
    {
        foreach (var item in _cases)
        {
            try
            {
                await Signals.Done(item.Action()).ConfigureAwait(true);
                _passed++;
                _results.Add(new { name = item.Name, status = "passed" });
                Console.WriteLine("通过 " + item.Name);
            }
            catch (Exception error)
            {
                _failed++;
                _results.Add(new { name = item.Name, status = "failed", detail = error.ToString() });
                Console.WriteLine("失败 " + item.Name + "\n" + error);
            }
        }
        var report = new { total = _passed + _failed, passed = _passed, failed = _failed, skipped = 0, exit = _failed == 0 ? 0 : 1, cases = _results };
        await File.WriteAllTextAsync(Path.Combine(output, "results.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n").ConfigureAwait(true);
        Console.WriteLine($"共 {_passed + _failed} 项，通过 {_passed}，失败 {_failed}，跳过 0，退出 {report.exit}");
        return report.exit;
    }
}
