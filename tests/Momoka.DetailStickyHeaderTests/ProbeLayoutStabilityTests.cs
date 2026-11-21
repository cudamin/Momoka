using Momoka.Shell;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class ProbeLayoutStabilityTests
{
    internal static void Register()
    {
        Test("布局稳定等待：未就绪与过渡尺寸先跳过，连续三份相同才返回", () =>
        {
            var reads = 0;
            ProbeLayoutStability.WaitAsync(() => ++reads switch
            {
                1 => new(false, [360, 202.5]),
                2 => new(true, [17.76, 10]),
                _ => new(true, [360, 202.5])
            }, () => Task.CompletedTask, "测试").GetAwaiter().GetResult();
            Assert.Equal(5, reads);
        });
        Test("布局稳定等待：稳定错误尺寸也返回，交给原几何断言报错而不等碰巧转绿", () =>
        {
            var reads = 0;
            ProbeLayoutStability.WaitAsync(() => { reads++; return new(true, [17.76, 10]); },
                () => Task.CompletedTask, "测试").GetAwaiter().GetResult();
            Assert.Equal(3, reads);
        });
        Test("布局稳定等待：始终变化、不就绪和非有限值都在原预算内失败", () =>
        {
            foreach (var mode in new[] { "changing", "unready", "non-finite" })
            {
                var reads = 0;
                var failed = false;
                try
                {
                    ProbeLayoutStability.WaitAsync(() =>
                    {
                        reads++;
                        return new(mode != "unready", [mode switch
                        { "changing" => reads, "non-finite" => double.NaN, _ => 360 }]);
                    }, () => Task.CompletedTask, "测试").GetAwaiter().GetResult();
                }
                catch (TimeoutException) { failed = true; }
                Assert.True(failed, mode);
                Assert.Equal(80, reads);
            }
        });
    }
}
