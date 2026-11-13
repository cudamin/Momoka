using System.Text.Json;
using Momoka.Shell;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class PlaybackReturnTraceTests
{
    internal static void Register()
    {
        Test("返回只读轨迹保留全部阶段及原始偏移，不把暂时夹取重写为通过", () =>
        {
            var now = 10L;
            var observation = Reading(787, 900, true);
            var writes = new List<string>();
            var trace = new PlaybackReturnTrace(() => observation, writes.Add, () => now);
            trace.Record("before");
            now = 20;
            observation = Reading(442, 442, true);
            trace.Record("extent-changed");
            now = 700;
            observation = Reading(442, 900, false);
            trace.Record("final-read");
            using var json = JsonDocument.Parse(writes[^1]);
            var rows = json.RootElement.EnumerateArray().ToArray();
            Assert.Equal(3, rows.Length);
            Assert.Equal("before", rows[0].GetProperty("stage").GetString());
            Assert.Equal(442d, rows[^1].GetProperty("observation").GetProperty("Offset").GetDouble());
            Assert.Equal(690L, rows[^1].GetProperty("elapsedMilliseconds").GetInt64());
            Assert.False(writes[^1].Contains("Passed", StringComparison.Ordinal));
        });
        Test("返回轨迹写失败后保留前件，重试不会掩盖旧阶段", () =>
        {
            var fail = true;
            var output = "";
            var trace = new PlaybackReturnTrace(() => Reading(787, 900, true), text =>
            {
                if (fail) throw new IOException("fixture write failure");
                output = text;
            }, () => 0);
            Assert.Throws<IOException>(() => trace.Record("before"));
            fail = false;
            trace.Record("player-hidden");
            using var json = JsonDocument.Parse(output);
            Assert.Equal(2, json.RootElement.GetArrayLength());
            Assert.Equal("before", json.RootElement[0].GetProperty("stage").GetString());
        });
        Test("正式返回观察接线覆盖前后与原生变化，最终读数解绑", () =>
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) directory = directory.Parent;
            Assert.NotNull(directory);
            var shell = File.ReadAllText(Path.Combine(directory!.FullName, "src/Momoka.Shell/ShellSelfCheck.cs"));
            Assert.Contains("BeginPlaybackReturnTrace(back);", shell);
            Assert.Contains("EndPlaybackReturnTrace();", shell);
            foreach (var stage in new[] { "player-shown", "refresh-requested", "player-hidden" })
                Assert.Contains("_playbackReturnTrace?.Record(\"" + stage + "\");", shell);
            var events = File.ReadAllText(Path.Combine(directory.FullName, "src/Momoka.Shell/ShellSelfCheck.PlaybackReturn.cs"));
            foreach (var (signal, handler) in new[] { ("ViewChanged", "view"), ("ExtentChanged", "extent"), ("StateChanged", "state") })
            {
                Assert.Contains("body." + signal + " += " + handler + ";", events);
                Assert.Contains("body." + signal + " -= " + handler + ";", events);
            }
            Assert.Contains("_playbackReturnTrace?.Record(\"final-read\");", events);
            Assert.Contains("page.ViewModel.PropertyChanged -= ready;", events);
        });
    }

    private static PlaybackReturnReading Reading(double offset, double max, bool busy) =>
        new(offset, max, max + 708, 708, max + 708, busy, !busy, 787, 700, "Idle", "Button", 3);
}
