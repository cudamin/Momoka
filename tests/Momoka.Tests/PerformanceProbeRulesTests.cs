using Momoka.Shell;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class PerformanceProbeRulesTests
{
    internal static void Register()
    {
        Test("性能计时守卫：缺少、重复、错归属和倒序阶段均拒绝", () =>
        {
            var begin = new Momoka.Diagnostics.NavigationTimingEvent("run", "probe", "work-begin", 2, 0, 1, default);
            var end = begin with { Stage = "work-end", ElapsedMs = 3, Count = 4 };
            PerformanceProbeRules.TimingSpan([begin, end], "run", "work", 4);
            Assert.Throws<InvalidOperationException>(() => PerformanceProbeRules.TimingSpan([begin], "run", "work", 4));
            Assert.Throws<InvalidOperationException>(() => PerformanceProbeRules.TimingSpan([begin, begin, end], "run", "work", 4));
            Assert.Throws<InvalidOperationException>(() => PerformanceProbeRules.TimingSpan([begin, end with { RunId = "other" }], "run", "work", 4));
            Assert.Throws<InvalidOperationException>(() => PerformanceProbeRules.TimingSpan([begin, end with { ElapsedMs = 1 }], "run", "work", 4));
            Assert.Throws<InvalidOperationException>(() => PerformanceProbeRules.TimingSpan([begin, end], "run", "work", 5));
        });
        Test("性能探针守卫：正常收尾与有界唤醒通过", () =>
        {
            PerformanceProbeRules.ReleasedSearch(0, 0);
            PerformanceProbeRules.MatchingReadCanceled(true, true, 0);
            PerformanceProbeRules.OneLogWake(1, 1, 80, 80, true);
        });
        Test("性能探针守卫：残余单例订阅和结果均拒绝", () =>
        {
            Assert.Throws<InvalidOperationException>(() => PerformanceProbeRules.ReleasedSearch(1, 0));
            Assert.Throws<InvalidOperationException>(() => PerformanceProbeRules.ReleasedSearch(0, 1));
        });
        Test("性能探针守卫：未取消与旧success/finally覆盖新访问均拒绝", () =>
        {
            Assert.Throws<InvalidOperationException>(() => PerformanceProbeRules.MatchingReadCanceled(false, true, 0));
            Assert.Throws<InvalidOperationException>(() => PerformanceProbeRules.MatchingReadCanceled(true, false, 0));
            Assert.Throws<InvalidOperationException>(() => PerformanceProbeRules.MatchingReadCanceled(true, true, 1));
        });
        Test("性能探针守卫：每日志唤醒、未消费或丢尾部均拒绝", () =>
        {
            Assert.Throws<InvalidOperationException>(() => PerformanceProbeRules.OneLogWake(10000, 10000, 80, 80, true));
            Assert.Throws<InvalidOperationException>(() => PerformanceProbeRules.OneLogWake(1, 0, 80, 80, true));
            Assert.Throws<InvalidOperationException>(() => PerformanceProbeRules.OneLogWake(1, 1, 79, 80, true));
            Assert.Throws<InvalidOperationException>(() => PerformanceProbeRules.OneLogWake(1, 1, 80, 80, false));
        });
    }
}
