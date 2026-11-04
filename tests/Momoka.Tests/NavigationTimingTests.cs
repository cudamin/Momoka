using System.Text.Json;
using Momoka.Diagnostics;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 导航阶段计时只在显式开关下工作：运行归属、单调耗时和隐私白名单先在这里钉住，再由真实页面埋点使用。
/// 这些用例不启动 WinUI、不读服务器，也不依赖真实时钟的精度。
/// </summary>
internal static class NavigationTimingTests
{
    public static void Register()
    {
        Test("导航计时：未启用时不产生运行或记录", () =>
        {
            NavigationTiming.Stop();
            var sink = new CollectingSink();
            NavigationTiming.Start(new NavigationTimingRecorder(sink));
            NavigationTiming.Stop();

            NavigationTiming.Press("card");
            NavigationTiming.Click("card");
            NavigationTiming.Mark("click");
            NavigationTiming.Complete("end");

            Assert.True(!NavigationTiming.Enabled, "Stop 后不应仍处于启用状态");
            Assert.Null(NavigationTiming.Capture(), "Stop 后不应有当前运行");
            Assert.Equal(0, sink.Count, "未启用时不应写任何事件");
        });

        Test("导航计时：按下与点击开始一次运行，阶段共享运行 ID", () =>
        {
            var sink = new CollectingSink();
            var recorder = new NavigationTimingRecorder(sink);
            NavigationTiming.Start(recorder);
            try
            {
                NavigationTiming.Press("card");
                var run = NavigationTiming.Click("card");
                Assert.NotNull(run, "按下后点击应开始运行");
                run!.Mark("navigate-begin");
                run.Mark("apply", 3);
                NavigationTiming.Complete("first-render");

                var events = sink.Snapshot();
                Assert.Equal(5, events.Count, "应记录 press/click/navigate-begin/apply/first-render");
                Assert.Equal("press", events[0].Stage);
                Assert.Equal("click", events[1].Stage);
                Assert.Equal("navigate-begin", events[2].Stage);
                Assert.Equal("apply", events[3].Stage);
                Assert.Equal(3, events[3].Count);
                Assert.Equal("first-render", events[4].Stage);
                Assert.True(events.All(item => item.RunId == run.RunId && item.Scenario == "card"),
                    "同一次运行的阶段必须共享运行 ID 和场景");
                Assert.True(events.Zip(events.Skip(1)).All(pair => pair.Second.ElapsedMs >= pair.First.ElapsedMs),
                    "同一次运行的耗时应单调不减");
                Assert.True(events.All(item => item.ThreadId > 0), "每条记录都应带线程号");
                Assert.True(events[1].ElapsedMs < 2000, "按下到点击不应借用过期按压");
            }
            finally
            {
                NavigationTiming.Stop();
            }
        });

        Test("导航计时：没有指针按压的按钮点击从 click 开始", () =>
        {
            var sink = new CollectingSink();
            NavigationTiming.Start(new NavigationTimingRecorder(sink));
            try
            {
                var run = NavigationTiming.Click("home-button");
                Assert.NotNull(run, "按钮点击应开始运行");
                run!.Mark("navigate-begin");

                var events = sink.Snapshot();
                Assert.Equal(2, events.Count, "没有按压时不应伪造 press 阶段");
                Assert.Equal("click", events[0].Stage);
                Assert.Equal("navigate-begin", events[1].Stage);
            }
            finally
            {
                NavigationTiming.Stop();
            }
        });

        Test("导航计时：场景不匹配的按压不被下一次点击借用", () =>
        {
            var sink = new CollectingSink();
            NavigationTiming.Start(new NavigationTimingRecorder(sink));
            try
            {
                NavigationTiming.Press("card");
                var run = NavigationTiming.Click("home-button");
                Assert.NotNull(run, "按钮点击仍应开始运行");
                Assert.Equal(1, sink.Snapshot().Count(item => item.Stage == "click"), "应只记录按钮自己的点击");
                Assert.True(sink.Snapshot().All(item => item.Scenario == "home-button"),
                    "卡片按压不能挂到主页按钮的运行上");
            }
            finally
            {
                NavigationTiming.Stop();
            }
        });

        Test("导航计时：跨 await 的异步阶段仍归属原运行", () =>
        {
            var sink = new CollectingSink();
            NavigationTiming.Start(new NavigationTimingRecorder(sink));
            try
            {
                NavigationTiming.Press("card");
                var first = NavigationTiming.Click("card")!;
                var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var continuation = MarkAfterAwaitAsync(gate.Task);

                NavigationTiming.Press("card");
                var second = NavigationTiming.Click("card")!;
                second.Mark("second");

                gate.SetResult();
                continuation.GetAwaiter().GetResult();

                var afterAwait = sink.Snapshot().Single(item => item.Stage == "after-await");
                Assert.Equal(first.RunId, afterAwait.RunId, "await 之后应仍记在启动它的那次运行上");
                Assert.True(second.RunId != first.RunId, "第二次点击应产生新的运行 ID");
            }
            finally
            {
                NavigationTiming.Stop();
            }
        });

        Test("导航计时：Capture 返回当前运行，Complete 清空", () =>
        {
            var sink = new CollectingSink();
            NavigationTiming.Start(new NavigationTimingRecorder(sink));
            try
            {
                NavigationTiming.Press("card");
                var run = NavigationTiming.Click("card")!;
                Assert.True(ReferenceEquals(run, NavigationTiming.Capture()), "Capture 应取到刚启动的运行");

                NavigationTiming.Complete("first-render");
                Assert.Null(NavigationTiming.Capture(), "Complete 后不应还有当前运行");
                NavigationTiming.Mark("late");
                Assert.Equal(0, sink.Snapshot().Count(item => item.Stage == "late"), "完成后迟到标记不应再记录");
            }
            finally
            {
                NavigationTiming.Stop();
            }
        });

        Test("导航计时：阶段和场景只接受小写白名单，非法内容不落原文", () =>
        {
            var sink = new CollectingSink();
            NavigationTiming.Start(new NavigationTimingRecorder(sink));
            try
            {
                NavigationTiming.Press("card");
                var run = NavigationTiming.Click("card")!;
                run.Mark("token=secret http://server/item");
                var unsafeScenario = NavigationTiming.Click("card /secret");
                Assert.NotNull(unsafeScenario, "非法场景仍应安全开始运行");

                var events = sink.Snapshot();
                Assert.Equal("invalid", events.Single(item => item.Stage == "invalid" && item.Scenario == "card").Stage);
                Assert.Equal("invalid", events.Single(item => item.Scenario == "invalid").Scenario);
                Assert.True(events.All(item => !item.Stage.Contains("secret", StringComparison.Ordinal)
                    && !item.Stage.Contains("http", StringComparison.Ordinal)
                    && !item.Scenario.Contains("secret", StringComparison.Ordinal)),
                    "事件里不能出现原始非法文本");
            }
            finally
            {
                NavigationTiming.Stop();
            }
        });

        Test("导航计时：负数计数按零记录", () =>
        {
            var sink = new CollectingSink();
            NavigationTiming.Start(new NavigationTimingRecorder(sink));
            try
            {
                NavigationTiming.Press("card");
                var run = NavigationTiming.Click("card")!;
                run.Mark("apply", -5);
                Assert.Equal(0, sink.Snapshot().Single(item => item.Stage == "apply").Count);
            }
            finally
            {
                NavigationTiming.Stop();
            }
        });

        Test("导航计时文件 sink：每行一个 JSON 且只含匿名阶段字段", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "momoka-navigation-timing-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "navigation-timing.ndjson");
            try
            {
                using (var sink = new NavigationTimingFileSink(path))
                {
                    var recorder = new NavigationTimingRecorder(sink);
                    NavigationTiming.Start(recorder);
                    NavigationTiming.Press("card");
                    var run = NavigationTiming.Click("card")!;
                    run.Mark("navigate-begin");
                    run.Mark("apply", 2);
                    NavigationTiming.Stop();
                }

                var lines = File.ReadAllLines(path);
                Assert.Equal(4, lines.Length, "每条事件应写一行");
                using var first = JsonDocument.Parse(lines[0]);
                var root = first.RootElement;
                Assert.True(root.TryGetProperty("runId", out _), "应包含匿名运行 ID");
                Assert.True(root.TryGetProperty("stage", out var stage), "应包含阶段名");
                Assert.Equal("press", stage.GetString());
                Assert.True(root.TryGetProperty("elapsedMs", out _), "应包含耗时");
                Assert.True(root.TryGetProperty("count", out _), "应包含数量");
                Assert.True(root.TryGetProperty("threadId", out _), "应包含线程号");
                Assert.True(root.TryGetProperty("timestamp", out _), "应包含 UTC 时间");
                Assert.True(!lines.Any(line => line.Contains("http", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("token", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("server", StringComparison.OrdinalIgnoreCase)),
                    "事件文件不能包含地址、令牌或服务器字段");
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
        });
    }

    private static async Task MarkAfterAwaitAsync(Task gate)
    {
        await gate.ConfigureAwait(false);
        NavigationTiming.Mark("after-await");
    }

    private sealed class CollectingSink : INavigationTimingSink
    {
        private readonly object _gate = new();
        private readonly List<NavigationTimingEvent> _events = [];

        public int Count
        {
            get { lock (_gate) return _events.Count; }
        }

        public void Write(NavigationTimingEvent entry)
        {
            lock (_gate) _events.Add(entry);
        }

        public IReadOnlyList<NavigationTimingEvent> Snapshot()
        {
            lock (_gate) return _events.ToArray();
        }
    }
}
