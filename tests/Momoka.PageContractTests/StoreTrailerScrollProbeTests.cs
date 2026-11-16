using Momoka.Shell;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class StoreTrailerScrollProbeTests
{
    internal static void Register()
    {
        Test("网页滚动回读：实际视频布尔状态和API计数必须完整", () =>
        {
            Assert.Equal(new StoreTrailerWebRead(false, true, 2, 3), StoreTrailerScrollProbeEvidence.ReadWeb(
                """{"paused":false,"muted":true,"playCalls":2,"pauseCalls":3,"caption":"zh"}"""));
            foreach (var json in new[] { "{", "[]", "{}",
                """{"paused":false,"muted":true,"playCalls":2}""",
                """{"paused":"false","muted":true,"playCalls":2,"pauseCalls":3}""",
                """{"paused":false,"muted":true,"playCalls":2.5,"pauseCalls":3}""",
                """{"paused":false,"muted":true,"playCalls":-1,"pauseCalls":3}""",
                """{"paused":false,"muted":true,"playCalls":2,"pauseCalls":"3"}""",
                """{"paused":false,"muted":true,"playCalls":2147483648,"pauseCalls":3}""" })
                Assert.Throws<InvalidOperationException>(() => StoreTrailerScrollProbeEvidence.ReadWeb(json));
        });
        Test("网页滚动判据：页顶实际播放、连续滚动、越界和回顶恰一次命令", () =>
        {
            var initial = Row("initial", 0, false, false, false, 1, 0, 0);
            StoreTrailerScrollProbeEvidence.RequireBaseline(initial);
            var below = Row("below", 200, false, false, false, 1, 0, 35);
            StoreTrailerScrollProbeEvidence.RequireStep(initial, below, false, 0, 0, false, false, 0, 250, 2);
            var crossed = Row("crossed", 280, true, true, false, 1, 1);
            StoreTrailerScrollProbeEvidence.RequireStep(below, crossed, true, 0, 1, true, false, 260, 800);
            var sameSide = Row("same", 700, true, true, false, 1, 1, 35);
            StoreTrailerScrollProbeEvidence.RequireStep(crossed, sameSide, true, 0, 0, true, false, 260, 800, 2);
            var returned = Row("returned", 0, false, false, false, 2, 1);
            StoreTrailerScrollProbeEvidence.RequireStep(sameSide, returned, false, 1, 0, false, false, 0, 0.5);
            Assert.Throws<InvalidOperationException>(() => StoreTrailerScrollProbeEvidence.RequireBaseline(initial with
            { Web = initial.Web with { PauseCalls = 1 } }));
        });
        Test("网页滚动判据：重复Pause、漏继续、错误偏移和缺少真实ViewChanged拒绝", () =>
        {
            var before = Row("crossed", 280, true, true, false, 1, 1);
            var after = Row("same", 700, true, true, false, 1, 1, 35);
            foreach (var malformed in new[]
            {
                after with { Web = after.Web with { PauseCalls = 2 } },
                after with { Web = after.Web with { PlayCalls = 2 } },
                after with { Web = after.Web with { Paused = false } },
                after with { ViewChanges = 0 }, after with { ScrollPaused = false }, after with { UserPaused = true },
                after with { Offset = 240, MinimumOffset = 240 }, after with { Offset = double.NaN },
                after with { MinimumOffset = double.NaN }, after with { Web = after.Web with { PauseCalls = -1 } }
            })
                Assert.Throws<InvalidOperationException>(() => StoreTrailerScrollProbeEvidence.RequireStep(before, malformed,
                    true, 0, 0, true, false, 260, 800, 2));
            Assert.Throws<InvalidOperationException>(() => StoreTrailerScrollProbeEvidence.RequireStep(after,
                Row("failed-resume", 0, true, false, false, 1, 1), false, 1, 0, false, false, 0, 0.5));
        });
        Test("网页滚动判据：用户暂停回顶保持暂停，继续调用或伪标志不能绕过", () =>
        {
            var before = Row("user-deep", 700, true, true, true, 2, 3);
            var after = Row("user-top", 0, true, false, true, 2, 3);
            StoreTrailerScrollProbeEvidence.RequireStep(before, after, true, 0, 0, false, true, 0, 0.5);
            foreach (var malformed in new[] { after with { Web = after.Web with { PlayCalls = 3 } },
                after with { Web = after.Web with { Paused = false } }, after with { UserPaused = false },
                after with { ScrollPaused = true } })
                Assert.Throws<InvalidOperationException>(() => StoreTrailerScrollProbeEvidence.RequireStep(before, malformed,
                    true, 0, 0, false, true, 0, 0.5));
        });
        Test("网页滚动性能判据：沿用60帧和p95预算，实际Idle及悬浮卡必须完成", () =>
        {
            StoreTrailerScrollProbeEvidence.RequirePerformance(70, 12, true, true, true, 0);
            foreach (var bad in new[] { (59, 12d, true, true, true, 0d), (70, 40d, true, true, true, 0d),
                (70, double.NaN, true, true, true, 0d), (70, 12d, false, true, true, 0d),
                (70, 12d, true, false, true, 0d), (70, 12d, true, true, false, 0d), (70, 12d, true, true, true, 4d) })
                Assert.Throws<InvalidOperationException>(() => StoreTrailerScrollProbeEvidence.RequirePerformance(
                    bad.Item1, bad.Item2, bad.Item3, bad.Item4, bad.Item5, bad.Item6));
        });
        Test("网页滚动探针：真实API经过页面回调，不直接写滚动暂停位或伪造内容高", () =>
        {
            var web = Read("src/Momoka.Shell/ShellNavigationProbe.StoreTrailerWeb.cs");
            var scroll = Read("src/Momoka.Shell/ShellNavigationProbe.StoreTrailerScroll.cs");
            var fixture = Read("src/Momoka.Shell/ShellNavigationProbe.StoreTrailer.cs");
            Assert.True(web.IndexOf("await StoreTrailerWebScrollAsync", StringComparison.Ordinal)
                > web.IndexOf("finally { _root.RemoveHandler", StringComparison.Ordinal));
            foreach (var required in new[] { "scroll.ViewChanged += OnChanged", "scroll.ViewChanged -= OnChanged",
                "scroll.ScrollTo(", "CompositionTarget.Rendering += OnRendering", "CompositionTarget.Rendering -= OnRendering",
                "await ScrollProbeAsync(scroll, 0)", "ScrollingInteractionState.Idle", "StoreTrailerScrollProbeEvidence.RequireStep",
                "var web = await readWeb()", "topIdleMs", "topCollapseMs", "physicalWheel = false", "await WriteAsync();" })
                Assert.Contains(required, scroll);
            Assert.DoesNotContain("Field(page,", scroll);
            Assert.DoesNotContain("ScrollStoreTrailer\")", scroll);
            Assert.DoesNotContain(".Height =", scroll);
            Assert.Contains("People = Enumerable.Range(0, 14)", fixture);
            Assert.Contains("Items(14, 14, \"preview-similar-\")", fixture);
            Assert.Contains("state.playCalls=playCalls;state.pauseCalls=pauseCalls", web);
            Assert.Contains("playCalls++;frame.contentWindow.postMessage('play'", web);
            Assert.Contains("pauseCalls++;frame.contentWindow.postMessage('pause'", web);
            Assert.Contains("paused:v.paused", web);
            Assert.Contains("store-trailer-web-state-readings.jsonl", web);
            Assert.True(web.IndexOf("store-trailer-web-state-readings.jsonl", StringComparison.Ordinal)
                < web.IndexOf("return StoreTrailerScrollProbeEvidence.ReadWeb", StringComparison.Ordinal));
            var page = Read("src/Momoka.Shell/Views/DetailPage.xaml.cs");
            var body = page.IndexOf("private void OnBodyViewChanged", StringComparison.Ordinal);
            Assert.True(body >= 0);
            var trailer = page.IndexOf("ScrollStoreTrailer();", body, StringComparison.Ordinal);
            var sticky = page.IndexOf("UpdateStickyHeader();", body, StringComparison.Ordinal);
            Assert.True(trailer >= body && sticky > trailer);
            Assert.Contains("ViewChanged=\"OnBodyViewChanged\"", Read("src/Momoka.Shell/Views/DetailPage.xaml"));
            var entry = Read("src/Momoka.Shell/ShellNavigationProbe.cs");
            Assert.Contains("StoreTrailerAsync, 60, section:", entry);
        });
    }

    private static StoreTrailerScrollRead Row(string stage, double offset, bool paused, bool scrollPaused, bool userPaused,
        int playCalls, int pauseCalls, int changes = 1) =>
        new(stage, offset, offset, offset, changes, scrollPaused, userPaused, new(paused, true, playCalls, pauseCalls));

    private static string Read(string relative)
    {
        for (var root = new DirectoryInfo(AppContext.BaseDirectory); root is not null; root = root.Parent)
            if (File.Exists(Path.Combine(root.FullName, "Momoka.sln"))) return File.ReadAllText(Path.Combine(root.FullName, relative));
        throw new AssertionException("找不到本树测试源");
    }
}
