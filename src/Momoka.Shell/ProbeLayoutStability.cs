namespace Momoka.Shell;

/// <summary>Waits for actual layout readings to stop changing; correctness remains the caller's assertion.</summary>
internal static class ProbeLayoutStability
{
    internal readonly record struct Reading(bool Ready, double[] Geometry);

    internal static async Task WaitAsync(Func<Reading> read, Func<Task> next, string context)
    {
        Reading? previous = null;
        var stable = 0;
        // Same two-second budget as the native probes' UntilAsync. Stable wrong dimensions return
        // to the strict caller check; this loop never waits for a desired width to happen by chance.
        for (var attempt = 0; attempt < 80; attempt++)
        {
            var actual = read();
            if (actual.Ready && actual.Geometry.Length > 0 && actual.Geometry.All(double.IsFinite))
            {
                var unchanged = previous is { } last && last.Ready
                    && last.Geometry.Length == actual.Geometry.Length
                    && last.Geometry.Zip(actual.Geometry).All(pair => Math.Abs(pair.First - pair.Second) < 0.01);
                stable = unchanged ? stable + 1 : 1;
                previous = actual;
                if (stable >= 3) return;
            }
            else
            {
                previous = null;
                stable = 0;
            }
            await next();
        }
        throw new TimeoutException(context + "的实际布局读数没有稳定");
    }
}
