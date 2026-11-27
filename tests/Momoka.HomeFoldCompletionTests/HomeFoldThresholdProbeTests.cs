using Momoka.Shell.Views;

namespace Momoka.Tests;

internal static class HomeFoldThresholdProbeTests
{
    internal static void Register(Action<string, Action> test)
    {
        test("probe/home-fold-caption-reads-real-finite-following-line", () =>
        {
            Equal(860d, HomeFoldThresholdProbeContract.CaptionLine((860, true)), "measured subtitle line survives unchanged");
            foreach (var invalid in new (double Line, bool HasNext)?[]
                { null, (860, false), (0, true), (-1, true), (double.NaN, true), (double.PositiveInfinity, true) })
                Reject(() => HomeFoldThresholdProbeContract.CaptionLine(invalid));
        });
        test("probe/home-fold-caption-guard-requires-still-visible-text", () =>
        {
            Check(HomeFoldThresholdProbeContract.AtTarget(860, 864, true), "four DIP of text clearance is the early-fold sample");
            Check(HomeFoldThresholdProbeContract.AtTarget(860, 868, true), "the exact eight DIP boundary belongs to fold");
            Check(!HomeFoldThresholdProbeContract.AtTarget(860, 860, true), "already clipped text cannot stand in for the early-fold sample");
            Check(!HomeFoldThresholdProbeContract.AtTarget(860, 868.01, true), "text outside the guard cannot pass the fold sample");
            Check(HomeFoldThresholdProbeContract.AtTarget(860, 868.01, false), "room below the entire guard belongs to flow");
            Check(!HomeFoldThresholdProbeContract.AtTarget(860, 868, false), "flow sample cannot include the fold boundary");
            foreach (var viewport in new[] { 0d, -1d, double.NaN, double.PositiveInfinity })
                Reject(() => HomeFoldThresholdProbeContract.AtTarget(860, viewport, true));
        });
        test("probe/home-fold-client-size-comes-from-caption-and-live-viewport", () =>
        {
            Equal(909, HomeFoldThresholdProbeContract.ClientHeight(1000, 1000, 877, false), "flow resize leaves a full guard and 24 DIP room");
            Equal(881, HomeFoldThresholdProbeContract.ClientHeight(1000, 1000, 877, true), "fold resize leaves the text visible with four DIP clearance");
            Equal(929, HomeFoldThresholdProbeContract.ClientHeight(1000, 980, 877, false), "real non-client/viewport difference survives conversion");
            Equal(1217, HomeFoldThresholdProbeContract.ClientHeight(880, 760, 1092.5, true), "a different measured line replaces any fixed window-height expectation");
            foreach (var client in new[] { 0d, -1d, double.NaN, double.PositiveInfinity, (double)int.MaxValue })
                Reject(() => HomeFoldThresholdProbeContract.ClientHeight(client, 1, 877, true));
        });
        test("probe/home-fold-final-reading-rejects-wrong-placement-or-stale-host", () =>
        {
            HomeFoldThresholdProbeContract.RequirePlacement(860, 900, false, false, false);
            HomeFoldThresholdProbeContract.RequirePlacement(860, 864, true, true, true);
            foreach (var (on, actual, visible) in new[]
                { (true, false, true), (true, true, false), (false, true, false), (false, false, true) })
                Reject(() => HomeFoldThresholdProbeContract.RequirePlacement(860, on ? 864 : 900, on, actual, visible));
            Reject(() => HomeFoldThresholdProbeContract.RequirePlacement(860, 900, true, true, true));
            Reject(() => HomeFoldThresholdProbeContract.RequirePlacement(860, 864, false, false, false));
        });
    }

    private static void Equal<T>(T expected, T actual, string message) =>
        Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected={expected}; actual={actual}");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("The production probe contract accepted an invalid layout or state");
    }
}
