using System.Text.Json;

namespace Momoka.Shell;

/// <summary>只判断匿名网页的实际视频状态、API调用计数及真实滚动读数，不驱动产品页面。</summary>
internal static class StoreTrailerScrollProbeEvidence
{
    internal static StoreTrailerWebRead ReadWeb(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("paused", out var paused) || paused.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !root.TryGetProperty("muted", out var muted) || muted.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !root.TryGetProperty("playCalls", out var plays) || plays.ValueKind != JsonValueKind.Number
                || !plays.TryGetInt32(out var playCalls) || playCalls < 0
                || !root.TryGetProperty("pauseCalls", out var pauses) || pauses.ValueKind != JsonValueKind.Number
                || !pauses.TryGetInt32(out var pauseCalls) || pauseCalls < 0)
                throw new InvalidOperationException("网页滚动回读缺少实际视频状态或非负整数API计数");
            return new(paused.GetBoolean(), muted.GetBoolean(), playCalls, pauseCalls);
        }
        catch (JsonException error) { throw new InvalidOperationException("网页滚动回读不是有效JSON", error); }
    }

    internal static void RequireBaseline(StoreTrailerScrollRead read)
    {
        RequireValid(read);
        if (Math.Abs(read.Offset) > 0.5 || read.ViewChanges != 0 || read.ScrollPaused || read.UserPaused
            || read.Web.Paused || !read.Web.Muted || read.Web.PlayCalls < 1 || read.Web.PauseCalls != 0)
            throw new InvalidOperationException("网页滚动补验没有从页顶、静音播放且无暂停调用的实际状态开始");
    }

    internal static void RequireStep(StoreTrailerScrollRead before, StoreTrailerScrollRead after,
        bool paused, int playDelta, int pauseDelta, bool scrollPaused, bool userPaused,
        double minimum, double maximum, int minimumChanges = 1)
    {
        RequireValid(before);
        RequireValid(after);
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || maximum < minimum
            || minimumChanges < 0 || playDelta < 0 || pauseDelta < 0
            || after.Offset < minimum || after.Offset > maximum || after.MinimumOffset < minimum
            || after.MaximumOffset > maximum || after.ViewChanges < minimumChanges
            || after.Web.Paused != paused || !after.Web.Muted
            || (long)after.Web.PlayCalls - before.Web.PlayCalls != playDelta
            || (long)after.Web.PauseCalls - before.Web.PauseCalls != pauseDelta
            || after.ScrollPaused != scrollPaused || after.UserPaused != userPaused)
            throw new InvalidOperationException($"网页滚动阶段 {after.Stage} 的实际偏移、暂停归属或API增量不符");
    }

    internal static void RequirePerformance(int frames, double p95Ms, bool stickyShown, bool collapsed, bool idle, double offset)
    {
        // 沿用 PerformanceDetailScrollAsync 的60帧与p95<40ms；回顶Idle/收起只有状态要求，耗时另行报告。
        if (frames < 60 || !double.IsFinite(p95Ms) || p95Ms <= 0 || p95Ms >= 40 || !stickyShown
            || !collapsed || !idle || !double.IsFinite(offset) || Math.Abs(offset) > 0.5)
            throw new InvalidOperationException("网页连续滚动未满足既有帧预算、悬浮卡收放或实际回顶Idle状态");
    }

    private static void RequireValid(StoreTrailerScrollRead read)
    {
        if (string.IsNullOrWhiteSpace(read.Stage) || !double.IsFinite(read.Offset) || read.Offset < 0
            || !double.IsFinite(read.MinimumOffset) || !double.IsFinite(read.MaximumOffset)
            || read.MinimumOffset < 0 || read.MinimumOffset > read.Offset || read.MaximumOffset < read.Offset
            || read.ViewChanges < 0 || read.Web.PlayCalls < 0 || read.Web.PauseCalls < 0)
            throw new InvalidOperationException("网页滚动实际读数不完整或无效");
    }
}

internal sealed record StoreTrailerWebRead(bool Paused, bool Muted, int PlayCalls, int PauseCalls);
internal sealed record StoreTrailerScrollRead(string Stage, double Offset, double MinimumOffset, double MaximumOffset,
    int ViewChanges, bool ScrollPaused, bool UserPaused, StoreTrailerWebRead Web);
