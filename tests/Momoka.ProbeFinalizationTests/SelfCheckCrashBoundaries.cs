namespace Momoka.Shell;

internal static partial class ShellSelfCheck
{
    // Values and timer endpoint only; ReportCrash is extracted unchanged from production.
    private static int _stage;
    private static readonly List<CrashSetting> _settings = [];
    private static CrashTimer? _timer;
    private sealed record CrashSetting(string Category);
    private sealed class CrashTimer
    {
        internal int Stopped { get; private set; }
        internal void Stop() => Stopped++;
    }

    internal static void ConfigureCrashStageForFixture(int stage, string? category)
    {
        _stage = stage;
        _settings.Clear();
        if (category is not null) _settings.Add(new(category));
        _timer = new();
    }

    internal static int CrashTimerStopsForFixture => _timer?.Stopped ?? 0;
}
