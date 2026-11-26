using Momoka.Shell;

namespace Momoka.Tests;

internal static partial class Program
{
    private static void RegisterSelfCheckCrashStages()
    {
        var cases = new (int Stage, string? Category, string Expected)[]
        {
            (0, null, "主页"),
            (1, null, "媒体库页面"),
            (2, null, "详情页面"),
            (3, null, "服务器页面"),
            (4, null, "诊断页面"),
            (5, null, "设置页面"),
            (5, "Appearance", "设置页面（「Appearance」之后）"),
            (6, "Playback", "设置页面（「Playback」之后）")
        };
        foreach (var entry in cases)
            Test("Q013-crash-stage-" + entry.Stage + "-" + (entry.Category ?? "none"), () =>
            {
                var paths = Paths("Q013-crash-stage-" + entry.Stage + "-" + (entry.Category ?? "none"));
                ShellSelfCheck.ConfigureCrashStageForFixture(entry.Stage, entry.Category);
                ShellSelfCheck.ReportCrash(new(paths), new IOException("anonymous fixture crash"), "anonymous fixture crash");
                Equal(1, ShellSelfCheck.ExitCode, "crash report remains nonzero");
                Equal(1, ShellSelfCheck.CrashTimerStopsForFixture, "actual ReportCrash stops its timer once");
                Ensure(ReadSelf(paths).Contains("走到「" + entry.Expected + "」时崩溃", StringComparison.Ordinal), "production report names the actual stage");
                Ensure(ReadSelf(paths).Contains("结果：1 项失败", StringComparison.Ordinal), "crash report retains its failure summary");
                return Task.CompletedTask;
            });
    }
}
