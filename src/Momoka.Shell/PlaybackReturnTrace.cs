using System.Text.Json;

namespace Momoka.Shell;

/// <summary>Numeric UI observations only; no item names, server addresses or credentials.</summary>
internal sealed record PlaybackReturnReading(double Offset, double ScrollableHeight, double ExtentHeight,
    double ViewportHeight, double ContentHeight, bool Busy, bool Ready, double? RevealTarget,
    long RevealAgeMilliseconds, string ScrollState, string? FocusType, int EpisodeCount,
    int? PendingCorrelation, double? RequestedOffset);

internal sealed class PlaybackReturnTrace(Func<PlaybackReturnReading> read, Action<string> write, Func<long> clock)
{
    private readonly long _started = clock();
    private readonly List<object> _readings = [];

    internal void Record(string stage, int? completedCorrelation = null)
    {
        var observation = read();
        _readings.Add(new { stage, elapsedMilliseconds = clock() - _started, completedCorrelation, observation });
        // Preserve every preceding stage even if the last write failed and a later event retries it.
        write(JsonSerializer.Serialize(_readings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
