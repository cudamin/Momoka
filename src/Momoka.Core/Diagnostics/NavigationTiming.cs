using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Momoka.Diagnostics;

/// <summary>One anonymous stage timestamp from a navigation gesture.</summary>
public readonly record struct NavigationTimingEvent(
    string RunId,
    string Scenario,
    string Stage,
    double ElapsedMs,
    int Count,
    int ThreadId,
    DateTimeOffset Timestamp);

/// <summary>Where stage timestamps go. Implementations must not block the UI thread for long.</summary>
public interface INavigationTimingSink
{
    void Write(NavigationTimingEvent entry);
}

/// <summary>
/// Starts runs for <see cref="NavigationTiming"/>. One recorder is created per process when
/// <c>--navigation-timing</c> is present; tests supply their own sink.
/// </summary>
public sealed class NavigationTimingRecorder(INavigationTimingSink sink, Func<DateTimeOffset>? clock = null)
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public NavigationTimingRun Begin(string scenario) => Begin(scenario, Stopwatch.GetTimestamp());

    internal NavigationTimingRun Begin(string scenario, long startTimestamp) =>
        new(sink, scenario, startTimestamp, _clock);
}

/// <summary>
/// Anonymous timestamps for one gesture. The run starts at the recent pointer press when there is one,
/// so elapsed milliseconds include the press-to-click gap. Stage and scenario names are restricted to a
/// lowercase ASCII whitelist: a mistaken call cannot put a token, URL or server name in the evidence.
/// </summary>
public sealed class NavigationTimingRun
{
    private readonly INavigationTimingSink _sink;
    private readonly long _startTimestamp;
    private readonly Func<DateTimeOffset> _clock;

    internal NavigationTimingRun(
        INavigationTimingSink sink, string scenario, long startTimestamp, Func<DateTimeOffset> clock)
    {
        _sink = sink;
        RunId = Guid.NewGuid().ToString("N")[..8];
        Scenario = SafeName(scenario);
        _startTimestamp = startTimestamp;
        _clock = clock;
    }

    public string RunId { get; }

    public string Scenario { get; }

    public double ElapsedMs => Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds;

    public void Mark(string stage, int count = 0)
    {
        var entry = new NavigationTimingEvent(
            RunId,
            Scenario,
            SafeName(stage),
            Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds,
            Math.Max(0, count),
            Environment.CurrentManagedThreadId,
            _clock());
        try
        {
            // Measurement must never break navigation; a full disk or a closed writer is not the user's problem.
            _sink.Write(entry);
        }
        catch (Exception)
        {
        }
    }

    private static string SafeName(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 40) return "invalid";
        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '-') return "invalid";
        }
        return value;
    }
}

/// <summary>
/// The ambient navigation run for the UI gesture in flight. When no recorder is attached every entry
/// point is one static check and no allocation. Marks from async continuations stay with the run that
/// started them; callers that live longer than the click (for example a rendering callback) keep the
/// returned run and mark it directly.
/// </summary>
public static class NavigationTiming
{
    private static readonly AsyncLocal<NavigationTimingRun?> Current = new();
    private static NavigationTimingRecorder? _recorder;
    private static (long Timestamp, string Scenario) _press;

    public static bool Enabled => _recorder is not null;

    public static void Start(NavigationTimingRecorder recorder)
    {
        _recorder = recorder;
        Current.Value = null;
        _press = default;
    }

    public static void Stop()
    {
        _recorder = null;
        Current.Value = null;
        _press = default;
    }

    /// <summary>Notes the pointer press without starting a run; a click that never comes leaves nothing behind.</summary>
    public static void Press(string scenario)
    {
        if (_recorder is null) return;
        _press = (Stopwatch.GetTimestamp(), scenario);
    }

    /// <summary>
    /// Starts the run for a click. A matching press younger than two seconds supplies the run start and
    /// its own <c>press</c> stage; a button without an observed press starts at <c>click</c>.
    /// </summary>
    public static NavigationTimingRun? Click(string scenario)
    {
        var recorder = _recorder;
        if (recorder is null) return null;
        var press = _press;
        _press = default;

        NavigationTimingRun run;
        if (press.Timestamp != 0
            && string.Equals(press.Scenario, scenario, StringComparison.Ordinal)
            && Stopwatch.GetElapsedTime(press.Timestamp) < TimeSpan.FromSeconds(2))
        {
            run = recorder.Begin(scenario, press.Timestamp);
            run.Mark("press");
        }
        else
        {
            run = recorder.Begin(scenario);
        }

        run.Mark("click");
        Current.Value = run;
        return run;
    }

    public static NavigationTimingRun? Capture() => Current.Value;

    public static void Mark(string stage, int count = 0) => Current.Value?.Mark(stage, count);

    public static void Complete(string stage, int count = 0)
    {
        var run = Current.Value;
        if (run is null) return;
        run.Mark(stage, count);
        Current.Value = null;
    }
}

/// <summary>NDJSON evidence file. Buffered so stage marks stay cheap; dispose (or flush) at shutdown.</summary>
public sealed class NavigationTimingFileSink : INavigationTimingSink, IDisposable
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private bool _disposed;

    public NavigationTimingFileSink(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public void Write(NavigationTimingEvent entry)
    {
        var line = JsonSerializer.Serialize(entry, Json);
        lock (_gate)
        {
            if (_disposed) return;
            _writer.WriteLine(line);
        }
    }

    public void Flush()
    {
        lock (_gate)
        {
            if (!_disposed) _writer.Flush();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _writer.Dispose();
        }
    }
}
